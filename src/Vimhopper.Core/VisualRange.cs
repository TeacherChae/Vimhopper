using System;
using System.Collections.Generic;
using System.Linq;

namespace Vimhopper.Core;

/// <summary>비주얼 모드에서 선택되는 객체.</summary>
/// <remarks>
/// vim의 비주얼 선택이 시작점과 커서 사이의 글자 전부이듯, 범위는 두 끝만으로 정해지고 지나온 경로와는 무관하다.
/// 두 끝 객체를 감싸는 직사각형 안에 중심이 들어오는 객체를 고르므로, 정렬된 화면에서는 열과 행으로 이루어진
/// 블록이 된다. 같은 열의 폭이 달라도 두 끝의 상자 전체를 쓰기 때문에 열 안의 객체가 빠지지 않는다.
/// </remarks>
public static class VisualRange
{
    /// <summary>시작점과 커서 사이의 객체들. 시작점이 사라졌으면(삭제, 실행 취소) 커서 하나만.</summary>
    public static IReadOnlyList<Guid> Select(IReadOnlyList<Box> boxes, Guid anchor, Guid cursor)
    {
        var ends = boxes.Where(b => b.Id == anchor || b.Id == cursor).ToList();
        if (ends.Count == 0)
            return Array.Empty<Guid>();

        var left = ends.Min(b => b.Left);
        var top = ends.Min(b => b.Top);
        var right = ends.Max(b => b.Right);
        var bottom = ends.Max(b => b.Bottom);

        return boxes
            .Where(b => b.Id == anchor || b.Id == cursor
                || (b.CenterX >= left && b.CenterX <= right && b.CenterY >= top && b.CenterY <= bottom))
            .Select(b => b.Id)
            .ToList();
    }
}
