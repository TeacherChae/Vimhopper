using System;
using System.Collections.Generic;
using System.Linq;

namespace Vimhopper.Core;

/// <summary>정렬할 객체 하나. 좌표는 캔버스 좌표의 왼쪽 위 (y는 아래로 증가).</summary>
public readonly struct LayoutNode
{
    public LayoutNode(Guid id, float x, float y, float width, float height)
    {
        Id = id;
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public Guid Id { get; }
    public float X { get; }
    public float Y { get; }
    public float Width { get; }
    public float Height { get; }
}

/// <summary>와이어 하나. 오프셋은 각 객체의 위쪽 가장자리에서 출력/입력 그립까지의 세로 거리.</summary>
public readonly struct LayoutEdge
{
    public LayoutEdge(Guid from, float fromOffset, Guid to, float toOffset)
    {
        From = from;
        FromOffset = fromOffset;
        To = to;
        ToOffset = toOffset;
    }

    public Guid From { get; }
    public float FromOffset { get; }
    public Guid To { get; }
    public float ToOffset { get; }
}

public sealed class LayoutOptions
{
    /// <summary>열 사이 가로 간격.</summary>
    public float ColumnGap { get; set; } = 40;

    /// <summary>같은 열 안 객체 사이 세로 간격. 가상 노드(지나가는 와이어)끼리는 절반.</summary>
    public float RowGap { get; set; } = 16;

    /// <summary>문서 전체 정렬에서 덩어리 사이 간격.</summary>
    public float IslandGap { get; set; } = 60;
}

/// <summary>
/// 와이어 연결을 따라 객체를 열(층)로 나눠 배치하는 자동 정렬 (Sugiyama 방식).
/// </summary>
/// <remarks>
/// <list type="number">
/// <item>층 나누기: 컴포넌트는 소스로부터의 최장 거리. 입력이 없는 객체(슬라이더, 패널 등)는
/// 처음 쓰이는 객체 바로 앞 열에 붙인다.</item>
/// <item>두 층 이상 건너뛰는 와이어는 중간 층마다 가상 노드를 끼워 지나갈 자리를 만든다.</item>
/// <item>층 안 순서: 기존 화면 위→아래 순서에서 시작해 와이어 교차가 줄도록 바꾼다.</item>
/// <item>좌표: 열은 오른쪽 정렬, 세로 위치는 맨 위 입력의 와이어가 수평이 되게 한다
/// (입력이 없으면 맨 위 출력의 와이어). 같은 열에서 겹치면 아래쪽 객체가 밀려난다.</item>
/// </list>
/// 같은 입력에는 같은 결과가 나오고, 이미 정렬된 배치를 다시 정렬해도 움직이지 않는다.
/// </remarks>
public static class CanvasLayout
{
    /// <summary>
    /// 주어진 객체들을 한 덩어리로 정렬한다. 결과 영역의 왼쪽 위는 원래 영역의 왼쪽 위와 같다.
    /// </summary>
    /// <returns>객체별 새 왼쪽 위 좌표. 입력의 모든 객체를 포함한다.</returns>
    public static Dictionary<Guid, (float X, float Y)> Arrange(
        IReadOnlyList<LayoutNode> nodes, IReadOnlyList<LayoutEdge> edges, LayoutOptions? options = null) =>
        new LayeredLayout(nodes, edges, options ?? new LayoutOptions()).Run();

    /// <summary>
    /// 와이어로 이어진 덩어리마다 따로 정렬하고, 커진 덩어리가 위쪽 덩어리와 겹치면 아래로 민다.
    /// 연결이 없는 객체는 겹칠 때만 움직인다.
    /// </summary>
    public static Dictionary<Guid, (float X, float Y)> ArrangeAll(
        IReadOnlyList<LayoutNode> nodes, IReadOnlyList<LayoutEdge> edges, LayoutOptions? options = null)
    {
        options ??= new LayoutOptions();
        var result = new Dictionary<Guid, (float X, float Y)>();
        var placed = new List<Rect>();

        foreach (var island in Islands(nodes, edges)
                     .OrderBy(i => i.Min(n => n.Y))
                     .ThenBy(i => i.Min(n => n.X)))
        {
            var ids = new HashSet<Guid>(island.Select(n => n.Id));
            var islandEdges = edges.Where(e => ids.Contains(e.From) && ids.Contains(e.To)).ToList();
            var positions = Arrange(island, islandEdges, options);
            var bounds = Rect.Of(island, positions);

            // 이미 놓인 덩어리와 겹치는 동안 그 아래로 민다 (dy는 커지기만 하므로 끝난다)
            var dy = 0f;
            bool moved;
            do
            {
                moved = false;
                foreach (var other in placed)
                {
                    if (!bounds.Offset(dy).Overlaps(other, options.IslandGap)) continue;
                    dy = other.Bottom + options.IslandGap - bounds.Top;
                    moved = true;
                }
            } while (moved);

            foreach (var pair in positions)
                result[pair.Key] = (pair.Value.X, pair.Value.Y + dy);
            placed.Add(bounds.Offset(dy));
        }

        return result;
    }

    /// <summary>와이어 방향을 무시하고 이어진 객체끼리 묶는다.</summary>
    static IEnumerable<List<LayoutNode>> Islands(IReadOnlyList<LayoutNode> nodes, IReadOnlyList<LayoutEdge> edges)
    {
        var index = new Dictionary<Guid, int>();
        for (var i = 0; i < nodes.Count; i++)
            index[nodes[i].Id] = i;

        var parent = Enumerable.Range(0, nodes.Count).ToArray();
        int Find(int i)
        {
            while (parent[i] != i)
                i = parent[i] = parent[parent[i]];
            return i;
        }

        foreach (var e in edges)
            if (index.TryGetValue(e.From, out var a) && index.TryGetValue(e.To, out var b))
                parent[Find(a)] = Find(b);

        return Enumerable.Range(0, nodes.Count)
            .GroupBy(Find)
            .Select(g => g.Select(i => nodes[i]).ToList());
    }

    readonly struct Rect
    {
        Rect(float left, float top, float right, float bottom)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }

        public float Left { get; }
        public float Top { get; }
        public float Right { get; }
        public float Bottom { get; }

        public static Rect Of(IEnumerable<LayoutNode> nodes, IReadOnlyDictionary<Guid, (float X, float Y)> positions)
        {
            float left = float.MaxValue, top = float.MaxValue, right = float.MinValue, bottom = float.MinValue;
            foreach (var n in nodes)
            {
                var p = positions[n.Id];
                left = Math.Min(left, p.X);
                top = Math.Min(top, p.Y);
                right = Math.Max(right, p.X + n.Width);
                bottom = Math.Max(bottom, p.Y + n.Height);
            }
            return new Rect(left, top, right, bottom);
        }

        public Rect Offset(float dy) => new(Left, Top + dy, Right, Bottom + dy);

        public bool Overlaps(Rect other, float gap) =>
            Left < other.Right + gap && other.Left < Right + gap &&
            Top < other.Bottom + gap && other.Top < Bottom + gap;
    }
}

