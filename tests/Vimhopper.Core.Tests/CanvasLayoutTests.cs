using System;
using System.Collections.Generic;
using System.Linq;
using Vimhopper.Core;
using Xunit;

namespace Vimhopper.Core.Tests;

public class CanvasLayoutTests
{
    /// <summary>이름으로 객체와 와이어를 만드는 테스트용 캔버스.</summary>
    sealed class Canvas
    {
        readonly Dictionary<string, LayoutNode> _nodes = new();
        readonly Dictionary<(string Node, bool Output, float Offset), Guid> _ports = new();
        public readonly List<LayoutEdge> Edges = new();

        public Canvas Add(string name, float x, float y, float width = 80, float height = 60)
        {
            _nodes[name] = new LayoutNode(Guid.NewGuid(), x, y, width, height);
            return this;
        }

        public Canvas Relay(string name, float x, float y)
        {
            var options = new LayoutOptions();
            _nodes[name] = new LayoutNode(Guid.NewGuid(), x, y, options.RelayWidth, options.RelayHeight, isRelay: true);
            return this;
        }

        /// <summary>from의 가운데 높이 출력 → to의 가운데 높이 입력.</summary>
        public Canvas Wire(string from, string to) =>
            Wire(from, _nodes[from].Height / 2, to, _nodes[to].Height / 2);

        /// <summary>그립은 객체와 높이로 구분한다 (같은 높이 = 같은 파라미터).</summary>
        public Canvas Wire(string from, float fromOffset, string to, float toOffset)
        {
            Edges.Add(new LayoutEdge(
                _nodes[from].Id, Port(from, true, fromOffset), fromOffset,
                _nodes[to].Id, Port(to, false, toOffset), toOffset));
            return this;
        }

        public Guid Port(string node, bool output, float offset)
        {
            // Relay는 객체 자체가 파라미터
            if (_nodes[node].IsRelay)
                return _nodes[node].Id;
            if (!_ports.TryGetValue((node, output, offset), out var id))
                _ports[(node, output, offset)] = id = Guid.NewGuid();
            return id;
        }

        public PortRef Output(string node, float offset) => PortRef.Real(_nodes[node].Id, Port(node, true, offset));

        public Connection Connection(string from, float fromOffset, string to, float toOffset) =>
            new(_nodes[from].Id, Port(from, true, fromOffset), _nodes[to].Id, Port(to, false, toOffset));

        public IReadOnlyList<LayoutNode> Nodes => _nodes.Values.ToList();
        public LayoutNode this[string name] => _nodes[name];

        public Result Arrange() => new(this, CanvasLayout.Arrange(Nodes, Edges));
        public Result ArrangeAll() => new(this, CanvasLayout.ArrangeAll(Nodes, Edges));
        public LayoutPlan Plan() => CanvasLayout.Plan(Nodes, Edges);
    }

    sealed class Result
    {
        readonly Canvas _canvas;
        public readonly Dictionary<Guid, (float X, float Y)> Positions;

        public Result(Canvas canvas, Dictionary<Guid, (float X, float Y)> positions)
        {
            _canvas = canvas;
            Positions = positions;
        }

        public float X(string name) => Positions[_canvas[name].Id].X;
        public float Y(string name) => Positions[_canvas[name].Id].Y;
        public float Right(string name) => X(name) + _canvas[name].Width;
        public float Bottom(string name) => Y(name) + _canvas[name].Height;
    }

    // 예제 (README 층 기반 레이아웃 검토):
    //   w (slider) ──→ w/2.A
    //   '2' (panel) ─→ w/2.B
    //   w/2 ──→ Neg ──→ Domain.A
    //    └──────────────→ Domain.B
    static Canvas HalfWidthDomain() => new Canvas()
        .Add("w", 0, 0, 160, 20)
        .Add("2", 0, 40, 60, 30)
        .Add("w/2", 200, 0, 80, 50)
        .Add("Neg", 320, 0, 60, 30)
        .Add("Domain", 420, 0, 90, 50)
        .Wire("w", 10, "w/2", 15)
        .Wire("2", 15, "w/2", 35)
        .Wire("w/2", 25, "Neg", 15)
        .Wire("Neg", 15, "Domain", 15)
        .Wire("w/2", 25, "Domain", 35);

