using System;
using System.Collections.Generic;
using System.Linq;
using Vimhopper.Core;
using Xunit;

namespace Vimhopper.Core.Tests;

public class TreeNavigatorTests
{
    /// <summary>간선 목록으로 만드는 테스트용 그래프. 목록 순서 = 화면상 위→아래 순서.</summary>
    sealed class FakeGraph : IGraph
    {
        readonly List<(Guid From, Guid To)> _edges = new();

        public FakeGraph Wire(Guid from, params Guid[] to)
        {
            foreach (var t in to) _edges.Add((from, t));
            return this;
        }

        public IReadOnlyList<Guid> Downstream(Guid id) => _edges.Where(e => e.From == id).Select(e => e.To).ToList();
        public IReadOnlyList<Guid> Upstream(Guid id) => _edges.Where(e => e.To == id).Select(e => e.From).ToList();
    }

    //  sliderA ─┐
    //           ├─ P ─┬─ C1 ── D
    //  sliderB ─┘     ├─ C2
    //                 └─ C3
    static readonly Guid SliderA = Guid.NewGuid(), SliderB = Guid.NewGuid();
    static readonly Guid P = Guid.NewGuid(), C1 = Guid.NewGuid(), C2 = Guid.NewGuid(), C3 = Guid.NewGuid(), D = Guid.NewGuid();

    static TreeNavigator Start(Guid at)
    {
        var graph = new FakeGraph()
            .Wire(SliderA, P)
            .Wire(SliderB, P)
            .Wire(P, C1, C2, C3)
            .Wire(C1, D);
        var nav = new TreeNavigator(graph);
        nav.Reset(at);
        return nav;
    }

    [Fact]
    public void L_goes_to_first_child_and_jk_walk_its_siblings()
    {
        var nav = Start(P);
        Assert.Equal(C1, nav.Move(Direction.Right));
        Assert.Equal(C2, nav.Move(Direction.Down));
        Assert.Equal(C3, nav.Move(Direction.Down));
        Assert.Null(nav.Move(Direction.Down));
        Assert.Equal(C3, nav.Current);
        Assert.Equal(C2, nav.Move(Direction.Up));
    }

    [Fact]
    public void H_returns_to_the_parent_we_came_from()
    {
        var nav = Start(P);
        nav.Move(Direction.Right);
        nav.Move(Direction.Down); // C2
        Assert.Equal(P, nav.Move(Direction.Left));
    }

    [Fact]
    public void Backtracking_restores_sibling_context()
    {
        var nav = Start(P);
        nav.Move(Direction.Right);           // C1 (siblings: C1 C2 C3)
        Assert.Equal(D, nav.Move(Direction.Right));
        Assert.Equal(C1, nav.Move(Direction.Left));
        Assert.Equal(C2, nav.Move(Direction.Down)); // 여전히 P의 자식들 사이
    }

    [Fact]
    public void H_then_jk_walks_the_other_parents()
    {
        var nav = Start(P);
        Assert.Equal(SliderA, nav.Move(Direction.Left));
        Assert.Equal(SliderB, nav.Move(Direction.Down));
        Assert.Equal(P, nav.Move(Direction.Right));
    }

    [Fact]
    public void Without_history_jk_use_first_parent_as_anchor()
    {
        var nav = Start(C2);
        Assert.Equal(C3, nav.Move(Direction.Down));
        Assert.Equal(C2, nav.Move(Direction.Up));
        Assert.Equal(C1, nav.Move(Direction.Up));
    }

    [Fact]
    public void Root_without_history_walks_co_parents()
    {
        var nav = Start(SliderA);
        Assert.Equal(SliderB, nav.Move(Direction.Down));
    }

    [Fact]
    public void Dead_ends_stay_put()
    {
        var nav = Start(D);
        Assert.Null(nav.Move(Direction.Right));
        Assert.Equal(D, nav.Current);

        nav.Reset(SliderA);
        Assert.Null(nav.Move(Direction.Left));
    }

    [Fact]
    public void Isolated_object_cannot_move()
    {
        var nav = new TreeNavigator(new FakeGraph());
        var lone = Guid.NewGuid();
        nav.Reset(lone);
        foreach (var d in new[] { Direction.Left, Direction.Down, Direction.Up, Direction.Right })
            Assert.Null(nav.Move(d));
    }
}
