using System;

namespace Vimhopper.Core;

public enum Direction { Left, Down, Up, Right }

public enum ParamSide { Output, Input }

public enum ParamOp { Flatten, Graft, Simplify, Reverse, Invert }

public enum FormatScope { Island, Document, Selection }

/// <summary>범위를 받는 연산자: <c>y</c> 복사, <c>=</c> 정렬, <c>d</c> 삭제, <c>gc</c> 켜기/끄기, <c>gy</c> 참조 복사, <c>z</c> 화면에 맞춤, <c>gG</c> 그룹.</summary>
public enum Operator { Yank, Format, Delete, Disable, Reference, Zoom, Group }

/// <summary>범위의 종류: 와이어 방향(상류/하류)을 따라가는 범위, 또는 덩어리 전체.</summary>
public enum RangeKind { Wire, Island, Target }

/// <summary>
/// 연산자 뒤의 범위. <c>h</c>/<c>l</c>은 상류/하류로 Depth단계까지, <c>H</c>/<c>L</c>은 끝까지(Depth null).
/// <c>p</c>는 커서 객체가 속한 덩어리 전체(<see cref="Island"/>: Kind가 Island이고 Upstream·Depth는 쓰지 않음).
/// <c>t{라벨}</c>은 커서 객체와 대상 객체(Target)를 두 끝으로 하는 블록 (Kind가 Target, <see cref="VisualRange"/>와 같은 범위).
/// <c>'{a-z}</c>는 같은 블록이고 대상은 마크(Mark)의 객체 (Target은 null, Engine이 실행할 때마다 마크를 찾는다).
/// 커서 객체도 범위에 들어간다.
/// </summary>
public sealed record WireRange(bool Upstream, int? Depth, RangeKind Kind = RangeKind.Wire, Guid? Target = null, char? Mark = null)
{
    /// <summary><c>p</c>: 와이어 방향과 무관하게 이어진 덩어리 전체 (vim의 문단, <c>==</c>가 정렬하는 단위).</summary>
    public static WireRange Island { get; } = new(false, null, RangeKind.Island);

    /// <summary><c>t{라벨}</c>: 커서 객체에서 대상 객체까지의 블록 (비주얼 모드에서 두 끝을 고른 것과 같음).</summary>
    public static WireRange To(Guid target) => new(false, null, RangeKind.Target, target);

