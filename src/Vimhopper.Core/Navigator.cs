using System;
using System.Collections.Generic;

namespace Vimhopper.Core;

/// <summary>캔버스 위 객체 하나의 영역 (Grasshopper 캔버스 좌표, y는 아래로 증가).</summary>
public readonly struct Box
{
    public Box(Guid id, float x, float y, float width, float height)
    {
        Id = id;
        Left = x;
        Top = y;
        Right = x + width;
        Bottom = y + height;
        CenterX = x + width / 2;
        CenterY = y + height / 2;
    }

    public Guid Id { get; }
    public float Left { get; }
    public float Top { get; }
    public float Right { get; }
    public float Bottom { get; }
    public float CenterX { get; }
    public float CenterY { get; }
}

/// <summary>hjkl 방향 이동 시 다음 객체를 고른다.</summary>
public static class Navigator
{
    /// <summary>
    /// 진행 방향으로 가장 가까운 객체. 진행 방향 ±63° 원뿔 안의 객체를 우선하고,
    /// 없으면 같은 쪽 반평면 전체에서 찾는다. 축에서 벗어난 거리는 2배로 가중한다.
    /// </summary>
    public static Guid? Next(IReadOnlyList<Box> boxes, Guid current, Direction direction)
    {
        Box? origin = null;
        foreach (var box in boxes)
            if (box.Id == current) origin = box;
        if (origin is not { } from)
            return null;

        Guid? best = null;
        var bestInCone = false;
        var bestScore = float.MaxValue;

        foreach (var box in boxes)
        {
            if (box.Id == current) continue;

            var dx = box.CenterX - from.CenterX;
            var dy = box.CenterY - from.CenterY;
            var (along, across) = direction switch
            {
                Direction.Right => (dx, dy),
                Direction.Left => (-dx, dy),
                Direction.Down => (dy, dx),
                _ => (-dy, dx),
            };
            if (along <= 0.5f) continue;

            across = Math.Abs(across);
            var inCone = across <= along * 2;
            var score = along + 2 * across;

            if ((inCone && !bestInCone) || (inCone == bestInCone && score < bestScore))
            {
                best = box.Id;
                bestInCone = inCone;
                bestScore = score;
            }
        }

        return best;
    }

    /// <summary>주어진 점(보통 화면 중앙)에서 가장 가까운 객체.</summary>
    public static Guid? Nearest(IReadOnlyList<Box> boxes, float x, float y)
    {
        Guid? best = null;
        var bestDistance = float.MaxValue;
        foreach (var box in boxes)
        {
            var dx = box.CenterX - x;
            var dy = box.CenterY - y;
            var distance = dx * dx + dy * dy;
            if (distance < bestDistance)
            {
                best = box.Id;
                bestDistance = distance;
            }
        }
        return best;
    }
}
