using System.Linq;
using Vimhopper.Core;
using Xunit;

namespace Vimhopper.Core.Tests;

/// <summary><c>c</c> 바꾸기 접두어와 <c>cn</c> 별명 바꾸기.</summary>
public class RenameKeyParserTests
{
    static KeyParser Parser() => new(_ => 3);

    static Command[] Feed(KeyParser parser, string keys) =>
        keys.SelectMany(parser.Feed).ToArray();

    [Fact]
    public void Cn_opens_the_rename_prompt()
    {
        var parser = Parser();
        Assert.Empty(Feed(parser, "c"));
        Assert.Equal("c", parser.Pending);
        Assert.Equal(new Command[] { new RenamePromptCommand() }, Feed(parser, "n"));
        Assert.Equal("", parser.Pending);
    }

    [Fact]
    public void Count_before_c_is_ignored_but_shown()
    {
        var parser = Parser();
        Assert.Empty(Feed(parser, "3c"));
        Assert.Equal("3c", parser.Pending);
        Assert.Equal(new Command[] { new RenamePromptCommand() }, Feed(parser, "n"));
    }

    [Fact]
    public void Escape_after_c_cancels()
    {
        var parser = Parser();
        Assert.Equal(new Command[] { new CancelCommand() }, Feed(parser, "c\x1b"));
        Assert.Equal("", parser.Pending);
    }

    [Theory]
    [InlineData("cc")]
    [InlineData("cx")]
    [InlineData("cl")]
    public void Other_keys_after_c_are_errors(string keys)
    {
        var parser = Parser();
        var output = Feed(parser, keys);
        Assert.IsType<ErrorCommand>(Assert.Single(output));
        Assert.Equal("", parser.Pending);
        // 오류 뒤에는 평소 상태
        Assert.Equal(new Command[] { new MoveCommand(Direction.Left) }, Feed(parser, "h"));
    }

    [Fact]
    public void Visual_c_is_unassigned_and_gc_still_toggles()
    {
        var parser = Parser();
        Feed(parser, "v");
        Assert.Empty(Feed(parser, "c"));
        Assert.True(parser.Visual);
        Assert.Equal("", parser.Pending);
        // c 다음 n은 찾기 되풀이 (c가 접두어로 남아 있지 않음)
        Assert.Equal(new Command[] { new SearchNextCommand(Reverse: false) }, Feed(parser, "n"));
        Assert.Equal(new Command[] { new ToggleEnabledCommand(), new VisualCommand(false) }, Feed(parser, "gc"));
    }

    [Fact]
    // 비주얼 c는 비어 있어서 숫자를 붙여도 v3p처럼 조용히 아무것도 하지 않는다
    public void Visual_count_c_does_nothing()
    {
        var parser = Parser();
        Feed(parser, "v");
        Assert.Empty(Feed(parser, "3c"));
        Assert.True(parser.Visual);
        Assert.Equal("", parser.Pending);
    }

    [Fact]
    public void Gcc_is_not_affected()
    {
        Assert.Equal(new Command[] { new ToggleEnabledCommand() }, Feed(Parser(), "gcc"));
    }

    [Fact]
    public void Rename_hint_lists_n()
    {
        var parser = Parser();
        Feed(parser, "c");
        var hint = Assert.Single(parser.Hints);
        Assert.Equal(("n", "별명 바꾸기"), (hint.Keys, hint.Description));
    }

    [Fact]
    public void Rename_is_repeatable_but_the_prompt_is_not()
    {
        var register = new RepeatRegister();
        register.Record(new DeleteCommand());
        register.Record(new RenamePromptCommand());
        // 입력줄을 연 것만으로는 마지막 변경이 바뀌지 않는다 (Esc로 취소할 수도 있음)
        Assert.Equal(new Command[] { new DeleteCommand() }, register.Replay(null, _ => 0));

        register.Record(new RenameCommand("점수"));
        Assert.Equal(new Command[] { new RenameCommand("점수") }, register.Replay(null, _ => 0));
        // 숫자는 무시
        Assert.Equal(new Command[] { new RenameCommand("점수") }, register.Replay(3, _ => 0));

        // 실패한 별명 바꾸기는 마지막 변경을 덮어쓰지 않는다
        register.Record(new RenameCommand(null), succeeded: false);
        Assert.Equal(new Command[] { new RenameCommand("점수") }, register.Replay(null, _ => 0));
        register.Record(new RenameCommand(null));
        Assert.Equal(new Command[] { new RenameCommand(null) }, register.Replay(null, _ => 0));
    }
}