    [Fact]
    public void Longest_path_puts_every_object_right_of_all_its_parents()
    {
        var r = HalfWidthDomain().Arrange();

        Assert.True(r.Right("w") < r.X("w/2"));
        Assert.True(r.Right("w/2") < r.X("Neg"));
        Assert.True(r.Right("Neg") < r.X("Domain"));
    }

    [Fact]
    public void Columns_are_right_aligned()
    {
        var r = HalfWidthDomain().Arrange();

        // 슬라이더와 패널은 같은 열 (w/2 바로 앞)
        Assert.Equal(r.Right("w"), r.Right("2"));
    }

    [Fact]
    public void Input_values_sit_just_before_their_first_consumer()
    {
        //  A → B → C → D
        //          panel ↗
        var r = new Canvas()
            .Add("A", 0, 0).Add("B", 120, 0).Add("C", 240, 0).Add("D", 360, 0)
            .Add("panel", 0, 200, 100, 40)
            .Wire("A", "B").Wire("B", "C").Wire("C", "D").Wire("panel", "D")
            .Arrange();

        Assert.Equal(r.Right("C"), r.Right("panel"));
        Assert.True(r.Right("panel") < r.X("D"));
    }

    [Fact]
    public void Chain_wires_are_horizontal()
    {
        var r = new Canvas()
            .Add("A", 0, 0, 80, 60).Add("B", 150, 90, 80, 80).Add("C", 300, -40, 80, 40)
            .Wire("A", 30, "B", 20)
            .Wire("B", 60, "C", 20)
            .Arrange();

        Assert.Equal(r.Y("A") + 30, r.Y("B") + 20);
        Assert.Equal(r.Y("B") + 60, r.Y("C") + 20);
    }

    [Fact]
    public void Only_the_top_input_wire_is_horizontal()
    {
        var r = HalfWidthDomain().Arrange();

        // Domain.A(맨 위 입력) ← Neg 는 수평, Domain.B ← w/2 는 기울어도 됨
        Assert.Equal(r.Y("Neg") + 15, r.Y("Domain") + 15);
        Assert.NotEqual(r.Y("w/2") + 25, r.Y("Domain") + 35);

        // w/2.A ← w 는 수평. 패널은 슬라이더에 밀려 그 아래로
        Assert.Equal(r.Y("w") + 10, r.Y("w/2") + 15);
        Assert.True(r.Y("2") >= r.Bottom("w"));
    }

    [Fact]
    public void Upper_object_keeps_its_place_when_targets_collide()
    {
        //  A ─→ C.0      두 객체가 같은 높이를 원하면 위(C)가 차지하고 아래(D)가 밀린다
        //  B ─→ D.0
        var r = new Canvas()
            .Add("A", 0, 0, 80, 20).Add("B", 0, 30, 80, 20)
            .Add("C", 200, 0, 80, 60).Add("D", 200, 100, 80, 60)
            .Wire("A", 10, "C", 10).Wire("B", 10, "D", 10)
            .Arrange();

        Assert.Equal(r.Y("A") + 10, r.Y("C") + 10);
        Assert.True(r.Y("D") >= r.Bottom("C"));
    }

    [Fact]
    public void Crossing_wires_are_untangled()
    {
        //  A ─┐┌─ C        A ── D
        //     ╳       →
        //  B ─┘└─ D        B ── C
        var r = new Canvas()
            .Add("A", 0, 0).Add("B", 0, 100)
            .Add("C", 200, 0).Add("D", 200, 100)
            .Add("Sink", 400, 0)
            .Wire("A", "D").Wire("B", "C").Wire("C", "Sink").Wire("D", "Sink")
            .Arrange();

        Assert.True((r.Y("A") - r.Y("B")) * (r.Y("D") - r.Y("C")) > 0);
    }

