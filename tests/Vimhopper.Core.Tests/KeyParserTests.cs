using System.Linq;
using Vimhopper.Core;
using Xunit;

namespace Vimhopper.Core.Tests;

public class KeyParserTests
{
    static KeyParser Parser(int outputs = 3, int inputs = 3) =>
        new(side => side == ParamSide.Output ? outputs : inputs);

    static Command[] Feed(KeyParser parser, string keys) =>
        keys.SelectMany(parser.Feed).ToArray();

    [Theory]
    [InlineData('h', Direction.Left)]
    [InlineData('j', Direction.Down)]
    [InlineData('k', Direction.Up)]
    [InlineData('l', Direction.Right)]
    public void Hjkl_moves(char key, Direction expected)
    {
        Assert.Equal(new Command[] { new MoveCommand(expected) }, Feed(Parser(), key.ToString()));
    }

    [Fact]
    public void Single_digit_commits_immediately_when_ten_or_fewer_params()
    {
        var parser = Parser();
        Assert.Empty(Feed(parser, "o"));
        Assert.Equal("o", parser.Pending);
        Assert.Equal(new Command[] { new PickOutputCommand(1) }, Feed(parser, "1"));
        Assert.Equal("", parser.Pending);
    }

    [Fact]
    public void Pick_then_navigate_then_connect()
    {
        var commands = Feed(Parser(), "o2llji0");
        Assert.Equal(new Command[]
        {
            new PickOutputCommand(2),
            new MoveCommand(Direction.Right),
            new MoveCommand(Direction.Right),
            new MoveCommand(Direction.Down),
            new ConnectInputCommand(0, Append: false),
        }, commands);
    }

    [Fact]
    public void Capital_I_appends()
    {
        Assert.Equal(new Command[] { new ConnectInputCommand(1, Append: true) }, Feed(Parser(), "I1"));
    }

    [Fact]
    public void Ops_after_index_apply_to_that_side_and_chain()
    {
        Assert.Equal(new Command[]
        {
            new PickOutputCommand(0),
            new ParamOpCommand(ParamSide.Output, ParamOp.Flatten),
            new ParamOpCommand(ParamSide.Output, ParamOp.Reverse),
            new MoveCommand(Direction.Right),
            new ConnectInputCommand(1, Append: false),
            new ParamOpCommand(ParamSide.Input, ParamOp.Graft),
        }, Feed(Parser(), "o0frli1g"));
    }

    [Fact]
    public void Op_keys_without_index_do_nothing()
    {
        Assert.Empty(Feed(Parser(), "fgsrn"));
    }

    [Fact]
    public void Two_digit_index_waits_for_second_digit()
    {
        var parser = Parser(outputs: 15);
        Assert.Empty(Feed(parser, "o1"));
        Assert.Equal("o1", parser.Pending);
        Assert.Equal(new Command[] { new PickOutputCommand(12) }, Feed(parser, "2"));
    }

    [Fact]
    public void Two_digit_capable_index_commits_on_next_key()
    {
        Assert.Equal(new Command[] { new PickOutputCommand(1), new MoveCommand(Direction.Right) },
            Feed(Parser(outputs: 15), "o1l"));
    }

    [Fact]
    public void Two_digit_capable_index_commits_on_enter()
    {
        Assert.Equal(new Command[] { new PickOutputCommand(1) }, Feed(Parser(outputs: 15), "o1\r"));
    }

    [Fact]
    public void Digit_that_cannot_grow_commits_immediately()
    {
        // 출력 15개: "2" 뒤에 숫자가 붙으면 20 이상이라 불가능 → 즉시 확정
        Assert.Equal(new Command[] { new PickOutputCommand(2) }, Feed(Parser(outputs: 15), "o2"));
        Assert.Equal(new Command[] { new PickOutputCommand(0) }, Feed(Parser(outputs: 15), "o0"));
    }

    [Fact]
    public void Out_of_range_index_is_an_error()
    {
        var commands = Feed(Parser(outputs: 2), "o5");
        Assert.IsType<ErrorCommand>(Assert.Single(commands));
    }

    [Fact]
    public void Object_without_outputs_is_an_error()
    {
        var parser = Parser(outputs: 0);
        Assert.IsType<ErrorCommand>(Assert.Single(Feed(parser, "o")));
        Assert.Equal("", parser.Pending);
    }

    [Fact]
    public void Escape_cancels_pending_index()
    {
        var parser = Parser(outputs: 15);
        Assert.Equal(new Command[] { new CancelCommand() }, Feed(parser, "o1\x1b"));
        Assert.Equal("", parser.Pending);
    }

    [Fact]
    public void Backspace_removes_digit()
    {
        var parser = Parser(outputs: 15);
        Feed(parser, "o1\b");
        Assert.Equal("o", parser.Pending);
        Assert.Equal(new Command[] { new PickOutputCommand(3) }, Feed(parser, "3"));
    }

    [Fact]
    public void Non_digit_after_o_is_an_error()
    {
        Assert.IsType<ErrorCommand>(Assert.Single(Feed(Parser(), "ol")));
    }
}
