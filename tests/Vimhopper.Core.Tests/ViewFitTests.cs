using System;
using Vimhopper.Core;
using Xunit;

namespace Vimhopper.Core.Tests;

public class ViewFitTests
{
    static Box B(float x, float y, float w, float h) => new(Guid.NewGuid(), x, y, w, h);

    [Fact]
    public void Fit_centers_the_bounding_box_and_zooms_in_up_to_100_percent()
    {
        // 영역 400×100, 화면 1000×600, 여백 50 → 꼭 맞는 배율 2.25지만 확대는 100%까지
        var view = ViewFit.Fit(new[] { B(0, 0, 100, 50), B(300, 50, 100, 50) }, 1000, 600, 50, 0.5f)!.Value;
        Assert.Equal(1f, view.Zoom);
        var (cx, cy) = view.Project(200, 50);
        Assert.Equal(500f, cx, 3);
        Assert.Equal(300f, cy, 3);
    }

    [Fact]
    public void Fit_zooms_in_only_as_far_as_the_range_fits()
    {
        // 꼭 맞는 배율 0.8 (가로 900/1125) > 지금 0.5 → 0.8
        var view = ViewFit.Fit(new[] { B(0, 0, 1125, 100) }, 1000, 600, 50, 0.5f)!.Value;
        Assert.Equal(0.8f, view.Zoom, 4);
    }

    [Fact]
    public void Fit_never_zooms_out_a_range_that_already_fits()
    {
        // 300%에서 작은 범위(꼭 맞는 배율 9)는 가운데로만 옮긴다
        var view = ViewFit.Fit(new[] { B(0, 0, 100, 50) }, 1000, 600, 50, 3f)!.Value;
        Assert.Equal(3f, view.Zoom);
        var (cx, cy) = view.Project(50, 25);
        Assert.Equal(500f, cx, 3);
        Assert.Equal(300f, cy, 3);
    }

    [Fact]
    public void Fit_zooms_out_to_the_tighter_axis_when_it_does_not_fit()
    {
        // 영역 1800×500, 화면 1000×600, 여백 50 → 가로 900/1800 = 0.5, 세로 500/500 = 1 → 0.5 (지금 300%여도)
        var view = ViewFit.Fit(new[] { B(100, 100, 1800, 500) }, 1000, 600, 50, 3f)!.Value;
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
        var view = ViewFit.Fit(new[] { B(0, 0, 100000, 10) }, 1000, 600, 50, 1f)!.Value;
        Assert.Equal(ViewFit.MinZoom, view.Zoom);
    }

    [Fact]
    public void Fit_shrinks_the_margin_on_a_tiny_screen()
    {
        // 화면 100×100에 여백 50이면 남는 공간이 없다 → 여백은 화면의 1/4(25)로
        var view = ViewFit.Fit(new[] { B(0, 0, 100, 100) }, 100, 100, 50, 1f)!.Value;
        Assert.Equal(0.5f, view.Zoom, 4);
    }

    [Fact]
    public void Fit_of_a_point_keeps_the_zoom_up_to_100_percent()
    {
        // 크기 0인 영역은 어느 배율로나 들어간다 → 지금 배율과 100% 중 큰 쪽
        Assert.Equal(1f, ViewFit.Fit(new[] { B(10, 10, 0, 0) }, 1000, 600, 50, 0.5f)!.Value.Zoom);
        Assert.Equal(2f, ViewFit.Fit(new[] { B(10, 10, 0, 0) }, 1000, 600, 50, 2f)!.Value.Zoom);
    }

    [Fact]
    public void Fit_without_boxes_is_null()
    {
        Assert.Null(ViewFit.Fit(Array.Empty<Box>(), 1000, 600, 50, 1f));
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