    /// <summary><c>'{a-z}</c>: 커서 객체에서 마크의 객체까지의 블록. 마크는 라벨과 달리 그대로라서 <c>.</c>으로 되풀이한다.</summary>
    public static WireRange ToMark(char mark) => new(false, null, RangeKind.Target, Mark: mark);
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

/// <summary>
/// <c>t</c>: 화면에 보이는 객체에 라벨을 그리라는 요청. Engine은 라벨을 정해 <see cref="KeyParser.SetLabels"/>로 돌려주고,
/// 파서는 라벨 글자를 기다린다. 연산자 뒤(<c>dt</c>, WithOperator)면 라벨을 고른 뒤 범위 연산자가 된다.
/// </summary>
public sealed record ShowLabelsCommand(bool WithOperator = false) : Command;

/// <summary><c>t{라벨}</c>: 라벨이 붙은 객체로 커서를 옮긴다 (점프 목록에 기록).</summary>
public sealed record LabelJumpCommand(Guid Target) : Command;

/// <summary>Ctrl+hjkl: 연결과 무관하게 화면상 방향으로 가장 가까운 객체로 이동.</summary>
public sealed record JumpCommand(Direction Direction) : Command;

/// <summary>
/// <c>{n}Ctrl+o</c>: 점프 목록에서 n개 전 위치로 (vim과 같음). 목록 끝에서 처음 누르면 지금 위치를 먼저 저장한다.
/// </summary>
public sealed record JumpBackCommand(int Count = 1) : Command;

/// <summary><c>{n}Ctrl+i</c>: 점프 목록에서 n개 뒤 위치로.</summary>
public sealed record JumpForwardCommand(int Count = 1) : Command;

/// <summary><c>Ctrl+=</c> / <c>Ctrl+-</c>: 커서 객체를 기준으로 확대 / 축소 (커서가 없거나 화면 밖이면 화면 가운데 기준).</summary>
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

/// <summary><c>zz</c>: 커서 객체를 화면 가운데로.</summary>
public sealed record CenterViewCommand : Command;

/// <summary>비주얼 모드의 <c>z</c>: 선택된 객체들을 화면에 맞춘다. 문서를 바꾸지 않으므로 비주얼 모드에 머문다.</summary>
public sealed record FitSelectionCommand : Command;

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

/// <summary>
/// <c>gGG</c>: 커서 객체를, 비주얼 모드의 <c>gG</c>: 선택된 객체들을 Grasshopper 그룹으로 묶는다 (Range null).
/// Name이 null이면 Engine이 이름 입력줄을 연다. <c>gG{범위}</c>는 <see cref="OperatorCommand"/>로 오고,
/// 이름을 확정하면 Engine이 Range와 Name을 채운 이 명령을 <see cref="RepeatRegister"/>에 기록해 <c>.</c>이 묻지 않고 되풀이한다.
/// </summary>
public sealed record GroupCommand(WireRange? Range = null, string? Name = null) : Command;

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

/// <summary>
/// <c>.</c> / <c>{n}.</c>: 마지막 변경을 커서 객체에서 되풀이한다 (<see cref="RepeatRegister"/>).
/// Count는 범위 연산자의 단계 수를 바꾼다 (<c>d2l</c> 뒤 <c>3.</c> = <c>d3l</c>).
/// </summary>
public sealed record RepeatCommand(int? Count = null) : Command;

/// <summary>
/// <c>.</c> 되풀이 전용 (키에 묶이지 않음): 커서 객체의 파라미터를 데이터 연산자의 대상으로 정한다.
/// <c>o{n}f</c>를 되풀이할 때 소스를 바꾸지 않고 출력[n]에만 연산자를 적용하려고 쓴다.
/// </summary>
public sealed record TargetParamCommand(ParamSide Side, int Index) : Command;

/// <summary><c>m{a-z}</c>: 커서 객체에 마크를 단다 (문서마다 따로, <see cref="MarkStore{TDocument}"/>).</summary>
public sealed record SetMarkCommand(char Mark) : Command;

/// <summary><c>'{a-z}</c>: 마크의 객체로 점프한다 (점프 목록에 기록). 비주얼 모드면 선택을 늘린다.</summary>
public sealed record JumpToMarkCommand(char Mark) : Command;

/// <summary>
/// <c>p</c> / <c>P</c>: Grasshopper 클립보드의 객체를 커서 객체 오른쪽 / 왼쪽(Before)에 붙여넣는다 (<see cref="PastePlacement"/>).
/// </summary>
public sealed record PasteCommand(bool Before) : Command;

public sealed record ErrorCommand(string Message) : Command;

/// <summary>
/// <c>/</c> / <c>{n}/</c>: 찾기 입력줄을 연다. Enter로 커서 다음의 Count번째 맞는 객체로 (빈 Enter는 마지막 찾기).
/// 입력줄의 글자는 파서가 아니라 Engine이 <see cref="InputLine"/>으로 받는다.
/// </summary>
public sealed record SearchPromptCommand(int Count = 1) : Command;

/// <summary>
/// <c>{n}n</c> / <c>{n}N</c>: 마지막 찾기(<c>/</c>, <c>*</c>, <c>#</c>)를 같은 방향 / 반대 방향으로 Count번 되풀이.
/// <c>o{n}</c> / <c>i{n}</c> 바로 뒤의 <c>n</c>은 데이터 연산자 Invert다 (<see cref="ParamOpCommand"/>).
/// </summary>
public sealed record SearchNextCommand(bool Reverse, int Count = 1) : Command;

/// <summary><c>{n}*</c> / <c>{n}#</c>: 커서 객체와 같은 컴포넌트 종류의 다음(Forward) / 이전 객체로 Count번.</summary>
public sealed record SearchTypeCommand(bool Forward, int Count = 1) : Command;

/// <summary>
/// <c>{n}]{e,w,s,g}</c> / <c>{n}[{e,w,s,g}</c>: 다음(Forward) / 이전 오류 원인, 경고 원인, 느린 객체, 그룹으로 Count번 (<see cref="BracketNav"/>).
/// 점프 목록에 기록하고, 비주얼 모드면 선택을 늘린다.
/// </summary>
public sealed record BracketMoveCommand(BracketTarget Target, bool Forward, int Count = 1) : Command;

/// <summary>
/// <c>K</c>: 커서 객체의 입력·출력 데이터 모양을 패널로 보여 준다 (<see cref="DataShape"/>). 숫자는 무시하고,
/// 점프·변경이 아니라 undo와 <c>.</c>에 남지 않는다. 패널은 다음 키에 닫힌다 (<c>K</c>, <c>Esc</c>는 닫기만).
/// </summary>
public sealed record ShowDataShapeCommand : Command;

/// <summary>
/// <c>cn</c>: 커서 객체의 별명 입력줄을 연다 (지금 별명이 미리 채워짐). 숫자는 무시한다.
/// 입력줄의 글자는 Engine이 받고, Enter면 Engine이 <see cref="RenameCommand"/>를 실행한다 (<c>.</c>에 기록되게).
/// </summary>
public sealed record RenamePromptCommand : Command;

/// <summary>
/// <c>cn</c>의 Enter (키에 묶이지 않음): 커서 객체의 별명을 NickName으로. null이면 그 종류의 Grasshopper 기본 별명으로 되돌린다 (빈 Enter).
/// <c>.</c>은 같은 별명을 묻지 않고 지금 커서 객체에 붙인다.
/// </summary>
public sealed record RenameCommand(string? NickName) : Command;

/// <summary>
/// <c>:</c>: 명령줄을 연다 (<see cref="ExCommands"/>). 비주얼 모드에서 누르면(Visual) <c>'&lt;,'&gt;</c>가 미리 채워지고 선택이 범위가 된다.
/// 파서는 이때 비주얼 모드를 나가고(<see cref="VisualCommand"/>는 보내지 않음), Engine은 명령줄을 닫을 때 선택을 푼다 (vim처럼 Esc도).
/// </summary>
public sealed record CmdlinePromptCommand(bool Visual = false) : Command;
