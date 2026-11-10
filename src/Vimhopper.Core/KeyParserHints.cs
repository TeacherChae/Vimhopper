using System;
using System.Collections.Generic;

namespace Vimhopper.Core;

/// <summary>
/// 입력 중인 명령 다음에 올 수 있는 키 하나와 그 뜻 (which-key 힌트).
/// </summary>
/// <param name="Keys">
/// 다음에 누를 키. <c>{n}</c>은 숫자, <c>&lt;Enter&gt;</c> <c>&lt;Space&gt;</c> <c>&lt;BS&gt;</c>는 그 키, <c>&lt;C-o&gt;</c>는 Ctrl+o.
/// 테스트가 이 표기를 실제 키로 바꿔 파서에 넣어 보므로 표기를 바꾸면 테스트도 같이 고친다.
/// </param>
public sealed record KeyHint(string Keys, string Description);

// 힌트 표. 새 키를 파서에 넣으면 여기 해당 상태의 표에도 한 줄 더한다 (KeyParserHintTests가 어긋남을 잡는다).
public sealed partial class KeyParser
{
    /// <summary>
    /// 지금 입력 중인 명령 다음에 올 수 있는 키들. 입력 중인 명령이 없으면 비어 있다.
    /// <c>o{n}</c> / <c>i{n}</c> 직후(데이터 연산자 f g s r n을 받는 상태)도 비어 있다:
    /// <c>o0 … 이동 … i1</c>처럼 평소에 머무는 상태라 패널이 계속 뜨면 방해가 된다.
    /// </summary>
    public IReadOnlyList<KeyHint> Hints => _state switch
    {
        State.AwaitSecond => Visual ? VisualGHints : GHints,
        State.Count => Visual ? VisualCountHints : CountHints,
        State.AwaitRange => RangeHints(_operator),
        State.RangeCount => RangeCountHints(),
        State.Leader => LeaderHints,
        State.AwaitIndex => IndexHints(),
        _ => Array.Empty<KeyHint>(),
    };

    // g 다음
    static readonly KeyHint[] GHints =
    {
        new("=", "문서 전체 정렬"),
        new("i{n}", "입력 n의 출처로"),
        new("o{n}", "출력 n을 받는 객체로"),
        new("y", "참조 복사 (+범위)"),
        new("c", "켜기/끄기 (+범위)"),
    };

    static readonly KeyHint[] VisualGHints =
    {
        new("=", "문서 전체 정렬"),
        new("i{n}", "입력 n의 출처로"),
        new("o{n}", "출력 n을 받는 객체로"),
        new("y", "선택의 참조 복사"),
        new("c", "선택 켜기/끄기"),
    };

    // 이동·연산자 앞 숫자 다음 ({n}h, {n}y …)
    static readonly KeyHint[] CountHints =
    {
        new("h", "부모 쪽으로 n번"),
        new("j", "아래로 n번"),
        new("k", "위로 n번"),
        new("l", "자식 쪽으로 n번"),
        new("}", "n개 다음 덩어리로"),
        new("{", "n개 이전 덩어리로"),
        new("<C-o>", "점프 목록 n개 뒤로"),
        new("<C-i>", "점프 목록 n개 앞으로"),
        new("t", "라벨 점프 (숫자 무시)"),
        new("y", "복사 (범위 n배)"),
        new("=", "정렬 (범위 n배)"),
        new("d", "삭제 (범위 n배)"),
        new("g", "gy / gc (범위 n배)"),
        new(".", "되풀이 (범위를 n단계로)"),
    };

    static readonly KeyHint[] VisualCountHints =
    {
        new("h", "부모 쪽으로 n번"),
        new("j", "아래로 n번"),
        new("k", "위로 n번"),
        new("l", "자식 쪽으로 n번"),
        new("}", "n개 다음 덩어리로"),
        new("{", "n개 이전 덩어리로"),
        new("<C-o>", "점프 목록 n개 뒤로"),
        new("<C-i>", "점프 목록 n개 앞으로"),
        new("t", "라벨 점프 (숫자 무시)"),
    };

