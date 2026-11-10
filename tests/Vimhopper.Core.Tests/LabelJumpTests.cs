using System;
using System.Collections.Generic;
using System.Linq;
using Vimhopper.Core;
using Xunit;

namespace Vimhopper.Core.Tests;

public class LabelJumpTests
{
    // 가로로 한 줄: i번째 객체는 x = i × 100
    static Box[] Row(int count) =>
        Enumerable.Range(0, count).Select(i => new Box(Guid.NewGuid(), i * 100, 0, 50, 20)).ToArray();

    [Fact]
    public void Nearest_objects_get_home_row_letters_first()
    {
        var boxes = Row(4);
        // 기준점이 오른쪽 끝 객체 근처: 오른쪽부터 가까움
        var labels = LabelJump.Assign(boxes, 325, 10);
        Assert.Equal(boxes[3].Id, labels["a"]);
        Assert.Equal(boxes[2].Id, labels["s"]);
        Assert.Equal(boxes[1].Id, labels["d"]);
        Assert.Equal(boxes[0].Id, labels["f"]);
    }

    [Fact]
    public void Up_to_26_objects_get_single_letters()
    {
        var labels = LabelJump.Assign(Row(26), 0, 0);
        Assert.Equal(26, labels.Count);
        Assert.All(labels.Keys, l => Assert.Single(l));
        Assert.Equal(LabelJump.Alphabet.Select(c => c.ToString()).ToHashSet(), labels.Keys.ToHashSet());
    }

    [Theory]
    [InlineData(27)]
    [InlineData(51)]
    [InlineData(52)]
    [InlineData(200)]
    [InlineData(676)]
    public void More_objects_use_prefix_free_two_letter_labels(int count)
    {
        var labels = LabelJump.Labels(count);
        Assert.Equal(count, labels.Count);
        Assert.Equal(count, labels.Distinct().Count());
        Assert.All(labels, l => Assert.All(l, c => Assert.Contains(c, LabelJump.Alphabet)));
        Assert.All(labels, l => Assert.InRange(l.Length, 1, 2));
        foreach (var a in labels)
            foreach (var b in labels)
                if (a != b)
                    Assert.False(b.StartsWith(a, StringComparison.Ordinal), $"{a}가 {b}의 앞부분");
    }

    [Fact]
    public void Single_letters_go_first_and_stay_as_many_as_possible()
    {
        // 27개: 한 글자 25개 + 마지막 글자로 시작하는 두 글자 2개
        var labels = LabelJump.Labels(27);
        Assert.Equal(LabelJump.Alphabet.Take(25).Select(c => c.ToString()), labels.Take(25));
        Assert.Equal(new[] { "ma", "ms" }, labels.Skip(25));
        // 52개: 한 글자 24개
        Assert.Equal(24, LabelJump.Labels(52).Count(l => l.Length == 1));
    }

    [Fact]
    public void Nearest_object_keeps_a_single_letter_when_labels_overflow()
    {
        var boxes = Row(40);
        var labels = LabelJump.Assign(boxes, 0, 0);
        Assert.Equal(boxes[0].Id, labels["a"]);
        Assert.Equal(2, labels.First(p => p.Value == boxes[39].Id).Key.Length);
    }

    [Fact]
    public void Objects_beyond_capacity_get_no_label()
    {
        var labels = LabelJump.Assign(Row(LabelJump.Capacity + 5), 0, 0);
        Assert.Equal(LabelJump.Capacity, labels.Count);
    }

    [Fact]
    public void No_objects_no_labels()
    {
        Assert.Empty(LabelJump.Assign(Array.Empty<Box>(), 0, 0));
    }
}

public class KeyParserLabelTests
{
    static readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid(), C = Guid.NewGuid();

    static KeyParser Parser() => new(_ => 3);

    static Command[] Feed(KeyParser parser, string keys) =>
        keys.SelectMany(parser.Feed).ToArray();

    // t 다음 Engine이 하는 일: 라벨을 알려 준다
    static Command[] FeedWithLabels(KeyParser parser, string keys)
    {
        var output = new List<Command>();
        foreach (var key in keys)
            foreach (var command in parser.Feed(key))
            {
                output.Add(command);
                if (command is ShowLabelsCommand)
                    parser.SetLabels(new Dictionary<string, Guid> { ["a"] = A, ["s"] = B, ["md"] = C });
            }
        return output.ToArray();
    }

    [Fact]
    public void T_shows_labels_and_jumps()
    {
        var parser = Parser();
        Assert.Equal(new Command[] { new ShowLabelsCommand() }, Feed(parser, "t"));
        Assert.True(parser.AwaitingLabel);
        Assert.Equal("t", parser.Pending);
        parser.SetLabels(new Dictionary<string, Guid> { ["a"] = A });
        Assert.Equal(new Command[] { new LabelJumpCommand(A) }, Feed(parser, "a"));
        Assert.False(parser.AwaitingLabel);
        Assert.Empty(parser.Labels);
    }

    [Fact]
    public void Two_letter_label_waits_for_the_second_letter()
    {
        var parser = Parser();
        Assert.Equal(new Command[] { new ShowLabelsCommand() }, FeedWithLabels(parser, "tm"));
        Assert.Equal("m", parser.LabelTyped);
        Assert.Equal("tm", parser.Pending);
        Assert.Equal(new Command[] { new LabelJumpCommand(C) }, Feed(parser, "d"));
    }

