using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Undo;
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
internal sealed partial class VimController : IDisposable
{
    // 선택 객체를 화면 안에 유지할 때 가장자리와 띄울 여백 (화면 픽셀)
    const float ScrollOffPixels = 60;

    // Space Space 검색창을 커서 객체 오른쪽 끝에서 띄울 거리 (캔버스 좌표)
    const float InsertGap = 60;

    // 입력 중인 명령에서 이만큼 키가 없으면 다음 키 힌트를 띄운다 (which-key)
    const int HintDelayMs = 400;
    // 힌트 패널 한 열의 최대 줄 수. 넘치면 오른쪽에 열을 더한다
    const int HintRowsPerColumn = 8;

    const string StateVim = "vim";
    const string StateSelected = "selected";

    readonly GH_Canvas _canvas;
    readonly IDictionary<string, object?> _state;
    readonly KeyParser _parser;
    // . 으로 되풀이할 마지막 변경. 문서가 바뀌어도 vim처럼 남긴다
    readonly RepeatRegister _repeat = new();
    readonly CanvasKeyHook _hook;
    readonly WireHighlight _highlight = new();
    readonly Font _labelFont = new("Consolas", 6f, FontStyle.Bold);
    readonly Font _hudFont = new("Consolas", 10f, FontStyle.Bold);
    // 힌트 설명은 한글이라 한글 글꼴로
    readonly Font _hintFont = new("Malgun Gothic", 9f);
    readonly Timer _hintTimer = new() { Interval = HintDelayMs };

    bool _vim;
    bool _showHints;
    Guid? _current;
    // 비주얼 모드의 시작점. null이면 비주얼 모드가 아니다
    Guid? _anchor;
    // 비주얼 모드에서 H/L로 더한 객체들. 커서를 옮겨도 남는다
    readonly HashSet<Guid> _visualExtra = new();
    // 비주얼 모드에서 선택이 바뀌기 전 상태들. u로 하나씩 되돌린다
    readonly Stack<VisualState> _visualHistory = new();
    // Ctrl+o / Ctrl+i의 점프 목록. Vim 모드를 껐다 켜도 남고, 문서가 바뀌면 지운다
    readonly JumpList _jumps = new();
    IntPtr _savedIme;
    // Space Space로 검색창을 띄운 문서. 다음에 추가되는 객체로 커서를 옮긴다
    GH_Document? _insertDoc;
    IGH_Param? _source;
    IGH_Param? _opTarget;
    string _status = "";
    // 찾기 / 입력줄. _search는 / 입력 중일 때만 있다
    readonly InputLineBox _input;
    SearchSession? _search;
    // n / N이 되풀이할 마지막 찾기 (/, *, #)와 그 방향. 문서가 바뀌어도 vim처럼 남긴다
    SearchQuery? _lastQuery;
    bool _lastForward = true;

    public VimController(GH_Canvas canvas, IDictionary<string, object?> state)
    {
        _canvas = canvas;
        _state = state;
        _parser = new KeyParser(CountParams);
        _hook = new CanvasKeyHook(canvas, OnKeyDown);
        _input = new InputLineBox(canvas, _hook);
        canvas.CanvasPostPaintWires += OnPostPaintWires;
        canvas.CanvasPostPaintObjects += OnPostPaintObjects;
        canvas.CanvasPaintEnd += OnPaintEnd;
        canvas.DocumentChanged += OnDocumentChanged;
        _hintTimer.Tick += OnHintTimer;

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

        StopWatchingInsert();
        _input.Dispose();
        _hintTimer.Stop();
        _hintTimer.Tick -= OnHintTimer;
        _hintTimer.Dispose();
        _hook.Dispose();
        _canvas.CanvasPostPaintWires -= OnPostPaintWires;
        _canvas.CanvasPostPaintObjects -= OnPostPaintObjects;
        _canvas.CanvasPaintEnd -= OnPaintEnd;
        _canvas.DocumentChanged -= OnDocumentChanged;
        _labelFont.Dispose();
        _hudFont.Dispose();
        _highlight.Dispose();
        _hintFont.Dispose();
        _canvas.Refresh();
    }

    // 연결 강조: 커서의 와이어는 일반 와이어 바로 뒤에, h/l 표시는 객체 위에
    void OnPostPaintWires(GH_Canvas sender)
    {
        if (_vim && Selected is { } obj)
            _highlight.DrawWires(_canvas, obj);
    }

    void OnPostPaintObjects(GH_Canvas sender)
    {
        if (_vim && Selected is { } obj && Document is { } doc)
            _highlight.DrawHops(_canvas, doc, obj);
        DrawSearchMatches();
        DrawLabels();
    }

    void OnPaintEnd(GH_Canvas sender) => DrawHud();

