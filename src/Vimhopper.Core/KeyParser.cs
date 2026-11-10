using System;
using System.Collections.Generic;
using System.Linq;

namespace Vimhopper.Core;

/// <summary>
/// Vim 모드 키 입력을 명령으로 바꾸는 상태 머신.
/// </summary>
/// <remarks>
/// 문법: <c>{n}hjkl</c> 연결 따라 n번 이동, <c>{n}{</c> / <c>{n}}</c> 이전 / 다음 덩어리로, <c>gi{n}</c> / <c>go{n}</c> 입력 n의 출처 / 출력 n을 받는 객체로 이동,
/// <c>Ctrl+hjkl</c> 화면 방향 이동, <c>{n}Ctrl+o</c> / <c>{n}Ctrl+i</c> 점프 목록 뒤로 / 앞으로, <c>Ctrl+=</c>/<c>Ctrl+-</c> 줌, <c>u</c> / <c>Ctrl+r</c> 실행 취소 / 다시 실행,
/// <c>zz</c> 커서 객체를 가운데로, <c>o{n}{ops}</c> 출력 선택, <c>i{n}{ops}</c> / <c>I{n}{ops}</c> 입력 연결, <c>g=</c> 문서 전체 정렬.
/// 연산자 <c>y</c> 복사, <c>gy</c> 참조 복사, <c>=</c> 정렬, <c>d</c> 삭제, <c>gc</c> 켜기/끄기, <c>z</c> 화면에 맞춤 뒤에는 범위가 온다:
/// <c>{n}h</c> / <c>{n}l</c> 상류 / 하류 n단계, <c>H</c> / <c>L</c> 끝까지, <c>p</c> 덩어리 전체(숫자 무시),연산자 두 번(<c>yy</c>, <c>dd</c>, <c>gcc</c>)은 커서 객체, <c>gyy</c>는 Grasshopper 선택
/// (<c>==</c>만 덩어리). 앞뒤 숫자는 vim처럼 곱한다 (<c>2y3l</c> = 6단계). <c>di{n}</c> / <c>do{n}</c>는 와이어 끊기.
/// <c>t{라벨}</c> 라벨 점프 (화면에 보이는 객체로, 숫자 무시). 연산자 뒤 <c>t{라벨}</c>과 <c>'{a-z}</c>는 커서~대상 블록 범위.
/// <c>Space</c>는 리더 키: <c>Space Space</c> 컴포넌트 넣기 (Grasshopper 검색창).
/// <c>m{a-z}</c> 마크 달기, <c>'{a-z}</c> 마크로 점프, <c>p</c> / <c>P</c> 커서 오른쪽 / 왼쪽에 붙여넣기 (숫자 무시).
/// <c>.</c> / <c>{n}.</c> 마지막 변경 되풀이 (<see cref="RepeatRegister"/>).
/// <c>{n}/</c> 찾기 입력줄, <c>{n}n</c> / <c>{n}N</c> 마지막 찾기 되풀이, <c>{n}*</c> / <c>{n}#</c> 같은 종류 다음 / 이전 (<see cref="Search"/>).
/// <c>o{n}</c> / <c>i{n}</c> 바로 뒤의 <c>n</c>은 찾기가 아니라 Invert다.
/// <c>v</c> 비주얼 모드: <c>o</c> 양 끝 바꾸기, <c>H</c>/<c>L</c> 상류/하류 더하기, <c>u</c> 선택 한 단계 되돌리기,
/// 연산자는 범위 없이 선택에 바로 적용 (<c>z</c>만 비주얼 모드에 머문다).
/// 인덱스는 0부터 시작한다. 숫자를 더 붙여도 유효한 인덱스가 될 수 없으면 즉시 확정하고
/// (파라미터가 10개 이하면 한 자리에서 바로 확정), 그렇지 않으면 다음 키나 Enter에서 확정한다.
/// </remarks>
public sealed partial class KeyParser
{
    public const char Escape = '\x1b';
    public const char Enter = '\r';
    public const char Backspace = '\b';
    public const char Space = ' ';

    // Ctrl 조합은 대응하는 문자가 없으므로 유니코드 사용자 정의 영역의 값으로 표현
    public const char CtrlH = '';
    public const char CtrlJ = '';
    public const char CtrlK = '';
    public const char CtrlL = '';
    public const char CtrlPlus = '';
    public const char CtrlMinus = '';
    public const char CtrlR = '';
    public const char CtrlO = '';
    public const char CtrlI = '';