    [Fact]
    public void Backspace_removes_the_first_letter()
    {
        var parser = Parser();
        FeedWithLabels(parser, "tm\b");
        Assert.True(parser.AwaitingLabel);
        Assert.Equal("", parser.LabelTyped);
        Assert.Equal(new Command[] { new LabelJumpCommand(B) }, Feed(parser, "s"));
    }

    [Theory]
    [InlineData("tx")]
    [InlineData("tmx")]
    [InlineData("tma")]
    [InlineData("t1")]
    [InlineData("th")]
    [InlineData("t\b")]
    public void Non_label_key_cancels_with_a_message(string keys)
    {
        var parser = Parser();
        var output = FeedWithLabels(parser, keys);
        Assert.IsType<ErrorCommand>(output.Last());
        Assert.False(parser.AwaitingLabel);
        // 그만둔 키는 다른 명령으로 쓰이지 않는다
        Assert.DoesNotContain(output, c => c is MoveCommand);
    }

    [Fact]
    public void Escape_cancels_but_keeps_the_source()
    {
        // CancelCommand는 고른 소스를 지우므로 쓰지 않는다
        var output = FeedWithLabels(Parser(), "t\x1b");
        Assert.Equal(new Command[] { new ShowLabelsCommand(), new ErrorCommand("라벨 점프 취소") }, output);
    }

    [Fact]
    public void Labels_are_case_insensitive()
    {
        Assert.Equal(new LabelJumpCommand(A), FeedWithLabels(Parser(), "tA").Last());
    }

    [Fact]
    public void Empty_labels_end_the_jump()
    {
        var parser = Parser();
        Feed(parser, "t");
        parser.SetLabels(new Dictionary<string, Guid>());
        Assert.False(parser.AwaitingLabel);
        Assert.Equal(new Command[] { new MoveCommand(Direction.Left) }, Feed(parser, "h"));
    }

    [Fact]
    public void Count_is_ignored()
    {
        Assert.Equal(new Command[] { new ShowLabelsCommand(), new LabelJumpCommand(A) }, FeedWithLabels(Parser(), "3ta"));
    }

    [Fact]
    public void Picked_source_survives_label_jump()
    {
        // o0 t{라벨} i1: t는 데이터 연산자가 아니므로 o0 다음에 바로 라벨 점프가 된다
        var output = FeedWithLabels(Parser(), "o0tai1");
        Assert.Equal(new Command[]
        {
            new PickOutputCommand(0), new ShowLabelsCommand(), new LabelJumpCommand(A), new ConnectInputCommand(1, false),
        }, output);
    }

    [Fact]
    public void Visual_t_jumps_and_stays_visual()
    {
        var parser = Parser();
        var output = FeedWithLabels(parser, "vta");
        Assert.Equal(new LabelJumpCommand(A), output.Last());
        Assert.True(parser.Visual);
    }

    [Theory]
    [InlineData("yta", Operator.Yank)]
    [InlineData("dta", Operator.Delete)]
    [InlineData("=ta", Operator.Format)]
    [InlineData("gyta", Operator.Reference)]
    [InlineData("gcta", Operator.Disable)]
    // 숫자는 무시한다
    [InlineData("2dta", Operator.Delete)]
    [InlineData("d2ta", Operator.Delete)]
    [InlineData("3gcta", Operator.Disable)]
    public void Operator_t_is_a_block_range(string keys, Operator op)
    {
        Assert.Equal(new OperatorCommand(op, WireRange.To(A)), FeedWithLabels(Parser(), keys).Last());
    }

    [Theory]
    [InlineData("2dtm", "2dtm")]
    [InlineData("y2t", "y2t")]
    [InlineData("y2tm", "y2tm")]
    [InlineData("3gct", "3gct")]
    [InlineData("3t", "3t")]
    [InlineData("o0t", "t")]
    public void Pending_keeps_the_typed_keys(string keys, string pending)
    {
        var parser = Parser();
        FeedWithLabels(parser, keys);
        Assert.Equal(pending, parser.Pending);
    }

    [Theory]
    [InlineData("dt", true)]
    [InlineData("y2t", true)]
    [InlineData("gct", true)]
    [InlineData("t", false)]
    [InlineData("3t", false)]
    [InlineData("vt", false)]
    public void Show_labels_says_whether_an_operator_waits(string keys, bool withOperator)
    {
        Assert.Equal(new ShowLabelsCommand(withOperator), Feed(Parser(), keys).Last());
    }

    [Fact]
    public void Operator_t_without_labels_ends_cleanly()
    {
        // Engine이 커서 객체가 없어서 빈 라벨을 돌려주면 연산자도 함께 끝난다
        var parser = Parser();
        Feed(parser, "dt");
        parser.SetLabels(new Dictionary<string, Guid>());
        Assert.False(parser.AwaitingLabel);
        Assert.Equal("", parser.Pending);
        Assert.Equal(new Command[] { new MoveCommand(Direction.Left) }, Feed(parser, "h"));
    }

    [Fact]
    public void Operator_t_is_not_repeatable()
    {
        var register = new RepeatRegister();
        var before = new OperatorCommand(Operator.Delete, new WireRange(false, 2));
        register.Record(before);
        register.Record(new OperatorCommand(Operator.Delete, WireRange.To(A)));
        Assert.Equal(new Command[] { before }, register.Last);
        Assert.False(RepeatRegister.IsRepeatable(new OperatorCommand(Operator.Disable, WireRange.To(A))));
    }

    [Fact]
    public void Label_jump_is_not_a_change()
    {
        Assert.False(RepeatRegister.IsRepeatable(new LabelJumpCommand(A)));
        Assert.False(RepeatRegister.IsRepeatable(new ShowLabelsCommand()));
    }
}