    void OnDocumentChanged(GH_Canvas sender, GH_CanvasDocumentChangedEventArgs e)
    {
        StopWatchingInsert();
        _highlight.Release();
        ResetState();
        _jumps.Clear();
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

        // 입력줄이 열린 동안 키는 글자로 입력줄에 간다 (CanvasKeyHook.TextInput)
        if (_input.Active)
            return false;

        // 수정키만 누른 것(dH의 Shift 등)은 키가 아니므로 힌트를 닫지도 다시 기다리지도 않는다
        if (code is Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey
            or Keys.ControlKey or Keys.LControlKey or Keys.RControlKey
            or Keys.Menu or Keys.LMenu or Keys.RMenu)
            return false;

        // 힌트는 키를 누르면 닫고, 키 처리 뒤에도 입력 중이면 다시 기다린다
        HideHints();
        var handled = HandleVimKey(code, modifiers);
        ScheduleHints();
        return handled;
    }

    bool HandleVimKey(Keys code, Keys modifiers)
    {
        // 검색창을 닫고 캔버스로 돌아왔으면 더 기다리지 않는다 (검색창에 친 키는 여기로 오지 않음)
        StopWatchingInsert();

        if ((modifiers & Keys.Alt) != 0)
            return false;

        // Ctrl 조합은 Ctrl+hjkl, Ctrl+r, Ctrl+o/i, Ctrl+=/- 만 가로채고 나머지(Ctrl+S, Ctrl+Z 등)는 Grasshopper에 넘긴다
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
            Keys.OemOpenBrackets when shift => '{',
            Keys.OemCloseBrackets when shift => '}',
            Keys.OemPeriod when !shift => '.',
            Keys.Decimal => '.',
            Keys.OemQuestion when !shift => '/',
            Keys.Divide => '/',
            Keys.D8 when shift => '*',
            Keys.Multiply => '*',
            Keys.D3 when shift => '#',
            Keys.Space when !shift => KeyParser.Space,
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
        Keys.R => KeyParser.CtrlR,
        // Grasshopper의 Ctrl+O(파일 열기)는 Vim 모드에서 쓸 수 없다
        Keys.O => KeyParser.CtrlO,
        Keys.I => KeyParser.CtrlI,
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
        // 입력줄이 IME를 다시 끄므로 IME를 되돌리기 전에 닫는다
        CloseSearch();
        Ime.ImmAssociateContext(_canvas.Handle, _savedIme);
        _highlight.Release();
        ResetState();
        _canvas.Refresh();
    }

    void ResetState()
    {
        CloseSearch();
        HideHints();
        _parser.Reset();
        _anchor = null;
        _visualExtra.Clear();
        _visualHistory.Clear();
        _source = null;
        _opTarget = null;
        _status = "";
    }

    // ───────────────────────── 명령 실행 ─────────────────────────

    void Execute(Command command)
    {
        if (command is RepeatCommand repeat)
        {
            Repeat(repeat.Count);
            return;
        }
        _repeat.Record(command, Run(command));
    }

    /// <summary>
    /// <c>.</c>: 마지막 변경을 커서 객체에서 다시 실행한다. 무엇을 어떻게 되풀이할지는 <see cref="RepeatRegister"/>가 정한다.
    /// <c>i{n}</c>은 지금 고른 소스(<c>o{n}</c>)로 연결한다. 하나가 실패하면(고른 소스 없음 등) 뒤의 데이터 연산자까지 멈춘다.
    /// 여러 명령으로 된 변경(<c>i0f</c>, <c>o0fg</c>)은 Grasshopper undo 기록 하나로 합쳐서 <c>u</c> 한 번에 되돌린다.
    /// </summary>
    void Repeat(int? count)
    {
        var undo = Document?.UndoServer;
        var before = undo?.UndoCount ?? 0;
        foreach (var command in _repeat.Replay(count, CountParams))
            if (!Run(command))
                break;

        // 실행 중에 문서가 바뀌었거나 undo 기록이 최대 개수에 차 있으면 개수 차이가 맞지 않으므로 합치지 않는다
        var added = undo != null && undo == Document?.UndoServer ? undo.UndoCount - before : 0;
        if (added > 1)
            undo!.MergeRecords(added);
    }

