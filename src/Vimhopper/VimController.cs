using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Vimhopper.Core;

namespace Vimhopper;

/// <summary>캔버스 하나의 Vim 모드 상태와 명령 실행을 담당한다.</summary>
internal sealed class VimController
{
    static readonly HashSet<GH_Canvas> s_attached = new();

    readonly GH_Canvas _canvas;
    readonly KeyParser _parser;
    readonly Font _labelFont = new("Consolas", 6f, FontStyle.Bold);
    readonly Font _hudFont = new("Consolas", 10f, FontStyle.Bold);

    bool _normal;
    IntPtr _savedIme;
    Guid? _selected;
    IGH_Param? _source;
    IGH_Param? _opTarget;
    string _status = "";

    VimController(GH_Canvas canvas)
    {
        _canvas = canvas;
        _parser = new KeyParser(CountParams);
        _ = new CanvasKeyHook(canvas, OnKeyDown);
        canvas.CanvasPostPaintObjects += _ => DrawLabels();
        canvas.CanvasPaintEnd += _ => DrawHud();
        canvas.DocumentChanged += (_, _) => ResetState();
    }

    public static void Attach(GH_Canvas canvas)
    {
        if (s_attached.Add(canvas))
            _ = new VimController(canvas);
    }

    GH_Document? Document => _canvas.Document;

    IGH_DocumentObject? Selected =>
        _selected is { } id ? Document?.FindObject(id, true) : null;

    // ───────────────────────── 키 입력 ─────────────────────────

    bool OnKeyDown(Keys keyData)
    {
        var code = keyData & Keys.KeyCode;
        var modifiers = keyData & Keys.Modifiers;

        if (!_normal)
        {
            if (code == Keys.Escape && modifiers == Keys.None && Document != null && _canvas.ActiveInteraction == null)
            {
                EnterNormal();
                return true;
            }
            return false;
        }

        // Ctrl/Alt 조합(Ctrl+S, Ctrl+Z 등)은 Grasshopper에 그대로 넘긴다
        if ((modifiers & (Keys.Control | Keys.Alt)) != 0)
            return false;

        if (Translate(code, (modifiers & Keys.Shift) != 0) is not { } key)
            return false;

        _status = "";
        foreach (var command in _parser.Feed(key))
            Execute(command);
        _canvas.Refresh();
        return true;
    }

    static char? Translate(Keys code, bool shift)
    {
        if (code >= Keys.A && code <= Keys.Z)
        {
            var c = (char)('a' + (code - Keys.A));
            return shift ? char.ToUpperInvariant(c) : c;
        }
        if (code >= Keys.D0 && code <= Keys.D9 && !shift)
            return (char)('0' + (code - Keys.D0));
        if (code >= Keys.NumPad0 && code <= Keys.NumPad9)
            return (char)('0' + (code - Keys.NumPad0));

        return code switch
        {
            Keys.Escape => KeyParser.Escape,
            Keys.Enter => KeyParser.Enter,
            Keys.Back => KeyParser.Backspace,
            _ => null,
        };
    }

    void EnterNormal()
    {
        _normal = true;
        _savedIme = Ime.ImmAssociateContext(_canvas.Handle, IntPtr.Zero);
        if (Selected == null)
        {
            var current = Document?.SelectedObjects().OfType<IGH_ActiveObject>().FirstOrDefault();
            if (current != null)
                Select(current);
            else
                SelectNearestToView();
        }
        _canvas.Refresh();
    }

    void ExitNormal()
    {
        _normal = false;
        Ime.ImmAssociateContext(_canvas.Handle, _savedIme);
        ResetState();
        _canvas.Refresh();
    }

    void ResetState()
    {
        _parser.Reset();
        _source = null;
        _opTarget = null;
        _status = "";
    }

    // ───────────────────────── 명령 실행 ─────────────────────────

