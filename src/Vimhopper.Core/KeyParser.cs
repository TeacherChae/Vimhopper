using System;
using System.Collections.Generic;

namespace Vimhopper.Core;

/// <summary>
/// Normal 모드 키 입력을 명령으로 바꾸는 상태 머신.
/// </summary>
/// <remarks>
/// 문법: <c>hjkl</c> 이동, <c>o{n}{ops}</c> 출력 선택, <c>i{n}{ops}</c> / <c>I{n}{ops}</c> 입력 연결.
/// 인덱스는 0부터 시작한다. 숫자를 더 붙여도 유효한 인덱스가 될 수 없으면 즉시 확정하고
/// (파라미터가 10개 이하면 한 자리에서 바로 확정), 그렇지 않으면 다음 키나 Enter에서 확정한다.
/// </remarks>
public sealed class KeyParser
{
    public const char Escape = '\x1b';
    public const char Enter = '\r';
    public const char Backspace = '\b';

    enum State { Idle, AwaitIndex, AfterIndex }

    readonly Func<ParamSide, int> _paramCount;
    State _state = State.Idle;
    ParamSide _side;
    bool _append;
    string _digits = "";

    /// <param name="paramCount">현재 선택된 객체의 출력/입력 개수를 돌려주는 함수.</param>
    public KeyParser(Func<ParamSide, int> paramCount) => _paramCount = paramCount;

    /// <summary>HUD에 표시할 입력 중인 키 (예: "o1", "I").</summary>
    public string Pending => _state == State.AwaitIndex ? Prefix + _digits : "";

    string Prefix => _side == ParamSide.Output ? "o" : _append ? "I" : "i";

    public void Reset()
    {
        _state = State.Idle;
        _digits = "";
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
                Reset();
                output.Add(new CancelCommand());
                return;
            case Backspace:
                if (_digits.Length > 0)
                    _digits = _digits.Substring(0, _digits.Length - 1);
                else
                    Reset();
                return;
        }

        if (_digits.Length == 0)
        {
            Reset();
            output.Add(new ErrorCommand($"{Prefix} 다음에는 숫자가 와야 합니다"));
            return;
        }

        Commit(output);
        if (key != Enter)
            Process(key, output);
    }

    void ProcessIdleKey(char key, List<Command> output)
    {
        switch (key)
        {
            case 'h': output.Add(new MoveCommand(Direction.Left)); break;
            case 'j': output.Add(new MoveCommand(Direction.Down)); break;
            case 'k': output.Add(new MoveCommand(Direction.Up)); break;
            case 'l': output.Add(new MoveCommand(Direction.Right)); break;
            case 'o': BeginIndex(ParamSide.Output, append: false, output); break;
            case 'i': BeginIndex(ParamSide.Input, append: false, output); break;
            case 'I': BeginIndex(ParamSide.Input, append: true, output); break;
            case 'u': output.Add(new UndoCommand()); break;
            case 'z': output.Add(new CenterViewCommand()); break;
            case 'a': output.Add(new ExitNormalCommand()); break;
            case Escape: output.Add(new CancelCommand()); break;
        }
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
