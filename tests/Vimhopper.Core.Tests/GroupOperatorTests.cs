using System;
using System.Linq;
using Vimhopper.Core;
using Xunit;

namespace Vimhopper.Core.Tests;

/// <summary><c>gG{범위}</c> 그룹 연산자: 파서 문법과 <c>.</c> 되풀이.</summary>
public class GroupOperatorTests
{
    static KeyParser Parser() => new(_ => 3);

    static Command[] Feed(KeyParser parser, string keys) =>
        keys.SelectMany(parser.Feed).ToArray();

    [Theory]
    [InlineData("gGh", true, 1)]
    [InlineData("gGl", false, 1)]
    [InlineData("gG3l", false, 3)]
    [InlineData("gGH", true, null)]
    [InlineData("gGL", false, null)]
    [InlineData("2gG3h", true, 6)]
    [InlineData("3gGl", false, 3)]
    public void Group_takes_wire_ranges_and_multiplies_counts(string keys, bool upstream, int? depth)
    {
        var parser = Parser();
        Assert.Equal(new Command[] { new OperatorCommand(Operator.Group, new WireRange(upstream, depth)) }, Feed(parser, keys));
        Assert.Equal("", parser.Pending);
    }

    [Theory]
    [InlineData("gGp")]
    [InlineData("2gG3p")]
    public void Group_p_is_the_island(string keys)
    {
        Assert.Equal(new Command[] { new OperatorCommand(Operator.Group, WireRange.Island) }, Feed(Parser(), keys));
    }

    [Fact]
    public void Group_takes_mark_and_label_ranges()
    {
        Assert.Equal(new Command[] { new OperatorCommand(Operator.Group, WireRange.ToMark('a')) }, Feed(Parser(), "gG'a"));

        var parser = Parser();
        Assert.Equal(new Command[] { new ShowLabelsCommand(WithOperator: true) }, Feed(parser, "gGt"));
        Assert.Equal("gGt", parser.Pending);
        var target = Guid.NewGuid();
        parser.SetLabels(new System.Collections.Generic.Dictionary<string, Guid> { ["a"] = target });
        Assert.Equal(new Command[] { new OperatorCommand(Operator.Group, WireRange.To(target)) }, Feed(parser, "a"));
    }

    [Theory]
    [InlineData("gGG")]
    [InlineData("3gGG")]
    public void Doubled_group_is_the_cursor_object(string keys)
    {
        Assert.Equal(new Command[] { new GroupCommand() }, Feed(Parser(), keys));
    }

    // 안쪽 숫자 뒤의 두 번 누르기는 y2y처럼 오류다 (gGG만 커서 객체)
    [Theory]
    [InlineData("gG2G")]
    [InlineData("2gG3G")]
    public void Doubled_group_after_inner_count_is_an_error(string keys)
    {
        var parser = Parser();
        Assert.IsType<ErrorCommand>(Assert.Single(Feed(parser, keys)));
        Assert.IsType<ErrorCommand>(Assert.Single(Feed(Parser(), "y2y")));
        Assert.Equal("", parser.Pending);
    }

    [Fact]
    public void Group_pending_shows_the_operator()
    {
        var parser = Parser();
        Feed(parser, "2gG");
        Assert.Equal("2gG", parser.Pending);
        Feed(parser, "3");
        Assert.Equal("2gG3", parser.Pending);
        Assert.Equal(new Command[] { new CancelCommand() }, Feed(parser, KeyParser.Escape.ToString()));
        Assert.IsType<ErrorCommand>(Assert.Single(Feed(Parser(), "gGy")));
    }

    [Fact]
    public void Visual_group_groups_the_selection_and_leaves_visual_mode()
    {
        var parser = Parser();
        Feed(parser, "v");
        Assert.Equal(new Command[] { new GroupCommand(), new VisualCommand(false) }, Feed(parser, "gG"));
        Assert.False(parser.Visual);
    }

    [Fact]
    public void Asking_for_a_name_is_not_a_change()
    {
        var register = new RepeatRegister();
        register.Record(new DeleteCommand());
        register.Record(new OperatorCommand(Operator.Group, new WireRange(false, 2)));
        register.Record(new GroupCommand());
        Assert.Equal(new Command[] { new DeleteCommand() }, register.Last);
    }

    [Fact]
    public void Named_group_repeats_with_the_same_name_and_count()
    {
        var register = new RepeatRegister();
        var named = new GroupCommand(new WireRange(false, 2), "입력");
        register.Record(named);
        Assert.Equal(new Command[] { named }, register.Replay(null, _ => 0));
        Assert.Equal(new Command[] { new GroupCommand(new WireRange(false, 5), "입력") }, register.Replay(5, _ => 0));

        // 커서 객체(gGG, 비주얼 gG)와 마크 범위도 되풀이한다
        register.Record(new GroupCommand(null, ""));
        Assert.Equal(new Command[] { new GroupCommand(null, "") }, register.Replay(3, _ => 0));
        register.Record(new GroupCommand(WireRange.ToMark('a'), "x"));
        Assert.Equal(new Command[] { new GroupCommand(WireRange.ToMark('a'), "x") }, register.Last);
    }

    [Fact]
    public void Label_range_group_is_not_repeated()
    {
        var register = new RepeatRegister();
        register.Record(new DeleteCommand());
        register.Record(new GroupCommand(WireRange.To(Guid.NewGuid()), "x"));
        Assert.Equal(new Command[] { new DeleteCommand() }, register.Last);
    }

    [Fact]
    public void Input_line_takes_a_text_prompt()
    {
        var line = new InputLine();
        line.Open("그룹 이름: ");
        Assert.True(line.Active);
        Assert.Equal("그룹 이름: ", line.PromptText);
        line.Feed('입');
        Assert.Equal("입", line.Value);
        line.Close();
        Assert.False(line.Active);
        Assert.Equal("", line.PromptText);

        line.Open('/');
        Assert.Equal("/", line.PromptText);
    }
}
