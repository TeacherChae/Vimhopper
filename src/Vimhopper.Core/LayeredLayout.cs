using System;
using System.Collections.Generic;
using System.Linq;

namespace Vimhopper.Core;

/// <summary>CanvasLayout의 덩어리 하나 계산. Relay는 이미 풀린 상태로 받는다.</summary>
sealed class LayeredLayout
{
    public sealed class Result
    {
        public Dictionary<Guid, (float X, float Y)> Positions { get; } = new();
        public List<PlannedRelay> Relays { get; } = new();
        public Dictionary<Connection, PortRef> Routes { get; } = new();
    }

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

        /// <summary>Relay로 바꿀 수 있는 가상 노드인지 (뒤집힌 와이어 위에는 놓지 않는다).</summary>
        public bool Relayable;

        /// <summary>가상 노드가 지나는 와이어의 출발 출력.</summary>
        public PortRef Origin;

        /// <summary>Relay로 바뀌었으면 Result.Relays의 인덱스, 아니면 -1.</summary>
        public int Relay = -1;
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

    /// <summary>한 출력에서 나가는 긴 와이어들이 같이 쓰는 가상 노드 줄기와, 거기서 갈라지는 갈래.</summary>
    sealed class Trunk
    {
        public Trunk(Vertex source, float sourceOffset)
        {
            Source = source;
            SourceOffset = sourceOffset;
        }

        public Vertex Source { get; }
        public float SourceOffset { get; }

        /// <summary>왼쪽부터. Dummies[i]는 위치 i + 1 (위치 0은 출발 객체).</summary>
        public List<Vertex> Dummies { get; } = new();

        /// <summary>At 위치의 가상 노드에서 갈라져 Target으로 들어가는 갈래.</summary>
        public List<(int At, Vertex Target, float Offset)> Branches { get; } = new();
    }

    struct Wire
    {
        public int From;
        public Guid FromPort;
        public float FromOffset;
        public int To;
        public float ToOffset;
        public bool Reversed;
        public Connection Connection;
    }

    // 층 안 순서 바꾸기 / 세로 위치 맞추기 반복 횟수
    const int OrderingSweeps = 12;
    const int OrderingRounds = 20;

    readonly IReadOnlyList<LayoutNode> _nodes;
    readonly LayoutOptions _options;
    readonly List<Wire> _wires = new();
    readonly List<Vertex> _vertices = new();
    readonly List<List<Vertex>> _layers = new();
    readonly List<Trunk> _trunks = new();
    readonly List<float> _columnLeft = new();
    readonly List<float> _columnWidth = new();

