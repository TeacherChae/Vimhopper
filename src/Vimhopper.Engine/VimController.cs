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

/// <summary>
/// 캔버스 하나의 Vim 모드 상태와 명령 실행을 담당한다.
/// Vim 모드가 아닐 때는 Grasshopper 기본 조작(마우스) 그대로 동작한다.
/// </summary>
/// <remarks>
/// 핫 리로드 때 Dispose된 뒤 새 버전으로 교체되므로, 캔버스에 연결한 것은 모두 Dispose에서 해제해야 한다.
/// 하나라도 남으면 이전 버전이 계속 키를 받거나 그린다.
/// </remarks>
internal sealed class VimController : IDisposable
{
    // 선택 객체를 화면 안에 유지할 때 가장자리와 띄울 여백 (화면 픽셀)
    const float ScrollOffPixels = 60;

    const string StateVim = "vim";
    const string StateSelected = "selected";

    readonly GH_Canvas _canvas;
    readonly IDictionary<string, object?> _state;
    readonly KeyParser _parser;
    readonly TreeNavigator _tree;
    readonly CanvasKeyHook _hook;
    readonly Font _labelFont = new("Consolas", 6f, FontStyle.Bold);
    readonly Font _hudFont = new("Consolas", 10f, FontStyle.Bold);

    bool _vim;
    IntPtr _savedIme;
    IGH_Param? _source;
    IGH_Param? _opTarget;
    string _status = "";

    public VimController(GH_Canvas canvas, IDictionary<string, object?> state)
    {
        _canvas = canvas;
        _state = state;
        _parser = new KeyParser(CountParams);
        _tree = new TreeNavigator(new DocumentGraph(() => _canvas.Document));
        _hook = new CanvasKeyHook(canvas, OnKeyDown);
        canvas.CanvasPostPaintObjects += OnPostPaintObjects;
        canvas.CanvasPaintEnd += OnPaintEnd;
        canvas.DocumentChanged += OnDocumentChanged;

        // 리로드 전 상태 복원
        if (state.TryGetValue(StateSelected, out var selected) && selected is Guid id)
            _tree.Reset(id);
        if (state.TryGetValue(StateVim, out var vim) && vim is true)
        {
            EnterVim();
            _status = $"reloaded {DateTime.Now:HH:mm:ss}";
        }
    }

    public void Dispose()
    {
        _state[StateVim] = _vim;
        _state[StateSelected] = _tree.Current;
        if (_vim)
            ExitVim();

        _hook.Dispose();
        _canvas.CanvasPostPaintObjects -= OnPostPaintObjects;
        _canvas.CanvasPaintEnd -= OnPaintEnd;
        _canvas.DocumentChanged -= OnDocumentChanged;
        _labelFont.Dispose();
        _hudFont.Dispose();
        _canvas.Refresh();
    }

    void OnPostPaintObjects(GH_Canvas sender) => DrawLabels();

    void OnPaintEnd(GH_Canvas sender) => DrawHud();

    void OnDocumentChanged(GH_Canvas sender, GH_CanvasDocumentChangedEventArgs e)
    {
        ResetState();
        _tree.Reset(null);
    }

    GH_Document? Document => _canvas.Document;

    IGH_DocumentObject? Selected =>
        _tree.Current is { } id ? Document?.FindObject(id, true) : null;

    // ───────────────────────── 키 입력 ─────────────────────────

