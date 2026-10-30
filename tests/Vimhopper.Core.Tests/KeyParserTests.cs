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

    [Theory]
    [InlineData(KeyParser.CtrlH, Direction.Left)]
    [InlineData(KeyParser.CtrlJ, Direction.Down)]
    [InlineData(KeyParser.CtrlK, Direction.Up)]
    [InlineData(KeyParser.CtrlL, Direction.Right)]
    public void Ctrl_hjkl_jumps(char key, Direction expected)
    {
        Assert.Equal(new Command[] { new JumpCommand(expected) }, Feed(Parser(), key.ToString()));
    }

    [Fact]
    public void Zoom_keys()
    {
        Assert.Equal(new Command[] { new ZoomCommand(true), new ZoomCommand(false) },
            Feed(Parser(), $"{KeyParser.CtrlPlus}{KeyParser.CtrlMinus}"));
    }

    [Fact]
    public void Plain_shift_hjkl_and_minus_are_unbound()
    {
        Assert.Empty(Feed(Parser(), "HJKL-"));
    }

    [Theory]
    [InlineData("1h", Direction.Left, 1)]
    [InlineData("3l", Direction.Right, 3)]
    [InlineData("10l", Direction.Right, 10)]
    [InlineData("12j", Direction.Down, 12)]
    [InlineData("3k", Direction.Up, 3)]
    public void Count_before_hjkl(string keys, Direction direction, int count)
    {
        Assert.Equal(new Command[] { new MoveCommand(direction, count) }, Feed(Parser(), keys));
    }

    [Fact]
    public void Count_is_pending_until_a_motion_and_can_be_cancelled()
    {
        var parser = Parser();
        Assert.Empty(Feed(parser, "12"));
        Assert.Equal("12", parser.Pending);
        Assert.Empty(Feed(parser, KeyParser.Backspace.ToString()));
        Assert.Equal("1", parser.Pending);
        Assert.Equal(new Command[] { new CancelCommand() }, Feed(parser, KeyParser.Escape.ToString()));
        Assert.Equal(new Command[] { new MoveCommand(Direction.Left) }, Feed(parser, "h"));
    }

    [Fact]
    public void Count_before_other_key_is_an_error()
    {
        var parser = Parser();
        Assert.IsType<ErrorCommand>(Assert.Single(Feed(parser, "2o")));
        Assert.Equal("", parser.Pending);
    }

    [Fact]
    public void Zero_does_not_start_a_count()
    {
        Assert.Equal(new Command[] { new MoveCommand(Direction.Right) }, Feed(Parser(), "0l"));
    }

    [Theory]
    [InlineData("gi1", ParamSide.Input, 1)]
    [InlineData("go0", ParamSide.Output, 0)]
    public void G_i_and_g_o_follow_a_numbered_param(string keys, ParamSide side, int index)
    {
        var parser = Parser();
        Assert.Equal(new Command[] { new FollowParamCommand(side, index) }, Feed(parser, keys));
        Assert.Equal("", parser.Pending);
    }

    [Fact]
    public void G_i_shows_pending_and_takes_two_digits()
    {
        var parser = Parser(inputs: 15);
        Assert.Empty(Feed(parser, "gi1"));
        Assert.Equal("gi1", parser.Pending);
        Assert.Equal(new Command[] { new FollowParamCommand(ParamSide.Input, 12) }, Feed(parser, "2"));
    }

    [Fact]
    public void Follow_is_a_motion_so_op_keys_after_it_are_not_data_ops()
    {
        // go0 다음의 f는 데이터 연산자가 아님 (아무 동작 없음)
        Assert.Equal(new Command[] { new FollowParamCommand(ParamSide.Output, 0), new MoveCommand(Direction.Left) },
            Feed(Parser(), "go0fh"));
    }

    [Fact]
    public void Follow_out_of_range_or_without_params_is_an_error()
    {
        Assert.IsType<ErrorCommand>(Assert.Single(Feed(Parser(inputs: 2), "gi5")));
        Assert.IsType<ErrorCommand>(Assert.Single(Feed(Parser(outputs: 0), "go")));
    }

    [Fact]
    public void Huge_count_does_not_overflow()
    {
        Assert.Equal(new Command[] { new MoveCommand(Direction.Down, 9999) }, Feed(Parser(), "99999999999j"));
    }

    [Fact]
    public void Double_equals_formats_island_and_g_equals_formats_document()
    {
        var parser = Parser();
        Assert.Empty(Feed(parser, "="));
        Assert.Equal("=", parser.Pending);
        Assert.Equal(new Command[] { new FormatCommand(FormatScope.Island) }, Feed(parser, "="));
        Assert.Equal("", parser.Pending);
        Assert.Equal(new Command[] { new FormatCommand(FormatScope.Document) }, Feed(parser, "g="));
    }

    [Fact]
    public void Double_y_copies_and_gy_copies_info()
    {
        var parser = Parser();
        Assert.Empty(Feed(parser, "y"));
        Assert.Equal("y", parser.Pending);
        Assert.Equal(new Command[] { new CopyCommand() }, Feed(parser, "y"));
        Assert.Equal(new Command[] { new CopyInfoCommand() }, Feed(parser, "gy"));
    }

    [Fact]
    public void Visual_gy_copies_info_and_leaves_visual_mode()
    {
        var parser = Parser();
        Assert.Equal(new Command[] { new VisualCommand(true), new CopyInfoCommand(), new VisualCommand(false) }, Feed(parser, "vgy"));
        Assert.False(parser.Visual);
    }

    [Fact]
    public void V_enters_and_leaves_visual_mode()
    {
        var parser = Parser();
        Assert.Equal(new Command[] { new VisualCommand(true) }, Feed(parser, "v"));
        Assert.True(parser.Visual);
        Assert.Equal(new Command[] { new MoveCommand(Direction.Right), new MoveCommand(Direction.Down, 2) }, Feed(parser, "l2j"));
        Assert.True(parser.Visual);
        Assert.Equal(new Command[] { new VisualCommand(false) }, Feed(parser, "v"));
        Assert.False(parser.Visual);
    }

    [Fact]
    public void Escape_leaves_visual_mode()
    {
        var parser = Parser();
        Assert.Equal(new Command[] { new VisualCommand(true), new VisualCommand(false) }, Feed(parser, $"v{KeyParser.Escape}"));
        Assert.False(parser.Visual);
    }

    [Fact]
    public void Escape_in_visual_mode_cancels_pending_count_before_leaving()
    {
        var parser = Parser();
        Assert.Equal(new Command[] { new VisualCommand(true), new CancelCommand() }, Feed(parser, $"v3{KeyParser.Escape}"));
        Assert.True(parser.Visual);
    }

    [Fact]
    public void Visual_y_and_equals_act_once_and_leave_visual_mode()
    {
        var parser = Parser();
        Assert.Equal(new Command[] { new VisualCommand(true), new CopyCommand(), new VisualCommand(false) }, Feed(parser, "vy"));
        Assert.False(parser.Visual);
        Assert.Equal(new Command[] { new VisualCommand(true), new FormatCommand(FormatScope.Selection), new VisualCommand(false) },
            Feed(parser, "v="));
        Assert.False(parser.Visual);
    }

    [Fact]
    public void Visual_o_swaps_ends_and_connect_keys_are_errors()
    {
        var parser = Parser();
        Feed(parser, "v");
        Assert.Equal(new Command[] { new SwapVisualEndsCommand() }, Feed(parser, "o"));
        Assert.IsType<ErrorCommand>(Assert.Single(Feed(parser, "i")));
        Assert.True(parser.Visual);
    }

    [Fact]
    public void Reset_leaves_visual_mode()
    {
        var parser = Parser();
        Feed(parser, "v");
        parser.Reset();
        Assert.False(parser.Visual);
        Assert.Equal(new Command[] { new PickOutputCommand(0) }, Feed(parser, "o0"));
    }

    [Fact]
    public void Escape_cancels_pending_two_key_command()
    {
        var parser = Parser();
        Assert.Equal(new Command[] { new CancelCommand() }, Feed(parser, $"g{KeyParser.Escape}"));
        Assert.Equal(new Command[] { new MoveCommand(Direction.Right) }, Feed(parser, "l"));
    }

    [Fact]
    public void Unknown_second_key_is_an_error_and_resets()
    {
        var parser = Parser();
        Assert.IsType<ErrorCommand>(Assert.Single(Feed(parser, "gx")));
        Assert.Equal(new Command[] { new MoveCommand(Direction.Left) }, Feed(parser, "h"));
    }

    [Fact]
    public void Format_after_connect_ends_operator_chain()
    {
        Assert.Equal(new Command[] { new ConnectInputCommand(0, false), new FormatCommand(FormatScope.Island) },
            Feed(Parser(), "i0=="));
    }

    [Fact]
    public void Ctrl_jump_finishes_pending_index_first()
    {
        Assert.Equal(new Command[] { new PickOutputCommand(1), new JumpCommand(Direction.Right) },
            Feed(Parser(outputs: 15), $"o1{KeyParser.CtrlL}"));
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
        // g는 g= 의 시작 키이므로 제외
        Assert.Empty(Feed(Parser(), "fsrn"));
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
