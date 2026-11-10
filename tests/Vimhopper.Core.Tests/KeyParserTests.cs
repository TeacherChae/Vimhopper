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
    public void Double_y_copies_and_double_gy_copies_info()
    {
        var parser = Parser();
        Assert.Empty(Feed(parser, "y"));
        Assert.Equal("y", parser.Pending);
        Assert.Equal(new Command[] { new CopyCommand() }, Feed(parser, "y"));
        Assert.Empty(Feed(parser, "gy"));
        Assert.Equal("gy", parser.Pending);
        Assert.Equal(new Command[] { new CopyInfoCommand() }, Feed(parser, "y"));
    }

    [Theory]
    [InlineData("yl", Operator.Yank, false, 1)]
    [InlineData("yh", Operator.Yank, true, 1)]
    [InlineData("yL", Operator.Yank, false, null)]
    [InlineData("yH", Operator.Yank, true, null)]
    [InlineData("y2l", Operator.Yank, false, 2)]
    [InlineData("2yh", Operator.Yank, true, 2)]
    [InlineData("2y3l", Operator.Yank, false, 6)]
    [InlineData("y12h", Operator.Yank, true, 12)]
    [InlineData("=l", Operator.Format, false, 1)]
    [InlineData("=H", Operator.Format, true, null)]
    [InlineData("3=2h", Operator.Format, true, 6)]
    public void Operator_takes_a_wire_range(string keys, Operator op, bool upstream, int? depth)
    {
        Assert.Equal(new Command[] { new OperatorCommand(op, new WireRange(upstream, depth)) }, Feed(Parser(), keys));
    }

    [Fact]
    public void Operator_range_shows_pending_keys()
    {
        var parser = Parser();
        Assert.Empty(Feed(parser, "2y3"));
        Assert.Equal("2y3", parser.Pending);
        Assert.Empty(Feed(parser, KeyParser.Backspace.ToString()));
        Assert.Equal("2y", parser.Pending);
        Assert.Equal(new Command[] { new OperatorCommand(Operator.Yank, new WireRange(false, 2)) }, Feed(parser, "l"));
        Assert.Equal("", parser.Pending);
    }

    [Theory]
    [InlineData("yj")]
    [InlineData("y2j")]
    [InlineData("=k")]
    public void Operator_range_must_follow_wires(string keys)
    {
        var parser = Parser();
        Assert.IsType<ErrorCommand>(Assert.Single(Feed(parser, keys)));
        Assert.Equal(new Command[] { new MoveCommand(Direction.Left) }, Feed(parser, "h"));
    }

    [Theory]
    [InlineData("yp", Operator.Yank)]
    [InlineData("gyp", Operator.Reference)]
    [InlineData("dp", Operator.Delete)]
    [InlineData("gcp", Operator.Disable)]
    [InlineData("=p", Operator.Format)]
    [InlineData("3yp", Operator.Yank)]
    [InlineData("y3p", Operator.Yank)]
    [InlineData("2d3p", Operator.Delete)]
    [InlineData("2gcp", Operator.Disable)]
    public void P_is_the_island_range_and_ignores_counts(string keys, Operator op)
    {
        var parser = Parser();
        Assert.Equal(new Command[] { new OperatorCommand(op, WireRange.Island) }, Feed(parser, keys));
        Assert.Equal("", parser.Pending);
    }

    [Fact]
    public void Island_range_is_not_a_wire_range()
    {
        Assert.Equal(RangeKind.Island, WireRange.Island.Kind);
        Assert.Equal(RangeKind.Wire, new WireRange(true, null).Kind);
        Assert.NotEqual(new WireRange(false, null), WireRange.Island);
    }

    [Fact]
    public void P_in_visual_mode_is_not_an_operator_range()
    {
        var parser = Parser();
        Feed(parser, "v");
        Assert.Empty(Feed(parser, "p"));
        Assert.True(parser.Visual);
        Assert.Equal("", parser.Pending);
    }

    [Theory]
    [InlineData("}", true, 1)]
    [InlineData("{", false, 1)]
    [InlineData("3}", true, 3)]
    [InlineData("12{", false, 12)]
    public void Braces_move_between_islands(string keys, bool forward, int count)
    {
        Assert.Equal(new Command[] { new IslandMoveCommand(forward, count) }, Feed(Parser(), keys));
    }

    [Fact]
    public void Braces_move_in_visual_mode_too()
    {
        var parser = Parser();
        Feed(parser, "v");
        Assert.Equal(new Command[] { new IslandMoveCommand(true, 2) }, Feed(parser, "2}"));
        Assert.True(parser.Visual);
    }

    [Fact]
    public void Count_before_doubled_operator_is_ignored()
    {
        Assert.Equal(new Command[] { new CopyCommand() }, Feed(Parser(), "2yy"));
        Assert.Equal(new Command[] { new FormatCommand(FormatScope.Island) }, Feed(Parser(), "3=="));
    }

    [Fact]
    public void Visual_H_and_L_extend_and_stay_in_visual_mode()
    {
        var parser = Parser();
        Feed(parser, "v");
        Assert.Equal(new Command[] { new ExtendVisualCommand(Upstream: true) }, Feed(parser, "H"));
        Assert.Equal(new Command[] { new ExtendVisualCommand(Upstream: false) }, Feed(parser, "L"));
        Assert.True(parser.Visual);
        Assert.Equal(new Command[] { new CopyCommand(), new VisualCommand(false) }, Feed(parser, "y"));
    }

    [Fact]
    public void Visual_count_does_not_start_an_operator()
    {
        var parser = Parser();
        Feed(parser, "v");
        Assert.IsType<ErrorCommand>(Assert.Single(Feed(parser, "2y")));
        Assert.True(parser.Visual);
    }

    [Theory]
    [InlineData("dl", Operator.Delete, false, 1)]
    [InlineData("dH", Operator.Delete, true, null)]
    [InlineData("2d3h", Operator.Delete, true, 6)]
    [InlineData("gcl", Operator.Disable, false, 1)]
    [InlineData("gcL", Operator.Disable, false, null)]
    [InlineData("gc2h", Operator.Disable, true, 2)]
    public void Delete_and_disable_take_the_same_ranges(string keys, Operator op, bool upstream, int? depth)
    {
        Assert.Equal(new Command[] { new OperatorCommand(op, new WireRange(upstream, depth)) }, Feed(Parser(), keys));
    }

    [Fact]
    public void Doubled_operators_act_on_the_cursor_object()
    {
        Assert.Equal(new Command[] { new DeleteCommand() }, Feed(Parser(), "dd"));
        Assert.Equal(new Command[] { new ToggleEnabledCommand() }, Feed(Parser(), "gcc"));
        Assert.IsType<ErrorCommand>(Assert.Single(Feed(Parser(), "dy")));
        Assert.IsType<ErrorCommand>(Assert.Single(Feed(Parser(), "gcd")));
    }

    [Fact]
    public void Operator_pending_keys_show_the_operator()
    {
        var parser = Parser();
        Feed(parser, "gc");
        Assert.Equal("gc", parser.Pending);
        Feed(parser, KeyParser.Escape.ToString());
        Feed(parser, "3d2");
        Assert.Equal("3d2", parser.Pending);
    }

    [Theory]
    [InlineData("di1", ParamSide.Input, 1)]
    [InlineData("do0", ParamSide.Output, 0)]
    public void D_with_a_parameter_disconnects_its_wires(string keys, ParamSide side, int index)
    {
        var parser = Parser();
        Assert.Equal(new Command[] { new DisconnectCommand(side, index) }, Feed(parser, keys));
        // 데이터 연산자가 붙지 않으므로 f는 다시 일반 키
        Assert.Empty(Feed(parser, "f"));
    }

    [Fact]
    public void Disconnect_shows_pending_and_checks_the_index()
    {
        var parser = Parser(inputs: 12);
        Assert.Empty(Feed(parser, "di1"));
        Assert.Equal("di1", parser.Pending);
        Assert.IsType<ErrorCommand>(Assert.Single(Feed(Parser(inputs: 2), "di5")));
        Assert.IsType<ErrorCommand>(Assert.Single(Feed(Parser(outputs: 0), "do")));
    }

    [Fact]
    public void Visual_d_and_gc_act_on_the_selection_and_leave_visual_mode()
    {
        var parser = Parser();
        Feed(parser, "v");
        Assert.Equal(new Command[] { new DeleteCommand(), new VisualCommand(false) }, Feed(parser, "d"));
        Feed(parser, "v");
        Assert.Empty(Feed(parser, "g"));
        Assert.Equal(new Command[] { new ToggleEnabledCommand(), new VisualCommand(false) }, Feed(parser, "c"));
        Assert.False(parser.Visual);
    }

    [Fact]
    public void U_undoes_and_ctrl_r_redoes()
    {
        Assert.Equal(new Command[] { new UndoCommand(), new RedoCommand() }, Feed(Parser(), $"u{KeyParser.CtrlR}"));
    }

    [Theory]
    [InlineData("gyL", false, null)]
    [InlineData("gy2h", true, 2)]
    [InlineData("3gyl", false, 3)]
    [InlineData("2gy3h", true, 6)]
    public void Gy_takes_a_wire_range(string keys, bool upstream, int? depth)
    {
        Assert.Equal(new Command[] { new OperatorCommand(Operator.Reference, new WireRange(upstream, depth)) }, Feed(Parser(), keys));
    }

    [Fact]
    public void Count_before_g_reaches_gc_and_shows_pending()
    {
        var parser = Parser();
        Assert.Empty(Feed(parser, "2g"));
        Assert.Equal("2g", parser.Pending);
        Assert.Empty(Feed(parser, "c"));
        Assert.Equal("2gc", parser.Pending);
        Assert.Equal(new Command[] { new OperatorCommand(Operator.Disable, new WireRange(false, 2)) }, Feed(parser, "l"));
        Assert.Equal(new Command[] { new FormatCommand(FormatScope.Document) }, Feed(parser, "3g="));
    }

    [Fact]
    public void Visual_u_undoes_the_selection_and_stays_in_visual_mode()
    {
        var parser = Parser();
        Feed(parser, "vl");
        Assert.Equal(new Command[] { new UndoSelectionCommand() }, Feed(parser, "u"));
        Assert.True(parser.Visual);
        Feed(parser, KeyParser.Escape.ToString());
        Assert.Equal(new Command[] { new UndoCommand() }, Feed(parser, "u"));
    }

    [Fact]
    public void Space_is_a_leader_and_space_space_inserts_a_component()
    {
        var parser = Parser();
        Assert.Empty(Feed(parser, " "));
        Assert.Equal("<Space>", parser.Pending);
        Assert.Equal(new Command[] { new InsertComponentCommand() }, Feed(parser, " "));
        Assert.Equal("", parser.Pending);
    }

    [Fact]
    public void Leader_cancels_and_rejects_unmapped_keys()
    {
        var parser = Parser();
        Assert.Equal(new Command[] { new CancelCommand() }, Feed(parser, $" {KeyParser.Escape}"));
        Assert.IsType<ErrorCommand>(Assert.Single(Feed(parser, " x")));
        Assert.Equal(new Command[] { new MoveCommand(Direction.Left) }, Feed(parser, "h"));
    }

    [Fact]
    public void Space_space_in_visual_mode_leaves_it()
    {
        var parser = Parser();
        Feed(parser, "v");
        Assert.Equal(new Command[] { new InsertComponentCommand(), new VisualCommand(false) }, Feed(parser, "  "));
    }

    [Fact]
    public void Picked_output_survives_the_leader()
    {
        var parser = Parser();
        Assert.Equal(new Command[] { new PickOutputCommand(0), new InsertComponentCommand() }, Feed(parser, "o0  "));
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

    [Fact]
    public void Ctrl_o_and_ctrl_i_walk_the_jumplist()
    {
        Assert.Equal(new Command[] { new JumpBackCommand(1), new JumpForwardCommand(1) },
            Feed(Parser(), $"{KeyParser.CtrlO}{KeyParser.CtrlI}"));
    }

    [Theory]
    [InlineData("3", KeyParser.CtrlO, 3)]
    [InlineData("12", KeyParser.CtrlI, 12)]
    public void Count_before_ctrl_o_and_ctrl_i(string count, char key, int expected)
    {
        Command command = key == KeyParser.CtrlO ? new JumpBackCommand(expected) : new JumpForwardCommand(expected);
        Assert.Equal(new[] { command }, Feed(Parser(), count + key));
    }

    [Fact]
    public void Ctrl_o_works_in_visual_mode_and_stays_visual()
    {
        var parser = Parser();
        Feed(parser, "v");
        Assert.Equal(new Command[] { new JumpBackCommand(2) }, Feed(parser, $"2{KeyParser.CtrlO}"));
        Assert.True(parser.Visual);
    }

    [Fact]
    public void Ctrl_o_after_a_pending_operator_is_an_error()
    {
        var parser = Parser();
        Assert.IsType<ErrorCommand>(Assert.Single(Feed(parser, $"y{KeyParser.CtrlO}")));
        Assert.Equal("", parser.Pending);
    }

    [Fact]
    public void Dot_repeats()
    {
        Assert.Equal(new Command[] { new RepeatCommand() }, Feed(Parser(), "."));
    }

    [Fact]
    public void Count_before_dot()
    {
        var parser = Parser();
        Feed(parser, "12");
        Assert.Equal("12", parser.Pending);
        Assert.Equal(new Command[] { new RepeatCommand(12) }, Feed(parser, "."));
        Assert.Equal("", parser.Pending);
    }

    [Fact]
    public void Dot_after_param_ops_ends_the_chain()
    {
        Assert.Equal(new Command[]
        {
            new PickOutputCommand(0),
            new ParamOpCommand(ParamSide.Output, ParamOp.Flatten),
            new RepeatCommand(),
        }, Feed(Parser(), "o0f."));
    }

    [Fact]
    public void Dot_commits_a_pending_index_first()
    {
        // 출력이 15개면 o1은 o12가 될 수 있어서 기다리다가 .에서 확정된다
        var parser = Parser(outputs: 15);
        Assert.Empty(Feed(parser, "o1"));
        Assert.Equal(new Command[] { new PickOutputCommand(1), new RepeatCommand() }, Feed(parser, "."));
    }

    [Fact]
    public void Dot_in_visual_mode_is_an_error_and_stays_visual()
    {
        var parser = Parser();
        Feed(parser, "v");
        Assert.IsType<ErrorCommand>(Assert.Single(Feed(parser, ".")));
        Assert.IsType<ErrorCommand>(Assert.Single(Feed(parser, "2.")));
        Assert.True(parser.Visual);
    }

    [Fact]
    public void Zz_centers_and_bare_z_waits_for_a_range()
    {
        var parser = Parser();
        Assert.Empty(Feed(parser, "z"));
        Assert.Equal("z", parser.Pending);
        Assert.Equal(new Command[] { new CenterViewCommand() }, Feed(parser, "z"));
        Assert.Equal("", parser.Pending);
    }

    [Theory]
    [InlineData("zh", true, 1)]
    [InlineData("zl", false, 1)]
    [InlineData("z3h", true, 3)]
    [InlineData("2zl", false, 2)]
    [InlineData("2z3l", false, 6)]
    [InlineData("zH", true, null)]
    [InlineData("zL", false, null)]
    public void Z_is_an_operator_with_wire_ranges(string keys, bool upstream, int? depth)
    {
        Assert.Equal(new Command[] { new OperatorCommand(Operator.Zoom, new WireRange(upstream, depth)) },
            Feed(Parser(), keys));
    }

    [Theory]
    [InlineData("zp")]
    [InlineData("3zp")]
    [InlineData("z2p")]
    public void Zp_fits_the_island(string keys)
    {
        Assert.Equal(new Command[] { new OperatorCommand(Operator.Zoom, WireRange.Island) }, Feed(Parser(), keys));
    }

    [Fact]
    public void Z_rejects_other_keys_and_escape_cancels()
    {
        Assert.IsType<ErrorCommand>(Assert.Single(Feed(Parser(), "zi")));
        Assert.Equal(new Command[] { new CancelCommand() }, Feed(Parser(), "z"));
        Assert.Equal(new Command[] { new CenterViewCommand() }, Feed(Parser(), "3zz"));
    }

    [Fact]
    public void Visual_z_fits_the_selection_and_stays_in_visual_mode()
    {
        var parser = Parser();
        Feed(parser, "v");
        Assert.Equal(new Command[] { new FitSelectionCommand() }, Feed(parser, "z"));
        Assert.True(parser.Visual);
        Assert.Equal("", parser.Pending);
        // 비주얼 모드의 숫자 뒤에는 z가 오지 않는다 (다른 연산자와 같음)
        var error = Assert.IsType<ErrorCommand>(Assert.Single(Feed(parser, "2z")));
        // 비주얼 모드에서 쓸 수 없는 연산자는 알리지 않는다
        Assert.Equal("2 다음에는 h/j/k/l, {/}, Ctrl+o/i, t가 와야 합니다", error.Message);
        Assert.True(parser.Visual);
    }
}