/// <summary>CanvasLayout.Arrange 한 번의 계산.</summary>
sealed class LayeredLayout
{
    sealed class Vertex
    {
        public Vertex(int node, float width, float height)
        {
            Node = node;
            Width = width;
            Height = height;
        }

        /// <summary>원래 객체 인덱스. 가상 노드면 -1.</summary>
        public int Node { get; }
        public float Width { get; }
        public float Height { get; }
        public bool IsVirtual => Node < 0;

        public int Layer;
        public int Order;
        public int Rank;      // 층 안에서 실제 객체끼리의 순서 (가상 노드 제외)
        public int Sequence;  // 만든 순서. 완전히 같은 조건의 가상 노드끼리 순서를 정할 때
        public float InitialKey;
        public float X;
        public float Y;
        public readonly List<Segment> Left = new();
        public readonly List<Segment> Right = new();
    }

    /// <summary>이웃한 두 층 사이 와이어 조각. 오프셋은 각 정점의 위쪽 가장자리 기준.</summary>
    sealed class Segment
    {
        public Segment(Vertex from, float fromOffset, Vertex to, float toOffset)
        {
            From = from;
            FromOffset = fromOffset;
            To = to;
            ToOffset = toOffset;
        }

        public Vertex From { get; }
        public float FromOffset { get; }
        public Vertex To { get; }
        public float ToOffset { get; }
    }

