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
        public readonly List<LayoutEdge> Edges = new();

        public Canvas Add(string name, float x, float y, float width = 80, float height = 60)
        {
            _nodes[name] = new LayoutNode(Guid.NewGuid(), x, y, width, height);
            return this;
        }

        /// <summary>from의 가운데 높이 출력 → to의 가운데 높이 입력.</summary>
        public Canvas Wire(string from, string to) =>
            Wire(from, _nodes[from].Height / 2, to, _nodes[to].Height / 2);

        public Canvas Wire(string from, float fromOffset, string to, float toOffset)
        {
            Edges.Add(new LayoutEdge(_nodes[from].Id, fromOffset, _nodes[to].Id, toOffset));
            return this;
        }

        public IReadOnlyList<LayoutNode> Nodes => _nodes.Values.ToList();
        public LayoutNode this[string name] => _nodes[name];

        public Result Arrange() => new(this, CanvasLayout.Arrange(Nodes, Edges));
        public Result ArrangeAll() => new(this, CanvasLayout.ArrangeAll(Nodes, Edges));
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

    static (List<LayoutNode> Nodes, List<LayoutEdge> Edges) RandomGraph(int seed, int count = 30, double density = 0.08)
    {
        var random = new Random(seed);
        var nodes = Enumerable.Range(0, count)
            .Select(_ => new LayoutNode(
                Guid.NewGuid(),
                random.Next(0, 1500), random.Next(0, 1000),
                random.Next(40, 200), random.Next(20, 120)))
            .ToList();

        // 인덱스가 작은 쪽 → 큰 쪽만 이어 DAG를 만들고, 그립 높이는 객체 안에서 임의로
        var edges = new List<LayoutEdge>();
        for (var i = 0; i < nodes.Count; i++)
            for (var j = i + 1; j < nodes.Count; j++)
                if (random.NextDouble() < density)
                    edges.Add(new LayoutEdge(
                        nodes[i].Id, (float)(random.NextDouble() * nodes[i].Height),
                        nodes[j].Id, (float)(random.NextDouble() * nodes[j].Height)));
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
