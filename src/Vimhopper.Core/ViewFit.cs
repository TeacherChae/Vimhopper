using System;
using System.Collections.Generic;

namespace Vimhopper.Core;

/// <summary>
/// 캔버스 화면 상태. 캔버스 좌표 p는 화면(컨트롤) 좌표 <c>p * Zoom + (Tx, Ty)</c>에 그려진다
/// (Grasshopper <c>GH_Viewport.Zoom</c> / <c>Target</c>과 같은 뜻).
/// </summary>
public readonly record struct ViewState(float Zoom, float Tx, float Ty)
{
    /// <summary>캔버스 좌표를 화면 좌표로.</summary>
    public (float X, float Y) Project(float x, float y) => (x * Zoom + Tx, y * Zoom + Ty);
}

/// <summary>
/// 줌 계산: 범위를 화면에 맞추기(<c>z{범위}</c>, 비주얼 <c>z</c>)와 한 점을 고정한 확대·축소(<c>Ctrl+=</c> / <c>Ctrl+-</c>).
/// Engine은 결과를 <c>GH_Viewport</c>에 그대로 넣는다.
/// </summary>
public static class ViewFit
{
    /// <summary>화면에 맞출 때 확대하는 최대 배율 (100%). 작은 범위를 지나치게 키우지 않는다.</summary>
    public const float MaxFitZoom = 1f;

    /// <summary>줌 하한 / 상한. Grasshopper 휠 줌의 범위와 비슷하게.</summary>
    public const float MinZoom = 0.1f;
    public const float MaxZoom = 5f;

    /// <summary><c>Ctrl+=</c> / <c>Ctrl+-</c> 한 번의 배율.</summary>
    public const float ZoomStep = 1.25f;

    /// <summary>
    /// 상자들을 감싸는 영역이 화면(<paramref name="width"/>×<paramref name="height"/> 픽셀)에서
    /// 가장자리 <paramref name="margin"/> 픽셀을 남기고 들어오게 하는 화면. 영역의 가운데가 화면 가운데에 온다.
    /// 지금 배율(<paramref name="currentZoom"/>)로 들어가지 않으면 들어갈 때까지 축소하고(<see cref="MinZoom"/>까지),
    /// 들어가면 <see cref="MaxFitZoom"/>까지 확대하되 지금 배율보다 작아지지 않는다 (300%에서 작은 범위는 가운데로만).
    /// 상자가 없으면 null.
    /// </summary>
    public static ViewState? Fit(IReadOnlyCollection<Box> boxes, float width, float height, float margin, float currentZoom)
    {
        if (boxes.Count == 0) return null;

        float left = float.MaxValue, top = float.MaxValue, right = float.MinValue, bottom = float.MinValue;
        foreach (var b in boxes)
        {
            left = Math.Min(left, b.Left);
            top = Math.Min(top, b.Top);
            right = Math.Max(right, b.Right);
            bottom = Math.Max(bottom, b.Bottom);
        }

        // 화면이 여백보다 작으면 여백을 줄인다 (화면의 1/4까지)
        margin = Math.Min(margin, Math.Min(width, height) / 4);
        var availW = width - 2 * margin;
        var availH = height - 2 * margin;
        var w = right - left;
        var h = bottom - top;
        // 영역이 꼭 맞는 배율 (크기가 0인 축은 제한이 없음)
        var fit = float.MaxValue;
        if (w > 0) fit = Math.Min(fit, availW / w);
        if (h > 0) fit = Math.Min(fit, availH / h);
        var zoom = fit < currentZoom
            ? Math.Max(fit, MinZoom)
            : Math.Max(currentZoom, Math.Min(fit, MaxFitZoom));

        return Centered(zoom, (left + right) / 2, (top + bottom) / 2, width, height);
    }

    /// <summary>
    /// 캔버스 점 (<paramref name="x"/>, <paramref name="y"/>)이 화면에서 같은 자리에 머물도록 배율을 <paramref name="factor"/>배.
    /// 결과 배율은 <see cref="MinZoom"/>..<see cref="MaxZoom"/>로 자른다.
    /// </summary>
    public static ViewState ZoomAround(ViewState view, float x, float y, float factor)
    {
        var zoom = Clamp(view.Zoom * factor, MinZoom, MaxZoom);
        var (sx, sy) = view.Project(x, y);
        return new ViewState(zoom, sx - x * zoom, sy - y * zoom);
    }

    /// <summary>
    /// <c>Ctrl+=</c> / <c>Ctrl+-</c>의 기준점: 커서 객체 가운데가 화면 안에 있으면 그 점, 아니면(커서가 없거나 화면 밖) 화면 가운데.
    /// 화면 밖 커서를 기준으로 삼으면 커서가 화면 밖에 고정되므로.
    /// </summary>
    public static (float X, float Y) Anchor(ViewState view, Box? cursor, float width, float height)
    {
        if (cursor is { } c)
        {
            var (sx, sy) = view.Project(c.CenterX, c.CenterY);
            if (sx >= 0 && sx <= width && sy >= 0 && sy <= height)
                return (c.CenterX, c.CenterY);
        }
        return ((width / 2 - view.Tx) / view.Zoom, (height / 2 - view.Ty) / view.Zoom);
    }

    // 캔버스 점 (cx, cy)가 화면 가운데에 오는 화면
    static ViewState Centered(float zoom, float cx, float cy, float width, float height) =>
        new(zoom, width / 2 - cx * zoom, height / 2 - cy * zoom);

    static float Clamp(float value, float min, float max) => Math.Max(min, Math.Min(max, value));
}
