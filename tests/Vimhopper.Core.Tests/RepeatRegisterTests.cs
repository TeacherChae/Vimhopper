using System.Linq;
using Vimhopper.Core;
using Xunit;

namespace Vimhopper.Core.Tests;

public class RepeatRegisterTests
{
    static int Three(ParamSide side) => 3;

    static RepeatRegister Recorded(params Command[] commands)
    {
        var register = new RepeatRegister();
        foreach (var c in commands)
            register.Record(c);
        return register;
    }

    [Fact]
    public void Nothing_to_repeat_is_an_error()
    {
        var replay = new RepeatRegister().Replay(null, Three);
        Assert.IsType<ErrorCommand>(Assert.Single(replay));
    }

    public static TheoryData<Command> Changes => new()
    {
        new OperatorCommand(Operator.Delete, new WireRange(false, 2)),
        new OperatorCommand(Operator.Format, new WireRange(true, null)),
        new OperatorCommand(Operator.Disable, new WireRange(false, 1)),
        new DeleteCommand(),
        new ToggleEnabledCommand(),
        new DisconnectCommand(ParamSide.Input, 1),
        new FormatCommand(FormatScope.Island),
        new FormatCommand(FormatScope.Document),
    };

    [Theory]
    [MemberData(nameof(Changes))]
    public void Changes_are_repeated_as_is(Command change)
    {
        Assert.True(RepeatRegister.IsRepeatable(change));
        Assert.Equal(new[] { change }, Recorded(change).Replay(null, Three));
    }

    public static TheoryData<Command> NotChanges => new()
    {
        new OperatorCommand(Operator.Yank, new WireRange(false, 1)),
        new OperatorCommand(Operator.Reference, new WireRange(true, null)),
        new MoveCommand(Direction.Left),
        new FollowParamCommand(ParamSide.Input, 0),
        new JumpCommand(Direction.Up),
        new CopyCommand(),
        new CopyInfoCommand(),
        new UndoCommand(),
        new RedoCommand(),
        new VisualCommand(true),
        new ExtendVisualCommand(true),
        new InsertComponentCommand(),
        new PickOutputCommand(0),
        new ErrorCommand("x"),
        new RepeatCommand(),
    };

    [Theory]
    [MemberData(nameof(NotChanges))]
    public void Non_changes_do_not_replace_the_last_change(Command other)
    {
        Assert.False(RepeatRegister.IsRepeatable(other));
        Assert.Equal(new Command[] { new DeleteCommand() }, Recorded(new DeleteCommand(), other).Replay(null, Three));
    }

    [Fact]
    public void Visual_format_is_repeated_as_island_format()
    {
        Assert.Equal(new Command[] { new FormatCommand(FormatScope.Island) },
            Recorded(new FormatCommand(FormatScope.Selection), new VisualCommand(false)).Replay(null, Three));
    }

    [Fact]
    public void Count_replaces_numeric_depth_and_sticks()
    {
        var register = Recorded(new OperatorCommand(Operator.Delete, new WireRange(false, 2)));
        var d3l = new OperatorCommand(Operator.Delete, new WireRange(false, 3));
        Assert.Equal(new Command[] { d3l }, register.Replay(3, Three));
        // vim처럼 이후의 .도 새 숫자를 쓴다
        Assert.Equal(new Command[] { d3l }, register.Replay(null, Three));
    }

    [Fact]
    public void Count_is_ignored_for_whole_ranges_and_other_changes()
    {
        var dL = new OperatorCommand(Operator.Delete, new WireRange(false, null));
        Assert.Equal(new Command[] { dL }, Recorded(dL).Replay(5, Three));
        Assert.Equal(new Command[] { new DeleteCommand() }, Recorded(new DeleteCommand()).Replay(5, Three));
    }

    [Fact]
    public void Connect_and_its_ops_repeat_together()
    {
        var register = Recorded(
            new PickOutputCommand(0),
            new MoveCommand(Direction.Right),
            new ConnectInputCommand(1, false),
            new ParamOpCommand(ParamSide.Input, ParamOp.Flatten),
            new ParamOpCommand(ParamSide.Input, ParamOp.Reverse),
            new MoveCommand(Direction.Down));
        Assert.Equal(new Command[]
        {
            new ConnectInputCommand(1, false),
            new ParamOpCommand(ParamSide.Input, ParamOp.Flatten),
            new ParamOpCommand(ParamSide.Input, ParamOp.Reverse),
        }, register.Replay(null, Three));
    }

    [Fact]
    public void Output_ops_repeat_on_the_same_output_without_picking_a_source()
    {
        var register = Recorded(new PickOutputCommand(2), new ParamOpCommand(ParamSide.Output, ParamOp.Graft));
        Assert.Equal(new Command[]
        {
            new TargetParamCommand(ParamSide.Output, 2),
            new ParamOpCommand(ParamSide.Output, ParamOp.Graft),
        }, register.Replay(null, Three));
    }

