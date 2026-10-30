using System;

namespace Vimhopper.Core;

/// <summary>
/// Grasshopper가 그리는 와이어 곡선: 출력 그립에서 입력 그립까지, 양 끝에서 수평으로 출발하는 3차 베지어.
/// </summary>
/// <remarks>
/// 제어점이 양 끝과 같은 높이에 있으므로 곡선은 가로로도 세로로도 한 방향으로만 움직인다.
/// 그래서 상자와 겹치는지는 상자의 왼쪽·오른쪽 가장자리에서의 곡선 높이만 보면 정확히 알 수 있다.
/// </remarks>
public static class WireGeometry
{
    /// <summary>제어점을 양 끝에서 가로 거리의 이 비율만큼 수평으로 뺀다 (Grasshopper 근사값).</summary>
    public const double Tension = 0.5;

    /// <summary>
    /// 정방향 와이어 (x0, y0) → (x1, y1)이 상자 안쪽을 지나는지. 가장자리에 닿기만 하는 것은 겹침이 아니다.
    /// </summary>
    public static bool Crosses(float x0, float y0, float x1, float y1, float left, float top, float right, float bottom)
    {
        // 정렬 결과의 와이어는 항상 오른쪽으로 간다
        if (x1 <= x0) return false;

        // 곡선은 양 끝점이 만드는 직사각형 밖으로 나가지 않는다
        if (right <= x0 || left >= x1) return false;
        if (bottom <= Math.Min(y0, y1) || top >= Math.Max(y0, y1)) return false;

        var width = (double)x1 - x0;
        var ta = TAt((Math.Max(left, x0) - x0) / width);
        var tb = TAt((Math.Min(right, x1) - x0) / width);
        var ya = y0 + (y1 - y0) * Rise(ta);
        var yb = y0 + (y1 - y0) * Rise(tb);
        return Math.Max(ya, yb) > top && Math.Min(ya, yb) < bottom;
    }

    /// <summary>t에서 가로 진행 비율 (0~1, 단조 증가).</summary>
    static double Run(double t)
    {
        var u = 1 - t;
        return 3 * u * u * t * Tension + 3 * u * t * t * (1 - Tension) + t * t * t;
    }

    /// <summary>t에서 세로 진행 비율 (0~1, 단조 증가).</summary>
    static double Rise(double t) => t * t * (3 - 2 * t);

    /// <summary>가로 진행 비율이 fraction인 t (이분법).</summary>
    static double TAt(double fraction)
    {
        double low = 0, high = 1;
        for (var i = 0; i < 30; i++)
        {
            var mid = (low + high) / 2;
            if (Run(mid) < fraction) low = mid;
            else high = mid;
        }
        return (low + high) / 2;
    }
}