    // 와이어마다 받는 쪽 바로 앞의 가상 노드 (없으면 null)
    Vertex?[] _lastVirtual = Array.Empty<Vertex?>();

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
            _wires.Add(new Wire
            {
                From = from,
                FromPort = e.FromPort,
                FromOffset = e.FromOffset,
                To = to,
                ToOffset = e.ToOffset,
                Connection = e.Connection,
            });
        }
    }

    float VirtualOffset => _options.RelayHeight / 2;

    /// <summary>정렬은 하지 않고 층(열) 번호만. 인덱스는 생성자에 넘긴 객체 순서.</summary>
    public int[] Layers()
    {
        if (_nodes.Count == 0)
            return Array.Empty<int>();
        BreakCycles();
        return AssignLayers();
    }

    public Result Run()
    {
        var result = new Result();
        if (_nodes.Count == 0)
            return result;

        BreakCycles();
        var layers = AssignLayers();
        BuildVertices(layers);
        OrderLayers();
        PlaceColumns();
        PlaceRows();
        AlignInColumns();
        MarkRelays();

        // 원래 영역의 왼쪽 위에 맞추고 정수 좌표로
        var real = _vertices.Where(v => !v.IsVirtual).ToList();
        var dx = _nodes.Min(n => n.X) - real.Min(v => v.X);
        var dy = _nodes.Min(n => n.Y) - real.Min(v => v.Y);
        // Math.Round는 .5를 짝수 쪽으로 보내 같은 높이였던 두 그립이 1px 벌어질 수 있으므로 항상 올림 쪽으로
        foreach (var v in _vertices)
        {
            v.X = (float)Math.Floor(v.X + dx + 0.5);
            v.Y = (float)Math.Floor(v.Y + dy + 0.5);
        }

        foreach (var v in real)
            result.Positions[_nodes[v.Node].Id] = (v.X, v.Y);
        EmitRelays(result);
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
        _wires[w] = new Wire
        {
            From = wire.To,
            FromOffset = wire.ToOffset,
            To = wire.From,
            ToOffset = wire.FromOffset,
            Reversed = true,
            Connection = wire.Connection,
        };
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
    /// 모든 객체가 부모보다 오른쪽에 있으면서 와이어가 건너는 층 수의 합이 가장 작게 (Network Simplex).
    /// 입력이 없는 객체는 자연히 자식 중 가장 왼쪽 층의 바로 앞에 오고, 입력값만 받는 작은 가지도
    /// 쓰이는 곳 바로 앞으로 간다.
    /// </summary>
    int[] AssignLayers() =>
        NetworkSimplex.Rank(_nodes.Count, _wires.Select(w => new NetworkSimplex.Edge(w.From, w.To)).ToList());

    // ───────────────────────── 3. 가상 노드 ─────────────────────────

    void BuildVertices(int[] layers)
    {
        for (var i = 0; i < _nodes.Count; i++)
        {
            var n = _nodes[i];
            _vertices.Add(new Vertex(i, n.Width, n.Height) { Layer = layers[i], InitialKey = n.Y + n.Height / 2 });
        }

        _lastVirtual = new Vertex?[_wires.Count];

        // 정방향 와이어: 같은 출력에서 나가는 것끼리 가상 노드 줄기(trunk)를 같이 쓰고,
        // 받는 객체 바로 앞 층에서 갈라진다
        var groups = Enumerable.Range(0, _wires.Count)
            .Where(w => !_wires[w].Reversed)
            .GroupBy(w => (_wires[w].From, _wires[w].FromPort, _wires[w].FromOffset));
        foreach (var group in groups)
        {
            var first = _wires[group.First()];
            var from = _vertices[first.From];
            var origin = PortRef.Real(first.Connection.FromNode, first.Connection.FromPort);
            var portY = _nodes[first.From].Y + first.FromOffset;
            var end = group.Max(w => _vertices[_wires[w].To].Layer);

            var trunk = new Trunk(from, first.FromOffset);
            var previous = from;
            var previousOffset = first.FromOffset;
            for (var layer = from.Layer + 1; layer < end; layer++)
            {
                var dummy = NewVirtual(layer, portY, relayable: true, origin);
                Connect(previous, previousOffset, dummy, VirtualOffset);
                trunk.Dummies.Add(dummy);
                previous = dummy;
                previousOffset = VirtualOffset;
            }
            if (trunk.Dummies.Count > 0)
                _trunks.Add(trunk);

            foreach (var w in group)
            {
                var wire = _wires[w];
                var to = _vertices[wire.To];
                var span = to.Layer - from.Layer;
                if (span == 1)
                {
                    Connect(from, wire.FromOffset, to, wire.ToOffset);
                    continue;
                }
                var last = trunk.Dummies[span - 2];
                Connect(last, VirtualOffset, to, wire.ToOffset);
                trunk.Branches.Add((span - 1, to, wire.ToOffset));
                _lastVirtual[w] = last;
            }
        }

        // 뒤집힌 와이어(순환): 와이어마다 따로 자리만 만들고 Relay는 놓지 않는다
        for (var w = 0; w < _wires.Count; w++)
        {
            var wire = _wires[w];
            if (!wire.Reversed) continue;

            var from = _vertices[wire.From];
            var to = _vertices[wire.To];
            var span = to.Layer - from.Layer;
            var y0 = _nodes[wire.From].Y + wire.FromOffset;
            var y1 = _nodes[wire.To].Y + wire.ToOffset;

            var previous = from;
            var previousOffset = wire.FromOffset;
            for (var step = 1; step < span; step++)
            {
                var dummy = NewVirtual(from.Layer + step, y0 + (y1 - y0) * step / span, relayable: false, default);
                Connect(previous, previousOffset, dummy, VirtualOffset);
                previous = dummy;
                previousOffset = VirtualOffset;
            }
            Connect(previous, previousOffset, to, wire.ToOffset);
        }

        var layerCount = _vertices.Max(v => v.Layer) + 1;
        for (var l = 0; l < layerCount; l++)
            _layers.Add(new List<Vertex>());
        foreach (var v in _vertices)
            _layers[v.Layer].Add(v);

        // 처음 순서: 기존 화면 위→아래 (같으면 왼쪽→오른쪽, 그다음 만든 순서)
        foreach (var layer in _layers)
        {
            var sorted = layer
                .OrderBy(v => v.InitialKey)
                .ThenBy(v => v.IsVirtual ? float.MaxValue : _nodes[v.Node].X)
                .ThenBy(v => v.Sequence)
                .ToList();
            layer.Clear();
            layer.AddRange(sorted);
            Renumber(layer);
        }
    }

    Vertex NewVirtual(int layer, float initialKey, bool relayable, PortRef origin)
    {
        var dummy = new Vertex(-1, _options.RelayWidth, _options.RelayHeight)
        {
            Sequence = _vertices.Count,
            Layer = layer,
            InitialKey = initialKey,
            Relayable = relayable,
            Origin = origin,
        };
        _vertices.Add(dummy);
        return dummy;
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
            _columnLeft.Add(left);
            _columnWidth.Add(width);
            left += width + _options.ColumnGap;
        }
    }

    /// <summary>
    /// 열보다 좁은 객체와 가상 노드(Relay 자리)를 열 오른쪽(기본) 또는 왼쪽 중 같은 열 이웃을 덜 가로지르는 쪽에 붙인다.
    /// </summary>
    /// <remarks>
    /// 오른쪽에 붙이면 객체 왼쪽에 빈 자리가 생기고, 그리로 들어오는 와이어가 꺾이면서 같은 열의 더 넓은
    /// 이웃 위를 지날 수 있다. 왼쪽에 붙이면 같은 일이 나가는 와이어 쪽에서 생긴다. 열 사이 간격만으로는
    /// 이 꺾임을 담을 수 없고 Relay도 열 안에는 놓을 수 없으므로, 객체마다 붙는 쪽을 골라 줄인다.
    /// 높이는 이미 정해져 있어 가로 위치만 바뀐다. 결과는 배치만으로 정해지므로 다시 정렬해도 같다.
    /// </remarks>
    void AlignInColumns()
    {
        for (var pass = 0; pass < 2; pass++)
        {
            for (var l = 0; l < _layers.Count; l++)
            {
                var right = _columnLeft[l] + _columnWidth[l];
                foreach (var v in _layers[l])
                {
                    if (v.Width >= _columnWidth[l]) continue;

                    v.X = right - v.Width;
                    var rightCost = NeighborCrossings(v);
                    v.X = _columnLeft[l];
                    var leftCost = NeighborCrossings(v);
                    v.X = leftCost < rightCost ? _columnLeft[l] : right - v.Width;
                }
            }
        }
    }

    /// <summary>v에 붙은 와이어 조각이 v와 같은 열의 다른 실제 객체를 가로지르는 수.</summary>
    int NeighborCrossings(Vertex v)
    {
        var count = 0;
        foreach (var s in v.Left)
            count += Crossings(v.Layer, s.From.X + s.From.Width, s.From.Y + s.FromOffset, v.X, v.Y + s.ToOffset, s.From, v, 0);
        foreach (var s in v.Right)
            count += Crossings(v.Layer, v.X + v.Width, v.Y + s.FromOffset, s.To.X, s.To.Y + s.ToOffset, v, s.To, 0);
        return count;
    }

    /// <summary>
    /// 와이어가 한 층의 실제 객체(양 끝 제외)를 몇 개 가로지르는지. 층 안 정점은 위→아래로 정렬되어 있으므로
    /// 와이어 높이 범위에 걸치는 것만 이분 탐색으로 골라 본다.
    /// </summary>
    int Crossings(int layer, float x0, float y0, float x1, float y1, Vertex from, Vertex to, float margin)
    {
        var vertices = _layers[layer];
        var top = Math.Min(y0, y1) - margin;
        var bottom = Math.Max(y0, y1) + margin;

        // 아래 가장자리가 top보다 아래인 첫 정점
        int low = 0, high = vertices.Count;
        while (low < high)
        {
            var mid = (low + high) / 2;
            if (vertices[mid].Y + vertices[mid].Height < top) low = mid + 1;
            else high = mid;
        }

        var count = 0;
        for (var i = low; i < vertices.Count && vertices[i].Y <= bottom; i++)
        {
            var v = vertices[i];
            if (v.IsVirtual || v == from || v == to) continue;
            if (WireGeometry.Crosses(x0, y0, x1, y1, v.X - margin, v.Y - margin, v.X + v.Width + margin, v.Y + v.Height + margin))
                count++;
        }
        return count;
    }

    // 세로 위치 계산 단위 (반 픽셀). 그립 높이가 .5로 끝나도 와이어를 정확히 수평으로 맞추기 위해
    const int RowScale = 2;

    // 와이어 가중치: 줄기 > 받는 쪽의 맨 위 입력 > 나머지
    const int TrunkWeight = 100;
    const int TopInputWeight = 8;

    /// <summary>
    /// 세로 위치: 같은 열에서 순서와 간격을 지키면서 <c>가중치 × 와이어 양 끝 그립의 높이 차</c>의 합이 가장 작게.
    /// </summary>
    /// <remarks>
    /// Gansner 외(1993)의 좌표 계산을 그대로 쓴다. 와이어 조각마다 보조 정점 n을 두고 n → 출발, n → 도착 간선을
    /// 이으면, Network Simplex가 n을 두 그립 중 위쪽에 붙이므로 두 간선 길이의 합이 그립 높이 차가 된다.
    /// 같은 열에서 이웃한 정점은 위 → 아래 간선(최소 길이 = 위 정점 높이 + 간격, 가중치 0)으로 순서와 간격을 지킨다.
    /// <list type="bullet">
    /// <item>가중치: 줄기(<see cref="MainPath"/>) <see cref="TrunkWeight"/>, 맨 위 입력 와이어 <see cref="TopInputWeight"/>,
    /// 나머지 1. 줄기·맨 위 입력이 아닌 조각은 가상 노드가 끼면 2배, 양쪽 모두 가상 노드면 8배
    /// (긴 와이어를 곧게. Graphviz와 같은 비율).</item>
    /// <item>합을 줄이는 방식이라, 같은 가중치의 두 와이어가 같은 자리를 다투면 "하나는 수평, 하나는 크게 꺾임"과
    /// "둘 다 조금씩 꺾임"의 비용이 같을 수 있다. 줄기 가중치가 이런 경우 줄기 쪽을 수평으로 확정한다.</item>
    /// <item>전체를 한 번에 풀기 때문에, 겹칠 때 아래로만 미는 탐욕적 방식에서 생기던 "가라앉음"
    /// (서로만 기준으로 삼는 묶음이 반복마다 내려감)이 없다.</item>
    /// </list>
    /// </remarks>
    void PlaceRows()
    {
        var index = new Dictionary<Vertex, int>();
        foreach (var v in _vertices)
            index[v] = index.Count;
        var count = index.Count;
        var edges = new List<NetworkSimplex.Edge>();

        foreach (var layer in _layers)
            for (var i = 0; i + 1 < layer.Count; i++)
                edges.Add(new NetworkSimplex.Edge(
                    index[layer[i]], index[layer[i + 1]], weight: 0,
                    minLength: (int)Math.Ceiling((layer[i].Height + Gap(layer, i)) * RowScale)));

        var trunk = MainPath();
        foreach (var v in _vertices)
        {
            foreach (var s in v.Right)
            {
                // (출발 Y + 출발 그립) - (도착 Y + 도착 그립) = (출발 Y - a) - (도착 Y - b)
                var diff = (int)Math.Round((s.ToOffset - s.FromOffset) * RowScale);
                var a = Math.Max(0, diff);
                var b = a - diff;
                var aux = count++;
                var weight = trunk.Contains(s) ? TrunkWeight : SegmentWeight(s);
                edges.Add(new NetworkSimplex.Edge(aux, index[s.From], weight, a));
                edges.Add(new NetworkSimplex.Edge(aux, index[s.To], weight, b));
            }
        }

        var rank = NetworkSimplex.Rank(count, edges);
        foreach (var v in _vertices)
            v.Y = rank[index[v]] / (float)RowScale;
    }

    /// <summary>
    /// 줄기: 끝 객체(나가는 와이어가 없는 객체)에서 맨 위 입력 와이어를 거슬러 오른 경로 중 실제 객체를 가장 많이
    /// 지나는 것. 길이가 같으면 먼저 찾은 것 (왼쪽 층, 위쪽 순서).
    /// </summary>
    HashSet<Segment> MainPath()
    {
        var best = new List<Segment>();
        var bestLength = 0;
        foreach (var end in _layers.SelectMany(l => l).Where(v => !v.IsVirtual && v.Right.Count == 0))
        {
            var path = new List<Segment>();
            var length = 1;
            var seen = new HashSet<Vertex> { end };
            for (var s = TopInput(end); s != null && seen.Add(s.From); s = TopInput(s.From))
            {
                path.Add(s);
                if (!s.From.IsVirtual)
                    length++;
            }
            if (length > bestLength)
            {
                best = path;
                bestLength = length;
            }
        }
        return new HashSet<Segment>(best);
    }

    /// <summary>맨 위 입력 그립으로 들어오는 와이어 조각 (가상 노드는 하나뿐인 입력).</summary>
    static Segment? TopInput(Vertex v) =>
        v.Left
            .OrderBy(x => x.ToOffset)
            .ThenBy(x => Key(x.From, x.FromOffset))
            .FirstOrDefault();

    static int SegmentWeight(Segment s)
    {
        // 가상 노드는 입력이 하나뿐이라 "맨 위 입력"은 실제 객체로 들어가는 조각에만 따진다
        var top = !s.To.IsVirtual && TopInput(s.To) == s;
        var weight = top ? TopInputWeight : 1;
        return (s.From.IsVirtual, s.To.IsVirtual) switch
        {
            (true, true) => weight * 8,
            (false, false) => weight,
            _ => weight * 2,
        };
    }

    float Gap(List<Vertex> layer, int i) =>
        i + 1 < layer.Count && layer[i].IsVirtual && layer[i + 1].IsVirtual
            ? _options.RowGap / 2
            : _options.RowGap;

    // ───────────────────────── 6. Relay ─────────────────────────

    /// <summary>
    /// 줄기마다, Relay가 없으면 와이어가 객체를 가로지르는 곳에만 Relay를 놓는다.
    /// </summary>
    /// <remarks>
    /// Grasshopper는 와이어를 출력에서 입력까지 곡선 하나로 그리고 장애물을 피하지 않는다.
    /// 출발점(또는 마지막 Relay)에서 Relay 없이 갈 수 있는 가장 먼 가상 노드까지 한 번에 가고,
    /// 그 사이에서 갈라지는 갈래도 모두 가로지르지 않을 때만 그렇게 한다. 어디로도 못 가면
    /// 바로 다음 가상 노드에 Relay를 둔다 (이웃한 두 열 사이 와이어라 가로지를 일이 거의 없다).
    /// 장애물은 실제 객체만이다. 가상 노드 자리는 비어 있으므로 와이어가 지나가도 된다.
    /// </remarks>
    void MarkRelays()
    {
        foreach (var trunk in _trunks)
        {
            var anchor = 0;
            var end = trunk.Dummies.Count + 1;
            // 마지막 가상 노드에 Relay를 두고도 막히면 더 둘 곳이 없다 (남은 갈래는 이웃한 열 사이 와이어)
            while (anchor < trunk.Dummies.Count && !Reaches(trunk, anchor, end))
            {
                var next = anchor + 1;
                for (var b = end - 1; b > anchor + 1; b--)
                {
                    if (!Reaches(trunk, anchor, b)) continue;
                    next = b;
                    break;
                }
                trunk.Dummies[next - 1].Relay = 0;
                anchor = next;
            }
        }

        // 왼쪽 열부터 번호를 매겨 Relay의 앞쪽 Relay가 항상 작은 번호가 되게 한다
        var count = 0;
        foreach (var layer in _layers)
            foreach (var v in layer)
                if (v.Relay >= 0)
                    v.Relay = count++;
    }

    /// <summary>
    /// anchor 위치에서 next 위치의 가상 노드까지 Relay 없이 와이어를 그려도 되는지.
    /// 그 사이(anchor 이상 next 미만)에서 갈라지는 갈래도 anchor에서 그려지므로 함께 본다.
    /// next가 줄기 끝 너머면 남은 갈래만 본다.
    /// </summary>
    bool Reaches(Trunk trunk, int anchor, int next)
    {
        var from = anchor == 0 ? trunk.Source : trunk.Dummies[anchor - 1];
        var fromOffset = anchor == 0 ? trunk.SourceOffset : VirtualOffset;

        if (next <= trunk.Dummies.Count && !Clear(from, fromOffset, trunk.Dummies[next - 1], VirtualOffset))
            return false;
        foreach (var (at, target, offset) in trunk.Branches)
            if (at >= anchor && at < next && !Clear(from, fromOffset, target, offset))
                return false;
        return true;
    }

    /// <summary>from의 출력 그립에서 to의 입력 그립까지 그린 와이어가 실제 객체를 가로지르지 않는지.</summary>
    bool Clear(Vertex from, float fromOffset, Vertex to, float toOffset)
    {
        var x0 = from.X + from.Width;
        var y0 = from.Y + fromOffset;
        var x1 = to.X;
        var y1 = to.Y + toOffset;
        for (var l = from.Layer; l <= to.Layer; l++)
            if (Crossings(l, x0, y0, x1, y1, from, to, _options.WireClearance) > 0)
                return false;
        return true;
    }

    void EmitRelays(Result result)
    {
        // 왼쪽 층부터 표시했으므로 이 순서가 인덱스 순서다
        foreach (var layer in _layers)
            foreach (var v in layer)
                if (v.Relay >= 0)
                    result.Relays.Add(new PlannedRelay(v.Relay, v.X, v.Y, Upstream(v.Left[0].From, v.Origin)));

        for (var w = 0; w < _wires.Count; w++)
        {
            var wire = _wires[w];
            if (_lastVirtual[w] is { } last)
                result.Routes[wire.Connection] = Upstream(last, PortRef.Real(wire.Connection.FromNode, wire.Connection.FromPort));
        }
    }

    /// <summary>from부터 왼쪽으로 거슬러 올라가 처음 만나는 Relay. 없으면 와이어의 출발 출력.</summary>
    static PortRef Upstream(Vertex from, PortRef origin)
    {
        var v = from;
        while (v.IsVirtual)
        {
            if (v.Relay >= 0)
                return PortRef.OfRelay(v.Relay);
            v = v.Left[0].From;
        }
        return origin;
    }
}