    enum State { Idle, AwaitIndex, AfterIndex, AwaitSecond, Count, AwaitRange, RangeCount, Leader, AwaitLabel, AwaitMark, AwaitMarkJump, AwaitRangeMark }

    // 이동 앞 숫자의 최대 자릿수 (int 넘침 방지)
    const int MaxCountDigits = 4;

    readonly Func<ParamSide, int> _paramCount;
    State _state = State.Idle;
    ParamSide _side;
    bool _append;
    // 인덱스를 받는 명령의 종류: gi/go 이동, di/do 끊기, 아니면 연결
    bool _follow;
    bool _disconnect;
    string _digits = "";
    string _count = "";
    Operator _operator;
    // 연산자 앞에 붙인 반복 횟수 (2yl의 2)
    int _operatorCount = 1;
    // t 라벨 점프: Engine이 정해 준 라벨, 지금까지 친 글자, 연산자 뒤(dt)인지, HUD에서 t 앞에 보일 키(2d, y2).
    // _labelPrefix는 연산자 뒤 '(d'a)의 HUD에도 쓴다
    IReadOnlyDictionary<string, Guid> _labels = EmptyLabels;
    string _labelTyped = "";
    bool _labelOperator;
    string _labelPrefix = "";
    static readonly IReadOnlyDictionary<string, Guid> EmptyLabels = new Dictionary<string, Guid>();

    /// <param name="paramCount">현재 선택된 객체의 출력/입력 개수를 돌려주는 함수.</param>
    public KeyParser(Func<ParamSide, int> paramCount) => _paramCount = paramCount;

    /// <summary>HUD에 표시할 입력 중인 키 (예: "o1", "I", "g", "gi", "2y3").</summary>
    public string Pending => _state switch
    {
        State.AwaitIndex => Prefix + _digits,
        State.AwaitSecond => (_operatorCount > 1 ? _operatorCount.ToString() : "") + "g",
        State.Count => _count,
        State.AwaitRange => OperatorPrefix,
        State.RangeCount => OperatorPrefix + _count,
        State.Leader => "<Space>",
        State.AwaitLabel => _labelPrefix + "t" + _labelTyped,
        State.AwaitMark => "m",
        State.AwaitMarkJump => "'",
        State.AwaitRangeMark => _labelPrefix + "'",
        _ => "",
    };

    string OperatorPrefix => (_operatorCount > 1 ? _operatorCount.ToString() : "") + Label(_operator);

    string Prefix =>
        (_follow ? "g" : _disconnect ? "d" : "") + (_side == ParamSide.Output ? "o" : _append ? "I" : "i");

    static string Label(Operator op) => op switch
    {
        Operator.Yank => "y",
        Operator.Format => "=",
        Operator.Delete => "d",
        Operator.Reference => "gy",
        Operator.Zoom => "z",
        _ => "gc",
    };

    /// <summary>비주얼 모드인지. <c>v</c>로 들어가고 <c>v</c>, <c>Esc</c>, 또는 연산자 실행 후 나온다.</summary>
    public bool Visual { get; private set; }

    /// <summary>입력 중인 명령과 비주얼 모드를 모두 지운다 (Vim 모드를 나가거나 문서가 바뀔 때).</summary>
    public void Reset()
    {
        ClearPending();
        Visual = false;
    }

    void ClearPending()
    {
        _state = State.Idle;
        _digits = "";
        _count = "";
        _operatorCount = 1;
        _labels = EmptyLabels;
        _labelTyped = "";
        _labelOperator = false;
        _labelPrefix = "";
    }

    public IReadOnlyList<Command> Feed(char key)
    {
        var output = new List<Command>();
        Process(key, output);
        return output;
    }

