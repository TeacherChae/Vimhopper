using System;
using System.Collections.Generic;
using System.Linq;

namespace Vimhopper.Core;

/// <summary>정렬할 객체 하나. 좌표는 캔버스 좌표의 왼쪽 위 (y는 아래로 증가).</summary>
public readonly struct LayoutNode
{
    /// <param name="isRelay">
    /// 풀어도 되는 Relay. 정렬 전에 와이어로 풀어 직결하고, 정렬 결과에 필요한 Relay를 새로 계획한다.
    /// </param>
    /// <param name="isResult">
    /// 결과 확인용 객체 (값을 보여주기만 하는 Panel 등). 받는 와이어가 있고 내보내는 와이어가 없으면
    /// 흐름에서 빼고 덩어리 위쪽 한 줄에 놓는다.
    /// </param>
    public LayoutNode(Guid id, float x, float y, float width, float height, bool isRelay = false, bool isResult = false)
    {
        Id = id;
        X = x;
        Y = y;
        Width = width;
        Height = height;
        IsRelay = isRelay;
        IsResult = isResult;
    }

    public Guid Id { get; }
    public float X { get; }
    public float Y { get; }
    public float Width { get; }
    public float Height { get; }
    public bool IsRelay { get; }
    public bool IsResult { get; }
}

/// <summary>
/// 와이어 하나. 포트는 출력/입력 파라미터의 id (객체 자체가 파라미터면 객체 id와 같다).
/// 오프셋은 각 객체의 위쪽 가장자리에서 그립까지의 세로 거리.
/// </summary>
public readonly struct LayoutEdge
{
    public LayoutEdge(Guid from, float fromOffset, Guid to, float toOffset)
        : this(from, Guid.Empty, fromOffset, to, Guid.Empty, toOffset)
    {
    }

    public LayoutEdge(Guid from, Guid fromPort, float fromOffset, Guid to, Guid toPort, float toOffset)
    {
        From = from;
        FromPort = fromPort;
        FromOffset = fromOffset;
        To = to;
        ToPort = toPort;
        ToOffset = toOffset;
    }

    public Guid From { get; }
    public Guid FromPort { get; }
    public float FromOffset { get; }
    public Guid To { get; }
    public Guid ToPort { get; }
    public float ToOffset { get; }

    public Connection Connection => new(From, FromPort, To, ToPort);
}

/// <summary>Relay를 풀어낸 뒤의 논리적 연결: 어느 출력이 어느 입력으로 가는가.</summary>
public readonly struct Connection : IEquatable<Connection>
{
    public Connection(Guid fromNode, Guid fromPort, Guid toNode, Guid toPort)
    {
        FromNode = fromNode;
        FromPort = fromPort;
        ToNode = toNode;
        ToPort = toPort;
    }

    public Guid FromNode { get; }
    public Guid FromPort { get; }
    public Guid ToNode { get; }
    public Guid ToPort { get; }

    public bool Equals(Connection other) =>
        FromNode == other.FromNode && FromPort == other.FromPort && ToNode == other.ToNode && ToPort == other.ToPort;

    public override bool Equals(object? obj) => obj is Connection other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = FromNode.GetHashCode();
            hash = hash * 31 + FromPort.GetHashCode();
            hash = hash * 31 + ToNode.GetHashCode();
            return hash * 31 + ToPort.GetHashCode();
        }
    }
}

/// <summary>입력이 받을 출력: 실제 객체의 출력이거나 계획된 Relay.</summary>
public readonly struct PortRef : IEquatable<PortRef>
{
    PortRef(Guid node, Guid port, int relay)
    {
        Node = node;
        Port = port;
        Relay = relay;
    }

    public static PortRef Real(Guid node, Guid port) => new(node, port, -1);
    public static PortRef OfRelay(int index) => new(Guid.Empty, Guid.Empty, index);

    public Guid Node { get; }
    public Guid Port { get; }

    /// <summary>LayoutPlan.Relays의 인덱스. 실제 출력이면 -1.</summary>
    public int Relay { get; }

    public bool IsRelay => Relay >= 0;

    public bool Equals(PortRef other) => Node == other.Node && Port == other.Port && Relay == other.Relay;
    public override bool Equals(object? obj) => obj is PortRef other && Equals(other);
    public override int GetHashCode() => unchecked(Node.GetHashCode() * 31 + Port.GetHashCode() * 7 + Relay);
    public override string ToString() => IsRelay ? $"relay#{Relay}" : $"{Node}:{Port}";
}