    bool OnKeyDown(Keys keyData)
    {
        var code = keyData & Keys.KeyCode;
        var modifiers = keyData & Keys.Modifiers;

        if (!_vim)
        {
            if (code == Keys.Escape && modifiers == Keys.None && Document != null && _canvas.ActiveInteraction == null)
            {
                EnterVim();
                return true;
            }
            return false;
        }

        if ((modifiers & Keys.Alt) != 0)
            return false;

        // Ctrl 조합은 Ctrl+hjkl, Ctrl+=/- 만 가로채고 나머지(Ctrl+S, Ctrl+Z 등)는 Grasshopper에 넘긴다
        var key = (modifiers & Keys.Control) != 0
            ? TranslateCtrl(code)
            : Translate(code, (modifiers & Keys.Shift) != 0);
        if (key is not { } k)
            return false;

        _status = "";
        foreach (var command in _parser.Feed(k))
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

    static char? TranslateCtrl(Keys code) => code switch
    {
        Keys.H => KeyParser.CtrlH,
        Keys.J => KeyParser.CtrlJ,
        Keys.K => KeyParser.CtrlK,
        Keys.L => KeyParser.CtrlL,
        Keys.Oemplus or Keys.Add => KeyParser.CtrlPlus,
        Keys.OemMinus or Keys.Subtract => KeyParser.CtrlMinus,
        _ => null,
    };

    void EnterVim()
    {
        _vim = true;
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

    void ExitVim()
    {
        _vim = false;
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
            case JumpCommand jump: Jump(jump.Direction); break;
            case ZoomCommand zoom: Zoom(zoom.In); break;
            case PickOutputCommand pick: PickOutput(pick.Index); break;
            case ConnectInputCommand connect: ConnectInput(connect.Index, connect.Append); break;
            case ParamOpCommand op: ApplyOp(op.Op); break;
            case UndoCommand: Undo(); break;
            case CancelCommand: _source = null; _opTarget = null; break;
            case CenterViewCommand: CenterOnSelected(); break;
            case ExitVimCommand: ExitVim(); break;
            case ErrorCommand error: _status = error.Message; break;
        }
    }

    void Move(Direction direction)
    {
        if (Selected == null)
        {
            SelectNearestToView();
            return;
        }

        if (_tree.Move(direction) is { } id && Document?.FindObject(id, true) is { } obj)
            Highlight(obj);
        else
            _status = direction switch
            {
                Direction.Left => "부모 없음",
                Direction.Right => "자식 없음",
                _ => "형제 없음",
            };
    }

    void Jump(Direction direction)
    {
        if (Selected is not { } current)
        {
            SelectNearestToView();
            return;
        }

        if (Navigator.Next(Boxes(), current.InstanceGuid, direction) is { } id && Document?.FindObject(id, true) is { } obj)
            Select(obj);
    }

    void Zoom(bool zoomIn)
    {
        _canvas.Navigate(zoomIn ? GH_CanvasNavigation.ZoomIn : GH_CanvasNavigation.ZoomOut);
        if (Selected is { } obj)
            EnsureVisible(obj);
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

    /// <summary>연결 이동 기록을 지우고 새로 선택 (화면 방향 이동, 모드 진입 시).</summary>
    void Select(IGH_DocumentObject obj)
    {
        _tree.Reset(obj.InstanceGuid);
        Highlight(obj);
    }

    void Highlight(IGH_DocumentObject obj)
    {
        Document?.DeselectAll();
        obj.Attributes.Selected = true;
        EnsureVisible(obj);
    }

    /// <summary>
    /// 선택 객체가 화면 가장자리 여백 안으로 들어오도록 필요한 만큼만 화면을 민다 (vim의 scrolloff).
    /// 이미 보이면 화면은 움직이지 않는다.
    /// </summary>
    void EnsureVisible(IGH_DocumentObject obj)
    {
        var view = _canvas.Viewport.VisibleRegion;
        var b = obj.Attributes.Bounds;
        var margin = Math.Min(ScrollOffPixels / _canvas.Viewport.Zoom, Math.Min(view.Width, view.Height) / 4);

        var dx = Shift(b.Left, b.Right, view.Left + margin, view.Right - margin);
        var dy = Shift(b.Top, b.Bottom, view.Top + margin, view.Bottom - margin);
        if (dx == 0 && dy == 0) return;

        var mid = _canvas.Viewport.MidPoint;
        _canvas.Viewport.MidPoint = new PointF(mid.X + dx, mid.Y + dy);
    }

    // [low, high] 구간을 [min, max] 안에 넣기 위한 최소 이동량. 구간이 더 크면 시작점(왼쪽/위)을 맞춘다.
    static float Shift(float low, float high, float min, float max)
    {
        if (low < min || high - low > max - min) return low - min;
        if (high > max) return high - max;
        return 0;
    }

    void CenterOnSelected()
    {
        if (Selected is not { } obj) return;
        var b = obj.Attributes.Bounds;
        _canvas.Viewport.MidPoint = new PointF(b.X + b.Width / 2, b.Y + b.Height / 2);
    }

    int CountParams(ParamSide side) => Selected is { } obj ? Params(obj, side).Count : 0;

    internal static IReadOnlyList<IGH_Param> Params(IGH_DocumentObject obj, ParamSide side) => obj switch
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
        if (!_vim) return;
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
        if (!_vim) return;
        var g = _canvas.Graphics;
        var saved = g.Transform;
        g.ResetTransform();

        var text = "-- VIM --";
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