    /// <returns>바꾸기 전에 실패하지 않았는지 (<see cref="RepeatRegister.Record"/>에 넘김). 이동 등 변경이 아닌 명령은 항상 true.</returns>
    bool Run(Command command)
    {
        // 비주얼 모드에서 선택을 바꾸는 명령은 바꾸기 전 상태를 기록해 둔다 (u로 되돌리기)
        var before = _anchor != null
            && command is MoveCommand or IslandMoveCommand or FollowParamCommand or JumpCommand or JumpBackCommand or JumpForwardCommand
                or ExtendVisualCommand or SwapVisualEndsCommand or SearchNextCommand or SearchTypeCommand
            ? CaptureVisual()
            : null;

        var ok = true;
        switch (command)
        {
            case MoveCommand move: Move(move.Direction, move.Count); break;
            case IslandMoveCommand island: MoveIsland(island.Forward, island.Count); break;
            case FollowParamCommand follow: Follow(follow.Side, follow.Index); break;
            case JumpCommand jump: Jump(jump.Direction); break;
            case JumpBackCommand back: JumpBack(back.Count); break;
            case JumpForwardCommand forward: JumpForward(forward.Count); break;
            case ZoomCommand zoom: Zoom(zoom.In); break;
            case PickOutputCommand pick: ok = PickOutput(pick.Index); break;
            case ConnectInputCommand connect: ok = ConnectInput(connect.Index, connect.Append); break;
            case ParamOpCommand op: ok = ApplyOp(op.Op); break;
            case TargetParamCommand target: ok = TargetParam(target.Side, target.Index); break;
            case UndoCommand: Undo(); break;
            case RedoCommand: Redo(); break;
            case InsertComponentCommand: InsertComponent(); break;
            case CancelCommand: _source = null; _opTarget = null; break;
            case CenterViewCommand: CenterOnSelected(); break;
            case FormatCommand format: ok = Format(format.Scope); break;
            case CopyCommand: Copy(); break;
            case CopyInfoCommand: CopyInfo(); break;
            case OperatorCommand op: ok = ApplyOperator(op.Operator, op.Range); break;
            case DeleteCommand: ok = Delete(); break;
            case ToggleEnabledCommand: ok = ToggleEnabled(); break;
            case DisconnectCommand disconnect: ok = Disconnect(disconnect.Side, disconnect.Index); break;
            case VisualCommand visual: SetVisual(visual.On); break;
            case SwapVisualEndsCommand: SwapVisualEnds(); break;
            case ExtendVisualCommand extend: ExtendVisual(extend.Upstream); break;
            case UndoSelectionCommand: UndoSelection(); break;
            case SearchPromptCommand prompt: OpenSearch(prompt.Count); break;
            case SearchNextCommand next: SearchAgain(next.Reverse, next.Count); break;
            case SearchTypeCommand type: SearchType(type.Forward, type.Count); break;
            case ErrorCommand error: _status = error.Message; break;
        }
        if (before != null && _anchor != null && !before.SameAs(CaptureVisual()))
            _visualHistory.Push(before);
        return ok;
    }

    /// <param name="count">반복 횟수. h/l은 갈 수 있는 데까지만 간다.</param>
    void Move(Direction direction, int count)
    {
        if (Selected is not { } current || Document is not { } doc)
        {
            SelectNearestToView();
            return;
        }

        var next = GraphNavigator.Move(DocumentGraph.Build(doc), current.InstanceGuid, direction, count);
        if (next is { } id && doc.FindObject(id, true) is { } obj)
        {
            // 반복 횟수를 붙인 이동(3l)만 점프로 친다 (Vimhopper 규칙. vim에서는 숫자 j/k가 점프가 아님)
            if (count > 1)
                RecordJump();
            Select(obj);
        }
        else
            _status = direction switch
            {
                Direction.Left => "부모 없음",
                Direction.Right => "자식 없음",
                _ => "같은 열에 더 없음",
            };
    }

    /// <summary><c>}</c> / <c>{</c>: 다음 / 이전 덩어리의 첫 객체로. 끝 덩어리면 움직이지 않고 알린다.</summary>
    void MoveIsland(bool forward, int count)
    {
        if (Selected is not { } current || Document is not { } doc)
        {
            SelectNearestToView();
            return;
        }

        var next = GraphNavigator.Island(DocumentGraph.Build(doc), current.InstanceGuid, forward ? count : -count);
        if (next is { } id && doc.FindObject(id, true) is { } obj)
        {
            RecordJump();
            Select(obj);
        }
        else
            _status = forward ? "다음 덩어리 없음" : "이전 덩어리 없음";
    }

