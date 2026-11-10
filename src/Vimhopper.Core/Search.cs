using System;
using System.Collections.Generic;
using System.Linq;

namespace Vimhopper.Core;

/// <summary>찾기 대상 객체 하나: 이름들과 컴포넌트 종류.</summary>
/// <param name="Name">컴포넌트 이름 (예: Addition).</param>
/// <param name="Category">탭 이름 (예: Maths).</param>
/// <param name="SubCategory">탭 안 묶음 이름 (예: Operators).</param>
/// <param name="ComponentGuid">컴포넌트 종류. <c>*</c> / <c>#</c>가 비교한다.</param>
public sealed record SearchItem(Guid Id, string NickName, string Name, string Category, string SubCategory, Guid ComponentGuid);

/// <summary>
/// 찾을 것: 글자(<c>/</c>)이거나 컴포넌트 종류(<c>*</c> / <c>#</c>). 마지막 찾기로 기억해서 <c>n</c> / <c>N</c>이 되풀이한다.
/// </summary>
public abstract record SearchQuery
{
    public abstract bool Matches(SearchItem item);

    /// <summary>상태줄에 보일 이름 (<c>/abc</c>, <c>*Addition</c>).</summary>
    public abstract string Label { get; }
}

/// <summary>
/// <c>/</c>: NickName, Name, 탭·묶음 이름 중 하나에 글자가 들어 있으면 (대소문자 무시) 맞음.
/// 빈 글자는 아무것도 맞지 않는다.
/// </summary>
public sealed record TextQuery(string Text) : SearchQuery
{
    public override bool Matches(SearchItem item) =>
        Text.Length > 0 && (Contains(item.NickName) || Contains(item.Name) || Contains(item.Category) || Contains(item.SubCategory));

    bool Contains(string? field) => field != null && field.IndexOf(Text, StringComparison.OrdinalIgnoreCase) >= 0;

    public override string Label => "/" + Text;
}

/// <summary><c>*</c> / <c>#</c>: 커서 객체와 같은 컴포넌트 종류(ComponentGuid).</summary>
public sealed record TypeQuery(Guid ComponentGuid, string Name) : SearchQuery
{
    public override bool Matches(SearchItem item) => item.ComponentGuid == ComponentGuid;

    public override string Label => "*" + Name;
}

/// <summary>찾기 결과. Position은 맞는 객체들 중 1부터 센 순번 (<c>[3/12]</c>의 3).</summary>
/// <param name="Wrapped">끝을 넘어 처음(앞으로) / 처음을 넘어 끝(뒤로)으로 돌아갔는지.</param>
public sealed record SearchHit(Guid Target, int Position, int Total, bool Wrapped);

/// <summary>
/// 찾기의 순서와 다음 대상 고르기. 문서 순서는 <see cref="Order"/> 하나로 고정해서
/// <c>n</c>을 계속 누르면 모든 맞는 객체를 한 번씩 돈다.
/// </summary>
public static class Search
{
    /// <summary>
    /// 문서 순서: 덩어리 순서(<c>{</c> / <c>}</c>, <c>g=</c>가 쌓는 순서) → 덩어리 안에서 층(정렬의 열, 왼쪽부터) →
    /// 세로 가운데(위부터) → 왼쪽 끝. 정렬된 화면에서는 덩어리마다 왼쪽 열부터 위→아래로 읽는 순서다.
    /// 층이 없는 객체는 맨 뒤에 위→아래, 왼쪽→오른쪽.
    /// </summary>
    public static IReadOnlyList<Guid> Order(CanvasGraph graph) =>
        graph.Nodes
            .Select(n => (Node: n, Layer: graph.LayerOf(n.Id)))
            .OrderBy(x => x.Layer is null ? 1 : 0)
            .ThenBy(x => x.Layer?.Island ?? 0)
            .ThenBy(x => x.Layer?.Layer ?? 0)
            .ThenBy(x => x.Node.CenterY)
            .ThenBy(x => x.Node.X)
            .Select(x => x.Node.Id)
            .ToList();

    /// <summary>
    /// <paramref name="from"/> 다음(Forward) / 이전의 count번째 맞는 객체. 끝을 넘으면 반대쪽 끝에서 이어간다 (vim의 wrapscan).
    /// from 자신은 건너뛰므로, 맞는 객체가 from 하나뿐이면 돌아서 from으로 온다.
    /// </summary>
    /// <param name="order">문서 순서 (<see cref="Order"/>).</param>
    /// <param name="from">커서 객체. null이거나 순서에 없으면 앞으로는 처음부터, 뒤로는 끝부터 센다.</param>
    /// <returns>맞는 객체가 없으면 null.</returns>
    public static SearchHit? Next(IReadOnlyList<Guid> order, Func<Guid, bool> matches, Guid? from, bool forward, int count = 1)
    {
        var hits = new List<int>();
        var here = -1;
        for (var i = 0; i < order.Count; i++)
        {
            if (from == order[i]) here = i;
            if (matches(order[i])) hits.Add(i);
        }
        if (hits.Count == 0)
            return null;

        // from보다 뒤(앞으로) / 앞(뒤로)에 있는 첫 맞는 객체의 순번. 없으면 반대쪽 끝으로 돈다
        int index;
        var wrapped = false;
        if (forward)
        {
            index = hits.FindIndex(h => h > here);
            if (index < 0) { index = 0; wrapped = true; }
        }
        else
        {
            var start = here < 0 ? order.Count : here;
            index = hits.FindLastIndex(h => h < start);
            if (index < 0) { index = hits.Count - 1; wrapped = true; }
        }

        // 나머지 count - 1번
        var steps = Math.Max(0, count - 1);
        var target = forward ? index + steps : index - steps;
        if (target >= hits.Count || target < 0) wrapped = true;
        target = ((target % hits.Count) + hits.Count) % hits.Count;

        return new SearchHit(order[hits[target]], target + 1, hits.Count, wrapped);
    }

    /// <summary>맞는 객체들 (incsearch 강조용).</summary>
    public static IReadOnlyList<Guid> All(IEnumerable<SearchItem> items, SearchQuery query) =>
        items.Where(query.Matches).Select(i => i.Id).ToList();

    /// <summary>상태줄 문구: <c>[3/12] /abc</c>, 돌았으면 vim처럼 <c>search hit BOTTOM, continuing at TOP</c>를 붙인다.</summary>
    public static string Describe(SearchHit hit, SearchQuery query, bool forward)
    {
        var text = $"[{hit.Position}/{hit.Total}] {query.Label}";
        if (hit.Wrapped)
            text += forward ? "   search hit BOTTOM, continuing at TOP" : "   search hit TOP, continuing at BOTTOM";
        return text;
    }
}
