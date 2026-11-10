using System;
using System.Collections.Generic;
using System.Linq;

namespace Vimhopper.Core;

/// <summary>
/// <c>p</c> / <c>P</c>: 붙여넣을 객체들을 어디로 옮길지와 커서를 둘 객체.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>객체끼리의 배치는 그대로 두고 통째로 옮긴다 (Grasshopper의 붙여넣기와 같음).</item>
/// <item><c>p</c>: 묶음의 왼쪽 끝을 커서 객체 오른쪽 끝에서 <c>gap</c>만큼 띄운다 (<c>Space Space</c> 검색창 자리와 같음).
/// <c>P</c>: 묶음의 오른쪽 끝을 커서 객체 왼쪽 끝에서 <c>gap</c>만큼 띄운다.</item>
/// <item>세로는 커서가 옮겨 갈 첫 객체의 가운데를 커서 객체의 가운데에 맞춘다 (한 객체면 와이어가 수평이 됨).</item>
/// <item>커서 객체가 없으면 묶음의 가운데를 화면 가운데에.</item>
/// <item>첫 객체는 <c>gyy</c>의 순서: 왼쪽 열부터, 같은 열은 위에서부터.</item>
/// </list>
/// </remarks>
public static class PastePlacement
{
    /// <summary><c>gyy</c>와 같은 순서(왼쪽 끝, 같으면 위쪽 끝)에서 첫 객체. 비어 있으면 null.</summary>
    public static Box? First(IEnumerable<Box> boxes) =>
        boxes.OrderBy(b => b.Left).ThenBy(b => b.Top).Cast<Box?>().FirstOrDefault();

    /// <summary>붙여넣을 객체들을 옮길 거리 (dx, dy). 객체가 없으면 (0, 0).</summary>
    /// <param name="pasted">클립보드에 있던 위치의 객체 상자들.</param>
    /// <param name="cursor">커서 객체 상자. 없으면 null.</param>
    /// <param name="before"><c>P</c>(왼쪽에)면 true.</param>
    /// <param name="viewX">커서가 없을 때 쓸 화면 가운데.</param>
    public static (float Dx, float Dy) Offset(
        IReadOnlyList<Box> pasted, Box? cursor, bool before, float gap, float viewX, float viewY)
    {
        if (pasted.Count == 0 || First(pasted) is not { } first)
            return (0, 0);

        var left = pasted.Min(b => b.Left);
        var right = pasted.Max(b => b.Right);
        if (cursor is not { } c)
        {
            var top = pasted.Min(b => b.Top);
            var bottom = pasted.Max(b => b.Bottom);
            return (viewX - (left + right) / 2, viewY - (top + bottom) / 2);
        }

        var dx = before ? c.Left - gap - right : c.Right + gap - left;
        return (dx, c.CenterY - first.CenterY);
    }
}
