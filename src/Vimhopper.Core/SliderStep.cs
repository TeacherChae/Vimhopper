using System;

namespace Vimhopper.Core;

/// <summary>Number Slider의 종류 (Grasshopper의 <c>GH_SliderAccuracy</c>와 같은 순서).</summary>
public enum SliderKind { Float, Integer, Even, Odd }

/// <summary><see cref="SliderStep.Apply"/>의 결과: 새 값과, 범위 끝에서 멈췄는지.</summary>
/// <param name="Clamped">가려던 값이 최솟값·최댓값을 넘어서 끝에서 멈췄으면 true.</param>
public readonly record struct SliderStepResult(decimal Value, bool Clamped);

/// <summary>
/// <c>Ctrl+a</c> / <c>Ctrl+x</c>: Number Slider 값을 한 단계씩 올리고 내린다 (vim의 숫자 더하기·빼기).
/// </summary>
/// <remarks>
/// 한 단계는 슬라이더가 가질 수 있는 가장 작은 차이: 실수는 소수 자릿수의 마지막 자리 (2자리면 0.01), 정수는 1, 짝수·홀수는 2.
/// 결과는 최솟값..최댓값으로 자른다. 짝수·홀수는 범위 안의 마지막 짝수·홀수에서 멈춘다.
/// </remarks>
public static class SliderStep
{
    // Grasshopper가 허용하는 소수 자릿수 (0..12)
    const int MaxDecimalPlaces = 12;

    /// <summary>한 단계의 크기.</summary>
    public static decimal Step(SliderKind kind, int decimalPlaces) => kind switch
    {
        SliderKind.Integer => 1m,
        SliderKind.Even or SliderKind.Odd => 2m,
        _ => Pow10(-Math.Max(0, Math.Min(MaxDecimalPlaces, decimalPlaces))),
    };

    /// <summary>
    /// <paramref name="steps"/>단계 옮긴 값 (음수면 내림). 범위를 넘으면 끝에서 멈추고 Clamped.
    /// 이미 끝에 있으면 값이 그대로이고 Clamped다.
    /// </summary>
    public static SliderStepResult Apply(decimal value, decimal minimum, decimal maximum, SliderKind kind, int decimalPlaces, int steps)
    {
        if (minimum > maximum)
            (minimum, maximum) = (maximum, minimum);
        var target = value + Step(kind, decimalPlaces) * steps;
        if (target > maximum) return new SliderStepResult(Highest(kind, maximum) is { } high && high >= minimum ? high : value, true);
        if (target < minimum) return new SliderStepResult(Lowest(kind, minimum) is { } low && low <= maximum ? low : value, true);
        return new SliderStepResult(target, false);
    }

    // 범위 끝에서 그 종류가 가질 수 있는 마지막 값: 짝수 0..9의 위 끝은 8, 홀수 1..10의 위 끝은 9
    static decimal? Highest(SliderKind kind, decimal maximum) => kind switch
    {
        SliderKind.Even => decimal.Floor(maximum / 2) * 2,
        SliderKind.Odd => decimal.Floor((maximum - 1) / 2) * 2 + 1,
        _ => maximum,
    };

    static decimal? Lowest(SliderKind kind, decimal minimum) => kind switch
    {
        SliderKind.Even => decimal.Ceiling(minimum / 2) * 2,
        SliderKind.Odd => decimal.Ceiling((minimum - 1) / 2) * 2 + 1,
        _ => minimum,
    };

    static decimal Pow10(int exponent)
    {
        var result = 1m;
        for (var i = 0; i > exponent; i--)
            result /= 10m;
        return result;
    }
}