    void Execute(Command command)
    {
        switch (command)
        {
            case MoveCommand move: Move(move.Direction); break;
            case PickOutputCommand pick: PickOutput(pick.Index); break;
            case ConnectInputCommand connect: ConnectInput(connect.Index, connect.Append); break;
            case ParamOpCommand op: ApplyOp(op.Op); break;
            case UndoCommand: Undo(); break;
            case CancelCommand: _source = null; _opTarget = null; break;
            case CenterViewCommand: CenterOnSelected(); break;
            case ExitNormalCommand: ExitNormal(); break;
            case ErrorCommand error: _status = error.Message; break;
        }
    }

    void Move(Direction direction)
    {
        if (Selected is not { } current)
        {
            SelectNearestToView();
            return;
        }

        var next = Navigator.Next(Boxes(), current.InstanceGuid, direction);
        if (next is { } id && Document?.FindObject(id, true) is { } obj)
            Select(obj);
    }

    void PickOutput(int index)
    {
        if (Selected is not { } obj) return;
        _source = Params(obj, ParamSide.Output)[index];
        _opTarget = _source;
        _status = $"source: {Describe(_source)}";
    }

    void ConnectInput(int index, bool append)
    {
        if (Selected is not { } obj || Document is not { } doc) return;
        var target = Params(obj, ParamSide.Input)[index];
        _opTarget = target;

        if (_source == null)
        {
            _status = "먼저 o{n}으로 출력을 고르세요";
            return;
        }
        if (_source.Attributes.GetTopLevel.DocObject == target.Attributes.GetTopLevel.DocObject)
        {
            _status = "같은 객체끼리는 연결할 수 없습니다";
            return;
        }

        doc.UndoUtil.RecordWireEvent("Vimhopper wire", target);
        if (!append)
            target.RemoveAllSources();
        target.AddSource(_source);
        target.ExpireSolution(true);

        _status = $"{Describe(_source)} → {Describe(target)}";
        _source = null;
    }

    void ApplyOp(ParamOp op)
    {
        if (_opTarget is not { } param || Document is not { } doc) return;

        doc.UndoUtil.RecordGenericObjectEvent($"Vimhopper {op}", param);
        switch (op)
        {
            case ParamOp.Flatten:
                param.DataMapping = param.DataMapping == GH_DataMapping.Flatten ? GH_DataMapping.None : GH_DataMapping.Flatten;
                break;
            case ParamOp.Graft:
                param.DataMapping = param.DataMapping == GH_DataMapping.Graft ? GH_DataMapping.None : GH_DataMapping.Graft;
                break;
            case ParamOp.Simplify:
                param.Simplify = !param.Simplify;
                break;
            case ParamOp.Reverse:
                param.Reverse = !param.Reverse;
                break;
            case ParamOp.Invert:
                if (param is not Param_Boolean boolean)
                {
                    _status = "invert는 Boolean 파라미터에만 적용됩니다";
                    return;
                }
                boolean.Invert = !boolean.Invert;
                break;
        }

        // 출력 쪽 플래그는 컴포넌트가 다시 계산해야 반영되므로 최상위 객체를 만료시킨다
        param.Attributes.GetTopLevel.DocObject.ExpireSolution(true);
        _status = $"{Describe(param)}: {op}";
    }

    void Undo()
    {
        if (Document is not { } doc) return;
        doc.Undo();
        _source = null;
        _opTarget = null;
    }

    // ───────────────────────── 선택 / 뷰 ─────────────────────────

    // 그룹·스크리블 등은 제외하고 컴포넌트와 파라미터(슬라이더, 패널 포함)만 순회 대상
    List<Box> Boxes() =>
        Document?.Objects
            .OfType<IGH_ActiveObject>()
            .Select(o =>
            {
                var b = o.Attributes.Bounds;
                return new Box(o.InstanceGuid, b.X, b.Y, b.Width, b.Height);
            })
            .ToList()
        ?? new List<Box>();

