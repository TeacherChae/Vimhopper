using System.Linq;
using Vimhopper.Core;
using Xunit;

namespace Vimhopper.Core.Tests;

/// <summary>찾기 키 (/ n N * #)의 파서 문법.</summary>
public class SearchKeyParserTests
{
    static KeyParser Parser() => new(_ => 3);

    static Command[] Feed(KeyParser parser, string keys) =>
        keys.SelectMany(parser.Feed).ToArray();

    [Fact]
    public void Slash_opens_the_prompt()
    {
        Assert.Equal(new Command[] { new SearchPromptCommand() }, Feed(Parser(), "/"));
        Assert.Equal(new Command[] { new SearchPromptCommand(3) }, Feed(Parser(), "3/"));
    }

    [Theory]
    [InlineData("n", false, 1)]
    [InlineData("N", true, 1)]
    [InlineData("3n", false, 3)]
    [InlineData("12N", true, 12)]
    public void N_repeats_the_last_search(string keys, bool reverse, int count)
    {
        Assert.Equal(new Command[] { new SearchNextCommand(reverse, count) }, Feed(Parser(), keys));
    }

    [Theory]
    [InlineData("*", true, 1)]
    [InlineData("#", false, 1)]
    [InlineData("2*", true, 2)]
    [InlineData("4#", false, 4)]
    public void Star_and_hash_search_the_cursor_type(string keys, bool forward, int count)
    {
        Assert.Equal(new Command[] { new SearchTypeCommand(forward, count) }, Feed(Parser(), keys));
    }

    // o{n} / i{n} 바로 뒤의 n은 기존 문법(데이터 연산자 Invert)이 이긴다
    [Theory]
    [InlineData("o0n", ParamSide.Output)]
    [InlineData("i1n", ParamSide.Input)]
    public void N_after_a_param_is_invert(string keys, ParamSide side)
    {
        Assert.IsType<ParamOpCommand>(Feed(Parser(), keys).Last());
        Assert.Equal(new ParamOpCommand(side, ParamOp.Invert), Feed(Parser(), keys).Last());
    }

    [Fact]
    public void N_after_a_param_and_another_key_searches()
    {
        Assert.Equal(new SearchNextCommand(false), Feed(Parser(), "o0hn").Last());
    }

    // o0 뒤에 찾기로 멀리 가서 i1로 연결할 수 있다 (소스를 고른 상태는 Engine이 유지)
    [Fact]
    public void Slash_after_a_picked_output_opens_the_prompt()
    {
        Assert.Equal(new Command[] { new PickOutputCommand(0), new SearchPromptCommand() }, Feed(Parser(), "o0/"));
    }

    [Fact]
    public void Search_keys_are_motions_in_visual_mode()
    {
        var parser = Parser();
        Feed(parser, "v");
        Assert.Equal(new Command[] { new SearchNextCommand(false, 2) }, Feed(parser, "2n"));
        Assert.Equal(new Command[] { new SearchTypeCommand(false) }, Feed(parser, "#"));
        Assert.Equal(new Command[] { new SearchPromptCommand() }, Feed(parser, "/"));
        Assert.True(parser.Visual);
    }

    // 이번 텀에는 연산자 + / 범위가 없다
    [Theory]
    [InlineData("y/")]
    [InlineData("dn")]
    [InlineData("g*")]
    public void Search_is_not_a_range(string keys)
    {
        Assert.IsType<ErrorCommand>(Feed(Parser(), keys).Last());
    }
}