    /// <summary>gi{n} / go{n}. 번호가 범위를 벗어나는 경우는 KeyParser가 먼저 걸러낸다.</summary>
    void Follow(ParamSide side, int index)
    {
        if (Selected is not { } current || Document is not { } doc)
        {
            SelectNearestToView();
            return;
        }

        var graph = DocumentGraph.Build(doc);
        var next = side == ParamSide.Input
            ? GraphNavigator.Parent(graph, current.InstanceGuid, index)
            : GraphNavigator.Child(graph, current.InstanceGuid, index);
        if (next is { } id && doc.FindObject(id, true) is { } obj)
        {
            RecordJump();
            Select(obj);
        }
        else
            _status = side == ParamSide.Input ? $"입력 {index}에 연결된 와이어 없음" : $"출력 {index}에 연결된 와이어 없음";
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

    /// <summary>
    /// 커서를 다른 곳으로 점프시키기 직전에 부른다: 지금 커서 위치를 점프 목록에 넣는다 (vim의 setpcmark).
    /// gi/go, 반복 횟수를 붙인 이동, 덩어리 이동({ }), Space Space로 넣은 컴포넌트로의 이동이 쓴다. 검색·마크도 쓸 것.
    /// hjkl, Ctrl+hjkl은 점프가 아니다.
    /// </summary>
    void RecordJump()
    {
        if (_current is { } id)
            _jumps.Record(id);
    }

    // 점프 목록의 위치가 아직 순회할 수 있는 객체인지 (지운 객체는 건너뛰고, u로 되살아나면 다시 쓴다)
    bool JumpTargetExists(Guid id) => Document?.FindObject(id, true) is IGH_ActiveObject;

    void JumpBack(int count)
    {
        if (_jumps.Back(_current, count, JumpTargetExists) is { } id && Document?.FindObject(id, true) is { } obj)
            Select(obj);
        else
            _status = "점프 목록의 처음입니다";
    }

    void JumpForward(int count)
    {
        if (_jumps.Forward(count, JumpTargetExists) is { } id && Document?.FindObject(id, true) is { } obj)
            Select(obj);
        else
            _status = "점프 목록의 끝입니다";
    }

    void Zoom(bool zoomIn)
    {
        _canvas.Navigate(zoomIn ? GH_CanvasNavigation.ZoomIn : GH_CanvasNavigation.ZoomOut);
        if (Selected is { } obj)
            EnsureVisible(obj);
    }

    bool PickOutput(int index)
    {
        if (Selected is not { } obj) return false;
        _source = Params(obj, ParamSide.Output)[index];
        _opTarget = _source;
        _status = $"source: {Describe(_source)}";
        return true;
    }

    /// <returns>연결했는지.</returns>
    bool ConnectInput(int index, bool append)
    {
        if (Selected is not { } obj || Document is not { } doc) return false;
        var target = Params(obj, ParamSide.Input)[index];
        _opTarget = target;

        if (_source == null)
        {
            _status = "먼저 o{n}으로 출력을 고르세요";
            return false;
        }
        if (_source.Attributes.GetTopLevel.DocObject == target.Attributes.GetTopLevel.DocObject)
        {
            _status = "같은 객체끼리는 연결할 수 없습니다";
            return false;
        }

        doc.UndoUtil.RecordWireEvent("Vimhopper wire", target);
        if (!append)
            target.RemoveAllSources();
        target.AddSource(_source);
        target.ExpireSolution(true);

        _status = $"{Describe(_source)} → {Describe(target)}";
        _source = null;
        return true;
    }

    /// <summary><c>.</c>로 <c>o{n}{ops}</c>를 되풀이할 때: 소스는 그대로 두고 데이터 연산자의 대상만 정한다.</summary>
    bool TargetParam(ParamSide side, int index)
    {
        if (Selected is not { } obj) return false;
        _opTarget = Params(obj, side)[index];
        return true;
    }

    bool ApplyOp(ParamOp op)
    {
        if (_opTarget is not { } param || Document is not { } doc) return false;
        // 바꾸지 못하는 경우는 undo를 기록하기 전에 거른다
        if (op == ParamOp.Invert && param is not Param_Boolean)
        {
            _status = "invert는 Boolean 파라미터에만 적용됩니다";
            return false;
        }

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
                var boolean = (Param_Boolean)param;
                boolean.Invert = !boolean.Invert;
                break;
        }

        // 출력 쪽 플래그는 컴포넌트가 다시 계산해야 반영되므로 최상위 객체를 만료시킨다
        param.Attributes.GetTopLevel.DocObject.ExpireSolution(true);
        _status = $"{Describe(param)}: {op}";
        return true;
    }

    bool Format(FormatScope scope)
    {
        if (Document is not { } doc) return false;

        List<IGH_DocumentObject> targets;
        if (scope == FormatScope.Island)
        {
            if (Selected is not { } obj)
            {
                _status = "선택된 객체가 없습니다";
                return false;
            }
            targets = Island(obj);
            if (targets.Count < 2)
            {
                _status = "연결된 객체가 없습니다";
                return false;
            }
        }
        else if (scope == FormatScope.Selection)
        {
            targets = doc.SelectedObjects().OfType<IGH_ActiveObject>().Cast<IGH_DocumentObject>().ToList();
            if (targets.Count < 2)
            {
                _status = "정렬할 객체가 2개 이상이어야 합니다";
                return false;
            }
        }
        else
        {
            targets = doc.Objects.OfType<IGH_ActiveObject>().Cast<IGH_DocumentObject>().ToList();
        }

        // 선택 범위는 여러 덩어리에 걸칠 수 있으므로 문서 전체 정렬처럼 덩어리별로 나눈다
        FormatObjects(doc, targets, separateIslands: scope != FormatScope.Island);
        return true;
    }

