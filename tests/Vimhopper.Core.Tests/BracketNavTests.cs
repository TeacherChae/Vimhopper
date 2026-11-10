using System;
using System.Collections.Generic;
using System.Linq;
using Vimhopper.Core;
using Xunit;

namespace Vimhopper.Core.Tests;

public class BracketNavTests
{
    static readonly IReadOnlyList<IReadOnlyList<ParamRef>> None = Array.Empty<IReadOnlyList<ParamRef>>();

    // 이름 → 출처 이름들로 그래프를 만든다 (입력 하나에 출처 여럿). 좌표는 이름 순서대로 위→아래
    static (CanvasGraph Graph, Dictionary<string, Guid> Ids) Build(params (string Name, string[] Sources)[] nodes)
    {
        var ids = nodes.ToDictionary(n => n.Name, _ => Guid.NewGuid());
        var outputs = nodes.ToDictionary(n => n.Name, _ => new List<ParamRef>());
        foreach (var (name, sources) in nodes)
            foreach (var s in sources)
                outputs[s].Add(new ParamRef(ids[name], 0));
        var graph = new CanvasGraph(nodes.Select((n, i) => new GraphNode(
            ids[n.Name], 0, i * 50, 80, 40, false,
            n.Sources.Length == 0 ? None : new IReadOnlyList<ParamRef>[] { n.Sources.Select(s => new ParamRef(ids[s], 0)).ToList() },
            new IReadOnlyList<ParamRef>[] { outputs[n.Name] })));
        return (graph, ids);
    }

    static Func<Guid, MessageLevel> Levels(Dictionary<string, Guid> ids, Dictionary<string, MessageLevel> levels) =>
        id => levels.FirstOrDefault(p => ids[p.Key] == id).Value;

    // a(오류) → relay → b(오류) → c(오류)
    // d(오류) → e
    [Fact]
    public void Errors_downstream_of_an_error_are_not_root_causes()
    {
        var (graph, ids) = Build(
            ("a", new string[0]), ("relay", new[] { "a" }), ("b", new[] { "relay" }), ("c", new[] { "b" }),
            ("d", new string[0]), ("e", new[] { "d" }));
        var levels = Levels(ids, new()
        {
            ["a"] = MessageLevel.Error, ["b"] = MessageLevel.Error, ["c"] = MessageLevel.Error, ["d"] = MessageLevel.Error,
        });
        Assert.Equal(new[] { ids["a"], ids["d"] }.ToHashSet(), BracketNav.RootCauses(graph, levels, MessageLevel.Error));
    }

    // 경고는 상류에 경고나 오류가 있으면 건너뛴다. 오류는 상류의 경고를 보지 않는다
    //   w1(경고) → w2(경고)
    //   e1(오류) → w3(경고)
    //   w4(경고) → e2(오류)
    [Fact]
    public void Warnings_skip_anything_below_a_warning_or_error()
    {
        var (graph, ids) = Build(
            ("w1", new string[0]), ("w2", new[] { "w1" }),
            ("e1", new string[0]), ("w3", new[] { "e1" }),
            ("w4", new string[0]), ("e2", new[] { "w4" }));
        var levels = Levels(ids, new()
        {
            ["w1"] = MessageLevel.Warning, ["w2"] = MessageLevel.Warning, ["w3"] = MessageLevel.Warning, ["w4"] = MessageLevel.Warning,
            ["e1"] = MessageLevel.Error, ["e2"] = MessageLevel.Error,
        });
        Assert.Equal(new[] { ids["w1"], ids["w4"] }.ToHashSet(), BracketNav.RootCauses(graph, levels, MessageLevel.Warning));
        Assert.Equal(new[] { ids["e1"], ids["e2"] }.ToHashSet(), BracketNav.RootCauses(graph, levels, MessageLevel.Error));
    }

    // 깨끗한 객체를 여럿 거쳐도 끝까지 거슬러 올라간다. 다이아몬드도 한 번씩
    //   a(오류) → b → c ─┐
    //            └→ d ─→ e(오류)
    [Fact]
    public void Upstream_is_followed_to_the_end_through_clean_objects()
    {
        var (graph, ids) = Build(
            ("a", new string[0]), ("b", new[] { "a" }), ("c", new[] { "b" }), ("d", new[] { "b" }), ("e", new[] { "c", "d" }));
        var levels = Levels(ids, new() { ["a"] = MessageLevel.Error, ["e"] = MessageLevel.Error });
        Assert.Equal(new[] { ids["a"] }.ToHashSet(), BracketNav.RootCauses(graph, levels, MessageLevel.Error));
        // 상류 오류가 없으면 하류도 원인
        var clean = Levels(ids, new() { ["e"] = MessageLevel.Error });
        Assert.Equal(new[] { ids["e"] }.ToHashSet(), BracketNav.RootCauses(graph, clean, MessageLevel.Error));
    }