    struct Wire
    {
        public int From;
        public float FromOffset;
        public int To;
        public float ToOffset;
    }

    // 층 안 순서 바꾸기 / 세로 위치 맞추기 반복 횟수
    const int OrderingSweeps = 12;
    const int OrderingRounds = 20;
    const int PlacementRounds = 9;

    readonly IReadOnlyList<LayoutNode> _nodes;
    readonly LayoutOptions _options;
    readonly List<Wire> _wires = new();
    readonly List<Vertex> _vertices = new();
    readonly List<List<Vertex>> _layers = new();

    public LayeredLayout(IReadOnlyList<LayoutNode> nodes, IReadOnlyList<LayoutEdge> edges, LayoutOptions options)
    {
        _nodes = nodes;
        _options = options;

        var index = new Dictionary<Guid, int>();
        for (var i = 0; i < nodes.Count; i++)
            index[nodes[i].Id] = i;

        foreach (var e in edges)
        {
            if (!index.TryGetValue(e.From, out var from) || !index.TryGetValue(e.To, out var to) || from == to)
                continue;
            _wires.Add(new Wire { From = from, FromOffset = e.FromOffset, To = to, ToOffset = e.ToOffset });
        }
    }

    public Dictionary<Guid, (float X, float Y)> Run()
    {
        var result = new Dictionary<Guid, (float X, float Y)>();
        if (_nodes.Count == 0)
            return result;

        BreakCycles();
        var layers = AssignLayers();
        BuildVertices(layers);
        OrderLayers();
        PlaceColumns();
        PlaceRows();

        // 원래 영역의 왼쪽 위에 맞추고 정수 좌표로
        var real = _vertices.Where(v => !v.IsVirtual).ToList();
        var dx = _nodes.Min(n => n.X) - real.Min(v => v.X);
        var dy = _nodes.Min(n => n.Y) - real.Min(v => v.Y);
        foreach (var v in real)
            result[_nodes[v.Node].Id] = ((float)Math.Round(v.X + dx), (float)Math.Round(v.Y + dy));
        return result;
    }

    // ───────────────────────── 1. 순환 제거 ─────────────────────────

    /// <summary>
    /// 순환이 있으면 층을 나눌 수 없으므로 DFS에서 되돌아가는 와이어를 뒤집는다.
    /// 화면 왼쪽 객체부터 탐색하므로 화면상 오른쪽→왼쪽으로 가는 와이어가 뒤집힌다.
    /// </summary>
    void BreakCycles()
    {
        var outgoing = Outgoing();
        var state = new int[_nodes.Count]; // 0 미방문, 1 탐색 중, 2 완료
        var roots = Enumerable.Range(0, _nodes.Count).OrderBy(i => _nodes[i].X).ThenBy(i => _nodes[i].Y);

        foreach (var root in roots)
        {
            if (state[root] != 0) continue;
            var stack = new Stack<(int Node, int Next)>();
            stack.Push((root, 0));
            state[root] = 1;

            while (stack.Count > 0)
            {
                var (node, next) = stack.Pop();
                if (next >= outgoing[node].Count)
                {
                    state[node] = 2;
                    continue;
                }

                stack.Push((node, next + 1));
                var w = outgoing[node][next];
                var target = _wires[w].To;
                if (state[target] == 1)
                    Reverse(w);
                else if (state[target] == 0)
                {
                    state[target] = 1;
                    stack.Push((target, 0));
                }
            }
        }
    }

    void Reverse(int w)
    {
        var wire = _wires[w];
        _wires[w] = new Wire { From = wire.To, FromOffset = wire.ToOffset, To = wire.From, ToOffset = wire.FromOffset };
    }

    List<int>[] Outgoing()
    {
        var outgoing = new List<int>[_nodes.Count];
        for (var i = 0; i < outgoing.Length; i++)
            outgoing[i] = new List<int>();
        for (var w = 0; w < _wires.Count; w++)
            outgoing[_wires[w].From].Add(w);
        return outgoing;
    }

