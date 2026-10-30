using System;
using System.Collections.Generic;

namespace Vimhopper.Core;

/// <summary>
/// Vim 모드 키 입력을 명령으로 바꾸는 상태 머신.
/// </summary>
/// <remarks>
/// 문법: <c>{n}hjkl</c> 연결 따라 이동 (n은 h/l에서 입력/출력 번호, j/k에서 칸 수),
/// <c>Ctrl+hjkl</c> 화면 방향 이동, <c>Ctrl+=</c>/<c>Ctrl+-</c> 줌,
/// <c>o{n}{ops}</c> 출력 선택, <c>i{n}{ops}</c> / <c>I{n}{ops}</c> 입력 연결, <c>==</c> / <c>g=</c> 자동 정렬,
/// <c>yy</c> 커서 객체 복사, <c>gy</c> 선택 객체 정보 복사, <c>v</c> 비주얼 모드 (<c>o</c> 양 끝 바꾸기, <c>y</c> 복사, <c>=</c> 정렬).
/// 인덱스는 0부터 시작한다. 숫자를 더 붙여도 유효한 인덱스가 될 수 없으면 즉시 확정하고
/// (파라미터가 10개 이하면 한 자리에서 바로 확정), 그렇지 않으면 다음 키나 Enter에서 확정한다.
/// </remarks>
public sealed class KeyParser
{
    public const char Escape = '\x1b';
    public const char Enter = '\r';
    public const char Backspace = '\b';

    // Ctrl 조합은 대응하는 문자가 없으므로 유니코드 사용자 정의 영역의 값으로 표현
    public const char CtrlH = '';
    public const char CtrlJ = '';
    public const char CtrlK = '';
    public const char CtrlL = '';
    public const char CtrlPlus = '';
    public const char CtrlMinus = '';

    enum State { Idle, AwaitIndex, AfterIndex, AwaitSecond, Count }

    // 이동 앞 숫자의 최대 자릿수 (int 넘침 방지)
    const int MaxCountDigits = 4;

    readonly Func<ParamSide, int> _paramCount;
    State _state = State.Idle;
    ParamSide _side;
    bool _append;
    string _digits = "";
    string _count = "";
    char _first;

    /// <param name="paramCount">현재 선택된 객체의 출력/입력 개수를 돌려주는 함수.</param>
    public KeyParser(Func<ParamSide, int> paramCount) => _paramCount = paramCount;

    /// <summary>HUD에 표시할 입력 중인 키 (예: "o1", "I", "g").</summary>
    public string Pending => _state switch
    {
        State.AwaitIndex => Prefix + _digits,
        State.AwaitSecond => _first.ToString(),
        State.Count => _count,
        _ => "",
    };

    string Prefix => _side == ParamSide.Output ? "o" : _append ? "I" : "i";

    /// <summary>비주얼 모드인지. <c>v</c>로 들어가고 <c>v</c>, <c>Esc</c>, 또는 <c>y</c>·<c>=</c> 실행 후 나온다.</summary>
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
            case 'o': BeginIndex(ParamSide.Output, append: false, output); break;
            case 'i': BeginIndex(ParamSide.Input, append: false, output); break;
            case 'I': BeginIndex(ParamSide.Input, append: true, output); break;
            case 'u': output.Add(new UndoCommand()); break;
            case 'z': output.Add(new CenterViewCommand()); break;
            case 'v':
                Visual = true;
                output.Add(new VisualCommand(true));
                break;
            case '=' or 'g' or 'y':
                _state = State.AwaitSecond;
                _first = key;
                break;
            case >= '0' and <= '9':
                _state = State.Count;
                _count = key.ToString();
                break;
            case Escape: output.Add(new CancelCommand()); break;
        }
    }

    // 비주얼 모드에서 뜻이 달라지는 키. 처리했으면 true, 나머지(이동, u, z 등)는 Vim 모드와 같다
    bool ProcessVisualKey(char key, List<Command> output)
    {
        switch (key)
        {
            case 'v' or Escape:
                break;
            case 'o':
                output.Add(new SwapVisualEndsCommand());
                return true;
            case 'y':
                output.Add(new CopyCommand());
                break;
            case '=':
                output.Add(new FormatCommand(FormatScope.Selection));
                break;
            case 'i' or 'I':
                output.Add(new ErrorCommand("비주얼 모드에서는 연결할 수 없습니다"));
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

    // 이동 앞 숫자 ({n}h 등)
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
            case Escape: output.Add(new CancelCommand()); return;
        }
        output.Add(new ErrorCommand($"{label} 다음에는 h/j/k/l이 와야 합니다"));
    }

    // 두 글자 명령(==, g=, yy, gy)의 두 번째 키
    void ProcessSecondKey(char key, List<Command> output)
    {
        var first = _first;
        ClearPending();
        switch ((first, key))
        {
            case ('=', '='): output.Add(new FormatCommand(FormatScope.Island)); return;
            case ('g', '='): output.Add(new FormatCommand(FormatScope.Document)); LeaveVisual(output); return;
            case ('y', 'y'): output.Add(new CopyCommand()); return;
            case ('g', 'y'): output.Add(new CopyInfoCommand()); LeaveVisual(output); return;
            case (_, Escape): output.Add(new CancelCommand()); return;
        }
        output.Add(new ErrorCommand($"{first} 다음에 올 수 없는 키입니다"));
    }

    void BeginIndex(ParamSide side, bool append, List<Command> output)
    {
        if (_paramCount(side) == 0)
        {
            output.Add(new ErrorCommand(side == ParamSide.Output ? "출력이 없는 객체입니다" : "입력이 없는 객체입니다"));
            return;
        }
        _state = State.AwaitIndex;
        _side = side;
        _append = append;
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
