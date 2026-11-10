using System;
using System.Collections.Generic;
using System.Linq;
using Vimhopper.Core;
using Xunit;

namespace Vimhopper.Core.Tests;

public class SearchTests
{
    static readonly IReadOnlyList<IReadOnlyList<ParamRef>> None = Array.Empty<IReadOnlyList<ParamRef>>();

    static SearchItem Item(string nick, string name = "", string category = "", string sub = "", Guid? type = null) =>
        new(Guid.NewGuid(), nick, name, category, sub, type ?? Guid.Empty);

    [Theory]
    [InlineData("add", true)]
    [InlineData("ADD", true)]
    [InlineData("Maths", true)]
    [InlineData("oper", true)]
    [InlineData("반지름", true)]
    [InlineData("xyz", false)]
    [InlineData("", false)]
    public void Text_matches_nickname_name_and_category(string text, bool expected)
    {
        var item = Item("반지름 A", "Addition", "Maths", "Operators");
        Assert.Equal(expected, new TextQuery(text).Matches(item));
    }

    [Fact]
    public void Type_matches_the_component_guid()
    {
        var type = Guid.NewGuid();
        var query = new TypeQuery(type, "Addition");
        Assert.True(query.Matches(Item("A", type: type)));
        Assert.False(query.Matches(Item("A", type: Guid.NewGuid())));
        Assert.Equal("*Addition", query.Label);
    }

    // 덩어리 순서 → 층(열) → 위→아래
    //   a ─→ b          (덩어리 0, 위쪽)
    //   c ─┘
    //   d               (덩어리 1, 아래쪽, 연결 없음)
    [Fact]
    public void Order_is_island_then_column_then_top_to_bottom()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid(), d = Guid.NewGuid();
        var graph = new CanvasGraph(new[]
        {
            // b는 화면에서 a보다 위에 있어도 층이 오른쪽이라 뒤에 온다
            new GraphNode(b, 200, -50, 80, 40, false,
                new IReadOnlyList<ParamRef>[] { new[] { new ParamRef(a, 0) }, new[] { new ParamRef(c, 0) } }, None),
            new GraphNode(d, 0, 300, 80, 40, false, None, None),
            new GraphNode(c, 0, 100, 80, 40, false, None, new IReadOnlyList<ParamRef>[] { new[] { new ParamRef(b, 1) } }),
            new GraphNode(a, 0, 0, 80, 40, false, None, new IReadOnlyList<ParamRef>[] { new[] { new ParamRef(b, 0) } }),
        });
        Assert.Equal(new[] { a, c, b, d }, Search.Order(graph));
    }

    static readonly Guid[] Order = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToArray();

    // 0 1 2 3 4 5 중 1, 3, 4가 맞음
    static bool Matches(Guid id) => id == Order[1] || id == Order[3] || id == Order[4];

    [Fact]
    public void Next_goes_to_the_first_match_after_the_cursor()
    {
        var hit = Search.Next(Order, Matches, Order[1], forward: true)!;
        Assert.Equal(new SearchHit(Order[3], 2, 3, false), hit);
        // 커서가 맞지 않는 객체여도 그 뒤부터
        Assert.Equal(Order[3], Search.Next(Order, Matches, Order[2], forward: true)!.Target);
    }

    [Fact]
    public void Previous_goes_to_the_last_match_before_the_cursor()
    {
        Assert.Equal(new SearchHit(Order[1], 1, 3, false), Search.Next(Order, Matches, Order[3], forward: false));
    }

    [Fact]
    public void Wraps_around_both_ends()
    {
        Assert.Equal(new SearchHit(Order[1], 1, 3, true), Search.Next(Order, Matches, Order[4], forward: true));
        Assert.Equal(new SearchHit(Order[1], 1, 3, true), Search.Next(Order, Matches, Order[5], forward: true));
        Assert.Equal(new SearchHit(Order[4], 3, 3, true), Search.Next(Order, Matches, Order[1], forward: false));
        Assert.Equal(new SearchHit(Order[4], 3, 3, true), Search.Next(Order, Matches, Order[0], forward: false));
    }

    [Fact]
    public void Count_steps_through_matches_and_wraps()
    {
        Assert.Equal(new SearchHit(Order[4], 3, 3, false), Search.Next(Order, Matches, Order[0], true, count: 3));
        Assert.Equal(new SearchHit(Order[1], 1, 3, true), Search.Next(Order, Matches, Order[0], true, count: 4));
        Assert.Equal(new SearchHit(Order[1], 1, 3, false), Search.Next(Order, Matches, Order[5], false, count: 3));
        Assert.Equal(new SearchHit(Order[4], 3, 3, true), Search.Next(Order, Matches, Order[5], false, count: 4));
    }

    [Fact]
    public void Sole_match_on_the_cursor_wraps_to_itself()
    {
        Assert.Equal(new SearchHit(Order[1], 1, 1, true), Search.Next(Order, id => id == Order[1], Order[1], true));
    }

    [Fact]
    public void Unknown_cursor_starts_from_the_ends()
    {
        Assert.Equal(Order[1], Search.Next(Order, Matches, null, forward: true)!.Target);
        Assert.Equal(Order[4], Search.Next(Order, Matches, Guid.NewGuid(), forward: false)!.Target);
        Assert.False(Search.Next(Order, Matches, null, forward: true)!.Wrapped);
    }

    [Fact]
    public void No_match_is_null()
    {
        Assert.Null(Search.Next(Order, _ => false, Order[0], true));
    }

    [Fact]
    public void Describe_shows_position_and_wrap()
    {
        var query = new TextQuery("abc");
        Assert.Equal("[2/3] /abc", Search.Describe(new SearchHit(Order[3], 2, 3, false), query, true));
        Assert.Equal("[1/3] /abc   search hit BOTTOM, continuing at TOP",
            Search.Describe(new SearchHit(Order[1], 1, 3, true), query, true));
        Assert.EndsWith("search hit TOP, continuing at BOTTOM", Search.Describe(new SearchHit(Order[1], 1, 3, true), query, false));
    }

    [Fact]
    public void All_lists_matches()
    {
        var items = new[] { Item("Area"), Item("Move"), Item("area 2") };
        Assert.Equal(new[] { items[0].Id, items[2].Id }, Search.All(items, new TextQuery("area")));
    }
}