    void FormatObjects(GH_Document doc, List<IGH_DocumentObject> targets, bool separateIslands)
    {
        var result = Formatter.Format(doc, targets, separateIslands);
        _status = result.Changed
            ? $"정렬: {result.Moved}개 이동, Relay +{result.RelaysAdded} -{result.RelaysRemoved}"
            : "이미 정렬되어 있습니다";

        // 선택했던 Relay가 정렬로 지워졌을 수 있다
        if (Selected is { } selected)
            EnsureVisible(selected);
        else
            SelectNearestToView();
    }

    /// <summary>와이어 방향과 무관하게 obj와 이어진 객체 전부 (<c>p</c> 범위와 같음).</summary>
    List<IGH_DocumentObject> Island(IGH_DocumentObject obj)
    {
        if (Document is not { } doc) return new List<IGH_DocumentObject>();
        return GraphRange.Island(DocumentGraph.Build(doc), obj.InstanceGuid)
            .Select(id => doc.FindObject(id, true))
            .OfType<IGH_DocumentObject>()
            .ToList();
    }

    /// <summary>
    /// "파일경로:InstanceGuid,InstanceGuid,..." 한 줄을 클립보드에 복사한다 (에디터의 "파일:줄"처럼).
    /// 저장한 적 없는 문서면 GUID만. 경로 구분자는 /로 통일한다.
    /// 마우스로 여러 개를 고른 경우도 있으므로 Vim 선택이 아니라 Grasshopper 선택을 쓴다.
    /// </summary>
    void CopyInfo()
    {
        if (Document is { } doc)
            CopyReferences(doc, doc.SelectedObjects());
    }

    void CopyReferences(GH_Document doc, IEnumerable<IGH_DocumentObject> selection)
    {
        var objects = selection
            .OrderBy(o => o.Attributes.Bounds.X)
            .ThenBy(o => o.Attributes.Bounds.Y)
            .ToList();
        if (objects.Count == 0)
        {
            _status = "선택된 객체가 없습니다";
            return;
        }

        var prefix = string.IsNullOrEmpty(doc.FilePath) ? "" : doc.FilePath.Replace('\\', '/') + ":";
        Clipboard.SetText(prefix + string.Join(",", objects.Select(o => o.InstanceGuid)));
        _status = objects.Count == 1 ? $"ref: {objects[0].NickName}" : $"ref: {objects.Count} objects";
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
        CopyObjects(doc, ids);
    }

    void CopyObjects(GH_Document doc, IReadOnlyList<Guid> ids)
    {
        if (ids.Count == 0)
        {
            _status = "선택된 객체가 없습니다";
            return;
        }

        _status = new GH_DocumentIO(doc).Copy(GH_ClipboardType.System, ids.ToList())
            ? ids.Count == 1 ? $"copied {doc.FindObject(ids[0], true)?.NickName}" : $"copied {ids.Count} objects"
            : "복사하지 못했습니다";
    }

    /// <summary><c>y{범위}</c> / <c>={범위}</c>: 커서 객체에서 상류·하류로 이어진 객체들에 적용한다.</summary>
    bool ApplyOperator(Operator op, WireRange range)
    {
        if (Selected is not { } current || Document is not { } doc)
        {
            _status = "선택된 객체가 없습니다";
            return false;
        }

        var ids = GraphRange.Collect(DocumentGraph.Build(doc), current.InstanceGuid, range);
        var targets = ids.Select(id => doc.FindObject(id, true)).OfType<IGH_DocumentObject>().ToList();
        switch (op)
        {
            case Operator.Yank:
                CopyObjects(doc, ids);
                return true;
            case Operator.Delete:
                return DeleteObjects(doc, targets);
            case Operator.Reference:
                CopyReferences(doc, targets);
                return true;
            case Operator.Disable:
                return ToggleEnabled(doc, targets);
        }

        if (ids.Count < 2)
        {
            _status = range.Kind == RangeKind.Island ? "연결된 객체가 없습니다" : range.Upstream ? "부모 없음" : "자식 없음";
            return false;
        }
        // =p는 ==와 같다: 덩어리 하나를 통째로 정렬
        FormatObjects(doc, targets, separateIslands: false);
        return true;
    }

