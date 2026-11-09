using System;
using System.Collections.Generic;

namespace Vimhopper.Core;

/// <summary>
/// Vim 모드 키 입력을 명령으로 바꾸는 상태 머신.
/// </summary>
/// <remarks>
/// 문법: <c>{n}hjkl</c> 연결 따라 n번 이동, <c>gi{n}</c> / <c>go{n}</c> 입력 n의 출처 / 출력 n을 받는 객체로 이동,
/// <c>Ctrl+hjkl</c> 화면 방향 이동, <c>Ctrl+=</c>/<c>Ctrl+-</c> 줌, <c>u</c> / <c>Ctrl+r</c> 실행 취소 / 다시 실행,
/// <c>o{n}{ops}</c> 출력 선택, <c>i{n}{ops}</c> / <c>I{n}{ops}</c> 입력 연결, <c>g=</c> 문서 전체 정렬.
/// 연산자 <c>y</c> 복사, <c>gy</c> 참조 복사, <c>=</c> 정렬, <c>d</c> 삭제, <c>gc</c> 켜기/끄기 뒤에는 범위가 온다:
/// <c>{n}h</c> / <c>{n}l</c> 상류 / 하류 n단계, <c>H</c> / <c>L</c> 끝까지, 연산자 두 번(<c>yy</c>, <c>dd</c>, <c>gcc</c>)은 커서 객체, <c>gyy</c>는 Grasshopper 선택
/// (<c>==</c>만 덩어리). 앞뒤 숫자는 vim처럼 곱한다 (<c>2y3l</c> = 6단계). <c>di{n}</c> / <c>do{n}</c>는 와이어 끊기.
/// <c>Space</c>는 리더 키: <c>Space Space</c> 컴포넌트 넣기 (Grasshopper 검색창).
/// <c>.</c> / <c>{n}.</c> 마지막 변경 되풀이 (<see cref="RepeatRegister"/>).
/// <c>v</c> 비주얼 모드: <c>o</c> 양 끝 바꾸기, <c>H</c>/<c>L</c> 상류/하류 더하기, <c>u</c> 선택 한 단계 되돌리기,
/// 연산자는 범위 없이 선택에 바로 적용.
/// 인덱스는 0부터 시작한다. 숫자를 더 붙여도 유효한 인덱스가 될 수 없으면 즉시 확정하고
/// (파라미터가 10개 이하면 한 자리에서 바로 확정), 그렇지 않으면 다음 키나 Enter에서 확정한다.
/// </remarks>
public sealed class KeyParser
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

    enum State { Idle, AwaitIndex, AfterIndex, AwaitSecond, Count, AwaitRange, RangeCount, Leader }

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
            case 'z': output.Add(new CenterViewCommand()); break;
            case 'v':
                Visual = true;
                output.Add(new VisualCommand(true));
                break;
            case 'y': BeginOperator(Operator.Yank); break;
            case '=': BeginOperator(Operator.Format); break;
            case 'd': BeginOperator(Operator.Delete); break;
            case 'g': _state = State.AwaitSecond; break;
            case '.': output.Add(new RepeatCommand()); break;
            case Space: _state = State.Leader; break;
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
            // 되풀이는 커서 객체에서 하므로 비주얼 모드에서는 막는다
            case '.':
                output.Add(new ErrorCommand("비주얼 모드에서는 .을 쓸 수 없습니다"));
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
            // 비주얼 모드의 연산자는 바로 실행되므로 반복 횟수를 받지 않는다
            case 'y' when !Visual: BeginOperator(Operator.Yank, count); return;
            case '=' when !Visual: BeginOperator(Operator.Format, count); return;
            case 'd' when !Visual: BeginOperator(Operator.Delete, count); return;
            case '.' when !Visual: output.Add(new RepeatCommand(count)); return;
            // gc, gy가 반복 횟수를 받는다 (3gcl). 나머지 g 명령은 무시한다
            case 'g' when !Visual:
                _state = State.AwaitSecond;
                _operatorCount = count;
                return;
            case Escape: output.Add(new CancelCommand()); return;
        }
        output.Add(new ErrorCommand($"{label} 다음에는 h/j/k/l, y, =, d, g, .이 와야 합니다"));
    }

    void BeginOperator(Operator op, int count = 1)
    {
        _state = State.AwaitRange;
        _operator = op;
        _operatorCount = count;
    }

    // 연산자 뒤의 범위: h/l/H/L, 숫자, 연산자 두 번, d 뒤의 i/o
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

        var label = OperatorPrefix;
        var depth = _operatorCount;
        ClearPending();
        switch (key)
        {
            case 'h' or 'l' or 'H' or 'L':
                var upstream = key is 'h' or 'H';
                output.Add(new OperatorCommand(op, new WireRange(upstream, char.IsUpper(key) ? null : depth)));
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
            case Escape: output.Add(new CancelCommand()); return;
        }
        output.Add(new ErrorCommand($"{label} 다음에는 h/l이 와야 합니다"));
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
