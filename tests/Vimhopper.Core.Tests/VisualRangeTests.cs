using System;
using System.Linq;
using Vimhopper.Core;
using Xunit;

namespace Vimhopper.Core.Tests;

public class VisualRangeTests
{
    // 정렬된 2열 배치. 0열은 폭이 다른 객체 셋, 1열은 둘
    //   a(0,0 80×40)       d(150,0 60×40)
    //   b(0,60 40×20)      e(150,60 60×40)
    //   c(0,100 120×40)
    static readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid(), C = Guid.NewGuid(), D = Guid.NewGuid(), E = Guid.NewGuid();

    static readonly Box[] Boxes =
    {
        new(A, 0, 0, 80, 40),
        new(B, 0, 60, 40, 20),
        new(C, 0, 100, 120, 40),
        new(D, 150, 0, 60, 40),
        new(E, 150, 60, 60, 40),
    };

    static Guid[] Select(Guid anchor, Guid cursor) => VisualRange.Select(Boxes, anchor, cursor).OrderBy(Name).ToArray();

    static string Name(Guid id) => id == A ? "a" : id == B ? "b" : id == C ? "c" : id == D ? "d" : "e";

    [Fact]
    public void Same_end_selects_only_that_object()
    {
        Assert.Equal(new[] { A }, Select(A, A));
    }

    [Fact]
    public void Column_range_keeps_narrower_objects_in_the_column()
    {
        // b는 a보다 좁지만 중심이 a~c의 상자 안이므로 포함
        Assert.Equal(new[] { A, B, C }, Select(A, C));
    }

    [Fact]
    public void Block_range_spans_columns_and_rows()
    {
        Assert.Equal(new[] { A, B, D, E }, Select(A, E));
    }

    [Fact]
    public void Range_does_not_depend_on_direction()
    {
        Assert.Equal(Select(A, E), Select(E, A));
    }

    [Fact]
    public void Missing_anchor_falls_back_to_cursor()
    {
        Assert.Equal(new[] { D }, Select(Guid.NewGuid(), D));
    }
}
