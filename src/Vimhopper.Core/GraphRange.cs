using System;
using System.Collections.Generic;
using System.Linq;

namespace Vimhopper.Core;

/// <summary>
/// 연산자 범위(<c>yL</c>, <c>=2h</c>, <c>yp</c> 등)와 비주얼 모드 <c>H</c>/<c>L</c>이 고르는 객체들.
/// h/l 이동과 달리 맨 위 입력만이 아니라 <b>모든</b> 입력·출력 와이어를 따라간다 (원뿔 모양).
/// <c>p</c>는 방향 없이 덩어리 전체.
/// </summary>
public static class GraphRange
{
    /// <summary>
    /// starts에서 상류(upstream) 또는 하류로 depth단계까지 이어진 객체들. depth가 null이면 끝까지.
    /// 풀리는 Relay는 정렬에서 없어지는 객체라 단계로 세지 않고 지나간다 (범위에는 들어감).
    /// </summary>
    /// <returns>starts를 포함하고, 가까운 단계부터의 순서.</returns>
    public static IReadOnlyList<Guid> Collect(CanvasGraph graph, IEnumerable<Guid> starts, bool upstream, int? depth)
    {
        var result = new List<Guid>();
        var seen = new HashSet<Guid>();
        var frontier = new List<Guid>();
        foreach (var start in starts)
            if (graph.Find(start) != null && seen.Add(start))
            {
                result.Add(start);
                frontier.Add(start);
            }

        for (var step = 0; frontier.Count > 0 && (depth is not { } d || step < d); step++)
        {
            var next = new List<Guid>();
            // Relay는 같은 단계 안에서 바로 이어서 넓힌다
            var stack = new Stack<Guid>(frontier);
            while (stack.Count > 0)
            {
                foreach (var id in Neighbors(graph, stack.Pop(), upstream))
                {
                    if (!seen.Add(id)) continue;
                    result.Add(id);
                    if (graph.Find(id)!.IsRelay)
                        stack.Push(id);
                    else
                        next.Add(id);
                }
            }
            frontier = next;
        }
        return result;
    }

    public static IReadOnlyList<Guid> Collect(CanvasGraph graph, Guid start, WireRange range) =>
        range.Kind == RangeKind.Island
            ? Island(graph, start)
            : Collect(graph, new[] { start }, range.Upstream, range.Depth);

    /// <summary>
    /// <c>p</c> 범위와 <c>==</c>의 대상: 와이어 방향과 무관하게 start와 이어진 객체 전부 (Relay 포함).
    /// </summary>
    /// <returns>start를 포함하고, 가까운 객체부터의 순서. start가 그래프에 없으면 빈 목록.</returns>
    public static IReadOnlyList<Guid> Island(CanvasGraph graph, Guid start)
    {
        var result = new List<Guid>();
        if (graph.Find(start) == null)
            return result;

        var seen = new HashSet<Guid> { start };
        var queue = new Queue<Guid>(seen);
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            result.Add(id);
            foreach (var next in graph.Neighbors(id))
                if (graph.Find(next) != null && seen.Add(next))
                    queue.Enqueue(next);
        }
        return result;
    }

    static IEnumerable<Guid> Neighbors(CanvasGraph graph, Guid id, bool upstream)
    {
        var node = graph.Find(id)!;
        return (upstream ? node.Inputs : node.Outputs)
            .SelectMany(list => list)
            .Select(p => p.Node)
            .Where(n => n != id && graph.Find(n) != null);
    }
}
