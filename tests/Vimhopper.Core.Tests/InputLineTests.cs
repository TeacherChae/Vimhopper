using Vimhopper.Core;
using Xunit;

namespace Vimhopper.Core.Tests;

public class InputLineTests
{
    static InputLine Open(string text = "")
    {
        var line = new InputLine();
        line.Open('/');
        foreach (var c in text) line.Feed(c);
        return line;
    }

    [Fact]
    public void Typing_appends()
    {
        var line = Open();
        Assert.Equal(InputLineResult.Changed, line.Feed('a'));
        Assert.Equal(InputLineResult.Changed, line.Feed('한'));
        Assert.Equal("a한", line.Text);
        Assert.Equal('/', line.Prompt);
        Assert.True(line.Active);
    }

    [Fact]
    public void Backspace_deletes_then_cancels_on_empty()
    {
        var line = Open("ab");
        Assert.Equal(InputLineResult.Changed, line.Feed('\b'));
        Assert.Equal("a", line.Text);
        Assert.Equal(InputLineResult.Changed, line.Feed('\b'));
        Assert.Equal(InputLineResult.Cancelled, line.Feed('\b'));
    }

    [Fact]
    public void Enter_confirms_and_escape_cancels()
    {
        Assert.Equal(InputLineResult.Confirmed, Open("x").Feed('\r'));
        Assert.Equal(InputLineResult.Confirmed, Open().Feed('\r'));
        Assert.Equal(InputLineResult.Cancelled, Open("x").Feed('\x1b'));
    }

    [Fact]
    public void Value_includes_the_composition()
    {
        var line = Open("a");
        line.Composition = "하";
        Assert.Equal("a하", line.Value);
        Assert.Equal("a", line.Text);
        // 조합 중에는 빈 줄 Backspace로 취소되지 않는다
        var empty = Open();
        empty.Composition = "ㅎ";
        Assert.Equal(InputLineResult.Changed, empty.Feed('\b'));
    }

    [Fact]
    public void Ctrl_u_clears_and_other_controls_are_ignored()
    {
        var line = Open("abc");
        Assert.Equal(InputLineResult.None, line.Feed('\t'));
        Assert.Equal(InputLineResult.Changed, line.Feed(InputLine.CtrlU));
        Assert.Equal("", line.Text);
    }

    [Fact]
    public void Insert_takes_the_first_line()
    {
        var line = Open("a");
        Assert.Equal(InputLineResult.Changed, line.Insert("b\tc\r\nnext"));
        Assert.Equal("abc", line.Text);
    }

    [Fact]
    public void Closed_line_ignores_input()
    {
        var line = Open("a");
        line.Close();
        Assert.False(line.Active);
        Assert.Equal(InputLineResult.None, line.Feed('b'));
        Assert.Equal("", line.Value);
    }
}
