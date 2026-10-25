using System;
using Vimhopper.Core;
using Xunit;

namespace Vimhopper.Core.Tests;

public class NavigatorTests
{
    static readonly Guid Center = Guid.NewGuid();
    static readonly Guid Right = Guid.NewGuid();
    static readonly Guid FarRight = Guid.NewGuid();
    static readonly Guid Below = Guid.NewGuid();
    static readonly Guid UpperRightDiagonal = Guid.NewGuid();

    // 50x30 크기 객체를 중심 좌표로 배치
    static Box At(Guid id, float cx, float cy) => new(id, cx - 25, cy - 15, 50, 30);

    static readonly Box[] Layout =
    {
        At(Center, 0, 0),
        At(Right, 100, 10),
        At(FarRight, 300, 0),
        At(Below, 0, 100),
        At(UpperRightDiagonal, 60, -200),
    };

    [Fact]
    public void Picks_closest_in_direction()
    {
        Assert.Equal(Right, Navigator.Next(Layout, Center, Direction.Right));
        Assert.Equal(Below, Navigator.Next(Layout, Center, Direction.Down));
    }

    [Fact]
    public void Prefers_in_cone_over_closer_off_axis()
    {
        // 위쪽에는 대각선 객체 하나뿐 → 원뿔 안이므로 선택
        Assert.Equal(UpperRightDiagonal, Navigator.Next(Layout, Center, Direction.Up));
    }

    [Fact]
    public void Returns_null_when_nothing_in_direction()
    {
        Assert.Null(Navigator.Next(Layout, Center, Direction.Left));
        Assert.Null(Navigator.Next(Layout, FarRight, Direction.Right));
    }

    [Fact]
    public void Returns_null_for_unknown_current()
    {
        Assert.Null(Navigator.Next(Layout, Guid.NewGuid(), Direction.Right));
    }

    [Fact]
    public void Nearest_to_point()
    {
        Assert.Equal(FarRight, Navigator.Nearest(Layout, 280, 20));
        Assert.Null(Navigator.Nearest(Array.Empty<Box>(), 0, 0));
    }
}
