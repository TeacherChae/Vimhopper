namespace Vimhopper.Core;

public enum Direction { Left, Down, Up, Right }

public enum ParamSide { Output, Input }

public enum ParamOp { Flatten, Graft, Simplify, Reverse, Invert }

public enum FormatScope { Island, Document, Selection }

/// <summary>KeyParser가 키 입력을 해석해 내보내는 명령.</summary>
public abstract record Command;

/// <summary>
/// hjkl: 와이어 연결을 따라 이동 (h/l 부모·자식, j/k 같은 층).
/// Count는 앞에 붙인 숫자: h/l에서는 입력/출력 번호, j/k에서는 칸 수.
/// </summary>
public sealed record MoveCommand(Direction Direction, int? Count = null) : Command;

/// <summary>Ctrl+hjkl: 연결과 무관하게 화면상 방향으로 가장 가까운 객체로 이동.</summary>
public sealed record JumpCommand(Direction Direction) : Command;

public sealed record ZoomCommand(bool In) : Command;

/// <summary>o{n}: 선택된 객체의 출력[n]을 연결 소스로 고른다.</summary>
public sealed record PickOutputCommand(int Index) : Command;

/// <summary>i{n} / I{n}: 고른 소스를 선택된 객체의 입력[n]에 연결한다. Append면 기존 와이어를 유지.</summary>
public sealed record ConnectInputCommand(int Index, bool Append) : Command;

/// <summary>o{n} / i{n} 직후의 연산자(f, g, s, r, n). 방금 고른 파라미터에 적용된다.</summary>
public sealed record ParamOpCommand(ParamSide Side, ParamOp Op) : Command;

public sealed record UndoCommand : Command;

public sealed record CancelCommand : Command;

public sealed record CenterViewCommand : Command;

/// <summary><c>yy</c>: 커서 객체를, 비주얼 모드의 <c>y</c>: 선택된 객체들을 복사 (Grasshopper의 Ctrl+C와 같음).</summary>
public sealed record CopyCommand : Command;

/// <summary><c>gy</c>: Grasshopper에서 선택된 객체들의 InstanceGuid·이름·위치를 텍스트로 클립보드에.</summary>
public sealed record CopyInfoCommand : Command;

/// <summary>
/// <c>==</c>: 선택 객체와 와이어로 이어진 덩어리를, <c>g=</c>: 문서 전체를,
/// 비주얼 모드의 <c>=</c>: 선택된 객체들을 자동 정렬.
/// </summary>
public sealed record FormatCommand(FormatScope Scope) : Command;

/// <summary><c>v</c>: 비주얼 모드 시작(On) / 끝. 끝나면 커서 객체 하나만 선택된다.</summary>
public sealed record VisualCommand(bool On) : Command;

/// <summary>비주얼 모드의 <c>o</c>: 시작점과 커서를 맞바꾼다 (vim과 같음).</summary>
public sealed record SwapVisualEndsCommand : Command;

public sealed record ErrorCommand(string Message) : Command;
