using System;
using System.Collections.Generic;
using System.Linq;

namespace Vimhopper.Core;

/// <summary>
/// 연산자 범위(<c>yL</c>, <c>=2h</c> 등)와 비주얼 모드 <c>H</c>/<c>L</c>이 고르는 객체들.
/// h/l 이동과 달리 맨 위 입력만이 아니라 <b>모든</b> 입력·출력 와이어를 따라간다 (원뿔 모양).
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
        Collect(graph, new[] { start }, range.Upstream, range.Depth);

    static IEnumerable<Guid> Neighbors(CanvasGraph graph, Guid id, bool upstream)
    {
        var node = graph.Find(id)!;
        return (upstream ? node.Inputs : node.Outputs)
            .SelectMany(list => list)
            .Select(p => p.Node)
            .Where(n => n != id && graph.Find(n) != null);
    }
}
