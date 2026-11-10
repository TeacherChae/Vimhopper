using System;
using System.Linq;
using Vimhopper.Core;
using Xunit;

namespace Vimhopper.Core.Tests;

public class DataShapeTests
{
    // 가지 개수만큼 {0;i} 경로에 counts[i]개씩
    static ParamShape P(string name, params int[] counts) =>
        P(name, DataAccess.Item, counts);

    static ParamShape P(string name, DataAccess access, params int[] counts) =>
        new(name, access, counts.Select((c, i) => new BranchShape($"{{0;{i}}}", c)).ToList(), new[] { "Number" }, Array.Empty<ParamOp>());

    // ───── 요약 ─────

    [Fact]
    public void Summary_counts_branches_items_nulls_and_types()
    {
        var shape = new ParamShape("A", DataAccess.Item,
            new[] { new BranchShape("{0;0}", 10, 2), new BranchShape("{0;1}", 20) },
            new[] { "Point", "Curve" }, new[] { ParamOp.Flatten, ParamOp.Reverse });
        Assert.Equal("item·flatten·reverse  가지 2 · 항목 30 · null 2 · Point, Curve", DataShape.Summary(shape));
    }

    [Fact]
    public void Summary_without_nulls_omits_null()
    {
        Assert.Equal("list  가지 1 · 항목 3 · Number", DataShape.Summary(P("A", DataAccess.List, 3)));
    }

    [Fact]
    public void Summary_of_empty_param()
    {
        Assert.Equal("tree  비어 있음", DataShape.Summary(P("A", DataAccess.Tree)));
        Assert.Equal("item  비어 있음", DataShape.Summary(P("A", 0)));
    }

    [Fact]
    public void Summary_lists_at_most_three_types()
    {
        var shape = new ParamShape("A", DataAccess.Item, new[] { new BranchShape("{0}", 4) },
            new[] { "Point", "Curve", "Brep", "Mesh" }, Array.Empty<ParamOp>());
        Assert.EndsWith("Point, Curve, Brep 외 1", DataShape.Summary(shape));
    }

    [Fact]
    public void Few_branches_are_all_listed()
    {
        Assert.Equal("{0;0} 10, {0;1} 7, {0;2} 3", DataShape.Branches(P("A", 10, 7, 3).Branches));
    }

    [Fact]
    public void Many_uniform_branches_are_summarized()
    {
        Assert.Equal("{0;0} 10, {0;1} 10 … 가지마다 10개", DataShape.Branches(P("A", 10, 10, 10, 10).Branches));
    }

    [Fact]
    public void Many_uneven_branches_show_min_and_max()
    {
        Assert.Equal("{0;0} 10, {0;1} 7 … 가지당 3~10개", DataShape.Branches(P("A", 10, 7, 3, 5).Branches));
    }

    [Fact]
    public void Lines_have_sections_params_details_and_hints()
    {
        var lines = DataShape.Lines(new[] { P("A", 100), P("B", 1) }, new[] { P("R", 100) });
        Assert.Equal(
            new[]
            {
                (DataShapeLineKind.Section, "입력"),
                (DataShapeLineKind.Param, "A"), (DataShapeLineKind.Detail, ""),
                (DataShapeLineKind.Param, "B"), (DataShapeLineKind.Detail, ""),
                (DataShapeLineKind.Section, "출력"),
                (DataShapeLineKind.Param, "R"), (DataShapeLineKind.Detail, ""),
                (DataShapeLineKind.Section, "짝 맞춤"),
                (DataShapeLineKind.Hint, ""),
            },
            lines.Select(l => (l.Kind, l.Label)));
        Assert.Equal("B: 항목 1개가 100번 재사용", lines.Last().Text);
    }

    [Fact]
    public void Lines_skip_empty_sections_and_have_no_hint_section_without_hints()
    {
        var lines = DataShape.Lines(Array.Empty<ParamShape>(), new[] { P("R", 3) });
        Assert.Equal(new[] { "출력", "R", "" }, lines.Select(l => l.Label));
    }

    // ───── 짝 맞춤 ─────

    [Fact]
    public void Fewer_branches_repeat_the_last_branch()
    {
        Assert.Equal(new[] { "B: 가지 3개 vs A 10개 → B의 마지막 가지가 7번 반복" },
            DataShape.Mismatches(new[] { P("A", Enumerable.Repeat(1, 10).ToArray()), P("B", 1, 1, 1) }));
    }

    [Fact]
    public void Single_item_is_reused()
    {
        Assert.Equal(new[] { "B: 항목 1개가 100번 재사용" }, DataShape.Mismatches(new[] { P("A", 100), P("B", 1) }));
    }

    [Fact]
    public void Single_item_against_grafted_branches_reports_reuse_only()
    {
        // 슬라이더 하나를 가지 10개(항목 1개씩)에: 가지 힌트는 내지 않고 재사용 하나만
        Assert.Equal(new[] { "B: 항목 1개가 10번 재사용" },
            DataShape.Mismatches(new[] { P("A", Enumerable.Repeat(1, 10).ToArray()), P("B", 1) }));
    }

    [Fact]
    public void Shorter_list_repeats_its_last_item()
    {
        Assert.Equal(new[] { "B: 항목 3개 vs A 10개 → B의 마지막 항목이 7번 반복" },
            DataShape.Mismatches(new[] { P("A", 10), P("B", 3) }));
    }

    [Fact]
    public void Short_branches_repeat_items_within_branches()
    {
        // 가지 0: 10 vs 8 → 2번, 가지 1: 10 vs 10
        Assert.Equal(new[] { "B: 가지 안 항목이 모자라 마지막 항목이 모두 2번 반복" },
            DataShape.Mismatches(new[] { P("A", 10, 10), P("B", 8, 10) }));
    }

    [Fact]
    public void Repeated_branches_are_not_counted_again_as_items()
    {
        // B는 가지 1개(10개)를 A의 가지 3개(10개씩)에 다시 씀: 가지 힌트만
        Assert.Equal(new[] { "B: 가지 1개 vs A 3개 → B의 마지막 가지가 2번 반복" },
            DataShape.Mismatches(new[] { P("A", 10, 10, 10), P("B", 10) }));
    }

    [Fact]
    public void Matching_inputs_have_no_hints()
    {
        Assert.Empty(DataShape.Mismatches(new[] { P("A", 10, 5), P("B", 10, 5) }));
    }

    [Fact]
    public void All_single_items_have_no_hints()
    {
        Assert.Empty(DataShape.Mismatches(new[] { P("A", 1), P("B", 1), P("C", 1) }));
    }

    [Fact]
    public void List_and_tree_inputs_are_ignored()
    {
        Assert.Empty(DataShape.Mismatches(new[] { P("A", 100), P("B", DataAccess.List, 1), P("C", DataAccess.Tree, 1) }));
    }

    [Fact]
    public void Empty_inputs_are_ignored()
    {
        Assert.Empty(DataShape.Mismatches(new[] { P("A", 100), P("B", 0) }));
        Assert.Empty(DataShape.Mismatches(new[] { P("A", 100) }));
    }

    [Fact]
    public void Empty_branch_is_not_reported_as_item_repeat()
    {
        Assert.Empty(DataShape.Mismatches(new[] { P("A", 5, 5), P("B", 5, 0) }));
    }
}