    [Fact]
    public void Deep_chains_do_not_overflow()
    {
        const int n = 20000;
        var nodes = Enumerable.Range(0, n).Select(i => ($"n{i}", i == 0 ? new string[0] : new[] { $"n{i - 1}" })).ToArray();
        var (graph, ids) = Build(nodes);
        var last = ids[$"n{n - 1}"];
        Assert.Equal(new[] { last }.ToHashSet(), BracketNav.RootCauses(graph, id => id == last ? MessageLevel.Error : MessageLevel.None, MessageLevel.Error));
    }

    [Fact]
    public void Slowest_orders_by_time_then_document_order_and_skips_zero()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid(), d = Guid.NewGuid(), e = Guid.NewGuid();
        var order = new[] { a, b, c, d };
        var slow = BracketNav.Slowest(new[] { (a, 5.0), (b, 0.0), (c, 30.0), (d, 5.0), (e, 5.0) }, order);
        Assert.Equal(new[] { c, a, d, e }, slow);
    }

    [Fact]
    public void Group_starts_are_the_first_member_in_document_order()
    {
        var order = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();
        var starts = BracketNav.GroupStarts(order, new (string, IEnumerable<Guid>)[]
        {
            ("A", new[] { order[3], order[1] }),
            ("B", new[] { order[4] }),
            // 첫 멤버가 A와 같고 멤버 수도 같음 → 먼저 온 A가 이김
            ("C", new[] { order[1], order[4] }),
            // 순서에 있는 멤버가 없음 (스크리블만 든 그룹 등)
            ("D", new[] { Guid.NewGuid() }),
        });
        Assert.Equal(new Dictionary<Guid, string> { [order[1]] = "A", [order[4]] = "B" }, starts);
    }

    // 중첩 그룹: 바깥(Outer)과 안쪽(Inner)의 첫 멤버가 같으면 안쪽 그룹 이름을 쓴다 (순서와 무관)
    [Fact]
    public void Nested_groups_sharing_a_first_member_use_the_inner_group()
    {
        var order = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray();
        var outer = ("Outer", (IEnumerable<Guid>)order);
        var inner = ("Inner", (IEnumerable<Guid>)new[] { order[0], order[1] });
        Assert.Equal("Inner", BracketNav.GroupStarts(order, new[] { outer, inner })[order[0]]);
        Assert.Equal("Inner", BracketNav.GroupStarts(order, new[] { inner, outer })[order[0]]);
    }

    // ]s 순서는 느린 순 목록이고 끝을 넘으면 돈다 (Search.Next를 그대로 씀)
    [Fact]
    public void Slow_list_steps_with_count_and_wraps()
    {
        var slow = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToArray();
        Assert.Equal(new SearchHit(slow[0], 1, 3, false), Search.Next(slow, _ => true, null, forward: true));
        Assert.Equal(new SearchHit(slow[2], 3, 3, false), Search.Next(slow, _ => true, slow[0], true, count: 2));
        Assert.Equal(new SearchHit(slow[0], 1, 3, true), Search.Next(slow, _ => true, slow[2], true));
        Assert.Equal(new SearchHit(slow[2], 3, 3, true), Search.Next(slow, _ => true, slow[0], forward: false));
        // 커서가 순위에 없으면 (시간 없음) ]s는 가장 느린 객체, [s는 가장 빠른 객체로. 돈 것으로 치지 않는다
        var unranked = System.Guid.NewGuid();
        Assert.Equal(new SearchHit(slow[0], 1, 3, false), Search.Next(slow, _ => true, unranked, forward: true));
        Assert.Equal(new SearchHit(slow[2], 3, 3, false), Search.Next(slow, _ => true, unranked, forward: false));
        Assert.Equal(new SearchHit(slow[1], 2, 3, false), Search.Next(slow, _ => true, unranked, true, count: 2));
    }

    [Theory]
    [InlineData(0.42, "0.4 ms")]
    [InlineData(3, "3 ms")]
    [InlineData(12.4, "12 ms")]
    [InlineData(999, "999 ms")]
    [InlineData(1234, "1.23 s")]
    [InlineData(20000, "20.0 s")]
    public void Format_time(double ms, string expected)
    {
        Assert.Equal(expected, BracketNav.FormatTime(ms));
    }

    [Fact]
    public void Describe_shows_position_detail_and_wrap()
    {
        var id = Guid.NewGuid();
        Assert.Equal("오류 원인 2/3 (오류 12개)", BracketNav.Describe(new SearchHit(id, 2, 3, false), "오류 원인", "(오류 12개)", true));
        Assert.Equal("그룹 1/4   search hit BOTTOM, continuing at TOP", BracketNav.Describe(new SearchHit(id, 1, 4, true), "그룹", "", true));
        Assert.EndsWith("search hit TOP, continuing at BOTTOM", BracketNav.Describe(new SearchHit(id, 4, 4, true), "그룹", "", false));
    }
}
