using Vimhopper.Core;
using Xunit;

namespace Vimhopper.Core.Tests;

/// <summary>:s/찾을/바꿀/[gi] 인자 읽기와 별명 바꾸기.</summary>
public class SubstitutionTests
{
    static string Apply(string args, string input)
    {
        var sub = Substitution.Parse(args, out var error);
        Assert.True(sub != null, error);
        return sub!.Apply(input);
    }

    static string? Error(string args)
    {
        Assert.Null(Substitution.Parse(args, out var error));
        return error;
    }

    [Theory]
    [InlineData("/a/b/", "aaa", "baa")]
    [InlineData("/a/b/g", "aaa", "bbb")]
    [InlineData("/A/b/", "aaa", "aaa")]
    [InlineData("/A/b/i", "aAa", "bAa")]
    [InlineData("/A/b/gi", "aAa", "bbb")]
    [InlineData("/A/b/ig", "aAa", "bbb")]
    [InlineData("/x/y/", "aaa", "aaa")]
    public void Flags_g_and_i(string args, string input, string expected)
    {
        Assert.Equal(expected, Apply(args, input));
    }

    [Theory]
    [InlineData("#a#b#", "a/a", "b/a")]
    [InlineData("#/#-#g", "a/b/c", "a-b-c")]
    [InlineData(",a,b,", "aa", "ba")]
    public void Any_delimiter(string args, string input, string expected)
    {
        Assert.Equal(expected, Apply(args, input));
    }

    [Theory]
    [InlineData(@"/a\/b/c/", "a/b", "c")]
    [InlineData(@"/a/b\/c/", "a", "b/c")]
    [InlineData(@"/\d+/n/", "x12y", "xny")]
    [InlineData(@"/a\\/x/", @"a\", "x")]
    [InlineData(@"/\./,/", "a.b", "a,b")]
    public void Backslash_escapes(string args, string input, string expected)
    {
        Assert.Equal(expected, Apply(args, input));
    }

    [Theory]
    [InlineData("/a/b", "aa", "ba")]
    [InlineData("/a", "aba", "ba")]
    [InlineData("/a/", "aba", "ba")]
    [InlineData("/a//g", "aba", "b")]
    public void Trailing_delimiters_can_be_left_out(string args, string input, string expected)
    {
        Assert.Equal(expected, Apply(args, input));
    }

    [Theory]
    [InlineData(@"/(\w+)_(\w+)/$2_$1/", "pt_a", "a_pt")]
    [InlineData(@"/(\w+)_(\w+)/\2_\1/", "pt_a", "a_pt")]
    [InlineData(@"/^/x_/", "pt", "x_pt")]
    [InlineData(@"/.*/[$0]/", "pt", "[pt]")]
    [InlineData(@"/a/\\/", "a", @"\")]
    [InlineData(@"/a/$$/", "a", "$")]
    [InlineData(@"/a/&/", "a", "&")]
    public void Replacement_takes_dotnet_and_vim_groups(string args, string input, string expected)
    {
        Assert.Equal(expected, Apply(args, input));
    }

    [Fact]
    public void Korean_nicknames()
    {
        Assert.Equal("거리2", Apply("/길이/거리/", "길이2"));
    }

    [Fact]
    public void Errors()
    {
        Assert.Equal(Substitution.Usage, Error(""));
        Assert.Contains("구분자", Error("a/b/"));
        Assert.Contains("구분자", Error(" /a/b/"));
        Assert.Contains("구분자", Error(@"\a\b\"));
        Assert.Contains("비었", Error("//b/"));
        Assert.Contains("플래그", Error("/a/b/x"));
        Assert.Contains("패턴 오류", Error("/(/b/"));
    }
}
