using System;
using Vimhopper.Core;
using Xunit;

namespace Vimhopper.Core.Tests;

public class ViewFitTests
{
    static Box B(float x, float y, float w, float h) => new(Guid.NewGuid(), x, y, w, h);

    [Fact]
    public void Fit_centers_the_bounding_box_and_keeps_the_margin()
    {
        // 영역 400×100, 화면 1000×600, 여백 50 → 가로가 좁다: 900/400 = 2.25 → 최대 100%로 자름
        var view = ViewFit.Fit(new[] { B(0, 0, 100, 50), B(300, 50, 100, 50) }, 1000, 600, 50)!.Value;
        Assert.Equal(1f, view.Zoom);
        var (cx, cy) = view.Project(200, 50);
        Assert.Equal(500f, cx, 3);
        Assert.Equal(300f, cy, 3);
    }

    [Fact]
    public void Fit_zooms_out_to_the_tighter_axis()
    {
        // 영역 1800×500, 화면 1000×600, 여백 50 → 가로 900/1800 = 0.5, 세로 500/500 = 1 → 0.5
        var view = ViewFit.Fit(new[] { B(100, 100, 1800, 500) }, 1000, 600, 50)!.Value;
        Assert.Equal(0.5f, view.Zoom, 4);
        var (left, top) = view.Project(100, 100);
        var (right, bottom) = view.Project(1900, 600);
        Assert.Equal(50f, left, 3);
        Assert.Equal(950f, right, 3);
        Assert.True(top >= 50 && bottom <= 550);
    }

    [Fact]
    public void Fit_is_clamped_to_the_minimum_zoom()
    {
        var view = ViewFit.Fit(new[] { B(0, 0, 100000, 10) }, 1000, 600, 50)!.Value;
        Assert.Equal(ViewFit.MinZoom, view.Zoom);
    }

    [Fact]
    public void Fit_shrinks_the_margin_on_a_tiny_screen()
    {
        // 화면 100×100에 여백 50이면 남는 공간이 없다 → 여백은 화면의 1/4(25)로
        var view = ViewFit.Fit(new[] { B(0, 0, 100, 100) }, 100, 100, 50)!.Value;
        Assert.Equal(0.5f, view.Zoom, 4);
    }

    [Fact]
    public void Fit_without_boxes_is_null()
    {
        Assert.Null(ViewFit.Fit(Array.Empty<Box>(), 1000, 600, 50));
    }

    [Fact]
    public void Zoom_around_keeps_the_point_in_place()
    {
        var view = new ViewState(1, 30, -20);
        var before = view.Project(400, 250);
        var zoomed = ViewFit.ZoomAround(view, 400, 250, ViewFit.ZoomStep);
        Assert.Equal(ViewFit.ZoomStep, zoomed.Zoom);
        var after = zoomed.Project(400, 250);
        Assert.Equal(before.X, after.X, 3);
        Assert.Equal(before.Y, after.Y, 3);
    }

    [Fact]
    public void Zoom_around_is_clamped()
    {
        Assert.Equal(ViewFit.MaxZoom, ViewFit.ZoomAround(new ViewState(4.9f, 0, 0), 0, 0, 2).Zoom);
        Assert.Equal(ViewFit.MinZoom, ViewFit.ZoomAround(new ViewState(0.11f, 0, 0), 0, 0, 0.5f).Zoom);
    }

    [Fact]
    public void Anchor_is_the_cursor_center_when_it_is_on_screen()
    {
        var view = new ViewState(2, 0, 0);
        Assert.Equal((60f, 35f), ViewFit.Anchor(view, B(50, 30, 20, 10), 1000, 600));
    }

    [Fact]
    public void Anchor_falls_back_to_the_screen_center()
    {
        // 화면 가운데 (500, 300)은 캔버스 ((500-100)/2, (300-50)/2)
        var view = new ViewState(2, 100, 50);
        Assert.Equal((200f, 125f), ViewFit.Anchor(view, null, 1000, 600));
        // 커서가 화면 밖이면 가운데 기준
        Assert.Equal((200f, 125f), ViewFit.Anchor(view, B(5000, 0, 10, 10), 1000, 600));
    }
}
