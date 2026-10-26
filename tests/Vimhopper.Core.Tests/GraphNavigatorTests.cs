using System;
using System.Collections.Generic;
using System.Linq;
using Vimhopper.Core;
using Xunit;

namespace Vimhopper.Core.Tests;

public class GraphNavigatorTests
{
    /// <summary>이름과 파라미터 번호로 와이어를 만드는 테스트용 캔버스.</summary>
    sealed class Canvas
    {
        readonly Dictionary<string, (Guid Id, float X, float Y, bool Relay)> _objects = new();
        readonly List<(string From, int Output, string To, int Input)> _wires = new();

        public Canvas Add(string name, float x, float y, bool relay = false)
        {
            _objects[name] = (Guid.NewGuid(), x, y, relay);
            return this;
        }

        public Canvas Wire(string from, int output, string to, int input)
        {
            _wires.Add((from, output, to, input));
            return this;
        }

        public Canvas Wire(string from, string to) => Wire(from, 0, to, 0);

        public Guid this[string name] => _objects[name].Id;

        public string Name(Guid? id) => id is { } value ? _objects.First(o => o.Value.Id == value).Key : "(none)";

        public CanvasGraph Graph()
        {
            var nodes = _objects.Select(o =>
            {
                var name = o.Key;
                var inputCount = _wires.Where(w => w.To == name).Select(w => w.Input + 1).DefaultIfEmpty(0).Max();
                var outputCount = _wires.Where(w => w.From == name).Select(w => w.Output + 1).DefaultIfEmpty(0).Max();
                var inputs = Enumerable.Range(0, inputCount)
                    .Select(i => (IReadOnlyList<ParamRef>)_wires.Where(w => w.To == name && w.Input == i)
                        .Select(w => new ParamRef(this[w.From], w.Output)).ToList())
                    .ToList();
                var outputs = Enumerable.Range(0, outputCount)
                    .Select(i => (IReadOnlyList<ParamRef>)_wires.Where(w => w.From == name && w.Output == i)
                        .Select(w => new ParamRef(this[w.To], w.Input)).ToList())
                    .ToList();
                return new GraphNode(o.Value.Id, o.Value.X, o.Value.Y, 80, 40, o.Value.Relay, inputs, outputs);
            });
            return new CanvasGraph(nodes);
        }
    }

    //  w ──→ w/2.0             층: w, '2' = 0 / w/2 = 1 / Neg = 2 / Domain = 3
    //  '2' → w/2.1
    //  w/2 ──→ Neg ──→ Domain.0
    //   └───────────→ Domain.1
    static Canvas HalfWidthDomain() => new Canvas()
        .Add("w", 0, 0).Add("2", 0, 60).Add("w/2", 120, 0).Add("Neg", 240, 0).Add("Domain", 360, 0)
        .Wire("w", 0, "w/2", 0)
        .Wire("2", 0, "w/2", 1)
        .Wire("w/2", 0, "Neg", 0)
        .Wire("Neg", 0, "Domain", 0)
        .Wire("w/2", 0, "Domain", 1);

    [Theory]
    [InlineData("Domain", null, "Neg")]
    [InlineData("Domain", 0, "Neg")]
    [InlineData("Domain", 1, "w/2")]
    [InlineData("w/2", null, "w")]
    [InlineData("w/2", 1, "2")]
    [InlineData("w", null, "(none)")]
    [InlineData("Domain", 2, "(none)")]
    public void H_goes_to_the_source_of_the_top_or_given_input(string from, int? input, string expected)
    {
        var canvas = HalfWidthDomain();
        Assert.Equal(expected, canvas.Name(GraphNavigator.Parent(canvas.Graph(), canvas[from], input)));
    }

    [Fact]
    public void H_skips_unconnected_top_inputs()
    {
        var canvas = new Canvas().Add("A", 0, 0).Add("B", 120, 0).Wire("A", 0, "B", 2);
        Assert.Equal("A", canvas.Name(GraphNavigator.Parent(canvas.Graph(), canvas["B"])));
    }

    [Fact]
    public void L_prefers_the_child_whose_top_input_is_this_object()
    {
        // w/2의 자식은 Neg(맨 위 입력이 w/2)와 Domain(맨 위 입력은 Neg) → Neg
        var canvas = HalfWidthDomain();
        Assert.Equal("Neg", canvas.Name(GraphNavigator.Child(canvas.Graph(), canvas["w/2"])));
    }

    [Fact]
    public void L_prefers_aligned_child_even_if_it_is_lower_on_screen()
    {
        //  Z ──→ Upper.0        X ──→ Upper.1 (화면 위, 하지만 맨 위 입력은 Z)
        //                       X ──→ Lower.0 (화면 아래, 맨 위 입력이 X)
        var canvas = new Canvas()
            .Add("Z", 0, -100).Add("X", 0, 0).Add("Upper", 120, -50).Add("Lower", 120, 100)
            .Wire("Z", 0, "Upper", 0)
            .Wire("X", 0, "Upper", 1)
            .Wire("X", 0, "Lower", 0);
        Assert.Equal("Lower", canvas.Name(GraphNavigator.Child(canvas.Graph(), canvas["X"])));
    }

    [Fact]
    public void L_falls_back_to_first_child_by_output_then_screen_order()
    {
        //  P.1 → C1.1 (위)   P.0 → C2.1 (아래)   둘 다 맨 위 입력은 다른 객체
        var canvas = new Canvas()
            .Add("Other", 0, -200).Add("P", 0, 0).Add("C1", 120, 0).Add("C2", 120, 100)
            .Wire("Other", 0, "C1", 0).Wire("Other", 0, "C2", 0)
            .Wire("P", 1, "C1", 1)
            .Wire("P", 0, "C2", 1);
        var graph = canvas.Graph();

        Assert.Equal("C2", canvas.Name(GraphNavigator.Child(graph, canvas["P"])));
        Assert.Equal("C1", canvas.Name(GraphNavigator.Child(graph, canvas["P"], 1)));
        Assert.Equal("(none)", canvas.Name(GraphNavigator.Child(graph, canvas["P"], 2)));
    }