/// <summary>정렬 결과에 놓을 Relay 하나. 좌표는 왼쪽 위.</summary>
public sealed class PlannedRelay
{
    public PlannedRelay(int index, float x, float y, PortRef upstream)
    {
        Index = index;
        X = x;
        Y = y;
        Upstream = upstream;
    }

    public int Index { get; }
    public float X { get; }
    public float Y { get; }

    /// <summary>이 Relay의 입력에 연결할 출력.</summary>
    public PortRef Upstream { get; }
}

/// <summary>정렬 계획: 객체 위치, 새로 놓을 Relay, 각 연결이 실제로 받을 출력.</summary>
public sealed class LayoutPlan
{
    /// <summary>Relay가 아닌 객체의 새 왼쪽 위 좌표.</summary>
    public Dictionary<Guid, (float X, float Y)> Positions { get; } = new();

    /// <summary>놓을 Relay. 왼쪽 열부터 순서대로이고, Upstream이 가리키는 Relay는 항상 앞쪽 인덱스.</summary>
    public List<PlannedRelay> Relays { get; } = new();

    /// <summary>
    /// 논리적 연결마다 입력 쪽이 실제로 연결할 출력. 없으면 원래 출력에 직접 연결한다.
    /// </summary>
    public Dictionary<Connection, PortRef> Routes { get; } = new();

    /// <summary>입력이 실제로 받을 출력 (Routes에 없으면 원래 출력).</summary>
    public PortRef Route(Connection connection) =>
        Routes.TryGetValue(connection, out var route) ? route : PortRef.Real(connection.FromNode, connection.FromPort);
}

public sealed class LayoutOptions
{
    /// <summary>열 사이 가로 간격.</summary>
    public float ColumnGap { get; set; } = 40;

    /// <summary>같은 열 안 객체 사이 세로 간격. 지나가는 와이어(가상 노드)끼리는 절반.</summary>
    public float RowGap { get; set; } = 16;

    /// <summary>문서 전체 정렬에서 덩어리 사이 간격.</summary>
    public float IslandGap { get; set; } = 60;

    /// <summary>와이어가 객체와 이만큼은 떨어져 지나가야 가로지르지 않는 것으로 본다.</summary>
    public float WireClearance { get; set; } = 4;

    /// <summary>Relay 크기. 그립은 세로 가운데라고 가정한다.</summary>
    public float RelayWidth { get; set; } = 40;

    public float RelayHeight { get; set; } = 16;
}

/// <summary>
/// 와이어 연결을 따라 객체를 열(층)로 나눠 배치하는 자동 정렬 (Sugiyama 방식).
/// </summary>
/// <remarks>
/// <list type="number">
/// <item>Relay 풀기: 풀어도 되는 Relay는 없애고 앞뒤를 직결한다. 사용자가 넣은 것도 마찬가지.</item>
/// <item>층 나누기: 모든 객체가 부모보다 오른쪽이면서 와이어가 건너는 층 수의 합이 가장 작게 (Network Simplex).
/// 입력이 없는 객체(슬라이더, 패널 등)는 자연히 처음 쓰이는 객체 바로 앞 열에 온다.</item>
/// <item>두 층 이상 건너뛰는 와이어는 중간 층마다 가상 노드를 끼워 지나갈 자리를 만든다.
/// 같은 출력에서 나가는 긴 와이어들은 가상 노드를 같이 쓰다가 갈라진다.</item>
/// <item>층 안 순서: 기존 화면 위→아래 순서에서 시작해 와이어 교차가 줄도록 바꾼다.</item>
/// <item>좌표: 열은 오른쪽 정렬. 세로 위치는 같은 열의 순서와 간격을 지키면서 와이어 양 끝 높이 차의
/// 가중 합이 가장 작게 (줄기 > 맨 위 입력 > 나머지).</item>
/// <item>Relay 놓기: 와이어가 꺾이거나 갈라지는 가상 노드에 Relay를 놓는다. Grasshopper는 와이어를
/// 출력에서 입력까지 곡선 하나로 그리므로, 이렇게 해야 와이어가 비워둔 통로를 따라 그려진다.</item>
/// <item>결과 확인용 객체는 위 과정에서 빠지고, 덩어리 위쪽 한 줄에 위쪽 끝을 맞춰 출처의 다음 열에 놓는다.</item>
/// </list>
/// 같은 입력에는 같은 결과가 나오고, 이미 정렬된 배치를 다시 정렬해도 움직이지 않는다.
/// </remarks>
public static class CanvasLayout
{
    /// <param name="separateIslands">
    /// true면 와이어로 이어진 덩어리마다 따로 정렬하고, 커진 덩어리가 위쪽 덩어리와 겹치면 아래로 민다
    /// (연결이 없는 객체는 겹칠 때만 움직인다). false면 전체를 한 덩어리로 정렬한다.
    /// 어느 쪽이든 각 덩어리의 왼쪽 위는 원래 왼쪽 위에 남는다.
    /// </param>
    public static LayoutPlan Plan(
        IReadOnlyList<LayoutNode> nodes, IReadOnlyList<LayoutEdge> edges,
        LayoutOptions? options = null, bool separateIslands = false)
    {
        options ??= new LayoutOptions();
        var (kept, connections) = DissolveRelays(nodes, edges);

        var plan = new LayoutPlan();
        if (!separateIslands)
        {
            Merge(plan, Run(kept, connections, options), 0);
            return plan;
        }

        var placed = new List<Rect>();
        foreach (var island in Islands(kept, connections)
                     .OrderBy(i => i.Min(n => n.Y))
                     .ThenBy(i => i.Min(n => n.X)))
        {
            var ids = new HashSet<Guid>(island.Select(n => n.Id));
            var islandEdges = connections.Where(e => ids.Contains(e.From)).ToList();
            var result = Run(island, islandEdges, options);
            var bounds = Rect.Of(island, result, options);

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

            Merge(plan, result, dy);
            placed.Add(bounds.Offset(dy));
        }
        return plan;
    }