    // 모든 연산자에 공통인 범위. 연산자 앞 숫자(2y의 2)가 있으면 실제 단계 수로 쓴다
    static KeyHint[] CommonRangeHints(int count)
    {
        var one = count > 1 ? $"{count}단계" : "한 단계";
        var many = count > 1 ? $"{count}×n단계" : "n단계";
        return new KeyHint[]
        {
            new("h", $"상류 {one}"),
            new("l", $"하류 {one}"),
            new("{n}h", $"상류 {many}"),
            new("{n}l", $"하류 {many}"),
            new("H", "상류 전체"),
            new("L", "하류 전체"),
            new("p", "덩어리 전체 (숫자 무시)"),
            new("t", "라벨 대상까지 블록 (숫자 무시)"),
        };
    }

    // 연산자마다 앞에 붙는 것 (두 번 누르기)과 뒤에 붙는 것 (d의 i/o)
    static readonly Dictionary<Operator, (KeyHint[] Before, KeyHint[] After)> OperatorHints = new()
    {
        [Operator.Yank] = (new KeyHint[] { new("y", "커서 객체 복사") }, Array.Empty<KeyHint>()),
        [Operator.Format] = (new KeyHint[] { new("=", "덩어리 정렬") }, Array.Empty<KeyHint>()),
        [Operator.Delete] = (
            new KeyHint[] { new("d", "커서 객체 삭제") },
            new KeyHint[] { new("i{n}", "입력 n의 와이어 끊기"), new("o{n}", "출력 n의 와이어 끊기") }),
        [Operator.Reference] = (new KeyHint[] { new("y", "Grasshopper 선택의 참조 복사") }, Array.Empty<KeyHint>()),
        [Operator.Disable] = (new KeyHint[] { new("c", "커서 객체 켜기/끄기") }, Array.Empty<KeyHint>()),
    };

    KeyHint[] RangeHints(Operator op)
    {
        var (before, after) = OperatorHints[op];
        var hints = new List<KeyHint>(before);
        hints.AddRange(CommonRangeHints(_operatorCount));
        hints.AddRange(after);
        return hints.ToArray();
    }

    // 연산자 뒤 숫자 다음 (y2 …). 앞뒤 숫자를 곱한 실제 단계 수
    KeyHint[] RangeCountHints()
    {
        var depth = _operatorCount * int.Parse(_count);
        return new KeyHint[]
        {
            new("h", $"상류 {depth}단계"),
            new("l", $"하류 {depth}단계"),
            new("p", "덩어리 전체 (숫자 무시)"),
            new("t", "라벨 대상까지 블록 (숫자 무시)"),
        };
    }

    // Space(리더) 다음
    static readonly KeyHint[] LeaderHints =
    {
        new("<Space>", "컴포넌트 검색창"),
    };

    // o i I gi go di do 다음의 번호
    KeyHint[] IndexHints()
    {
        var count = _paramCount(_side);
        // 번호를 기다리는 사이 커서 객체가 사라졌으면 고를 번호가 없다
        if (count == 0)
            return Array.Empty<KeyHint>();
        if (_digits.Length == 0)
        {
            var what = (_follow, _disconnect, _side, _append) switch
            {
                (true, _, ParamSide.Input, _) => "따라갈 입력",
                (true, _, _, _) => "따라갈 출력",
                (_, true, ParamSide.Input, _) => "끊을 입력",
                (_, true, _, _) => "끊을 출력",
                (_, _, ParamSide.Output, _) => "소스로 고를 출력",
                (_, _, _, true) => "추가 연결할 입력",
                _ => "연결할 입력",
            };
            return new[] { new KeyHint("{n}", $"{what} (0..{count - 1})") };
        }

        // 숫자를 더 붙일 수 있어서 기다리는 중 (파라미터 11개 이상). 기다린다는 것은 low <= count - 1이라는 뜻
        var low = int.Parse(_digits) * 10;
        var high = Math.Min(low + 9, count - 1);
        // 파라미터 100개 이상이면 그 뒤에도 숫자를 더 붙일 수 있다
        var more = (long)low * 10 < count ? ", 더 이어짐" : "";
        return new[]
        {
            new KeyHint("{n}", $"다음 자리 ({low}..{high}{more})"),
            new KeyHint("<Enter>", $"{Prefix}{_digits} 확정 (다른 키도 확정)"),
            new KeyHint("<BS>", "숫자 지우기"),
        };
    }
}