    void SelectNearestToView()
    {
        var mid = _canvas.Viewport.MidPoint;
        if (Navigator.Nearest(Boxes(), mid.X, mid.Y) is { } id && Document?.FindObject(id, true) is { } obj)
            Select(obj);
    }

    void Select(IGH_DocumentObject obj)
    {
        Document?.DeselectAll();
        obj.Attributes.Selected = true;
        _selected = obj.InstanceGuid;

        if (!_canvas.Viewport.VisibleRegion.Contains(obj.Attributes.Bounds))
            CenterOnSelected();
    }

    void CenterOnSelected()
    {
        if (Selected is not { } obj) return;
        var b = obj.Attributes.Bounds;
        _canvas.Viewport.MidPoint = new PointF(b.X + b.Width / 2, b.Y + b.Height / 2);
    }

    int CountParams(ParamSide side) => Selected is { } obj ? Params(obj, side).Count : 0;

    static IReadOnlyList<IGH_Param> Params(IGH_DocumentObject obj, ParamSide side) => obj switch
    {
        IGH_Component c => side == ParamSide.Output ? c.Params.Output : c.Params.Input,
        IGH_Param p when (side == ParamSide.Output ? p.Attributes.HasOutputGrip : p.Attributes.HasInputGrip) => new[] { p },
        _ => Array.Empty<IGH_Param>(),
    };

    static string Describe(IGH_Param param)
    {
        var owner = param.Attributes.GetTopLevel.DocObject;
        return owner == param ? param.NickName : $"{owner.NickName}.{param.NickName}";
    }

    // ───────────────────────── 그리기 ─────────────────────────

    /// <summary>선택된 객체의 입출력 옆에 인덱스 번호, 고른 소스 출력에 표시 (캔버스 좌표).</summary>
    void DrawLabels()
    {
        if (!_normal) return;
        var g = _canvas.Graphics;

        if (Selected is { } obj)
        {
            DrawIndices(g, Params(obj, ParamSide.Input), p => p.Attributes.InputGrip, StringAlignment.Far, -4);
            DrawIndices(g, Params(obj, ParamSide.Output), p => p.Attributes.OutputGrip, StringAlignment.Near, 4);
        }

        if (_source != null)
        {
            var grip = _source.Attributes.OutputGrip;
            using var pen = new Pen(Color.DarkOrange, 2f);
            g.DrawEllipse(pen, grip.X - 5, grip.Y - 5, 10, 10);
        }
    }

    void DrawIndices(Graphics g, IReadOnlyList<IGH_Param> parameters, Func<IGH_Param, PointF> grip, StringAlignment align, float offset)
    {
        using var format = new StringFormat { Alignment = align, LineAlignment = StringAlignment.Center };
        for (var i = 0; i < parameters.Count; i++)
        {
            var p = grip(parameters[i]);
            g.DrawString(i.ToString(), _labelFont, Brushes.DarkOrange, p.X + offset, p.Y, format);
        }
    }

    /// <summary>좌하단 모드 표시줄 (화면 좌표).</summary>
    void DrawHud()
    {
        if (!_normal) return;
        var g = _canvas.Graphics;
        var saved = g.Transform;
        g.ResetTransform();

        var text = "-- NORMAL --";
        if (_source != null) text += $"  [{Describe(_source)}]";
        if (_parser.Pending.Length > 0) text += $"  {_parser.Pending}";
        if (_status.Length > 0) text += $"   {_status}";

        var size = g.MeasureString(text, _hudFont);
        var rect = new RectangleF(8, _canvas.ClientSize.Height - size.Height - 12, size.Width + 12, size.Height + 4);
        using (var back = new SolidBrush(Color.FromArgb(200, 30, 30, 30)))
            g.FillRectangle(back, rect);
        g.DrawString(text, _hudFont, Brushes.White, rect.X + 6, rect.Y + 2);

        g.Transform = saved;
    }
}