    void Process(char key, List<Command> output)
    {
        switch (_state)
        {
            case State.AwaitIndex:
                ProcessIndexKey(key, output);
                return;

            case State.AwaitSecond:
                ProcessSecondKey(key, output);
                return;

            case State.Count:
                ProcessCountKey(key, output);
                return;

            case State.AwaitRange:
                ProcessRangeKey(key, output);
                return;

            case State.RangeCount:
                ProcessRangeCountKey(key, output);
                return;

            case State.Leader:
                ProcessLeaderKey(key, output);
                return;

            case State.AwaitLabel:
                ProcessLabelKey(key, output);
                return;

            case State.AwaitMark:
            case State.AwaitMarkJump:
            case State.AwaitRangeMark:
                ProcessMarkKey(key, output);
                return;

            case State.AfterIndex:
                if (TryParseOp(key, out var op))
                {
                    output.Add(new ParamOpCommand(_side, op));
                    return;
                }
                _state = State.Idle;
                break;
        }

        ProcessIdleKey(key, output);
    }

    void ProcessIndexKey(char key, List<Command> output)
    {
        if (key >= '0' && key <= '9')
        {
            _digits += key;
            if (IsComplete())
                Commit(output);
            return;
        }

        switch (key)
        {
            case Escape:
                ClearPending();
                output.Add(new CancelCommand());
                return;
            case Backspace:
                if (_digits.Length > 0)
                    _digits = _digits.Substring(0, _digits.Length - 1);
                else
                    ClearPending();
                return;
        }

        if (_digits.Length == 0)
        {
            ClearPending();
            output.Add(new ErrorCommand($"{Prefix} 다음에는 숫자가 와야 합니다"));
            return;
        }

        Commit(output);
        if (key != Enter)
            Process(key, output);
    }

    void ProcessIdleKey(char key, List<Command> output)
    {
        if (Visual && ProcessVisualKey(key, output))
            return;

        switch (key)
        {
            case 'h': output.Add(new MoveCommand(Direction.Left)); break;
            case 'j': output.Add(new MoveCommand(Direction.Down)); break;
            case 'k': output.Add(new MoveCommand(Direction.Up)); break;
            case 'l': output.Add(new MoveCommand(Direction.Right)); break;
            case '{': output.Add(new IslandMoveCommand(Forward: false)); break;
            case '}': output.Add(new IslandMoveCommand(Forward: true)); break;
            case CtrlH: output.Add(new JumpCommand(Direction.Left)); break;
            case CtrlJ: output.Add(new JumpCommand(Direction.Down)); break;
            case CtrlK: output.Add(new JumpCommand(Direction.Up)); break;
            case CtrlL: output.Add(new JumpCommand(Direction.Right)); break;
            case CtrlPlus: output.Add(new ZoomCommand(In: true)); break;
            case CtrlMinus: output.Add(new ZoomCommand(In: false)); break;
            case 'o': BeginIndex(ParamSide.Output, output); break;
            case 'i': BeginIndex(ParamSide.Input, output); break;
            case 'I': BeginIndex(ParamSide.Input, output, append: true); break;
            case 'u': output.Add(new UndoCommand()); break;
            case CtrlR: output.Add(new RedoCommand()); break;
            case CtrlO: output.Add(new JumpBackCommand()); break;
            case CtrlI: output.Add(new JumpForwardCommand()); break;
            case 'z': BeginOperator(Operator.Zoom); break;
            case 't': BeginLabel(output, ""); break;
            case 'v':
                Visual = true;
                output.Add(new VisualCommand(true));
                break;
            case 'y': BeginOperator(Operator.Yank); break;
            case '=': BeginOperator(Operator.Format); break;
            case 'd': BeginOperator(Operator.Delete); break;
            case 'g': _state = State.AwaitSecond; break;
            case '.': output.Add(new RepeatCommand()); break;
            case '/': output.Add(new SearchPromptCommand()); break;
            case 'n': output.Add(new SearchNextCommand(Reverse: false)); break;
            case 'N': output.Add(new SearchNextCommand(Reverse: true)); break;
            case '*': output.Add(new SearchTypeCommand(Forward: true)); break;
            case '#': output.Add(new SearchTypeCommand(Forward: false)); break;
            case Space: _state = State.Leader; break;
            case 'm': _state = State.AwaitMark; break;
            case '\'': _state = State.AwaitMarkJump; break;
            case 'p': output.Add(new PasteCommand(Before: false)); break;
            case 'P': output.Add(new PasteCommand(Before: true)); break;
            // vim처럼 0으로는 반복 횟수를 시작하지 않는다
            case >= '1' and <= '9':
                _state = State.Count;
                _count = key.ToString();
                break;
            case Escape: output.Add(new CancelCommand()); break;
        }
    }

