using System.Linq;
using Vimhopper.Core;
using Xunit;

namespace Vimhopper.Core.Tests;

public class SliderStepTests
{
    [Theory]
    [InlineData(SliderKind.Float, 0, "1")]
    [InlineData(SliderKind.Float, 1, "0.1")]
    [InlineData(SliderKind.Float, 3, "0.001")]
    [InlineData(SliderKind.Float, 12, "0.000000000001")]
    [InlineData(SliderKind.Integer, 3, "1")]
    [InlineData(SliderKind.Even, 0, "2")]
    [InlineData(SliderKind.Odd, 0, "2")]
    public void Step_is_the_smallest_change(SliderKind kind, int places, string expected)
    {
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), SliderStep.Step(kind, places));
    }

    [Fact]
    public void Decimal_places_out_of_range_are_limited()
    {
        Assert.Equal(1m, SliderStep.Step(SliderKind.Float, -1));
        Assert.Equal(0.000000000001m, SliderStep.Step(SliderKind.Float, 20));
    }

    [Fact]
    public void One_step_up_and_down()
    {
        Assert.Equal(new SliderStepResult(0.26m, false), SliderStep.Apply(0.25m, 0m, 1m, SliderKind.Float, 2, 1));
        Assert.Equal(new SliderStepResult(0.24m, false), SliderStep.Apply(0.25m, 0m, 1m, SliderKind.Float, 2, -1));
        Assert.Equal(new SliderStepResult(6m, false), SliderStep.Apply(5m, 0m, 10m, SliderKind.Integer, 0, 1));
        Assert.Equal(new SliderStepResult(6m, false), SliderStep.Apply(4m, 0m, 10m, SliderKind.Even, 0, 1));
    }

    [Fact]
    public void Count_moves_n_steps()
    {
        Assert.Equal(new SliderStepResult(8m, false), SliderStep.Apply(5m, 0m, 10m, SliderKind.Integer, 0, 3));
        Assert.Equal(new SliderStepResult(0.2m, false), SliderStep.Apply(0.5m, 0m, 1m, SliderKind.Float, 1, -3));
    }

    [Fact]
    public void Past_the_ends_clamps()
    {
        Assert.Equal(new SliderStepResult(10m, true), SliderStep.Apply(8m, 0m, 10m, SliderKind.Integer, 0, 5));
        Assert.Equal(new SliderStepResult(0m, true), SliderStep.Apply(2m, 0m, 10m, SliderKind.Integer, 0, -5));
    }

    [Fact]
    public void Landing_exactly_on_the_end_is_not_clamped()
    {
        Assert.Equal(new SliderStepResult(10m, false), SliderStep.Apply(7m, 0m, 10m, SliderKind.Integer, 0, 3));
    }

    [Fact]
    public void Already_at_the_end_stays_and_clamps()
    {
        Assert.Equal(new SliderStepResult(1m, true), SliderStep.Apply(1m, 0m, 1m, SliderKind.Float, 2, 1));
        Assert.Equal(new SliderStepResult(0m, true), SliderStep.Apply(0m, 0m, 1m, SliderKind.Float, 2, -1));
    }

    [Fact]
    public void Negative_range_works()
    {
        Assert.Equal(new SliderStepResult(-4.9m, false), SliderStep.Apply(-5m, -10m, -1m, SliderKind.Float, 1, 1));
    }

    [Fact]
    public void Swapped_limits_are_still_a_range()
    {
        Assert.Equal(new SliderStepResult(10m, true), SliderStep.Apply(9m, 10m, 0m, SliderKind.Integer, 0, 4));
    }

    [Fact]
    public void Steps_add_up_exactly()
    {
        // 0.1을 열 번 더해도 decimal이라 정확히 1
        var value = 0m;
        foreach (var _ in Enumerable.Range(0, 10))
            value = SliderStep.Apply(value, 0m, 2m, SliderKind.Float, 1, 1).Value;
        Assert.Equal(1m, value);
    }
}