    [Fact]
    public void Keeps_existing_vertical_order_when_there_is_no_crossing()
    {
        var r = new Canvas()
            .Add("Top", 0, 0).Add("Bottom", 0, 300).Add("Sink", 200, 100)
            .Wire("Top", "Sink").Wire("Bottom", "Sink")
            .Arrange();

        Assert.True(r.Bottom("Top") <= r.Y("Bottom"));
    }

    [Fact]
    public void Keeps_top_left_corner()
    {
        var canvas = HalfWidthDomain();
        var r = canvas.Arrange();

        Assert.Equal(canvas.Nodes.Min(n => n.X), r.Positions.Values.Min(p => p.X));
        Assert.Equal(canvas.Nodes.Min(n => n.Y), r.Positions.Values.Min(p => p.Y));
    }

    [Fact]
    public void Cycles_do_not_break_layout()
    {
        var r = new Canvas()
            .Add("A", 0, 0).Add("B", 200, 0).Add("C", 400, 0)
            .Wire("A", "B").Wire("B", "C").Wire("C", "A")
            .Arrange();

        Assert.Equal(3, r.Positions.Count);
        Assert.True(r.Right("A") < r.X("B"));
        Assert.True(r.Right("B") < r.X("C"));
    }

    [Fact]
    public void Single_object_does_not_move()
    {
        var canvas = new Canvas().Add("A", 13, 27);
        var r = canvas.Arrange();

        Assert.Equal((13f, 27f), r.Positions[canvas["A"].Id]);
    }