    /// <summary>
    /// 정렬과 같은 규칙으로 계산한 객체별 덩어리 번호와 층(열) 번호. 좌표는 덩어리 순서에만 쓰인다.
    /// </summary>
    /// <remarks>
    /// 덩어리 번호는 문서 전체 정렬이 쌓는 순서 (위→아래, 같으면 왼쪽→오른쪽)이고, 층 번호는 덩어리마다 0부터다.
    /// 풀리는 Relay는 층 계산에서 빠지므로, 입력값과 같은 규칙으로 받는 객체 중 가장 왼쪽 층의 바로 앞에 둔다
    /// (Relay끼리 이어져 있으면 뒤쪽 Relay부터). 정렬도 Relay를 대부분 받는 쪽 바로 앞에 놓으므로 화면과 거의 맞는다.
    /// </remarks>
    public static Dictionary<Guid, (int Island, int Layer)> Layers(
        IReadOnlyList<LayoutNode> nodes, IReadOnlyList<LayoutEdge> edges)
    {
        var (kept, connections) = DissolveRelays(nodes, edges);
        var result = new Dictionary<Guid, (int Island, int Layer)>();

        var islands = Islands(kept, connections)
            .OrderBy(i => i.Min(n => n.Y))
            .ThenBy(i => i.Min(n => n.X))
            .ToList();
        for (var i = 0; i < islands.Count; i++)
        {
            var island = islands[i];
            var ids = new HashSet<Guid>(island.Select(n => n.Id));
            var islandEdges = connections.Where(e => ids.Contains(e.From)).ToList();
            var layers = new LayeredLayout(island, islandEdges, new LayoutOptions()).Layers();
            for (var k = 0; k < island.Count; k++)
                result[island[k].Id] = (i, layers[k]);
        }

        // 풀린 Relay: 받는 쪽 층 - 1
        var children = edges.GroupBy(e => e.From).ToDictionary(g => g.Key, g => g.Select(e => e.To).ToList());
        (int Island, int Layer)? RelayLayer(Guid relay, HashSet<Guid> visiting)
        {
            if (result.TryGetValue(relay, out var known))
                return known;
            if (!visiting.Add(relay) || !children.TryGetValue(relay, out var targets))
                return null;

            (int Island, int Layer)? best = null;
            foreach (var target in targets)
            {
                if ((result.TryGetValue(target, out var t) ? t : RelayLayer(target, visiting)) is not { } layer) continue;
                if (best is not { } b || layer.Layer - 1 < b.Layer)
                    best = (layer.Island, layer.Layer - 1);
            }
            visiting.Remove(relay);
            if (best is { } found)
                result[relay] = found;
            return best;
        }

        foreach (var n in nodes)
            if (!result.ContainsKey(n.Id))
                RelayLayer(n.Id, new HashSet<Guid>());
        return result;
    }