    /// <summary>비주얼 모드면 선택된 객체들, 아니면 커서 객체 (<c>dd</c>, <c>gcc</c>와 비주얼 <c>d</c>, <c>gc</c>의 대상).</summary>
    List<IGH_DocumentObject> Targets()
    {
        if (_anchor != null && Document is { } doc)
            return doc.SelectedObjects().ToList();
        return Selected is { } obj ? new List<IGH_DocumentObject> { obj } : new List<IGH_DocumentObject>();
    }

    bool Delete() => Document is { } doc && DeleteObjects(doc, Targets());

    /// <summary>
    /// 객체를 지우고 커서를 지운 자리에서 가장 가까운 객체로 옮긴다. 남는 객체로 가던 와이어도 함께 기록해서
    /// <c>u</c> 한 번에 객체와 와이어가 모두 돌아오게 한다.
    /// </summary>
    bool DeleteObjects(GH_Document doc, List<IGH_DocumentObject> objects)
    {
        if (objects.Count == 0)
        {
            _status = "선택된 객체가 없습니다";
            return false;
        }

        const string name = "Vimhopper delete";
        var removed = new HashSet<Guid>(objects.Select(o => o.InstanceGuid));
        var recipients = objects
            .SelectMany(o => Params(o, ParamSide.Output))
            .SelectMany(p => p.Recipients)
            .Where(r => !removed.Contains(r.Attributes.GetTopLevel.DocObject.InstanceGuid))
            .Distinct()
            .ToList();
        var actions = new List<IGH_UndoAction>();
        if (recipients.Count > 0)
            actions.AddRange(doc.UndoUtil.CreateWireEvent(name, recipients).Actions);
        actions.AddRange(doc.UndoUtil.CreateRemoveObjectEvent(name, objects).Actions);
        doc.UndoUtil.RecordEvent(name, actions);

        var cursor = (Selected ?? objects[0]).Attributes.Bounds;
        doc.RemoveObjects(objects, true);

        _anchor = null;
        _visualExtra.Clear();
        _visualHistory.Clear();
        _current = null;
        _source = null;
        _opTarget = null;
        var mid = new PointF(cursor.X + cursor.Width / 2, cursor.Y + cursor.Height / 2);
        if (Navigator.Nearest(Boxes(), mid.X, mid.Y) is { } id && doc.FindObject(id, true) is { } next)
            Select(next);
        _status = objects.Count == 1 ? $"deleted {objects[0].NickName}" : $"deleted {objects.Count} objects";
        return true;
    }

    bool ToggleEnabled() => Document is { } doc && ToggleEnabled(doc, Targets());

    /// <summary>
    /// Grasshopper의 Enable/Disable. 켜진 것과 꺼진 것이 섞여 있으면 vim의 <c>gc</c>처럼 모두 같은 상태로:
    /// 하나라도 켜져 있으면 모두 끄고, 모두 꺼져 있으면 모두 켠다.
    /// </summary>
    bool ToggleEnabled(GH_Document doc, List<IGH_DocumentObject> objects)
    {
        var active = objects.OfType<IGH_ActiveObject>().ToList();
        if (active.Count == 0)
        {
            _status = "선택된 객체가 없습니다";
            return false;
        }

        const string name = "Vimhopper enable";
        var disable = active.Any(o => !o.Locked);
        var changed = active.Where(o => o.Locked != disable).ToList();
        doc.UndoUtil.RecordEvent(name, changed.SelectMany(o => doc.UndoUtil.CreateGenericObjectEvent(name, o).Actions).ToList());
        foreach (var o in changed)
        {
            o.Locked = disable;
            o.ExpireSolution(false);
        }
        doc.NewSolution(false);
        _status = $"{(disable ? "disabled" : "enabled")} {(changed.Count == 1 ? changed[0].NickName : $"{changed.Count} objects")}";
        return true;
    }

    /// <summary><c>di{n}</c> / <c>do{n}</c>: 그 파라미터의 와이어를 모두 끊는다.</summary>
    bool Disconnect(ParamSide side, int index)
    {
        if (Selected is not { } obj || Document is not { } doc) return false;
        const string name = "Vimhopper disconnect";
        var param = Params(obj, side)[index];

        if (side == ParamSide.Input)
        {
            if (param.Sources.Count == 0)
            {
                _status = $"입력 {index}에 연결된 와이어 없음";
                return false;
            }
            doc.UndoUtil.RecordWireEvent(name, param);
            param.RemoveAllSources();
            param.ExpireSolution(true);
            _status = $"{Describe(param)}: disconnected";
            return true;
        }

        var recipients = param.Recipients.ToList();
        if (recipients.Count == 0)
        {
            _status = $"출력 {index}에 연결된 와이어 없음";
            return false;
        }
        doc.UndoUtil.RecordEvent(name, doc.UndoUtil.CreateWireEvent(name, recipients).Actions);
        foreach (var r in recipients)
        {
            r.RemoveSource(param);
            r.ExpireSolution(false);
        }
        doc.NewSolution(false);
        _status = $"{Describe(param)}: {recipients.Count} wires disconnected";
        return true;
    }