    public static IEnumerable<object[]> Seeds => Enumerable.Range(1, 30).Select(s => new object[] { s });

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Random_graphs_have_no_overlaps(int seed)
    {
        var (nodes, edges) = RandomGraph(seed);
        var positions = CanvasLayout.Arrange(nodes, edges);

        AssertNoOverlap(nodes, positions);
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Formatting_twice_does_not_move_anything(int seed)
    {
        var (nodes, edges) = RandomGraph(seed);
        var first = CanvasLayout.Arrange(nodes, edges);
        var moved = nodes.Select(n => new LayoutNode(n.Id, first[n.Id].X, first[n.Id].Y, n.Width, n.Height)).ToList();
        var second = CanvasLayout.Arrange(moved, edges);

        foreach (var n in nodes)
            Assert.Equal(first[n.Id], second[n.Id]);
    }

    [Fact]
    public void Large_document_finishes_quickly()
    {
        var (nodes, edges) = RandomGraph(seed: 42, count: 400, density: 0.006);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        CanvasLayout.ArrangeAll(nodes, edges);

        Assert.True(watch.ElapsedMilliseconds < 1000, $"{watch.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void ArrangeAll_separates_islands_and_leaves_isolated_objects_alone()
    {
        //  섬 1: A → B (세로로 쌓여 있다가 가로로 펼쳐짐)   섬 2: C → D (섬 1과 간격이 부족)   외톨이: E (멀리)
        var canvas = new Canvas()
            .Add("A", 0, 0).Add("B", 0, 70)
            .Add("C", 0, 100).Add("D", 0, 170)
            .Add("E", 1000, 1000)
            .Wire("A", "B").Wire("C", "D");
        var r = canvas.ArrangeAll();

        AssertNoOverlap(canvas.Nodes, r.Positions);
        Assert.Equal(1000f, r.X("E"));
        Assert.Equal(1000f, r.Y("E"));
        Assert.True(r.Y("C") >= r.Bottom("A") + new LayoutOptions().IslandGap);
    }

    // ───────────────────────── Relay ─────────────────────────

    [Fact]
    public void Relay_is_placed_where_a_long_wire_bends()
    {
        // w/2 → Domain.B 는 Neg 열을 건너뛰고, Neg 아래로 밀려 꺾이므로 Relay 하나가 필요
        var canvas = HalfWidthDomain();
        var plan = canvas.Plan();

        var relay = Assert.Single(plan.Relays);
        Assert.Equal(canvas.Output("w/2", 25), relay.Upstream);
        Assert.Equal(PortRef.OfRelay(0), plan.Route(canvas.Connection("w/2", 25, "Domain", 35)));
        Assert.Equal(canvas.Output("Neg", 15), plan.Route(canvas.Connection("Neg", 15, "Domain", 15)));

        // Relay는 Neg와 같은 열, Neg 아래
        var positions = plan.Positions;
        Assert.Equal(positions[canvas["Neg"].Id].X + canvas["Neg"].Width, relay.X + new LayoutOptions().RelayWidth);
        Assert.True(relay.Y >= positions[canvas["Neg"].Id].Y + canvas["Neg"].Height);
    }

    // A.out0 → B → C → D.1        A.out1 ────────→ D.0 (수평으로 곧게 감)
    static Canvas StraightLongWire() => new Canvas()
        .Add("A", 0, 0, 80, 60).Add("B", 120, 0, 80, 30).Add("C", 240, 0, 80, 30).Add("D", 360, 0, 80, 60)
        .Wire("A", 10, "B", 15)
        .Wire("B", 15, "C", 15)
        .Wire("C", 15, "D", 45)
        .Wire("A", 50, "D", 15);

    [Fact]
    public void Straight_long_wire_needs_no_relay()
    {
        var canvas = StraightLongWire();
        var plan = canvas.Plan();

        Assert.Empty(plan.Relays);
        Assert.Equal(canvas.Output("A", 50), plan.Route(canvas.Connection("A", 50, "D", 15)));
        Assert.Equal(plan.Positions[canvas["A"].Id].Y + 50, plan.Positions[canvas["D"].Id].Y + 15);
    }

    [Fact]
    public void Long_wires_from_one_output_share_a_relay_and_branch()
    {
        // A.out1 → D.0, E.0 (둘 다 세 열 건너). D가 수평 자리를 차지하고 E는 아래로 밀려 갈라진다
        var canvas = StraightLongWire()
            .Add("E", 360, 200, 80, 60)
            .Wire("C", 15, "E", 45)
            .Wire("A", 50, "E", 15);
        var plan = canvas.Plan();

        var relay = Assert.Single(plan.Relays);
        Assert.Equal(canvas.Output("A", 50), relay.Upstream);
        Assert.Equal(PortRef.OfRelay(0), plan.Route(canvas.Connection("A", 50, "D", 15)));
        Assert.Equal(PortRef.OfRelay(0), plan.Route(canvas.Connection("A", 50, "E", 15)));
    }

    [Fact]
    public void Existing_relays_are_dissolved_and_recomputed()
    {
        //  A → R1 → R2 → B      (B는 A 바로 다음 열이 되므로 Relay가 필요 없다)
        //            R2 → C
        var canvas = new Canvas()
            .Add("A", 0, 0).Relay("R1", 100, 300).Relay("R2", 200, -100).Add("B", 300, 0).Add("C", 300, 200)
            .Wire("A", 30, "R1", 8)
            .Wire("R1", 8, "R2", 8)
            .Wire("R2", 8, "B", 30)
            .Wire("R2", 8, "C", 30);
        var plan = canvas.Plan();

        Assert.Empty(plan.Relays);
        Assert.False(plan.Positions.ContainsKey(canvas["R1"].Id));
        Assert.False(plan.Positions.ContainsKey(canvas["R2"].Id));
        Assert.Equal(canvas.Output("A", 30), plan.Route(canvas.Connection("A", 30, "B", 30)));
        Assert.Equal(canvas.Output("A", 30), plan.Route(canvas.Connection("A", 30, "C", 30)));
        Assert.True(plan.Positions[canvas["A"].Id].X + canvas["A"].Width < plan.Positions[canvas["B"].Id].X);
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Formatting_twice_with_relays_changes_nothing(int seed)
    {
        var (nodes, edges) = RandomGraph(seed);
        var options = new LayoutOptions();
        var first = CanvasLayout.Plan(nodes, edges, options);

        // 첫 결과를 문서에 적용한 상태: 객체는 새 위치에, Relay는 실제 객체로, 와이어는 경로대로
        var relayIds = first.Relays.Select(_ => Guid.NewGuid()).ToList();
        var applied = nodes
            .Select(n => new LayoutNode(n.Id, first.Positions[n.Id].X, first.Positions[n.Id].Y, n.Width, n.Height))
            .Concat(first.Relays.Select(r => new LayoutNode(relayIds[r.Index], r.X, r.Y, options.RelayWidth, options.RelayHeight, isRelay: true)))
            .ToList();
        var offsets = edges.ToDictionary(e => e.Connection, e => e.FromOffset);
        LayoutEdge From(PortRef source, Guid to, Guid toPort, float toOffset) => source.IsRelay
            ? new LayoutEdge(relayIds[source.Relay], relayIds[source.Relay], options.RelayHeight / 2, to, toPort, toOffset)
            : new LayoutEdge(source.Node, source.Port, edges.First(e => e.From == source.Node && e.FromPort == source.Port).FromOffset, to, toPort, toOffset);
        var appliedEdges = first.Relays
            .Select(r => From(r.Upstream, relayIds[r.Index], relayIds[r.Index], options.RelayHeight / 2))
            .Concat(edges.Select(e => From(first.Route(e.Connection), e.To, e.ToPort, e.ToOffset)))
            .ToList();

        var second = CanvasLayout.Plan(applied, appliedEdges, options);

        foreach (var n in nodes)
            Assert.Equal(first.Positions[n.Id], second.Positions[n.Id]);
        Assert.Equal(first.Relays.Count, second.Relays.Count);
        for (var i = 0; i < first.Relays.Count; i++)
        {
            Assert.Equal((first.Relays[i].X, first.Relays[i].Y), (second.Relays[i].X, second.Relays[i].Y));
            Assert.Equal(first.Relays[i].Upstream, second.Relays[i].Upstream);
        }
        foreach (var e in edges)
            Assert.Equal(first.Route(e.Connection), second.Route(e.Connection));
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Drawn_wires_bend_only_between_neighboring_columns(int seed)
    {
        var (nodes, edges) = RandomGraph(seed);
        var options = new LayoutOptions();
        var plan = CanvasLayout.Plan(nodes, edges, options);
        var size = nodes.ToDictionary(n => n.Id);

        // 열은 오른쪽 가장자리로 구분된다 (열 안 객체는 오른쪽 정렬)
        var rights = plan.Positions.Select(p => p.Value.X + size[p.Key].Width)
            .Concat(plan.Relays.Select(r => r.X + options.RelayWidth))
            .Distinct().OrderBy(x => x).ToList();
        int Column(float right) => rights.IndexOf(right);

        // 출력 쪽 (열, 그립 높이)
        (int Column, float Y) Source(PortRef source)
        {
            if (source.IsRelay)
            {
                var r = plan.Relays[source.Relay];
                return (Column(r.X + options.RelayWidth), r.Y + options.RelayHeight / 2);
            }
            var p = plan.Positions[source.Node];
            var offset = edges.First(e => e.From == source.Node && e.FromPort == source.Port).FromOffset;
            return (Column(p.X + size[source.Node].Width), p.Y + offset);
        }

        var wires = plan.Relays
            .Select(r => (From: Source(r.Upstream), To: (Column(r.X + options.RelayWidth), r.Y + options.RelayHeight / 2)))
            .Concat(edges.Select(e =>
            {
                var p = plan.Positions[e.To];
                return (From: Source(plan.Route(e.Connection)), To: (Column(p.X + size[e.To].Width), p.Y + e.ToOffset));
            }));

        foreach (var (from, to) in wires)
        {
            // 거꾸로 가는 와이어(순환)는 무작위 그래프에 없다
            Assert.True(to.Item1 > from.Column);
            if (to.Item1 - from.Column > 1)
                Assert.True(Math.Abs(to.Item2 - from.Y) < 1, $"column {from.Column}→{to.Item1}, y {from.Y}→{to.Item2}");
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Relays_do_not_overlap_objects(int seed)
    {
        var (nodes, edges) = RandomGraph(seed);
        var options = new LayoutOptions();
        var plan = CanvasLayout.Plan(nodes, edges, options);

        var all = nodes
            .Select(n => new LayoutNode(n.Id, 0, 0, n.Width, n.Height))
            .Concat(plan.Relays.Select(r => new LayoutNode(Guid.NewGuid(), 0, 0, options.RelayWidth, options.RelayHeight)))
            .ToList();
        var positions = new Dictionary<Guid, (float X, float Y)>(plan.Positions);
        for (var i = 0; i < plan.Relays.Count; i++)
            positions[all[nodes.Count + i].Id] = (plan.Relays[i].X, plan.Relays[i].Y);

        AssertNoOverlap(all, positions);
    }

    static (List<LayoutNode> Nodes, List<LayoutEdge> Edges) RandomGraph(int seed, int count = 30, double density = 0.08)
    {
        var random = new Random(seed);
        var nodes = Enumerable.Range(0, count)
            .Select(_ => new LayoutNode(
                Guid.NewGuid(),
                random.Next(0, 1500), random.Next(0, 1000),
                random.Next(40, 200), random.Next(20, 120)))
            .ToList();

        // 객체마다 출력 1~3개, 입력 1~4개를 높이 순으로 두고 (Grasshopper처럼 한 출력이 여러 곳으로 갈 수 있음)
        // 인덱스가 작은 쪽 → 큰 쪽만 이어 DAG를 만든다
        List<(Guid Port, float Offset)> Ports(LayoutNode n, int count) =>
            Enumerable.Range(0, count).Select(k => (Guid.NewGuid(), n.Height * (k + 0.5f) / count)).ToList();
        var outputs = nodes.Select(n => Ports(n, random.Next(1, 4))).ToList();
        var inputs = nodes.Select(n => Ports(n, random.Next(1, 5))).ToList();

        var edges = new List<LayoutEdge>();
        for (var i = 0; i < nodes.Count; i++)
        {
            for (var j = i + 1; j < nodes.Count; j++)
            {
                if (random.NextDouble() >= density) continue;
                var output = outputs[i][random.Next(outputs[i].Count)];
                var input = inputs[j][random.Next(inputs[j].Count)];
                edges.Add(new LayoutEdge(nodes[i].Id, output.Port, output.Offset, nodes[j].Id, input.Port, input.Offset));
            }
        }
        return (nodes, edges);
    }

    static void AssertNoOverlap(IReadOnlyList<LayoutNode> nodes, IReadOnlyDictionary<Guid, (float X, float Y)> positions)
    {
        for (var i = 0; i < nodes.Count; i++)
        {
            for (var j = i + 1; j < nodes.Count; j++)
            {
                var a = positions[nodes[i].Id];
                var b = positions[nodes[j].Id];
                var overlaps =
                    a.X < b.X + nodes[j].Width && b.X < a.X + nodes[i].Width &&
                    a.Y < b.Y + nodes[j].Height && b.Y < a.Y + nodes[i].Height;
                Assert.False(overlaps, $"{i} and {j} overlap");
            }
        }
    }
}
