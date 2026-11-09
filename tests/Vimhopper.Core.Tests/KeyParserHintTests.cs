using System.Collections.Generic;
using System.Linq;
using Vimhopper.Core;
using Xunit;

namespace Vimhopper.Core.Tests;

/// <summary>힌트가 파서 문법과 어긋나지 않는지: 힌트에 있는 키는 받아들여지고, 받아들여지는 키는 힌트에 있다.</summary>
public class KeyParserHintTests
{
    // 파라미터가 11개 이상이어야 o1처럼 숫자를 기다리는 상태를 만들 수 있다
    static KeyParser Parser() => new(_ => 15);

    static Command[] Feed(KeyParser parser, string keys) =>
        keys.SelectMany(parser.Feed).ToArray();

    // 힌트 표기를 실제 키로. {n}은 1 (어느 상태에서나 숫자를 시작할 수 있는 값)
    static string Expand(string keys) => keys
        .Replace("{n}", "1")
        .Replace("<Enter>", KeyParser.Enter.ToString())
        .Replace("<Space>", KeyParser.Space.ToString())
        .Replace("<BS>", KeyParser.Backspace.ToString());

    // 입력 중인 상태를 만드는 키. v로 시작하면 비주얼 모드
    public static IEnumerable<object[]> PendingStates() => new[]
    {
        "g", "vg", "2", "v2", "2g",
        "y", "=", "d", "gy", "gc", "2y", "3gc",
        "y2", "d3", "gc2",
        " ", "v ",
        "o", "i", "I", "gi", "go", "di", "do", "o1", "i1", "gi1",
    }.Select(s => new object[] { s });

    static KeyParser At(string setup)
    {
        var parser = Parser();
        Assert.DoesNotContain(Feed(parser, setup), c => c is ErrorCommand);
        return parser;
    }

    [Theory]
    [MemberData(nameof(PendingStates))]
    public void Pending_states_have_hints(string setup)
    {
        Assert.NotEmpty(At(setup).Hints);
    }

    [Theory]
    [MemberData(nameof(PendingStates))]
    public void Every_hinted_key_is_accepted(string setup)
    {
        foreach (var hint in At(setup).Hints)
        {
            var parser = At(setup);
            var output = Feed(parser, Expand(hint.Keys));
            Assert.False(output.Any(c => c is ErrorCommand), $"'{setup}' 다음 힌트 '{hint.Keys}'가 오류");
        }
    }

    // 다음 키가 고정된 상태 (g, 숫자, 연산자, 리더): 받아들여지는 글자는 모두 힌트에 있어야 한다.
    // o{n} 직후나 숫자를 기다리는 인덱스는 다른 키도 다음 명령으로 받아들이므로 빠진다.
    [Theory]
    [InlineData("g")]
    [InlineData("vg")]
    [InlineData("2")]
    [InlineData("v2")]
    [InlineData("y")]
    [InlineData("=")]
    [InlineData("d")]
    [InlineData("gy")]
    [InlineData("gc")]
    [InlineData("y2")]
    [InlineData(" ")]
    [InlineData("v ")]
    public void Every_accepted_key_is_hinted(string setup)
    {
        var firstKeys = At(setup).Hints
            .Select(h => Expand(h.Keys.Replace("{n}", ""))[0])
            .ToHashSet();
        const string candidates = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ=- \r";
        foreach (var key in candidates)
        {
            var output = Feed(At(setup), key.ToString());
            if (output.Any(c => c is ErrorCommand)) continue;
            Assert.True(firstKeys.Contains(key), $"'{setup}' 다음 '{key}'는 받아들여지지만 힌트에 없음");
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("v")]
    [InlineData("h")]
    [InlineData("yy")]
    [InlineData("o0h")]
    [InlineData("g\x1b")]
    // o{n} / i{n} 직후는 평소에 머무는 상태라 힌트를 띄우지 않는다
    [InlineData("o0")]
    [InlineData("i0")]
    [InlineData("I0")]
    [InlineData("o0f")]
    public void Idle_has_no_hints(string keys)
    {
        Assert.Empty(At(keys).Hints);
    }

    [Fact]
    public void Range_hints_use_the_operator_count()
    {
        var hints = At("2y").Hints;
        Assert.Equal("상류 2단계", hints.Single(h => h.Keys == "h").Description);
        Assert.Equal("상류 2×n단계", hints.Single(h => h.Keys == "{n}h").Description);
        Assert.Equal("상류 한 단계", At("y").Hints.Single(h => h.Keys == "h").Description);
        // 2y3 다음 h는 6단계
        Assert.Equal("상류 6단계", At("2y3").Hints.Single(h => h.Keys == "h").Description);
    }

    [Fact]
    public void Index_hints_are_empty_when_the_object_has_no_params()
    {
        var count = 3;
        var parser = new KeyParser(_ => count);
        parser.Feed('o');
        count = 0;
        Assert.Empty(parser.Hints);
    }

    [Fact]
    public void Index_hint_with_hundreds_of_params()
    {
        var parser = new KeyParser(_ => 150);
        Feed(parser, "o1");
        Assert.Equal("다음 자리 (10..19, 더 이어짐)", parser.Hints[0].Description);
        Feed(parser, "4");
        Assert.Equal("다음 자리 (140..149)", parser.Hints[0].Description);
    }

    [Fact]
    public void Operator_hints_start_with_the_doubled_key()
    {
        Assert.Equal("y", At("y").Hints[0].Keys);
        Assert.Equal("=", At("=").Hints[0].Keys);
        Assert.Equal("d", At("d").Hints[0].Keys);
        Assert.Equal("y", At("gy").Hints[0].Keys);
        Assert.Equal("c", At("gc").Hints[0].Keys);
    }

    [Fact]
    public void Only_delete_hints_disconnect()
    {
        Assert.Contains(At("d").Hints, h => h.Keys == "i{n}");
        Assert.DoesNotContain(At("y").Hints, h => h.Keys == "i{n}");
    }

    [Fact]
    public void Index_hint_shows_the_valid_range()
    {
        Assert.Contains("0..14", At("o").Hints.Single().Description);
        Assert.Contains("10..14", At("o1").Hints[0].Description);
    }

    [Fact]
    public void Visual_count_hints_only_motions()
    {
        Assert.Equal(new[] { "h", "j", "k", "l" }, At("v2").Hints.Select(h => h.Keys));
    }
}