    /// <summary>객체 위치만 필요할 때 (덩어리 하나로 정렬).</summary>
    public static Dictionary<Guid, (float X, float Y)> Arrange(
        IReadOnlyList<LayoutNode> nodes, IReadOnlyList<LayoutEdge> edges, LayoutOptions? options = null) =>
        Plan(nodes, edges, options).Positions;

    /// <summary>객체 위치만 필요할 때 (덩어리마다 정렬).</summary>
    public static Dictionary<Guid, (float X, float Y)> ArrangeAll(
        IReadOnlyList<LayoutNode> nodes, IReadOnlyList<LayoutEdge> edges, LayoutOptions? options = null) =>
        Plan(nodes, edges, options, separateIslands: true).Positions;

    /// <summary>
    /// 덩어리 하나를 정렬한다. 결과 확인용 객체는 흐름에서 빼고 정렬한 뒤 위쪽 한 줄에 놓고,
    /// 그 줄과 흐름을 합친 영역의 위쪽 끝을 원래 위쪽 끝에 맞춘다 (왼쪽 끝은 흐름 기준).
    /// </summary>
    static LayeredLayout.Result Run(List<LayoutNode> nodes, List<LayoutEdge> edges, LayoutOptions options)
    {
        var results = ResultNodes(nodes, edges);
        if (results.Count == 0)
            return new LayeredLayout(nodes, edges, options).Run();

        var flow = nodes.Where(n => !results.ContainsKey(n.Id)).ToList();
        var flowEdges = edges.Where(e => !results.ContainsKey(e.To)).ToList();
        var result = new LayeredLayout(flow, flowEdges, options).Run();

        // 결과 줄 아래로 흐름을 내린다
        var rowTop = (float)Math.Floor(nodes.Min(n => n.Y) + 0.5);
        var rowHeight = results.Keys.Max(id => nodes.First(n => n.Id == id).Height);
        var shift = (float)Math.Floor(rowTop + rowHeight + options.RowGap - flow.Min(n => result.Positions[n.Id].Y) + 0.5);
        foreach (var id in result.Positions.Keys.ToList())
        {
            var p = result.Positions[id];
            result.Positions[id] = (p.X, p.Y + shift);
        }
        for (var i = 0; i < result.Relays.Count; i++)
        {
            var r = result.Relays[i];
            result.Relays[i] = new PlannedRelay(r.Index, r.X, r.Y + shift, r.Upstream);
        }

        // 출처의 오른쪽 끝 + 열 간격에 놓되, 앞에 놓은 것과 겹치면 오른쪽으로 민다.
        // 같은 자리를 원하면 출처의 그립이 위에 있는 것부터.
        var width = nodes.ToDictionary(n => n.Id, n => n.Width);
        var wanted = results
            .Select(pair =>
            {
                var source = result.Positions[pair.Value.From];
                return (Id: pair.Key, X: source.X + width[pair.Value.From] + options.ColumnGap, GripY: source.Y + pair.Value.FromOffset);
            })
            .OrderBy(r => r.X)
            .ThenBy(r => r.GripY)
            .ThenBy(r => r.Id)
            .ToList();
        var right = float.MinValue;
        foreach (var (id, x, _) in wanted)
        {
            var left = Math.Max(x, right + options.RowGap);
            result.Positions[id] = (left, rowTop);
            right = left + width[id];
        }
        return result;
    }

    /// <summary>
    /// 결과 확인용 객체 중 흐름에서 뺄 것과, 그 출처가 되는 와이어 (맨 위 그립으로 들어오는 것).
    /// 흐름 안의 객체에서 받는 와이어가 있고 내보내는 와이어가 없어야 한다.
    /// </summary>
    static Dictionary<Guid, LayoutEdge> ResultNodes(List<LayoutNode> nodes, List<LayoutEdge> edges)
    {
        var candidates = new HashSet<Guid>(nodes.Where(n => n.IsResult).Select(n => n.Id));
        if (candidates.Count == 0)
            return new Dictionary<Guid, LayoutEdge>();

        var ids = new HashSet<Guid>(nodes.Select(n => n.Id));
        var sending = new HashSet<Guid>(edges.Where(e => e.To != e.From).Select(e => e.From));
        return edges
            .Where(e => candidates.Contains(e.To) && !sending.Contains(e.To) &&
                        ids.Contains(e.From) && !candidates.Contains(e.From))
            .GroupBy(e => e.To)
            .ToDictionary(g => g.Key, g => g.OrderBy(e => e.ToOffset).First());
    }