    // 비주얼 모드에서 뜻이 달라지는 키. 처리했으면 true, 나머지(이동, u, z, g 등)는 Vim 모드와 같다
    bool ProcessVisualKey(char key, List<Command> output)
    {
        switch (key)
        {
            case 'v' or Escape:
                break;
            case 'o':
                output.Add(new SwapVisualEndsCommand());
                return true;
            case 'H' or 'L':
                output.Add(new ExtendVisualCommand(Upstream: key == 'H'));
                return true;
            // vim의 비주얼 u(소문자로)는 Grasshopper에서 뜻이 없으므로 선택을 되돌리는 데 쓴다
            case 'u':
                output.Add(new UndoSelectionCommand());
                return true;
            // 연산자는 범위를 기다리지 않고 선택에 바로 적용된다
            case 'y':
                output.Add(new CopyCommand());
                break;
            case '=':
                output.Add(new FormatCommand(FormatScope.Selection));
                break;
            case 'd':
                output.Add(new DeleteCommand());
                break;
            case 'i' or 'I':
                output.Add(new ErrorCommand("비주얼 모드에서는 연결할 수 없습니다"));
                return true;
            // 비주얼 p / P는 아직 비어 있다 (선택을 바꿔 넣기는 정하지 않음)
            case 'p' or 'P':
                return true;
            // 되풀이는 커서 객체에서 하므로 비주얼 모드에서는 막는다
            case '.':
                output.Add(new ErrorCommand("비주얼 모드에서는 .을 쓸 수 없습니다"));
                return true;
            // 줌은 문서를 바꾸지 않으므로 선택을 이어 다듬을 수 있게 비주얼 모드에 머문다
            case 'z':
                output.Add(new FitSelectionCommand());
                return true;
            default:
                return false;
        }

        LeaveVisual(output);
        return true;
    }

    // vim처럼 동작을 실행하면 비주얼 모드를 나간다
    void LeaveVisual(List<Command> output)
    {
        if (!Visual) return;
        Visual = false;
        output.Add(new VisualCommand(false));
    }

    // 이동 앞 반복 횟수 ({n}h 등), 또는 연산자 앞 반복 횟수 ({n}yl 등)
    void ProcessCountKey(char key, List<Command> output)
    {
        if (key >= '0' && key <= '9')
        {
            if (_count.Length < MaxCountDigits)
                _count += key;
            return;
        }
        if (key == Backspace)
        {
            _count = _count.Substring(0, _count.Length - 1);
            if (_count.Length == 0)
                ClearPending();
            return;
        }

        var count = int.Parse(_count);
        var label = _count;
        ClearPending();
        switch (key)
        {
            case 'h': output.Add(new MoveCommand(Direction.Left, count)); return;
            case 'j': output.Add(new MoveCommand(Direction.Down, count)); return;
            case 'k': output.Add(new MoveCommand(Direction.Up, count)); return;
            case 'l': output.Add(new MoveCommand(Direction.Right, count)); return;
            case CtrlO: output.Add(new JumpBackCommand(count)); return;
            case CtrlI: output.Add(new JumpForwardCommand(count)); return;
            case '{': output.Add(new IslandMoveCommand(Forward: false, count)); return;
            case '}': output.Add(new IslandMoveCommand(Forward: true, count)); return;
            // 라벨 점프는 반복 횟수를 무시한다 (HUD에는 친 대로 보임)
            case 't': BeginLabel(output, label); return;
            case '/': output.Add(new SearchPromptCommand(count)); return;
            case 'n': output.Add(new SearchNextCommand(Reverse: false, count)); return;
            case 'N': output.Add(new SearchNextCommand(Reverse: true, count)); return;
            case '*': output.Add(new SearchTypeCommand(Forward: true, count)); return;
            case '#': output.Add(new SearchTypeCommand(Forward: false, count)); return;
            // 비주얼 모드의 연산자는 바로 실행되므로 반복 횟수를 받지 않는다
            case 'y' when !Visual: BeginOperator(Operator.Yank, count); return;
            case '=' when !Visual: BeginOperator(Operator.Format, count); return;
            case 'd' when !Visual: BeginOperator(Operator.Delete, count); return;
            case 'z' when !Visual: BeginOperator(Operator.Zoom, count); return;
            case '.' when !Visual: output.Add(new RepeatCommand(count)); return;
            // 마크와 붙여넣기는 숫자를 무시한다. 비주얼 모드에서도 m, '는 받는다 (붙여넣기는 비어 있음)
            case 'm': _state = State.AwaitMark; return;
            case '\'': _state = State.AwaitMarkJump; return;
            case 'p' when !Visual: output.Add(new PasteCommand(Before: false)); return;
            case 'P' when !Visual: output.Add(new PasteCommand(Before: true)); return;
            // gc, gy가 반복 횟수를 받는다 (3gcl). 나머지 g 명령은 무시한다
            case 'g' when !Visual:
                _state = State.AwaitSecond;
                _operatorCount = count;
                return;
            case Escape: output.Add(new CancelCommand()); return;
        }
        // 비주얼 모드에서는 연산자·g·.이 숫자를 받지 않으므로 이동만 알린다
        output.Add(new ErrorCommand(Visual
            ? $"{label} 다음에는 h/j/k/l, {{/}}, Ctrl+o/i, t, /, n/N, */#, m, '가 와야 합니다"
            : $"{label} 다음에는 h/j/k/l, {{/}}, Ctrl+o/i, t, /, n/N, */#, y, =, d, z, g, ., m, ', p, P가 와야 합니다"));
    }

