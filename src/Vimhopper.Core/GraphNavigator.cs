using System;
using System.Collections.Generic;
using System.Linq;

namespace Vimhopper.Core;

/// <summary>와이어 한쪽 끝: 객체와 그 객체의 파라미터 번호.</summary>
public readonly struct ParamRef
{
    public ParamRef(Guid node, int index)
    {
        Node = node;
        Index = index;
    }

    public Guid Node { get; }
    public int Index { get; }
}

/// <summary>탐색용 객체 하나. 좌표는 캔버스 좌표의 왼쪽 위.</summary>
public sealed class GraphNode
{
    /// <param name="isRelay">풀리는 Relay인지 (층 계산을 정렬과 맞추기 위해).</param>
    /// <param name="inputs">입력 i에 연결된 출력들 (Grasshopper 순서).</param>
    /// <param name="outputs">출력 i를 받는 입력들 (Grasshopper 순서).</param>
    public GraphNode(
        Guid id, float x, float y, float width, float height, bool isRelay,
        IReadOnlyList<IReadOnlyList<ParamRef>> inputs, IReadOnlyList<IReadOnlyList<ParamRef>> outputs)
    {
        Id = id;
        X = x;
        Y = y;
        Width = width;
        Height = height;
        IsRelay = isRelay;
        Inputs = inputs;
        Outputs = outputs;
    }

    public Guid Id { get; }
    public float X { get; }
    public float Y { get; }
    public float Width { get; }
    public float Height { get; }
    public bool IsRelay { get; }
    public IReadOnlyList<IReadOnlyList<ParamRef>> Inputs { get; }
    public IReadOnlyList<IReadOnlyList<ParamRef>> Outputs { get; }

    public float CenterY => Y + Height / 2;

    /// <summary>연결된 입력 중 맨 위 입력의 첫 출처. 자동 정렬이 이 와이어를 수평으로 맞춘다.</summary>
    public ParamRef? TopSource => Inputs.FirstOrDefault(i => i.Count > 0)?[0];
}

/// <summary>캔버스의 와이어 연결. 층 번호는 처음 필요할 때 자동 정렬과 같은 규칙으로 계산한다.</summary>
public sealed class CanvasGraph
{
    readonly Dictionary<Guid, GraphNode> _nodes;
    Dictionary<Guid, (int Island, int Layer)>? _layers;

    public CanvasGraph(IEnumerable<GraphNode> nodes) => _nodes = nodes.ToDictionary(n => n.Id);

    public IReadOnlyCollection<GraphNode> Nodes => _nodes.Values;

    public GraphNode? Find(Guid id) => _nodes.TryGetValue(id, out var node) ? node : null;

    /// <summary>와이어 방향과 무관하게 이웃한 객체들.</summary>
    public IEnumerable<Guid> Neighbors(Guid id) =>
        Find(id) is { } node
            ? node.Inputs.Concat(node.Outputs).SelectMany(list => list).Select(p => p.Node).Where(n => n != id).Distinct()
            : Enumerable.Empty<Guid>();

    /// <summary>덩어리 번호(문서 전체 정렬이 쌓는 순서)와 그 안의 층 번호. 계산되지 않는 객체면 null.</summary>
    public (int Island, int Layer)? LayerOf(Guid id)
    {
        _layers ??= ComputeLayers();
        return _layers.TryGetValue(id, out var layer) ? layer : null;
    }

    Dictionary<Guid, (int Island, int Layer)> ComputeLayers()
    {
        var nodes = _nodes.Values
            .Select(n => new LayoutNode(n.Id, n.X, n.Y, n.Width, n.Height, n.IsRelay))
            .ToList();
        var edges = _nodes.Values
            .SelectMany(n => n.Inputs.SelectMany(sources => sources
                .Where(s => s.Node != n.Id && _nodes.ContainsKey(s.Node))
                .Select(s => new LayoutEdge(s.Node, 0, n.Id, 0))))
            .ToList();
        return CanvasLayout.Layers(nodes, edges);
    }
}

/// <summary>
/// hjkl: 현재 객체만 보고 이동할 곳을 정한다 (이동 기록 없음). 기준이 자동 정렬과 같아서
/// 정렬된 화면에서는 h/l이 수평 와이어를 따라 좌우로, j/k가 같은 열을 따라 위아래로 움직인다.
/// </summary>
public static class GraphNavigator
{
    /// <summary>
    /// <c>h</c>: 맨 위 연결된 입력의 출처. <c>{n}h</c>: 입력 n의 출처.
    /// 한 입력에 여러 와이어가 연결되어 있으면 Grasshopper 순서로 첫 번째.
    /// </summary>
    public static Guid? Parent(CanvasGraph graph, Guid current, int? input = null)
    {
        if (graph.Find(current) is not { } node)
            return null;
        if (input is not { } n)
            return node.TopSource?.Node;
        return n < node.Inputs.Count && node.Inputs[n].Count > 0 ? node.Inputs[n][0].Node : null;
    }

    /// <summary>
    /// <c>l</c>: 자식 중 나를 맨 위 입력으로 받는 객체 (정렬에서 나와 수평 와이어로 이어진 객체).
    /// 없으면 자식 중 첫 번째. <c>{n}l</c>: 출력 n을 받는 객체 중에서 같은 규칙으로.
    /// 자식 순서는 출력 번호, 같은 출력 안에서는 화면 위→아래.
    /// </summary>
    public static Guid? Child(CanvasGraph graph, Guid current, int? output = null)
    {
        if (graph.Find(current) is not { } node)
            return null;

        var outputs = output is { } n
            ? n < node.Outputs.Count ? new[] { node.Outputs[n] } : Array.Empty<IReadOnlyList<ParamRef>>()
            : node.Outputs.ToArray();
        var children = outputs
            .SelectMany(recipients => recipients
                .Select(r => graph.Find(r.Node))
                .OfType<GraphNode>()
                .Where(c => c.Id != current)
                .OrderBy(c => c.CenterY)
                .ThenBy(c => c.X))
            .Distinct()
            .ToList();

        var aligned = children.FirstOrDefault(c => c.TopSource?.Node == current);
        return (aligned ?? children.FirstOrDefault())?.Id;
    }

    /// <summary>
    /// <c>j</c>/<c>k</c>: 같은 층에서 step만큼 아래(+)/위(-). 덩어리 안에서는 화면 위→아래 순서이고,
    /// 덩어리 끝에서 계속 가면 다음 덩어리의 같은 층으로 넘어간다. 끝을 넘으면 끝에서 멈춘다.
    /// </summary>
    /// <returns>움직이지 못하면 null.</returns>
    public static Guid? Sibling(CanvasGraph graph, Guid current, int step)
    {
        if (graph.LayerOf(current) is not { } here)
            return null;

        var column = graph.Nodes
            .Select(n => (Node: n, Layer: graph.LayerOf(n.Id)))
            .Where(x => x.Layer is { } l && l.Layer == here.Layer)
            .OrderBy(x => x.Layer!.Value.Island)
            .ThenBy(x => x.Node.CenterY)
            .ThenBy(x => x.Node.X)
            .Select(x => x.Node.Id)
            .ToList();

        var index = column.IndexOf(current);
        var target = Math.Max(0, Math.Min(column.Count - 1, index + step));
        return target == index ? null : column[target];
    }
}