    /// <summary>
    /// Space Space: 커서 객체 오른쪽에 Grasshopper 컴포넌트 검색창을 띄우고, 놓인 컴포넌트로 커서를 옮긴다.
    /// <c>o{n}</c>로 출력을 골라 둔 채 넣으면 이어서 <c>i{n}</c>으로 바로 연결할 수 있다.
    /// </summary>
    void InsertComponent()
    {
        if (Document is not { } doc) return;

        PointF at;
        if (Selected is { } obj)
        {
            var b = obj.Attributes.Bounds;
            at = new PointF(b.Right + InsertGap, b.Y + b.Height / 2);
        }
        else
        {
            at = _canvas.Viewport.MidPoint;
        }
        var screen = _canvas.PointToScreen(Point.Round(_canvas.Viewport.ProjectPoint(at)));

        StartWatchingInsert(doc);
        // 키 메시지 처리가 끝난 뒤에 띄운다. 처리 중에 띄우면 Space의 WM_CHAR가 삼켜지기 전에 검색창이 열린다
        _canvas.BeginInvoke(new Action(() =>
        {
            if (_canvas.ShowComponentSearchBox(screen)) return;
            StopWatchingInsert();
            _status = "검색창을 열 수 없습니다";
            _canvas.Refresh();
        }));
    }

    void StartWatchingInsert(GH_Document doc)
    {
        StopWatchingInsert();
        _insertDoc = doc;
        doc.ObjectsAdded += OnObjectsAdded;
    }

    void StopWatchingInsert()
    {
        if (_insertDoc == null) return;
        _insertDoc.ObjectsAdded -= OnObjectsAdded;
        _insertDoc = null;
    }

    void OnObjectsAdded(object sender, GH_DocObjectEventArgs e)
    {
        var added = e.Objects.OfType<IGH_ActiveObject>().FirstOrDefault();
        StopWatchingInsert();
        if (added == null) return;
        RecordJump();
        Select(added);
        _status = $"inserted {added.NickName}";
        _canvas.Refresh();
    }

    void Undo()
    {
        if (Document is not { } doc) return;
        doc.Undo();
        AfterHistory();
    }

    void Redo()
    {
        if (Document is not { } doc) return;
        doc.Redo();
        AfterHistory();
    }

