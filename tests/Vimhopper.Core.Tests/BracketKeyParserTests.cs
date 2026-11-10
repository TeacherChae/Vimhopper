using System.Linq;
using Vimhopper.Core;
using Xunit;

namespace Vimhopper.Core.Tests;

/// <summary>] / [ 순회 키의 파서 문법.</summary>
public class BracketKeyParserTests
{
    static KeyParser Parser() => new(_ => 3);

    static Command[] Feed(KeyParser parser, string keys) =>
        keys.SelectMany(parser.Feed).ToArray();

    [Theory]
    [InlineData("]e", BracketTarget.Error, true, 1)]
    [InlineData("[e", BracketTarget.Error, false, 1)]
    [InlineData("]w", BracketTarget.Warning, true, 1)]
    [InlineData("[w", BracketTarget.Warning, false, 1)]
    [InlineData("]s", BracketTarget.Slow, true, 1)]
    [InlineData("[s", BracketTarget.Slow, false, 1)]
    [InlineData("]g", BracketTarget.Group, true, 1)]
    [InlineData("[g", BracketTarget.Group, false, 1)]
    [InlineData("3]e", BracketTarget.Error, true, 3)]
    [InlineData("12[g", BracketTarget.Group, false, 12)]
    public void Bracket_then_letter_moves(string keys, BracketTarget target, bool forward, int count)
    {
        Assert.Equal(new Command[] { new BracketMoveCommand(target, forward, count) }, Feed(Parser(), keys));
    }

    [Fact]
    public void Pending_shows_the_prefix()
    {
        var parser = Parser();
        Feed(parser, "]");
        Assert.Equal("]", parser.Pending);
        parser = Parser();
        Feed(parser, "3[");
        Assert.Equal("3[", parser.Pending);
    }

    [Theory]
    [InlineData("]x")]
    [InlineData("[h")]
    [InlineData("]]")]
    [InlineData("2]1")]
    public void Other_keys_are_errors(string keys)
    {
        var output = Feed(Parser(), keys);
        Assert.IsType<ErrorCommand>(output.Last());
        Assert.DoesNotContain(output, c => c is BracketMoveCommand);
    }

    [Fact]
    public void Escape_cancels_and_the_count_is_forgotten()
    {
        var parser = Parser();
        Assert.Equal(new Command[] { new CancelCommand() }, Feed(parser, "3]\x1b"));
        Assert.Equal("", parser.Pending);
        Assert.Equal(new Command[] { new BracketMoveCommand(BracketTarget.Error, true) }, Feed(parser, "]e"));
    }

    [Fact]
    public void Brackets_are_motions_in_visual_mode()
    {
        var parser = Parser();
        Feed(parser, "v");
        Assert.Equal(new Command[] { new BracketMoveCommand(BracketTarget.Warning, false, 2) }, Feed(parser, "2[w"));
        Assert.Equal(new Command[] { new BracketMoveCommand(BracketTarget.Group, true) }, Feed(parser, "]g"));
        Assert.True(parser.Visual);
    }

    // 연산자 + ] 범위는 없다
    [Theory]
    [InlineData("y]")]
    [InlineData("d[")]
    public void Brackets_are_not_ranges(string keys)
    {
        Assert.IsType<ErrorCommand>(Feed(Parser(), keys).Last());
    }

    // o{n} 뒤에 ]e로 멀리 가서 i{n}으로 연결할 수 있다
    [Fact]
    public void Bracket_after_a_picked_output_moves()
    {
        Assert.Equal(new Command[] { new PickOutputCommand(0), new BracketMoveCommand(BracketTarget.Error, true) }, Feed(Parser(), "o0]e"));
    }
}