    [Fact]
    public void Picking_an_output_alone_keeps_the_previous_change()
    {
        var register = Recorded(new ConnectInputCommand(0, true), new PickOutputCommand(1), new MoveCommand(Direction.Left));
        Assert.Equal(new Command[] { new ConnectInputCommand(0, true) }, register.Replay(null, Three));
    }

    [Fact]
    public void Op_after_another_command_is_not_chained()
    {
        // 다른 명령 뒤의 데이터 연산자는 붙을 파라미터가 없으므로 무시
        var register = Recorded(new ConnectInputCommand(0, false), new MoveCommand(Direction.Left),
            new ParamOpCommand(ParamSide.Input, ParamOp.Flatten));
        Assert.Equal(new Command[] { new ConnectInputCommand(0, false) }, register.Replay(null, Three));
    }

    [Fact]
    public void New_op_chain_replaces_connect()
    {
        var register = Recorded(new ConnectInputCommand(0, false), new MoveCommand(Direction.Left),
            new PickOutputCommand(1), new ParamOpCommand(ParamSide.Output, ParamOp.Simplify));
        Assert.Equal(new Command[]
        {
            new TargetParamCommand(ParamSide.Output, 1),
            new ParamOpCommand(ParamSide.Output, ParamOp.Simplify),
        }, register.Replay(null, Three));
    }

    [Theory]
    [InlineData(ParamSide.Input)]
    [InlineData(ParamSide.Output)]
    public void Missing_param_at_cursor_is_an_error(ParamSide side)
    {
        var register = Recorded(new DisconnectCommand(side, 2));
        var replay = register.Replay(null, s => s == side ? 2 : 5);
        Assert.IsType<ErrorCommand>(Assert.Single(replay));
        // 기억한 변경은 그대로 남는다
        Assert.Equal(new Command[] { new DisconnectCommand(side, 2) }, register.Replay(null, Three));
    }

    [Fact]
    public void Missing_input_for_connect_is_an_error()
    {
        var replay = Recorded(new ConnectInputCommand(3, false)).Replay(null, Three);
        Assert.IsType<ErrorCommand>(Assert.Single(replay));
    }

    [Fact]
    public void Failed_connect_keeps_the_last_change()
    {
        var register = new RepeatRegister();
        register.Record(new DeleteCommand());
        register.Record(new ConnectInputCommand(1, false), succeeded: false);
        Assert.Equal(new Command[] { new DeleteCommand() }, register.Replay(null, Three));
    }

    [Fact]
    public void Failed_change_keeps_the_last_change()
    {
        var register = new RepeatRegister();
        register.Record(new DisconnectCommand(ParamSide.Input, 0));
        register.Record(new DeleteCommand(), succeeded: false);
        register.Record(new OperatorCommand(Operator.Format, new WireRange(false, 1)), succeeded: false);
        Assert.Equal(new Command[] { new DisconnectCommand(ParamSide.Input, 0) }, register.Replay(null, Three));
    }

    [Fact]
    public void Ops_after_failed_connect_repeat_on_that_input_only()
    {
        // 연결은 실패해도 Engine은 그 입력에 연산자를 적용하므로, 되풀이도 연산자만
        var register = new RepeatRegister();
        register.Record(new PickOutputCommand(0));
        register.Record(new ConnectInputCommand(2, false), succeeded: false);
        register.Record(new ParamOpCommand(ParamSide.Input, ParamOp.Flatten));
        Assert.Equal(new Command[]
        {
            new TargetParamCommand(ParamSide.Input, 2),
            new ParamOpCommand(ParamSide.Input, ParamOp.Flatten),
        }, register.Replay(null, Three));
    }

    [Fact]
    public void Failed_op_is_left_out_of_the_chain()
    {
        var register = new RepeatRegister();
        register.Record(new PickOutputCommand(1));
        register.Record(new ParamOpCommand(ParamSide.Output, ParamOp.Invert), succeeded: false);
        register.Record(new ParamOpCommand(ParamSide.Output, ParamOp.Reverse));
        Assert.Equal(new Command[]
        {
            new TargetParamCommand(ParamSide.Output, 1),
            new ParamOpCommand(ParamSide.Output, ParamOp.Reverse),
        }, register.Replay(null, Three));
    }

    [Fact]
    public void Parser_and_register_together()
    {
        var parser = new KeyParser(_ => 3);
        var register = new RepeatRegister();
        foreach (var c in "d2l".SelectMany(parser.Feed))
            register.Record(c);
        var repeat = Assert.IsType<RepeatCommand>(Assert.Single("4.".SelectMany(parser.Feed)));
        Assert.Equal(new Command[] { new OperatorCommand(Operator.Delete, new WireRange(false, 4)) },
            register.Replay(repeat.Count, Three));
    }
}