    // 되돌린 작업이 커서 객체를 없앴을 수 있다 (삭제를 다시 실행하거나 Relay 추가를 되돌린 경우)
    void AfterHistory()
    {
        _source = null;
        _opTarget = null;
        if (Selected == null)
            SelectNearestToView();
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

    /// <summary>
    /// 커서를 obj로 옮긴다. 비주얼 모드면 시작점부터 커서까지와 H/L로 더한 객체들을, 아니면 obj만 선택한다.
    /// </summary>
    void Select(IGH_DocumentObject obj)
    {
        _current = obj.InstanceGuid;
        Document?.DeselectAll();
        if (_anchor is { } anchor && Document is { } doc)
        {
            foreach (var id in VisualRange.Select(Boxes(), anchor, obj.InstanceGuid).Concat(_visualExtra))
                if (doc.FindObject(id, true) is { } selected)
                    selected.Attributes.Selected = true;
        }
        else
        {
            obj.Attributes.Selected = true;
        }
        EnsureVisible(obj);
    }

    /// <summary>비주얼 선택을 다시 만들 수 있는 상태: 두 끝과 H/L로 더한 객체들.</summary>
    sealed class VisualState
    {
        public VisualState(Guid? cursor, Guid? anchor, IEnumerable<Guid> extra)
        {
            Cursor = cursor;
            Anchor = anchor;
            Extra = new HashSet<Guid>(extra);
        }

        public Guid? Cursor { get; }
        public Guid? Anchor { get; }
        public HashSet<Guid> Extra { get; }

        public bool SameAs(VisualState other) =>
            Cursor == other.Cursor && Anchor == other.Anchor && Extra.SetEquals(other.Extra);
    }

    VisualState CaptureVisual() => new(_current, _anchor, _visualExtra);

    /// <summary>
    /// 비주얼 모드의 u: 선택을 한 단계 전으로. h/l은 서로의 역이 아니라서(l로 간 자식의 맨 위 입력이 원래 객체가
    /// 아닐 수 있음) 커서를 되돌려서는 원래 선택으로 못 돌아가는 경우가 있으므로, 지나온 상태를 직접 되살린다.
    /// </summary>
    void UndoSelection()
    {
        if (_visualHistory.Count == 0 || Document is not { } doc)
        {
            _status = "되돌릴 선택이 없습니다";
            return;
        }

        var state = _visualHistory.Pop();
        _anchor = state.Anchor;
        _visualExtra.Clear();
        _visualExtra.UnionWith(state.Extra);
        if (state.Cursor is { } id && doc.FindObject(id, true) is { } obj)
            Select(obj);
    }

    void SetVisual(bool on)
    {
        if (on && Selected == null)
            SelectNearestToView();
        _anchor = on ? _current : null;
        _visualExtra.Clear();
        _visualHistory.Clear();
        if (Selected is { } obj)
            Select(obj);
    }

    /// <summary>비주얼 모드의 H/L: 지금 선택된 객체들 전부의 상류/하류 전체를 선택에 더한다.</summary>
    void ExtendVisual(bool upstream)
    {
        if (Selected is not { } current || Document is not { } doc) return;
        var starts = doc.SelectedObjects().Select(o => o.InstanceGuid).ToList();
        _visualExtra.UnionWith(GraphRange.Collect(DocumentGraph.Build(doc), starts, upstream, depth: null));
        Select(current);
        var count = doc.SelectedObjects().Count;
        _status = count == starts.Count ? (upstream ? "더할 상류 없음" : "더할 하류 없음") : $"{count} selected";
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

        // 입력줄이 열려 있으면 vim의 명령줄처럼 모드 표시줄 자리에
        if (_input.Active)
        {
            _input.Draw(g, 8, _canvas.ClientSize.Height - 8, _search?.Info ?? "");
            g.Transform = saved;
            return;
        }

        var size = g.MeasureString(text, _hudFont);
        var rect = new RectangleF(8, _canvas.ClientSize.Height - size.Height - 12, size.Width + 12, size.Height + 4);
        using (var back = new SolidBrush(Color.FromArgb(200, 30, 30, 30)))
            g.FillRectangle(back, rect);
        g.DrawString(text, _hudFont, Brushes.White, rect.X + 6, rect.Y + 2);

        if (_showHints)
            DrawHints(g, rect.X, rect.Y - 6);

        g.Transform = saved;
    }

    // ───────────────────────── 다음 키 힌트 (which-key) ─────────────────────────

    // 입력 중인 명령이 있으면 HintDelayMs 뒤에 힌트를 띄운다
    void ScheduleHints()
    {
        _hintTimer.Stop();
        if (_vim && _parser.Hints.Count > 0)
            _hintTimer.Start();
    }

    void HideHints()
    {
        _hintTimer.Stop();
        if (!_showHints) return;
        _showHints = false;
        _canvas.Invalidate();
    }

    void OnHintTimer(object? sender, EventArgs e)
    {
        _hintTimer.Stop();
        if (!_vim || _parser.Hints.Count == 0) return;
        _showHints = true;
        _canvas.Refresh();
    }

    /// <summary>
    /// 모드 표시줄 바로 위에 다음 키 목록을 그린다 (화면 좌표라 줌과 무관하게 같은 크기).
    /// 키는 주황, 설명은 흰색. 줄이 많으면 열을 나눈다.
    /// </summary>
    void DrawHints(Graphics g, float left, float bottom)
    {
        var hints = _parser.Hints.Append(new KeyHint("<Esc>", "취소")).ToList();
        const float pad = 6, gap = 8, columnGap = 18;

        var keyWidth = hints.Max(h => g.MeasureString(h.Keys, _hudFont).Width);
        var descWidth = hints.Max(h => g.MeasureString(h.Description, _hintFont).Width);
        var rowHeight = Math.Max(_hudFont.GetHeight(g), _hintFont.GetHeight(g)) + 2;
        var rows = Math.Min(hints.Count, HintRowsPerColumn);
        var columns = (hints.Count + HintRowsPerColumn - 1) / HintRowsPerColumn;
        var cellWidth = keyWidth + gap + descWidth;

        var width = columns * cellWidth + (columns - 1) * columnGap + pad * 2;
        var height = rows * rowHeight + pad * 2;
        var rect = new RectangleF(left, bottom - height, width, height);
        using (var back = new SolidBrush(Color.FromArgb(220, 30, 30, 30)))
            g.FillRectangle(back, rect);

        using var keyBrush = new SolidBrush(Color.Orange);
        using var escBrush = new SolidBrush(Color.Gray);
        for (var i = 0; i < hints.Count; i++)
        {
            var x = rect.X + pad + i / HintRowsPerColumn * (cellWidth + columnGap);
            var y = rect.Y + pad + i % HintRowsPerColumn * rowHeight;
            var last = i == hints.Count - 1;
            g.DrawString(hints[i].Keys, _hudFont, last ? escBrush : keyBrush, x, y);
            g.DrawString(hints[i].Description, _hintFont, last ? escBrush : Brushes.White, x + keyWidth + gap, y + 1);
        }
    }
}
