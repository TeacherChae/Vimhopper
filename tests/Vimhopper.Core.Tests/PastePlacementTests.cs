using System;
using Vimhopper.Core;
using Xunit;

namespace Vimhopper.Core.Tests;

public class PastePlacementTests
{
    const float Gap = 60;

    static Box B(float x, float y, float w = 100, float h = 40) => new(Guid.NewGuid(), x, y, w, h);

    [Fact]
    public void First_is_left_column_then_top()
    {
        var topRight = B(300, 0);
        var lowLeft = B(0, 200);
        var highLeft = B(0, 100);
        Assert.Equal(highLeft.Id, PastePlacement.First(new[] { topRight, lowLeft, highLeft })?.Id);
        Assert.Null(PastePlacement.First(Array.Empty<Box>()));
    }

    [Fact]
    public void P_lowercase_puts_the_group_right_of_the_cursor()
    {
        var cursor = B(0, 0, 100, 40);            // 오른쪽 끝 100, 가운데 y 20
        var pasted = new[] { B(500, 500, 50, 20), B(700, 400, 50, 20) };
        var (dx, dy) = PastePlacement.Offset(pasted, cursor, before: false, Gap, 0, 0);
        // 묶음의 왼쪽 끝(500)이 100 + 60으로
        Assert.Equal(160 - 500, dx);
        // 첫 객체(왼쪽 열, 500,500)의 가운데 y(510)가 커서 가운데(20)로
        Assert.Equal(20 - 510, dy);
    }

    [Fact]
    public void P_uppercase_puts_the_group_left_of_the_cursor()
    {
        var cursor = B(1000, 0, 100, 40);         // 왼쪽 끝 1000
        var pasted = new[] { B(0, 0, 50, 20), B(200, 100, 80, 20) };  // 오른쪽 끝 280
        var (dx, dy) = PastePlacement.Offset(pasted, cursor, before: true, Gap, 0, 0);
        Assert.Equal(1000 - Gap - 280, dx);
        Assert.Equal(20 - 10, dy);
    }

    [Fact]
    public void Single_object_lines_up_with_the_cursor()
    {
        var cursor = B(0, 0, 100, 40);
        var pasted = new[] { B(-300, 77, 60, 30) };
        var (dx, dy) = PastePlacement.Offset(pasted, cursor, before: false, Gap, 0, 0);
        Assert.Equal(160, -300 + dx);
        // 가운데끼리 같은 높이: 77 + 15 + dy = 20
        Assert.Equal(20, 77 + 15 + dy);
    }

    [Fact]
    public void Without_a_cursor_the_group_is_centered_in_the_view()
    {
        var pasted = new[] { B(0, 0, 100, 100), B(200, 200, 100, 100) };  // 가운데 (150, 150)
        var (dx, dy) = PastePlacement.Offset(pasted, null, before: false, Gap, 1000, -50);
        Assert.Equal(850, dx);
        Assert.Equal(-200, dy);
    }

    [Fact]
    public void Nothing_to_paste_does_not_move()
    {
        Assert.Equal((0f, 0f), PastePlacement.Offset(Array.Empty<Box>(), B(0, 0), false, Gap, 5, 5));
    }
}