    void BeginOperator(Operator op, int count = 1)
    {
        _state = State.AwaitRange;
        _operator = op;
        _operatorCount = count;
    }

    // 연산자 뒤의 범위: h/l/H/L, p(덩어리), 숫자, 연산자 두 번, d 뒤의 i/o
    void ProcessRangeKey(char key, List<Command> output)
    {
        var op = _operator;
        if (key is >= '1' and <= '9')
        {
            _state = State.RangeCount;
            _count = key.ToString();
            return;
        }
        if (op == Operator.Delete && key is 'i' or 'o')
        {
            _operatorCount = 1;
            BeginIndex(key == 'i' ? ParamSide.Input : ParamSide.Output, output, disconnect: true);
            return;
        }
        // 연산자 + t{라벨}: 커서 객체에서 대상까지의 블록. 앞에 붙인 숫자는 무시한다
        if (key == 't')
        {
            var prefix = OperatorPrefix;
            _operatorCount = 1;
            BeginLabel(output, prefix, withOperator: true);
            return;
        }
        // 연산자 + '{a-z}: 커서 객체에서 마크의 객체까지의 블록 (t{라벨}과 같은 범위). 숫자는 무시한다
        if (key == '\'')
        {
            BeginRangeMark(OperatorPrefix);
            return;
        }

        var label = OperatorPrefix;
        var depth = _operatorCount;
        ClearPending();
        switch (key)
        {
            case 'h' or 'l' or 'H' or 'L':
                var upstream = key is 'h' or 'H';
                output.Add(new OperatorCommand(op, new WireRange(upstream, char.IsUpper(key) ? null : depth)));
                return;
            // 덩어리는 단계가 없으므로 앞에 붙인 숫자는 무시한다
            case 'p':
                output.Add(new OperatorCommand(op, WireRange.Island));
                return;
            case Escape:
                output.Add(new CancelCommand());
                return;
        }

        // 연산자 두 번은 커서 객체 (==만 덩어리). 앞에 붙인 숫자는 무시한다
        Command? doubled = (op, key) switch
        {
            (Operator.Yank, 'y') => new CopyCommand(),
            (Operator.Format, '=') => new FormatCommand(FormatScope.Island),
            (Operator.Delete, 'd') => new DeleteCommand(),
            (Operator.Disable, 'c') => new ToggleEnabledCommand(),
            (Operator.Reference, 'y') => new CopyInfoCommand(),
            (Operator.Zoom, 'z') => new CenterViewCommand(),
            _ => null,
        };
        output.Add(doubled ?? new ErrorCommand($"{label} 다음에 올 수 없는 키입니다"));
    }

