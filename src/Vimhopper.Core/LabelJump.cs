using System;
using System.Collections.Generic;
using System.Linq;

namespace Vimhopper.Core;

/// <summary>라벨 점프(<c>t</c>)의 라벨 붙이기 (nvim flash·leap).</summary>
/// <remarks>
/// 기준점(커서, 없으면 화면 중앙)에 가까운 객체부터 홈 행 글자 한 개짜리 라벨을 받는다.
/// 객체가 글자 수보다 많으면 뒤쪽 글자 몇 개를 두 글자 라벨의 첫 글자로 돌린다. 한 글자 라벨로 쓰는 글자는
/// 두 글자 라벨의 첫 글자가 되지 않으므로 어떤 라벨도 다른 라벨의 앞부분이 아니다 (첫 글자만 치고 기다릴 일이 없음).
/// </remarks>
public static class LabelJump
{
    /// <summary>라벨 글자. 앞쪽(홈 행)이 가까운 객체에 간다.</summary>
    public const string Alphabet = "asdfghjklqwertyuiopzxcvbnm";

    /// <summary>라벨을 붙일 수 있는 최대 객체 수. 넘는 먼 객체는 라벨을 받지 않는다.</summary>
    public static int Capacity => Alphabet.Length * Alphabet.Length;

    /// <summary>객체마다 라벨. 기준점에 가까운 순 (같으면 위, 왼쪽 순)으로 짧은 라벨이 간다.</summary>
    public static IReadOnlyDictionary<string, Guid> Assign(IReadOnlyList<Box> targets, float originX, float originY)
    {
        var ordered = targets
            .OrderBy(b => Distance(b, originX, originY))
            .ThenBy(b => b.Top)
            .ThenBy(b => b.Left)
            .Take(Capacity)
            .ToList();

        var labels = Labels(ordered.Count);
        var result = new Dictionary<string, Guid>();
        for (var i = 0; i < ordered.Count; i++)
            result[labels[i]] = ordered[i].Id;
        return result;
    }

    /// <summary>
    /// 라벨 n개. 한 글자 라벨 k개 + 남은 글자를 첫 글자로 하는 두 글자 라벨. k는 n개를 담을 수 있는 가장 큰 값
    /// (k + (26 - k) × 26 ≥ n). 한 글자가 먼저, 두 글자는 첫 글자·둘째 글자 모두 <see cref="Alphabet"/> 순.
    /// </summary>
    public static IReadOnlyList<string> Labels(int count)
    {
        var n = Alphabet.Length;
        count = Math.Min(count, Capacity);
        var singles = Math.Min(n, (Capacity - count) / (n - 1));
        var labels = new List<string>(count);
        for (var i = 0; i < singles && labels.Count < count; i++)
            labels.Add(Alphabet[i].ToString());
        // 두 글자 라벨의 첫 글자는 한 글자로 쓰지 않은 글자
        for (var first = singles; first < n && labels.Count < count; first++)
            for (var second = 0; second < n && labels.Count < count; second++)
                labels.Add($"{Alphabet[first]}{Alphabet[second]}");
        return labels;
    }

    static float Distance(Box box, float x, float y)
    {
        var dx = box.CenterX - x;
        var dy = box.CenterY - y;
        return dx * dx + dy * dy;
    }
}