    // ───────────────────────── 2. 층 나누기 ─────────────────────────

    /// <summary>
    /// 컴포넌트는 소스로부터의 최장 거리 (모든 부모보다 오른쪽).
    /// 입력이 없는 객체는 자식 중 가장 왼쪽 층의 바로 앞으로 당긴다.
    /// </summary>
    int[] AssignLayers()
    {
        var count = _nodes.Count;
        var outgoing = Outgoing();
        var indegree = new int[count];
        foreach (var w in _wires)
            indegree[w.To]++;

        var layer = new int[count];
        var remaining = (int[])indegree.Clone();
        var queue = new Queue<int>(Enumerable.Range(0, count).Where(i => indegree[i] == 0));
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            foreach (var w in outgoing[node])
            {
                var to = _wires[w].To;
                layer[to] = Math.Max(layer[to], layer[node] + 1);
                if (--remaining[to] == 0)
                    queue.Enqueue(to);
            }
        }

        for (var i = 0; i < count; i++)
            if (indegree[i] == 0 && outgoing[i].Count > 0)
                layer[i] = outgoing[i].Min(w => layer[_wires[w].To]) - 1;

        var min = layer.Min();
        for (var i = 0; i < count; i++)
            layer[i] -= min;
        return layer;
    }

    // ───────────────────────── 3. 가상 노드 ─────────────────────────

    void BuildVertices(int[] layers)
    {
        for (var i = 0; i < _nodes.Count; i++)
        {
            var n = _nodes[i];
            _vertices.Add(new Vertex(i, n.Width, n.Height) { Layer = layers[i], InitialKey = n.Y + n.Height / 2 });
        }

        foreach (var w in _wires)
        {
            var from = _vertices[w.From];
            var to = _vertices[w.To];
            var span = to.Layer - from.Layer;

            // 가상 노드의 처음 순서는 양 끝 그립 높이를 잇는 직선 위
            var y0 = _nodes[w.From].Y + w.FromOffset;
            var y1 = _nodes[w.To].Y + w.ToOffset;

            var previous = from;
            var previousOffset = w.FromOffset;
            for (var step = 1; step < span; step++)
            {
                var dummy = new Vertex(-1, 0, 0)
                {
                    Sequence = _vertices.Count,
                    Layer = from.Layer + step,
                    InitialKey = y0 + (y1 - y0) * step / span,
                };
                _vertices.Add(dummy);
                Connect(previous, previousOffset, dummy, 0);
                previous = dummy;
                previousOffset = 0;
            }
            Connect(previous, previousOffset, to, w.ToOffset);
        }

        var layerCount = _vertices.Max(v => v.Layer) + 1;
        for (var l = 0; l < layerCount; l++)
            _layers.Add(new List<Vertex>());
        foreach (var v in _vertices)
            _layers[v.Layer].Add(v);

        // 처음 순서: 기존 화면 위→아래 (같으면 왼쪽→오른쪽, 그다음 입력 순서)
        foreach (var layer in _layers)
        {
            var sorted = layer
                .OrderBy(v => v.InitialKey)
                .ThenBy(v => v.IsVirtual ? float.MaxValue : _nodes[v.Node].X)
                .ToList();
            layer.Clear();
            layer.AddRange(sorted);
            Renumber(layer);
        }
    }

    static void Connect(Vertex from, float fromOffset, Vertex to, float toOffset)
    {
        var segment = new Segment(from, fromOffset, to, toOffset);
        from.Right.Add(segment);
        to.Left.Add(segment);
    }

    static void Renumber(List<Vertex> layer)
    {
        for (var i = 0; i < layer.Count; i++)
            layer[i].Order = i;
    }

    // ───────────────────────── 4. 층 안 순서 (교차 줄이기) ─────────────────────────

    /// <summary>
    /// 이웃 층 연결 대상의 평균 위치(barycenter)로 정렬하는 것을 좌→우, 우→좌로 번갈아 반복하고,
    /// 이웃한 두 정점을 바꿔 교차가 줄면 바꾼다. 교차가 가장 적었던 순서를 쓰고,
    /// 같으면 처음 순서(기존 화면 순서)를 유지한다.
    /// </summary>
    /// <remarks>
    /// 더 나은 순서를 찾으면 그 순서에서 다시 탐색하고, 한 번의 탐색에서 나아지지 않을 때 멈춘다.
    /// 그렇게 끝난 순서는 다시 탐색해도 그대로이므로, 정렬된 결과를 다시 정렬해도 움직이지 않는다.
    /// </remarks>
    void OrderLayers()
    {
        PlaceVirtuals();
        var best = Snapshot();
        var bestCrossings = Crossings();

        var improved = true;
        for (var round = 0; improved && bestCrossings > 0 && round < OrderingRounds; round++)
        {
            improved = false;
            Restore(best);
            for (var sweep = 0; sweep < OrderingSweeps && bestCrossings > 0; sweep++)
            {
                var forward = sweep % 2 == 0;
                if (forward)
                    for (var l = 1; l < _layers.Count; l++)
                        SortByBarycenter(_layers[l], fromLeft: true);
                else
                    for (var l = _layers.Count - 2; l >= 0; l--)
                        SortByBarycenter(_layers[l], fromLeft: false);
                Transpose();
                PlaceVirtuals();

                var crossings = Crossings();
                if (crossings < bestCrossings)
                {
                    best = Snapshot();
                    bestCrossings = crossings;
                    improved = true;
                }
            }
        }

        Restore(best);
    }

    /// <summary>
    /// 실제 객체 순서는 그대로 두고, 가상 노드의 자리를 실제 객체 순서만으로 다시 정한다.
    /// </summary>
    /// <remarks>
    /// 교차 수가 같은 가상 노드 배치는 여럿일 수 있는데, 어느 것이 남는지가 처음 순서에 따라 달라지면
    /// 정렬된 결과를 다시 정렬할 때 실제 객체가 움직인다. 여기서 자리를 하나로 정해 그런 일을 막는다.
    /// 각 가상 노드는 (1) 왼쪽 층 와이어와의 교차, (2) 오른쪽이 실제 객체면 그쪽 교차가 가장 적은
    /// 실제 객체 사이 틈에 들어가고, 그래도 같으면 가장 위 틈을 고른다.
    /// </remarks>
    void PlaceVirtuals()
    {
        foreach (var layer in _layers)
        {
            var rank = 0;
            foreach (var v in layer)
                if (!v.IsVirtual)
                    v.Rank = rank++;
        }

        // 왼쪽 층부터: 가상 노드의 왼쪽 이웃 순서가 먼저 확정되어 있어야 한다
        foreach (var layer in _layers)
        {
            var virtuals = layer.Where(v => v.IsVirtual).ToList();
            if (virtuals.Count == 0) continue;
            var reals = layer.Where(v => !v.IsVirtual).ToList();

            var placed = virtuals
                .Select(d => (Vertex: d, Slot: BestSlot(d, reals), Left: LeftKey(d), End: EndKey(d)))
                .OrderBy(x => x.Slot)
                .ThenBy(x => x.Left)
                .ThenBy(x => x.End)
                .ThenBy(x => x.Vertex.Sequence)
                .ToList();

            layer.Clear();
            var k = 0;
            for (var slot = 0; slot <= reals.Count; slot++)
            {
                while (k < placed.Count && placed[k].Slot == slot)
                    layer.Add(placed[k++].Vertex);
                if (slot < reals.Count)
                    layer.Add(reals[slot]);
            }
            Renumber(layer);
        }
    }

    static float LeftKey(Vertex d) => Key(d.Left[0].From, d.Left[0].FromOffset);

    /// <summary>오른쪽 층 실제 객체끼리 비교하는 위치 (가상 노드 수와 무관).</summary>
    static float RankKey(Vertex v, float offset) =>
        v.Rank + (v.Height > 0 ? Math.Max(0, Math.Min(1, offset / v.Height)) : 0.5f);

    static float EndKey(Vertex d)
    {
        var segment = d.Right[0];
        while (segment.To.IsVirtual)
            segment = segment.To.Right[0];
        return segment.To.Layer * 1000f + RankKey(segment.To, segment.ToOffset);
    }

    static int BestSlot(Vertex d, List<Vertex> reals)
    {
        var left = LeftKey(d);
        var right = d.Right[0];
        float? rightKey = right.To.IsVirtual ? null : RankKey(right.To, right.ToOffset);

        // 틈 slot에 넣을 때의 교차 = (위쪽 실제 객체의 와이어 중 d보다 아래로 가는 것)
        //                          + (아래쪽 실제 객체의 와이어 중 d보다 위로 가는 것)
        // 처음 틈(맨 위)에서 시작해 실제 객체를 하나씩 위로 넘기며 증감만 반영한다
        var cost = (Left: 0, Right: 0);
        var delta = new (int Left, int Right)[reals.Count];
        for (var i = 0; i < reals.Count; i++)
        {
            foreach (var s in reals[i].Left)
            {
                var key = Key(s.From, s.FromOffset);
                if (key < left) { cost.Left++; delta[i].Left--; }
                else if (key > left) delta[i].Left++;
            }
            if (rightKey is not { } rk) continue;
            foreach (var s in reals[i].Right)
            {
                if (s.To.IsVirtual) continue;
                var key = RankKey(s.To, s.ToOffset);
                if (key < rk) { cost.Right++; delta[i].Right--; }
                else if (key > rk) delta[i].Right++;
            }
        }

        var best = 0;
        var bestCost = cost;
        for (var slot = 1; slot <= reals.Count; slot++)
        {
            cost = (cost.Left + delta[slot - 1].Left, cost.Right + delta[slot - 1].Right);
            if (cost.Left < bestCost.Left || (cost.Left == bestCost.Left && cost.Right < bestCost.Right))
            {
                best = slot;
                bestCost = cost;
            }
        }
        return best;
    }

    void SortByBarycenter(List<Vertex> layer, bool fromLeft)
    {
        var sorted = layer
            .OrderBy(v => Barycenter(v, fromLeft) ?? v.Order)
            .ThenBy(v => v.Order)
            .ToList();
        layer.Clear();
        layer.AddRange(sorted);
        Renumber(layer);
    }

    static float? Barycenter(Vertex v, bool fromLeft)
    {
        var segments = fromLeft ? v.Left : v.Right;
        if (segments.Count == 0)
            return null;
        var sum = 0f;
        foreach (var s in segments)
            sum += fromLeft ? Key(s.From, s.FromOffset) : Key(s.To, s.ToOffset);
        return sum / segments.Count;
    }

    /// <summary>순서 비교용 위치: 층 안 순서 + 정점 안에서 그립의 상대 높이(0~1).</summary>
    static float Key(Vertex v, float offset) =>
        v.Order + (v.Height > 0 ? Math.Max(0, Math.Min(1, offset / v.Height)) : 0.5f);

    void Transpose()
    {
        bool improved;
        var passes = 0;
        do
        {
            improved = false;
            foreach (var layer in _layers)
            {
                for (var i = 0; i + 1 < layer.Count; i++)
                {
                    var upper = layer[i];
                    var lower = layer[i + 1];
                    if (PairCrossings(lower, upper) >= PairCrossings(upper, lower)) continue;

                    layer[i] = lower;
                    layer[i + 1] = upper;
                    lower.Order = i;
                    upper.Order = i + 1;
                    improved = true;
                }
            }
        } while (improved && ++passes < 4);
    }

    /// <summary>upper가 lower 바로 위에 있을 때 두 정점의 와이어끼리 생기는 교차 수.</summary>
    static int PairCrossings(Vertex upper, Vertex lower) =>
        PairCrossings(upper.Left, lower.Left, s => Key(s.From, s.FromOffset)) +
        PairCrossings(upper.Right, lower.Right, s => Key(s.To, s.ToOffset));

    static int PairCrossings(List<Segment> upper, List<Segment> lower, Func<Segment, float> key)
    {
        var count = 0;
        foreach (var a in upper)
        {
            var ka = key(a);
            foreach (var b in lower)
                if (ka > key(b))
                    count++;
        }
        return count;
    }

    int Crossings()
    {
        var total = 0;
        for (var l = 0; l + 1 < _layers.Count; l++)
        {
            var segments = _layers[l]
                .SelectMany(v => v.Right)
                .Select(s => (A: Key(s.From, s.FromOffset), B: Key(s.To, s.ToOffset)))
                .ToList();
            for (var i = 0; i < segments.Count; i++)
                for (var j = i + 1; j < segments.Count; j++)
                    if ((segments[i].A - segments[j].A) * (segments[i].B - segments[j].B) < 0)
                        total++;
        }
        return total;
    }

    List<Vertex[]> Snapshot() => _layers.Select(l => l.ToArray()).ToList();

    void Restore(List<Vertex[]> snapshot)
    {
        for (var l = 0; l < _layers.Count; l++)
        {
            _layers[l].Clear();
            _layers[l].AddRange(snapshot[l]);
            Renumber(_layers[l]);
        }
    }

    // ───────────────────────── 5. 좌표 ─────────────────────────

    /// <summary>열 너비는 그 열에서 가장 넓은 객체. 객체는 열 오른쪽에 맞춰 출력 그립이 한 줄에 선다.</summary>
    void PlaceColumns()
    {
        var left = 0f;
        foreach (var layer in _layers)
        {
            var width = layer.Max(v => v.Width);
            foreach (var v in layer)
                v.X = left + width - v.Width;
            left += width + _options.ColumnGap;
        }
    }

    /// <summary>
    /// 각 정점을 기준 와이어 하나가 수평이 되는 높이로 보낸다. 기준 와이어는 맨 위 입력 그립의 와이어이고,
    /// 입력이 없으면 맨 위 출력 그립의 와이어다. 나머지 와이어는 기울어도 된다.
    /// </summary>
    /// <remarks>
    /// 같은 층에서 자리가 겹치면 위쪽 정점이 자리를 갖고 아래쪽이 밀려난다 (중간 타협 없음).
    /// 입력이 없는 정점은 오른쪽 층을 기준으로 하므로 좌→우, 우→좌를 번갈아 맞추되,
    /// 입력 쪽 기준이 정확히 지켜지도록 마지막은 좌→우로 끝낸다.
    /// </remarks>
    void PlaceRows()
    {
        foreach (var layer in _layers)
        {
            var y = 0f;
            for (var i = 0; i < layer.Count; i++)
            {
                layer[i].Y = y;
                y += layer[i].Height + Gap(layer, i);
            }
        }

        for (var round = PlacementRounds - 1; round >= 0; round--)
        {
            if (round % 2 == 0)
                foreach (var layer in _layers)
                    AlignLayer(layer);
            else
                for (var l = _layers.Count - 1; l >= 0; l--)
                    AlignLayer(_layers[l]);
        }
    }

    float Gap(List<Vertex> layer, int i) =>
        i + 1 < layer.Count && layer[i].IsVirtual && layer[i + 1].IsVirtual
            ? _options.RowGap / 2
            : _options.RowGap;

    /// <summary>위에서부터 기준 와이어 높이에 놓고, 위 정점과 겹치면 그 아래로 민다.</summary>
    void AlignLayer(List<Vertex> layer)
    {
        var floor = float.MinValue;
        for (var i = 0; i < layer.Count; i++)
        {
            var v = layer[i];
            v.Y = Math.Max(Target(v), floor);
            floor = v.Y + v.Height + Gap(layer, i);
        }
    }

    /// <summary>기준 와이어가 수평이 되는 높이. 연결이 없으면 현재 높이.</summary>
    static float Target(Vertex v)
    {
        if (v.Left.Count > 0)
        {
            var s = v.Left
                .OrderBy(x => x.ToOffset)
                .ThenBy(x => Key(x.From, x.FromOffset))
                .First();
            return s.From.Y + s.FromOffset - s.ToOffset;
        }
        if (v.Right.Count > 0)
        {
            var s = v.Right
                .OrderBy(x => x.FromOffset)
                .ThenBy(x => Key(x.To, x.ToOffset))
                .First();
            return s.To.Y + s.ToOffset - s.FromOffset;
        }
        return v.Y;
    }
}