    // 연산자 뒤 범위의 반복 횟수 (y2l의 2)
    void ProcessRangeCountKey(char key, List<Command> output)
    {
        if (key >= '0' && key <= '9')
        {
            if (_count.Length < MaxCountDigits)
                _count += key;
            return;
        }
        if (key == Backspace)
        {
            _count = _count.Substring(0, _count.Length - 1);
            if (_count.Length == 0)
                _state = State.AwaitRange;
            return;
        }

        var op = _operator;
        var label = OperatorPrefix + _count;
        var depth = _operatorCount * int.Parse(_count);
        ClearPending();
        switch (key)
        {
            case 'h' or 'l': output.Add(new OperatorCommand(op, new WireRange(key == 'h', depth))); return;
            // y2p: 덩어리에는 단계가 없으므로 숫자를 무시한다
            case 'p': output.Add(new OperatorCommand(op, WireRange.Island)); return;
            // y2t: 블록에도 단계가 없으므로 숫자를 무시한다
            case 't': BeginLabel(output, label, withOperator: true); return;
            case '\'': BeginRangeMark(label); return;
            case Escape: output.Add(new CancelCommand()); return;
        }
        output.Add(new ErrorCommand($"{label} 다음에는 h/l/p/t/'가 와야 합니다"));
    }

    /// <summary>라벨을 기다리는 중인지 (<c>t</c> 다음). Engine은 이때 <see cref="Labels"/>를 그린다.</summary>
    public bool AwaitingLabel => _state == State.AwaitLabel;

    /// <summary>라벨과 그 대상 객체. 라벨을 기다리는 중이 아니면 비어 있다.</summary>
    public IReadOnlyDictionary<string, Guid> Labels => _labels;

    /// <summary>두 글자 라벨에서 지금까지 친 첫 글자. 이 글자로 시작하지 않는 라벨은 흐리게 그린다.</summary>
    public string LabelTyped => _labelTyped;

    /// <summary>
    /// <see cref="ShowLabelsCommand"/>를 받은 Engine이 화면의 라벨을 알려 준다. 비어 있으면 라벨 점프를 그만둔다
    /// (Engine이 이유를 표시). 라벨은 글자만이고 어떤 라벨도 다른 라벨의 앞부분이 아니어야 한다 (<see cref="LabelJump"/>).
    /// </summary>
    public void SetLabels(IReadOnlyDictionary<string, Guid> labels)
    {
        if (_state != State.AwaitLabel) return;
        if (labels.Count == 0)
            ClearPending();
        else
            _labels = labels;
    }

    /// <param name="prefix">HUD에서 <c>t</c> 앞에 보일, 이미 친 키 (<c>3</c>, <c>2d</c>, <c>y2</c>).</param>
    void BeginLabel(List<Command> output, string prefix, bool withOperator = false)
    {
        _state = State.AwaitLabel;
        _labelOperator = withOperator;
        _labelPrefix = prefix;
        _labelTyped = "";
        _labels = EmptyLabels;
        output.Add(new ShowLabelsCommand(withOperator));
    }

    // t 다음: 라벨 글자만 받는다. 다른 키는 (Esc 포함) 알리고 그만둔다. 고른 소스(o{n})는 그대로 둔다
    void ProcessLabelKey(char key, List<Command> output)
    {
        if (key == Backspace && _labelTyped.Length > 0)
        {
            _labelTyped = "";
            return;
        }

        var typed = _labelTyped + char.ToLowerInvariant(key);
        var withOperator = _labelOperator;
        var op = _operator;
        if (_labels.TryGetValue(typed, out var target))
        {
            ClearPending();
            output.Add(withOperator ? new OperatorCommand(op, WireRange.To(target)) : new LabelJumpCommand(target));
            return;
        }
        if (char.IsLetter(key) && _labels.Keys.Any(l => l.StartsWith(typed, StringComparison.Ordinal)))
        {
            _labelTyped = typed;
            return;
        }

        ClearPending();
        output.Add(new ErrorCommand(key is Escape or Backspace ? "라벨 점프 취소" : "라벨이 아닙니다: 라벨 점프 취소"));
    }

    // 리더(Space) 다음 키. 지금은 Space Space(컴포넌트 넣기)뿐이고, 나머지는 사용자 매핑용으로 비워 둔다
    void ProcessLeaderKey(char key, List<Command> output)
    {
        ClearPending();
        switch (key)
        {
            case Space: output.Add(new InsertComponentCommand()); LeaveVisual(output); return;
            case Escape: output.Add(new CancelCommand()); return;
        }
        output.Add(new ErrorCommand("<Space> 다음에 올 수 없는 키입니다"));
    }

