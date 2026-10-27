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
    readonly CanvasKeyHook _hook;
    readonly Font _labelFont = new("Consolas", 6f, FontStyle.Bold);
    readonly Font _hudFont = new("Consolas", 10f, FontStyle.Bold);

    bool _vim;
    Guid? _current;
    // 비주얼 모드의 시작점. null이면 비주얼 모드가 아니다
    Guid? _anchor;
    IntPtr _savedIme;
    IGH_Param? _source;
    IGH_Param? _opTarget;
    string _status = "";

    public VimController(GH_Canvas canvas, IDictionary<string, object?> state)
    {
        _canvas = canvas;
        _state = state;
        _parser = new KeyParser(CountParams);
        _hook = new CanvasKeyHook(canvas, OnKeyDown);
        canvas.CanvasPostPaintObjects += OnPostPaintObjects;
        canvas.CanvasPaintEnd += OnPaintEnd;
        canvas.DocumentChanged += OnDocumentChanged;

        // 리로드 전 상태 복원
        if (state.TryGetValue(StateSelected, out var selected) && selected is Guid id)
            _current = id;
        if (state.TryGetValue(StateVim, out var vim) && vim is true)
        {
            EnterVim();
            _status = $"reloaded {DateTime.Now:HH:mm:ss}";
        }
    }

    public void Dispose()
    {
        _state[StateVim] = _vim;
        _state[StateSelected] = _current;
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
        _current = null;
    }

    GH_Document? Document => _canvas.Document;

    IGH_DocumentObject? Selected =>
        _current is { } id ? Document?.FindObject(id, true) : null;

    // ───────────────────────── 키 입력 ─────────────────────────

    bool OnKeyDown(Keys keyData)
    {
        var code = keyData & Keys.KeyCode;
        var modifiers = keyData & Keys.Modifiers;

        // 백틱(`)으로 Vim 모드를 켜고 끈다. 입력 중인 명령이 있어도 바로 나간다
        if (code == Keys.Oemtilde && modifiers == Keys.None)
        {
            if (_vim)
                ExitVim();
            else if (Document != null && _canvas.ActiveInteraction == null)
                EnterVim();
            else
                return false;
            return true;
        }

        if (!_vim)
            return false;

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
            Keys.Oemplus when !shift => '=',
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
        _anchor = null;
        _source = null;
        _opTarget = null;
        _status = "";
    }

    // ───────────────────────── 명령 실행 ─────────────────────────

    void Execute(Command command)
    {
        switch (command)
        {
            case MoveCommand move: Move(move.Direction, move.Count); break;
            case JumpCommand jump: Jump(jump.Direction); break;
            case ZoomCommand zoom: Zoom(zoom.In); break;
            case PickOutputCommand pick: PickOutput(pick.Index); break;
            case ConnectInputCommand connect: ConnectInput(connect.Index, connect.Append); break;
            case ParamOpCommand op: ApplyOp(op.Op); break;
            case UndoCommand: Undo(); break;
            case CancelCommand: _source = null; _opTarget = null; break;
            case CenterViewCommand: CenterOnSelected(); break;
            case FormatCommand format: Format(format.Scope); break;
            case CopyCommand: Copy(); break;
            case CopyInfoCommand: CopyInfo(); break;
            case VisualCommand visual: SetVisual(visual.On); break;
            case SwapVisualEndsCommand: SwapVisualEnds(); break;
            case ErrorCommand error: _status = error.Message; break;
        }
    }

    /// <param name="count">h/l: 입력/출력 번호, j/k: 칸 수.</param>
    void Move(Direction direction, int? count)
    {
        if (Selected is not { } current || Document is not { } doc)
        {
            SelectNearestToView();
            return;
        }

        var graph = DocumentGraph.Build(doc);
        var id = current.InstanceGuid;
        var next = direction switch
        {
            Direction.Left => GraphNavigator.Parent(graph, id, count),
            Direction.Right => GraphNavigator.Child(graph, id, count),
            Direction.Down => GraphNavigator.Sibling(graph, id, count ?? 1),
            _ => GraphNavigator.Sibling(graph, id, -(count ?? 1)),
        };

        if (next is { } nextId && doc.FindObject(nextId, true) is { } obj)
            Select(obj);
        else
            _status = (direction, count) switch
            {
                (Direction.Left, { } n) => $"입력 {n}에 연결된 객체 없음",
                (Direction.Left, _) => "부모 없음",
                (Direction.Right, { } n) => $"출력 {n}을 받는 객체 없음",
                (Direction.Right, _) => "자식 없음",
                _ => "같은 열에 더 없음",
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

    void Format(FormatScope scope)
    {
        if (Document is not { } doc) return;

        List<IGH_DocumentObject> targets;
        if (scope == FormatScope.Island)
        {
            if (Selected is not { } obj)
            {
                _status = "선택된 객체가 없습니다";
                return;
            }
            targets = Island(obj);
            if (targets.Count < 2)
            {
                _status = "연결된 객체가 없습니다";
                return;
            }
        }
        else if (scope == FormatScope.Selection)
        {
            targets = doc.SelectedObjects().OfType<IGH_ActiveObject>().Cast<IGH_DocumentObject>().ToList();
            if (targets.Count < 2)
            {
                _status = "정렬할 객체가 2개 이상이어야 합니다";
                return;
            }
        }
        else
        {
            targets = doc.Objects.OfType<IGH_ActiveObject>().Cast<IGH_DocumentObject>().ToList();
        }

        // 선택 범위는 여러 덩어리에 걸칠 수 있으므로 문서 전체 정렬처럼 덩어리별로 나눈다
        var result = Formatter.Format(doc, targets, separateIslands: scope != FormatScope.Island);
        _status = result.Changed
            ? $"정렬: {result.Moved}개 이동, Relay +{result.RelaysAdded} -{result.RelaysRemoved}"
            : "이미 정렬되어 있습니다";

        // 선택했던 Relay가 정렬로 지워졌을 수 있다
        if (Selected is { } selected)
            EnsureVisible(selected);
        else
            SelectNearestToView();
    }

    /// <summary>와이어 방향과 무관하게 obj와 이어진 객체 전부.</summary>
    List<IGH_DocumentObject> Island(IGH_DocumentObject obj)
    {
        if (Document is not { } doc) return new List<IGH_DocumentObject>();
        var graph = DocumentGraph.Build(doc);
        var seen = new HashSet<Guid> { obj.InstanceGuid };
        var queue = new Queue<Guid>(seen);
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            foreach (var next in graph.Neighbors(id))
                if (seen.Add(next))
                    queue.Enqueue(next);
        }
        return seen.Select(id => Document?.FindObject(id, true)).OfType<IGH_DocumentObject>().ToList();
    }

    /// <summary>
    /// 선택된 객체마다 한 줄씩 "InstanceGuid  이름 (닉네임)  @ x,y  w×h"를 클립보드에 복사한다.
    /// GUID로 .ghx에서 객체를 찾고, 위치는 저장하지 않은 정렬 결과도 그대로 보여준다.
    /// 마우스로 여러 개를 고른 경우도 있으므로 Vim 선택이 아니라 Grasshopper 선택을 쓴다.
    /// </summary>
    void CopyInfo()
    {
        var objects = Document?.SelectedObjects()
            .OrderBy(o => o.Attributes.Bounds.X)
            .ThenBy(o => o.Attributes.Bounds.Y)
            .ToList();
        if (objects is not { Count: > 0 })
        {
            _status = "선택된 객체가 없습니다";
            return;
        }

        var lines = objects.Select(o =>
        {
            var b = o.Attributes.Bounds;
            var name = o.NickName == o.Name || string.IsNullOrEmpty(o.NickName) ? o.Name : $"{o.Name} ({o.NickName})";
            return $"{o.InstanceGuid}  {name}  @ {b.X:0},{b.Y:0}  {b.Width:0}×{b.Height:0}";
        });
        Clipboard.SetText(string.Join(Environment.NewLine, lines));
        _status = objects.Count == 1 ? $"info: {objects[0].NickName}" : $"info: {objects.Count} objects";
    }

    /// <summary>
    /// Grasshopper의 Ctrl+C처럼 OS 클립보드에 객체를 복사한다 (Ctrl+V로 붙여넣기).
    /// 비주얼 모드면 선택 범위를, 아니면 커서 객체만 복사한다.
    /// </summary>
    void Copy()
    {
        if (Document is not { } doc) return;
        var ids = _anchor != null
            ? doc.SelectedObjects().Select(o => o.InstanceGuid).ToList()
            : Selected is { } obj ? new List<Guid> { obj.InstanceGuid } : new List<Guid>();
        if (ids.Count == 0)
        {
            _status = "선택된 객체가 없습니다";
            return;
        }

        _status = new GH_DocumentIO(doc).Copy(GH_ClipboardType.System, ids)
            ? ids.Count == 1 ? $"copied {Selected?.NickName}" : $"copied {ids.Count} objects"
            : "복사하지 못했습니다";
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

    /// <summary>커서를 obj로 옮긴다. 비주얼 모드면 시작점부터 커서까지를, 아니면 obj만 선택한다.</summary>
    void Select(IGH_DocumentObject obj)
    {
        _current = obj.InstanceGuid;
        Document?.DeselectAll();
        if (_anchor is { } anchor && Document is { } doc)
        {
            foreach (var id in VisualRange.Select(Boxes(), anchor, obj.InstanceGuid))
                if (doc.FindObject(id, true) is { } selected)
                    selected.Attributes.Selected = true;
        }
        else
        {
            obj.Attributes.Selected = true;
        }
        EnsureVisible(obj);
    }

    void SetVisual(bool on)
    {
        if (on && Selected == null)
            SelectNearestToView();
        _anchor = on ? _current : null;
        if (Selected is { } obj)
            Select(obj);
    }

    void SwapVisualEnds()
    {
        if (_anchor is not { } anchor || Document?.FindObject(anchor, true) is not { } target)
            return;
        _anchor = _current;
        Select(target);
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

        var text = _anchor != null ? "-- VIM(visual) --" : "-- VIM(normal) --";
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
