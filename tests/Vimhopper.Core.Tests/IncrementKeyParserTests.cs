using System.Linq;
using Vimhopper.Core;
using Xunit;

namespace Vimhopper.Core.Tests;

/// <summary>Ctrl+a / Ctrl+x 키 문법과 되풀이.</summary>
public class IncrementKeyParserTests
{
    static KeyParser Parser() => new(_ => 3);

    static Command[] Feed(KeyParser parser, string keys) => keys.SelectMany(parser.Feed).ToArray();

    static readonly string A = KeyParser.CtrlA.ToString();
    static readonly string X = KeyParser.CtrlX.ToString();

    [Fact]
    public void Ctrl_a_and_ctrl_x_step_once()
    {
        Assert.Equal(new Command[] { new IncrementCommand(Up: true) }, Feed(Parser(), A));
        Assert.Equal(new Command[] { new IncrementCommand(Up: false) }, Feed(Parser(), X));
    }

    [Fact]
    public void Count_is_the_number_of_steps()
    {
        Assert.Equal(new Command[] { new IncrementCommand(true, 3) }, Feed(Parser(), "3" + A));
        Assert.Equal(new Command[] { new IncrementCommand(false, 12) }, Feed(Parser(), "12" + X));
    }

    [Fact]
    public void Visual_mode_stays_visual()
    {
        var parser = Parser();
        Feed(parser, "v");
        Assert.Equal(new Command[] { new IncrementCommand(true, 2) }, Feed(parser, "2" + A));
        Assert.Equal(new Command[] { new IncrementCommand(false) }, Feed(parser, X));
        Assert.True(parser.Visual);
    }

    [Fact]
    public void After_an_operator_is_an_error()
    {
        Assert.IsType<ErrorCommand>(Assert.Single(Feed(Parser(), "y" + A)));
    }

    [Fact]
    public void Repeat_uses_the_same_count()
    {
        var register = new RepeatRegister();
        register.Record(new IncrementCommand(true, 3));
        Assert.Equal(new Command[] { new IncrementCommand(true, 3) }, register.Replay(null, _ => 0));
    }

    [Fact]
    public void Repeat_count_replaces_the_count_and_sticks()
    {
        var register = new RepeatRegister();
        register.Record(new IncrementCommand(false, 3));
        Assert.Equal(new Command[] { new IncrementCommand(false, 5) }, register.Replay(5, _ => 0));
        Assert.Equal(new Command[] { new IncrementCommand(false, 5) }, register.Replay(null, _ => 0));
    }

    [Fact]
    public void Failed_increment_keeps_the_last_change()
    {
        var register = new RepeatRegister();
        register.Record(new DeleteCommand());
        register.Record(new IncrementCommand(true), succeeded: false);
        Assert.Equal(new Command[] { new DeleteCommand() }, register.Replay(null, _ => 0));
    }
}
