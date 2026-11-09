namespace Vimhopper.Core;

public enum Direction { Left, Down, Up, Right }

public enum ParamSide { Output, Input }

public enum ParamOp { Flatten, Graft, Simplify, Reverse, Invert }

public enum FormatScope { Island, Document, Selection }

/// <summary>범위를 받는 연산자: <c>y</c> 복사, <c>=</c> 정렬, <c>d</c> 삭제, <c>gc</c> 켜기/끄기, <c>gy</c> 참조 복사.</summary>
public enum Operator { Yank, Format, Delete, Disable, Reference }

/// <summary>범위의 종류: 와이어 방향(상류/하류)을 따라가는 범위, 또는 덩어리 전체.</summary>
public enum RangeKind { Wire, Island }

/// <summary>
/// 연산자 뒤의 범위. <c>h</c>/<c>l</c>은 상류/하류로 Depth단계까지, <c>H</c>/<c>L</c>은 끝까지(Depth null).
/// <c>p</c>는 커서 객체가 속한 덩어리 전체(<see cref="Island"/>: Kind가 Island이고 Upstream·Depth는 쓰지 않음).
/// 커서 객체도 범위에 들어간다.
/// </summary>
public sealed record WireRange(bool Upstream, int? Depth, RangeKind Kind = RangeKind.Wire)
{
    /// <summary><c>p</c>: 와이어 방향과 무관하게 이어진 덩어리 전체 (vim의 문단, <c>==</c>가 정렬하는 단위).</summary>
    public static WireRange Island { get; } = new(false, null, RangeKind.Island);
}

/// <summary>KeyParser가 키 입력을 해석해 내보내는 명령.</summary>
public abstract record Command;

/// <summary>
/// hjkl: 와이어 연결을 따라 이동 (h/l 부모·자식, j/k 같은 층).
/// Count는 앞에 붙인 숫자로, vim처럼 반복 횟수 (<c>3l</c> = <c>l</c> 세 번).
/// </summary>
public sealed record MoveCommand(Direction Direction, int Count = 1) : Command;

/// <summary><c>gi{n}</c>: 입력 n에 연결된 출처로, <c>go{n}</c>: 출력 n을 받는 객체로 이동.</summary>
public sealed record FollowParamCommand(ParamSide Side, int Index) : Command;

/// <summary>
/// <c>}</c> / <c>{</c>: 다음(Forward) / 이전 덩어리의 첫 객체로 Count번 (vim의 문단 이동).
/// 덩어리 순서는 <c>g=</c>가 쌓는 순서이고, 끝을 넘으면 끝 덩어리에서 멈춘다.
/// </summary>
public sealed record IslandMoveCommand(bool Forward, int Count = 1) : Command;

/// <summary>Ctrl+hjkl: 연결과 무관하게 화면상 방향으로 가장 가까운 객체로 이동.</summary>
public sealed record JumpCommand(Direction Direction) : Command;

public sealed record ZoomCommand(bool In) : Command;

/// <summary>o{n}: 선택된 객체의 출력[n]을 연결 소스로 고른다.</summary>
public sealed record PickOutputCommand(int Index) : Command;

/// <summary>i{n} / I{n}: 고른 소스를 선택된 객체의 입력[n]에 연결한다. Append면 기존 와이어를 유지.</summary>
public sealed record ConnectInputCommand(int Index, bool Append) : Command;

/// <summary>o{n} / i{n} 직후의 연산자(f, g, s, r, n). 방금 고른 파라미터에 적용된다.</summary>
public sealed record ParamOpCommand(ParamSide Side, ParamOp Op) : Command;

/// <summary>
/// <c>Space Space</c>: 커서 객체 오른쪽에 Grasshopper 컴포넌트 검색창을 띄운다. 놓인 컴포넌트로 커서가 옮겨 간다.
/// </summary>
public sealed record InsertComponentCommand : Command;

public sealed record UndoCommand : Command;

public sealed record RedoCommand : Command;

public sealed record CancelCommand : Command;

public sealed record CenterViewCommand : Command;

/// <summary><c>yy</c>: 커서 객체를, 비주얼 모드의 <c>y</c>: 선택된 객체들을 복사 (Grasshopper의 Ctrl+C와 같음).</summary>
public sealed record CopyCommand : Command;

/// <summary><c>gyy</c>, 비주얼 모드의 <c>gy</c>: Grasshopper에서 선택된 객체들의 참조(<c>파일경로:guid,guid</c>)를 클립보드에.</summary>
public sealed record CopyInfoCommand : Command;

/// <summary>
/// <c>==</c>: 선택 객체와 와이어로 이어진 덩어리를, <c>g=</c>: 문서 전체를,
/// 비주얼 모드의 <c>=</c>: 선택된 객체들을 자동 정렬.
/// </summary>
public sealed record FormatCommand(FormatScope Scope) : Command;

/// <summary><c>dd</c>: 커서 객체를, 비주얼 모드의 <c>d</c>: 선택된 객체들을 삭제.</summary>
public sealed record DeleteCommand : Command;

/// <summary><c>gcc</c>: 커서 객체를, 비주얼 모드의 <c>gc</c>: 선택된 객체들을 켜고 끈다 (Grasshopper의 Enable/Disable).</summary>
public sealed record ToggleEnabledCommand : Command;

/// <summary><c>di{n}</c>: 입력 n에 들어오는 와이어를, <c>do{n}</c>: 출력 n에서 나가는 와이어를 모두 끊는다.</summary>
public sealed record DisconnectCommand(ParamSide Side, int Index) : Command;

/// <summary>
/// <c>y</c> <c>=</c> <c>d</c> <c>gc</c> + 범위: 커서 객체에서 시작하는 범위에 연산자를 적용 (예: <c>yL</c> 하류 전체 복사).
/// </summary>
public sealed record OperatorCommand(Operator Operator, WireRange Range) : Command;

/// <summary>비주얼 모드의 <c>u</c>: 커서 이동이나 <c>H</c>/<c>L</c>로 바뀐 선택을 한 단계 되돌린다.</summary>
public sealed record UndoSelectionCommand : Command;

/// <summary>비주얼 모드의 <c>H</c>/<c>L</c>: 선택된 객체들의 상류/하류 전체를 선택에 더한다.</summary>
public sealed record ExtendVisualCommand(bool Upstream) : Command;

/// <summary><c>v</c>: 비주얼 모드 시작(On) / 끝. 끝나면 커서 객체 하나만 선택된다.</summary>
public sealed record VisualCommand(bool On) : Command;

/// <summary>비주얼 모드의 <c>o</c>: 시작점과 커서를 맞바꾼다 (vim과 같음).</summary>
public sealed record SwapVisualEndsCommand : Command;

public sealed record ErrorCommand(string Message) : Command;