    [Fact]
    public void J_and_k_move_within_the_same_layer()
    {
        var canvas = HalfWidthDomain();
        var graph = canvas.Graph();

        Assert.Equal("2", canvas.Name(GraphNavigator.Sibling(graph, canvas["w"], 1)));
        Assert.Equal("w", canvas.Name(GraphNavigator.Sibling(graph, canvas["2"], -1)));
        Assert.Equal("(none)", canvas.Name(GraphNavigator.Sibling(graph, canvas["2"], 1)));
        Assert.Equal("(none)", canvas.Name(GraphNavigator.Sibling(graph, canvas["Neg"], 1)));
    }

    [Fact]
    public void J_continues_into_the_same_layer_of_the_next_island()
    {
        // 아래쪽 덩어리 P → Q → R: Q는 1층, w/2와 같은 층
        var canvas = HalfWidthDomain()
            .Add("P", 0, 300).Add("Q", 120, 300).Add("R", 240, 300)
            .Wire("P", "Q").Wire("Q", "R");
        var graph = canvas.Graph();

        Assert.Equal("Q", canvas.Name(GraphNavigator.Sibling(graph, canvas["w/2"], 1)));
        Assert.Equal("w/2", canvas.Name(GraphNavigator.Sibling(graph, canvas["Q"], -1)));
        Assert.Equal("R", canvas.Name(GraphNavigator.Sibling(graph, canvas["Neg"], 1)));
    }

    [Fact]
    public void Count_moves_several_steps_and_stops_at_the_end()
    {
        var canvas = new Canvas()
            .Add("S", 0, 0)
            .Add("A", 120, 0).Add("B", 120, 100).Add("C", 120, 200)
            .Wire("S", "A").Wire("S", "B").Wire("S", "C");
        var graph = canvas.Graph();

        Assert.Equal("C", canvas.Name(GraphNavigator.Sibling(graph, canvas["A"], 2)));
        Assert.Equal("C", canvas.Name(GraphNavigator.Sibling(graph, canvas["A"], 9)));
        Assert.Equal("A", canvas.Name(GraphNavigator.Sibling(graph, canvas["C"], -9)));
    }

    [Fact]
    public void Layers_ignore_screen_position()
    {
        // 화면상 B가 A보다 왼쪽에 있어도 층은 연결로 정해진다
        var canvas = new Canvas().Add("A", 500, 0).Add("B", 0, 0).Add("C", 900, 0).Add("D", 0, 100)
            .Wire("A", "B").Wire("C", "D").Wire("A", "C");
        var graph = canvas.Graph();

        Assert.Equal(graph.LayerOf(canvas["B"])!.Value.Layer, graph.LayerOf(canvas["C"])!.Value.Layer);
        Assert.Equal("C", canvas.Name(GraphNavigator.Sibling(graph, canvas["B"], 1)));
    }

    [Fact]
    public void Relays_are_stops_and_sit_just_before_what_they_feed()
    {
        //  A → B → C.0        A → R(Relay) → C.1      R은 C 바로 앞 층 = B와 같은 층
        var canvas = new Canvas()
            .Add("A", 0, 0).Add("B", 120, 0).Add("R", 120, 100, relay: true).Add("C", 240, 0)
            .Wire("A", "B").Wire("B", 0, "C", 0)
            .Wire("A", "R").Wire("R", 0, "C", 1);
        var graph = canvas.Graph();

        Assert.Equal(graph.LayerOf(canvas["B"])!.Value.Layer, graph.LayerOf(canvas["R"])!.Value.Layer);
        Assert.Equal("R", canvas.Name(GraphNavigator.Sibling(graph, canvas["B"], 1)));
        Assert.Equal("R", canvas.Name(GraphNavigator.Parent(graph, canvas["C"], 1)));
        Assert.Equal("A", canvas.Name(GraphNavigator.Parent(graph, canvas["R"])));
    }

    [Fact]
    public void Relay_layer_does_not_shift_what_comes_after()
    {
        // A → R → B: Relay를 일반 객체로 세면 B가 2층이 되지만, 정렬처럼 풀고 세면 1층
        var canvas = new Canvas()
            .Add("A", 0, 0).Add("R", 120, 0, relay: true).Add("B", 240, 0).Add("X", 0, 100).Add("Y", 120, 100)
            .Wire("A", "R").Wire("R", "B").Wire("X", "Y").Wire("Y", 0, "B", 1);
        var graph = canvas.Graph();

        Assert.Equal(graph.LayerOf(canvas["Y"])!.Value.Layer + 1, graph.LayerOf(canvas["B"])!.Value.Layer);
        Assert.Equal(graph.LayerOf(canvas["Y"])!.Value.Layer, graph.LayerOf(canvas["R"])!.Value.Layer);
    }

    [Fact]
    public void Cycles_do_not_break_navigation()
    {
        var canvas = new Canvas().Add("A", 0, 0).Add("B", 120, 0).Wire("A", "B").Wire("B", "A");
        var graph = canvas.Graph();

        Assert.Equal("B", canvas.Name(GraphNavigator.Child(graph, canvas["A"])));
        Assert.Equal("B", canvas.Name(GraphNavigator.Parent(graph, canvas["A"])));
        Assert.NotNull(graph.LayerOf(canvas["A"]));
    }
}