    /// <param name="prefix">HUD에서 <c>'</c> 앞에 보일, 이미 친 키 (<c>d</c>, <c>2d</c>, <c>y2</c>).</param>
    void BeginRangeMark(string prefix)
    {
        _state = State.AwaitRangeMark;
        _operatorCount = 1;
        _labelPrefix = prefix;
    }

    // m 또는 ' 다음의 마크 글자 (a-z). 연산자 뒤 '면 범위
    void ProcessMarkKey(char key, List<Command> output)
    {
        var jump = _state == State.AwaitMarkJump;
        var range = _state == State.AwaitRangeMark;
        var prefix = _labelPrefix;
        var op = _operator;
        ClearPending();
        if (key == Escape)
        {
            output.Add(new CancelCommand());
            return;
        }
        if (!MarkStore<object>.IsValid(key))
        {
            output.Add(new ErrorCommand(jump || range ? $"{prefix}' 다음에는 마크 a-z가 와야 합니다" : "m 다음에는 마크 a-z가 와야 합니다"));
            return;
        }
        output.Add(range ? new OperatorCommand(op, WireRange.ToMark(key)) : jump ? new JumpToMarkCommand(key) : new SetMarkCommand(key));
    }

    // g로 시작하는 두 글자 명령(g=, gy, gi, go, gc)의 두 번째 키
    void ProcessSecondKey(char key, List<Command> output)
    {
        var count = _operatorCount;
        ClearPending();
        switch (key)
        {
            case 'i': BeginIndex(ParamSide.Input, output, follow: true); return;
            case 'o': BeginIndex(ParamSide.Output, output, follow: true); return;
            case '=': output.Add(new FormatCommand(FormatScope.Document)); LeaveVisual(output); return;
            case 'y' when Visual: output.Add(new CopyInfoCommand()); LeaveVisual(output); return;
            case 'y': BeginOperator(Operator.Reference, count); return;
            case 'c' when Visual: output.Add(new ToggleEnabledCommand()); LeaveVisual(output); return;
            case 'c': BeginOperator(Operator.Disable, count); return;
            case Escape: output.Add(new CancelCommand()); return;
        }
        output.Add(new ErrorCommand("g 다음에 올 수 없는 키입니다"));
    }

    void BeginIndex(ParamSide side, List<Command> output, bool append = false, bool follow = false, bool disconnect = false)
    {
        if (_paramCount(side) == 0)
        {
            ClearPending();
            output.Add(new ErrorCommand(side == ParamSide.Output ? "출력이 없는 객체입니다" : "입력이 없는 객체입니다"));
            return;
        }
        _state = State.AwaitIndex;
        _side = side;
        _append = append;
        _follow = follow;
        _disconnect = disconnect;
        _digits = "";
    }

    // 숫자를 하나 더 붙여도 유효한 인덱스가 나올 수 없으면 확정
    bool IsComplete() => _digits[0] == '0' || int.Parse(_digits) * 10 >= _paramCount(_side);

    void Commit(List<Command> output)
    {
        var index = int.Parse(_digits);
        var count = _paramCount(_side);
        var label = Prefix + _digits;
        _digits = "";

        if (index >= count)
        {
            _state = State.Idle;
            output.Add(new ErrorCommand($"{label}: 범위를 벗어났습니다 (0..{count - 1})"));
            return;
        }

        // gi{n}/go{n}은 이동, di{n}/do{n}은 끊기라 뒤에 데이터 연산자가 붙지 않는다
        if (_follow || _disconnect)
        {
            _state = State.Idle;
            output.Add(_follow ? new FollowParamCommand(_side, index) : new DisconnectCommand(_side, index));
            return;
        }

        _state = State.AfterIndex;
        output.Add(_side == ParamSide.Output
            ? new PickOutputCommand(index)
            : new ConnectInputCommand(index, _append));
    }

    static bool TryParseOp(char key, out ParamOp op)
    {
        switch (key)
        {
            case 'f': op = ParamOp.Flatten; return true;
            case 'g': op = ParamOp.Graft; return true;
            case 's': op = ParamOp.Simplify; return true;
            case 'r': op = ParamOp.Reverse; return true;
            case 'n': op = ParamOp.Invert; return true;
            default: op = default; return false;
        }
    }
}