    static void Merge(LayoutPlan plan, LayeredLayout.Result result, float dy)
    {
        var offset = plan.Relays.Count;
        PortRef Shift(PortRef r) => r.IsRelay ? PortRef.OfRelay(r.Relay + offset) : r;

        foreach (var pair in result.Positions)
            plan.Positions[pair.Key] = (pair.Value.X, pair.Value.Y + dy);
        foreach (var relay in result.Relays)
            plan.Relays.Add(new PlannedRelay(relay.Index + offset, relay.X, relay.Y + dy, Shift(relay.Upstream)));
        foreach (var pair in result.Routes)
            plan.Routes[pair.Key] = Shift(pair.Value);
    }

    /// <summary>
    /// Relay를 없애고 그 앞뒤를 직접 잇는다. Relay가 여러 출력을 받으면 받는 쪽에 그 순서대로 모두 잇는다.
    /// 입력이나 출력 한쪽이 비어 있는 Relay는 풀지 않고 일반 객체로 둔다.
    /// </summary>
    static (List<LayoutNode> Nodes, List<LayoutEdge> Edges) DissolveRelays(
        IReadOnlyList<LayoutNode> nodes, IReadOnlyList<LayoutEdge> edges)
    {
        var hasIn = new HashSet<Guid>(edges.Select(e => e.To));
        var hasOut = new HashSet<Guid>(edges.Select(e => e.From));
        var relays = new HashSet<Guid>(nodes
            .Where(n => n.IsRelay && hasIn.Contains(n.Id) && hasOut.Contains(n.Id))
            .Select(n => n.Id));

        var incoming = edges
            .Where(e => relays.Contains(e.To))
            .GroupBy(e => e.To)
            .ToDictionary(g => g.Key, g => g.ToList());

        // relay가 받는 실제 출력들 (Relay → Relay 사슬과 순환을 따라가며)
        IEnumerable<LayoutEdge> Origins(Guid relay, HashSet<Guid> visiting)
        {
            if (!visiting.Add(relay) || !incoming.TryGetValue(relay, out var list))
                yield break;
            foreach (var e in list)
            {
                if (relays.Contains(e.From))
                    foreach (var origin in Origins(e.From, visiting))
                        yield return origin;
                else
                    yield return e;
            }
            visiting.Remove(relay);
        }

        var result = new List<LayoutEdge>();
        var seen = new HashSet<(Connection, float, float)>();
        foreach (var e in edges)
        {
            if (relays.Contains(e.To)) continue;
            var origins = relays.Contains(e.From) ? Origins(e.From, new HashSet<Guid>()) : new[] { e };
            foreach (var o in origins)
            {
                var direct = new LayoutEdge(o.From, o.FromPort, o.FromOffset, e.To, e.ToPort, e.ToOffset);
                if (seen.Add((direct.Connection, direct.FromOffset, direct.ToOffset)))
                    result.Add(direct);
            }
        }

        return (nodes.Where(n => !relays.Contains(n.Id)).ToList(), result);
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

        /// <summary>덩어리의 객체와 새로 놓을 Relay를 모두 감싸는 영역.</summary>
        public static Rect Of(IEnumerable<LayoutNode> nodes, LayeredLayout.Result result, LayoutOptions options)
        {
            float left = float.MaxValue, top = float.MaxValue, right = float.MinValue, bottom = float.MinValue;
            void Include(float x, float y, float width, float height)
            {
                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x + width);
                bottom = Math.Max(bottom, y + height);
            }

            foreach (var n in nodes)
            {
                var p = result.Positions[n.Id];
                Include(p.X, p.Y, n.Width, n.Height);
            }
            foreach (var r in result.Relays)
                Include(r.X, r.Y, options.RelayWidth, options.RelayHeight);
            return new Rect(left, top, right, bottom);
        }

        public Rect Offset(float dy) => new(Left, Top + dy, Right, Bottom + dy);

        public bool Overlaps(Rect other, float gap) =>
            Left < other.Right + gap && other.Left < Right + gap &&
            Top < other.Bottom + gap && other.Top < Bottom + gap;
    }
}
