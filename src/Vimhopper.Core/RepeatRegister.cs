using System;
using System.Collections.Generic;
using System.Linq;

namespace Vimhopper.Core;

/// <summary>
/// vim의 <c>.</c>가 되풀이할 마지막 변경을 기억한다.
/// </summary>
/// <remarks>
/// Engine은 실행한 명령을 모두 <see cref="Record"/>에 넘기고, <see cref="RepeatCommand"/>를 받으면
/// <see cref="Replay"/>가 돌려준 명령들을 (다시 기록하지 않고) 커서 객체에서 실행한다.
/// <list type="bullet">
/// <item>변경: 범위 연산자(<c>y</c>, <c>gy</c> 제외), <c>dd</c>, <c>gcc</c>, <c>di{n}</c>/<c>do{n}</c>, <c>i{n}</c>/<c>I{n}</c>, 데이터 연산자, 정렬.</item>
/// <item>변경이 아님: 이동, 복사, 실행 취소/다시 실행, 비주얼 모드 전환, 넣기(<c>Space Space</c>), 오류.</item>
/// <item>비주얼 모드에서 한 변경은 커서 객체 형태로 되풀이한다 (비주얼 <c>d</c> → <c>dd</c>, <c>=</c> → <c>==</c>).</item>
/// <item><c>i{n}</c>, <c>o{n}</c> 바로 뒤의 데이터 연산자는 한 변경으로 묶는다 (<c>i0fg</c>, <c>o1f</c>).
/// <c>o{n}</c> 자체는 변경이 아니므로 <c>o1f</c>는 소스를 바꾸지 않고 출력[1]에만 flatten을 되풀이한다.</item>
/// </list>
/// </remarks>
public sealed class RepeatRegister
{
    List<Command>? _last;
    // 데이터 연산자가 붙을 파라미터 (바로 앞의 o{n} / i{n}). 다른 명령이 오면 지운다
    (ParamSide Side, int Index)? _param;
    // 지금 _last가 _param에 데이터 연산자를 이어 붙이는 중인지
    bool _chain;

    /// <summary>되풀이할 명령들. 아직 변경이 없으면 null.</summary>
    public IReadOnlyList<Command>? Last => _last;

    /// <summary>실행한 명령 하나를 알린다. 변경이면 기억하고, 아니면 무시한다.</summary>
    /// <param name="succeeded">
    /// Engine에서 실제로 바뀌었는지. 실패한 변경(소스 없는 <c>i{n}</c>, 커서 객체 없음 등)은 마지막 변경을 덮어쓰지 않는다.
    /// </param>
    public void Record(Command command, bool succeeded = true)
    {
        if (!succeeded)
        {
            RecordFailure(command);
            return;
        }

        switch (command)
        {
            case PickOutputCommand pick:
                _param = (ParamSide.Output, pick.Index);
                _chain = false;
                return;
            case ConnectInputCommand connect:
                _last = new List<Command> { connect };
                _param = (ParamSide.Input, connect.Index);
                _chain = true;
                return;
            case ParamOpCommand op:
                if (_param is not { } param) return;
                if (!_chain)
                {
                    _last = new List<Command> { new TargetParamCommand(param.Side, param.Index) };
                    _chain = true;
                }
                _last!.Add(op);
                return;
        }

        _param = null;
        _chain = false;
        if (Normalize(command) is { } change)
            _last = new List<Command> { change };
    }

    void RecordFailure(Command command)
    {
        switch (command)
        {
            // 연결은 실패해도 Engine은 그 입력을 데이터 연산자 대상으로 정하므로, 뒤의 연산자는 그 입력에 붙인다
            case ConnectInputCommand connect:
                _param = (ParamSide.Input, connect.Index);
                _chain = false;
                return;
            // 실패한 데이터 연산자는 묶음에 넣지 않지만 묶음은 이어진다
            case ParamOpCommand:
                return;
        }
        _param = null;
        _chain = false;
    }

    /// <summary>
    /// 되풀이할 명령들. <paramref name="count"/>가 있으면 숫자 단계 범위 연산자의 단계 수를 바꾸고
    /// 이후의 <c>.</c>도 그 단계 수를 쓴다 (vim과 같음). <c>H</c>/<c>L</c> 범위와 다른 명령은 숫자를 무시한다.
    /// </summary>
    /// <param name="paramCount">커서 객체의 출력/입력 개수. 파라미터 번호가 없는 객체면 오류를 돌려준다.</param>
    public IReadOnlyList<Command> Replay(int? count, Func<ParamSide, int> paramCount)
    {
        if (_last == null)
            return new Command[] { new ErrorCommand("되풀이할 변경이 없습니다") };

        if (count is { } n)
            _last = _last.Select(c => WithCount(c, n)).ToList();

        foreach (var command in _last)
        {
            if (ParamIndex(command) is not { } p) continue;
            if (p.Index >= paramCount(p.Side))
                return new Command[]
                {
                    new ErrorCommand(p.Side == ParamSide.Output ? $".: 출력 {p.Index}이 없습니다" : $".: 입력 {p.Index}이 없습니다"),
                };
        }
        return _last.ToArray();
    }

    /// <summary>변경이면 커서 객체에서 되풀이할 형태로, 아니면 null.</summary>
    public static Command? Normalize(Command command) => command switch
    {
        OperatorCommand { Operator: Operator.Yank or Operator.Reference } => null,
        // t{라벨} 범위는 화면마다 라벨이 달라서 되풀이할 대상이 없다 (마지막 변경을 덮어쓰지 않음)
        OperatorCommand { Range.Kind: RangeKind.Target } => null,
        OperatorCommand => command,
        DeleteCommand or ToggleEnabledCommand or DisconnectCommand or ConnectInputCommand or ParamOpCommand => command,
        // 비주얼 =는 선택 대신 커서 객체의 덩어리를 정렬한다 (==)
        FormatCommand { Scope: FormatScope.Selection } => new FormatCommand(FormatScope.Island),
        FormatCommand => command,
        _ => null,
    };

    /// <summary>되풀이할 수 있는 변경인지.</summary>
    public static bool IsRepeatable(Command command) => Normalize(command) != null;

    /// <summary><c>{n}.</c>: 숫자 단계 범위 연산자면 단계 수를 n으로 바꾼다. 나머지는 그대로.</summary>
    public static Command WithCount(Command command, int count) => command switch
    {
        OperatorCommand { Range.Depth: not null } op => op with { Range = op.Range with { Depth = count } },
        _ => command,
    };

    static (ParamSide Side, int Index)? ParamIndex(Command command) => command switch
    {
        ConnectInputCommand c => (ParamSide.Input, c.Index),
        TargetParamCommand t => (t.Side, t.Index),
        DisconnectCommand d => (d.Side, d.Index),
        _ => null,
    };
}
