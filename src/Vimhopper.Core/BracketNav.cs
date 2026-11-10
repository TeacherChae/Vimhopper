using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Vimhopper.Core;

/// <summary><c>]</c> / <c>[</c> 다음에 오는 대상: <c>e</c> 오류, <c>w</c> 경고, <c>s</c> 느린 객체, <c>g</c> 그룹.</summary>
public enum BracketTarget { Error, Warning, Slow, Group }

/// <summary>객체의 런타임 메시지 중 가장 높은 등급 (Remark는 None으로 친다).</summary>
public enum MessageLevel { None, Warning, Error }

/// <summary>
/// <c>]</c> / <c>[</c> 순회의 대상 고르기와 순서. 다음 대상은 <see cref="Search.Next"/>로 고른다 (끝을 넘으면 반대쪽 끝에서 이어감).
/// </summary>
public static class BracketNav
{
    /// <summary>
    /// 원인인 객체: 등급이 정확히 <paramref name="level"/>이고, 상류(입력 와이어를 거슬러, Relay 포함, 끝까지)에
    /// 등급이 <paramref name="level"/> 이상인 객체가 없는 것. 경고는 상류에 경고나 오류가 있으면 번진 것으로 보고 건너뛴다.
    /// </summary>
    /// <param name="levels">객체의 등급. 그래프에 없는 객체는 상류로 따라가지 않는다.</param>
    public static HashSet<Guid> RootCauses(CanvasGraph graph, Func<Guid, MessageLevel> levels, MessageLevel level)
    {
        // 객체마다 "상류에 level 이상인 객체가 있는지"를 한 번만 계산한다 (깊은 사슬도 넘치지 않게 직접 쌓음)
        var tainted = new Dictionary<Guid, bool>();
        bool Bad(Guid id) => levels(id) >= level;
        IEnumerable<Guid> Sources(GraphNode node) =>
            node.Inputs.SelectMany(list => list).Select(p => p.Node).Where(s => s != node.Id).Distinct();

        bool UpstreamTainted(Guid start)
        {
            if (tainted.TryGetValue(start, out var known)) return known;
            var stack = new Stack<Guid>();
            var visiting = new HashSet<Guid>();
            stack.Push(start);
            while (stack.Count > 0)
            {
                var id = stack.Peek();
                if (tainted.ContainsKey(id)) { stack.Pop(); continue; }
                var node = graph.Find(id);
                if (node == null) { tainted[id] = false; stack.Pop(); continue; }
                visiting.Add(id);

                var pending = new List<Guid>();
                var result = false;
                foreach (var source in Sources(node))
                {
                    if (Bad(source)) { result = true; break; }
                    if (tainted.TryGetValue(source, out var t)) { if (t) { result = true; break; } continue; }
                    // 순환(Grasshopper에는 없지만)은 깨끗한 것으로 친다
                    if (visiting.Contains(source)) continue;
                    pending.Add(source);
                }
                if (!result && pending.Count > 0)
                {
                    // 상류를 먼저 계산하고 이 객체로 돌아온다
                    foreach (var source in pending) stack.Push(source);
                    continue;
                }
                tainted[id] = result;
                stack.Pop();
            }
            return tainted[start];
        }

        var roots = new HashSet<Guid>();
        foreach (var node in graph.Nodes)
            if (levels(node.Id) == level && !UpstreamTainted(node.Id))
                roots.Add(node.Id);
        return roots;
    }

    /// <summary>
    /// 계산 시간이 긴 순서. 0 이하는 빠진다. 같은 시간이면 문서 순서(<paramref name="order"/>), 순서에 없는 객체는 뒤로.
    /// </summary>
    public static IReadOnlyList<Guid> Slowest(IEnumerable<(Guid Id, double Milliseconds)> times, IReadOnlyList<Guid> order)
    {
        var index = new Dictionary<Guid, int>();
        for (var i = 0; i < order.Count; i++) index[order[i]] = i;
        return times
            .Where(t => t.Milliseconds > 0)
            .OrderByDescending(t => t.Milliseconds)
            .ThenBy(t => index.TryGetValue(t.Id, out var i) ? i : int.MaxValue)
            .Select(t => t.Id)
            .ToList();
    }

    /// <summary>
    /// 그룹마다 문서 순서에서 처음 오는 멤버 (<c>]g</c>가 커서를 놓는 곳). 멤버가 순서에 하나도 없는 그룹은 빠진다.
    /// 두 그룹의 첫 멤버가 같으면(중첩·겹친 그룹) 멤버가 적은 그룹(안쪽 그룹)을 쓰고, 멤버 수도 같으면 먼저 온 그룹을 쓴다.
    /// 멤버 수는 순서에 있는 멤버만 센다.
    /// </summary>
    public static IReadOnlyDictionary<Guid, T> GroupStarts<T>(IReadOnlyList<Guid> order, IEnumerable<(T Group, IEnumerable<Guid> Members)> groups)
    {
        var index = new Dictionary<Guid, int>();
        for (var i = 0; i < order.Count; i++) index[order[i]] = i;
        var starts = new Dictionary<Guid, (T Group, int Size)>();
        foreach (var (group, members) in groups)
        {
            var first = -1;
            var size = 0;
            foreach (var m in members.Distinct())
            {
                if (!index.TryGetValue(m, out var i)) continue;
                size++;
                if (first < 0 || i < first) first = i;
            }
            if (first < 0) continue;
            var start = order[first];
            if (!starts.TryGetValue(start, out var known) || size < known.Size)
                starts[start] = (group, size);
        }
        return starts.ToDictionary(p => p.Key, p => p.Value.Group);
    }

    /// <summary>계산 시간 표기: <c>0.4 ms</c>, <c>12 ms</c>, <c>1.2 s</c>.</summary>
    public static string FormatTime(double milliseconds)
    {
        var c = CultureInfo.InvariantCulture;
        if (milliseconds < 10) return milliseconds.ToString("0.#", c) + " ms";
        if (milliseconds < 1000) return milliseconds.ToString("0", c) + " ms";
        return (milliseconds / 1000).ToString("0.0#", c) + " s";
    }

    /// <summary>상태줄 문구: <c>{what} 2/3</c>, 돌았으면 찾기와 같은 vim 문구를 붙인다.</summary>
    /// <param name="what">대상 이름 (<c>오류 원인</c>).</param>
    /// <param name="detail">뒤에 붙일 것 (<c>(오류 12개)</c>). 비어 있으면 붙이지 않음.</param>
    public static string Describe(SearchHit hit, string what, string detail, bool forward)
    {
        var text = $"{what} {hit.Position}/{hit.Total}";
        if (detail.Length > 0) text += " " + detail;
        if (hit.Wrapped)
            text += forward ? "   search hit BOTTOM, continuing at TOP" : "   search hit TOP, continuing at BOTTOM";
        return text;
    }
}
