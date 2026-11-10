using System.Linq;
using Vimhopper.Core;
using Xunit;

namespace Vimhopper.Core.Tests;

/// <summary>연산자 + <c>'{a-z}</c>: 커서 객체에서 마크의 객체까지의 블록 (<c>t{라벨}</c>과 같은 범위).</summary>
public class MarkRangeTests
{
    static KeyParser Parser() => new(_ => 3);

    static Command[] Feed(KeyParser parser, string keys) => keys.SelectMany(parser.Feed).ToArray();

    [Theory]
    [InlineData("y'a", Operator.Yank)]
    [InlineData("d'a", Operator.Delete)]
    [InlineData("='a", Operator.Format)]
    [InlineData("gy'a", Operator.Reference)]
    [InlineData("gc'a", Operator.Disable)]
    [InlineData("z'a", Operator.Zoom)]
    // 숫자는 앞뒤 모두 무시한다 (t와 같음)
    [InlineData("2d'a", Operator.Delete)]
    [InlineData("d2'a", Operator.Delete)]
    [InlineData("3gc'a", Operator.Disable)]
    [InlineData("2y3'a", Operator.Yank)]
    public void Operator_quote_is_a_mark_block_range(string keys, Operator op)
    {
        Assert.Equal(new Command[] { new OperatorCommand(op, WireRange.ToMark('a')) }, Feed(Parser(), keys));
    }

    [Fact]
    public void Mark_range_is_a_target_block_without_a_resolved_target()
    {
        var range = WireRange.ToMark('q');
        Assert.Equal(RangeKind.Target, range.Kind);
        Assert.Null(range.Target);
        Assert.Equal('q', range.Mark);
        Assert.Null(range.Depth);
    }

    [Theory]
    [InlineData("d'", "d'")]
    [InlineData("2d'", "2d'")]
    [InlineData("y2'", "y2'")]
    [InlineData("3gc'", "3gc'")]
    [InlineData("gy'", "gy'")]
    public void Pending_keeps_the_typed_keys(string keys, string pending)
    {
        var parser = Parser();
        Assert.Empty(Feed(parser, keys));
        Assert.Equal(pending, parser.Pending);
    }

    [Theory]
    [InlineData("d'A")]
    [InlineData("y'1")]
    [InlineData("d''")]
    [InlineData("gc' ")]
    public void Only_lowercase_marks(string keys)
    {
        var parser = Parser();
        var error = Assert.IsType<ErrorCommand>(Assert.Single(Feed(parser, keys)));
        Assert.Contains("마크 a-z", error.Message);
        Assert.Equal("", parser.Pending);
    }

    [Fact]
    public void Escape_cancels()
    {
        Assert.Equal(new Command[] { new CancelCommand() }, Feed(Parser(), "d'\x1b"));
        Assert.Equal(new Command[] { new CancelCommand() }, Feed(Parser(), "y2'\x1b"));
    }

    [Fact]
    public void Count_does_not_leak_into_the_next_command()
    {
        var parser = Parser();
        Feed(parser, "3d'a");
        Assert.Equal(new Command[] { new OperatorCommand(Operator.Delete, new WireRange(false, 1)) }, Feed(parser, "dl"));
    }

    [Fact]
    public void Visual_operators_do_not_wait_for_a_range()
    {
        // 비주얼 모드의 d는 선택에 바로 적용되고, 뒤의 '는 마크 점프다
        var parser = Parser();
        Feed(parser, "v");
        Assert.Equal(new Command[] { new DeleteCommand(), new VisualCommand(false) }, Feed(parser, "d"));
        Assert.Equal(new Command[] { new JumpToMarkCommand('a') }, Feed(parser, "'a"));
    }

    [Fact]
    public void Mark_hints_after_an_operator()
    {
        var parser = Parser();
        parser.MarkNames = () => new[] { ('a', "Slider"), ('q', "Panel") };
        Feed(parser, "d'");
        Assert.Equal(new[] { ("a", "Slider"), ("q", "Panel") }, parser.Hints.Select(h => (h.Keys, h.Description)));

        var empty = Parser();
        Feed(empty, "y'");
        Assert.Equal("{a-z}", empty.Hints.Single().Keys);
        Assert.Contains("없음", empty.Hints.Single().Description);
    }

    [Theory]
    [InlineData("y")]
    [InlineData("=")]
    [InlineData("d")]
    [InlineData("gy")]
    [InlineData("gc")]
    [InlineData("z")]
    [InlineData("d2")]
    public void Operator_hints_list_quote(string keys)
    {
        var parser = Parser();
        Feed(parser, keys);
        Assert.Contains(parser.Hints, h => h.Keys == "'");
    }

    [Fact]
    public void Mark_range_is_repeatable_with_the_same_letter()
    {
        var register = new RepeatRegister();
        var command = new OperatorCommand(Operator.Delete, WireRange.ToMark('a'));
        register.Record(command);
        Assert.Equal(new Command[] { command }, register.Last);
        Assert.True(RepeatRegister.IsRepeatable(new OperatorCommand(Operator.Disable, WireRange.ToMark('b'))));
        Assert.True(RepeatRegister.IsRepeatable(new OperatorCommand(Operator.Format, WireRange.ToMark('b'))));
        // {n}.은 블록에 단계가 없으므로 그대로
        Assert.Equal(new Command[] { command }, register.Replay(3, _ => 0));
    }

    [Theory]
    [InlineData(Operator.Yank)]
    [InlineData(Operator.Reference)]
    [InlineData(Operator.Zoom)]
    public void Non_changing_operators_with_a_mark_are_not_repeatable(Operator op)
    {
        Assert.False(RepeatRegister.IsRepeatable(new OperatorCommand(op, WireRange.ToMark('a'))));
    }

    [Fact]
    public void Failed_mark_range_keeps_the_previous_change()
    {
        var register = new RepeatRegister();
        var before = new OperatorCommand(Operator.Delete, new WireRange(false, 2));
        register.Record(before);
        register.Record(new OperatorCommand(Operator.Delete, WireRange.ToMark('a')), succeeded: false);
        Assert.Equal(new Command[] { before }, register.Last);
    }
}
