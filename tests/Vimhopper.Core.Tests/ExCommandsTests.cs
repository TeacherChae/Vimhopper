using System;
using System.Collections.Generic;
using System.Linq;
using Vimhopper.Core;
using Xunit;

namespace Vimhopper.Core.Tests;

/// <summary>: 명령줄 (ExCommands, 기록, Tab 완성, : 키).</summary>
public class ExCommandsTests
{
    // 등록한 명령을 부르면 받은 호출을 남긴다
    static (ExCommands Commands, List<ExCall> Calls) Registry()
    {
        var commands = new ExCommands();
        var calls = new List<ExCall>();
        ExResult Run(ExCall call)
        {
            calls.Add(call);
            return ExResult.Success(call.Name);
        }
        commands.Register("w", 1, Run, "저장");
        commands.Register("recompute", 3, Run, "다시 계산");
        commands.Register("rh", 2, Run, "Rhino");
        commands.Register("lock", 3, Run, "잠금");
        commands.Register("unlock", 3, Run, "풀기");
        commands.Register("unmap", 3, Run, "매핑 지우기");
        commands.Register("s", 1, Run, "바꾸기");
        commands.Register("set", 2, Run, "옵션");
        return (commands, calls);
    }

    [Theory]
    [InlineData("w", "w")]
    [InlineData("recompute", "recompute")]
    [InlineData("rec", "recompute")]
    [InlineData("reco", "recompute")]
    [InlineData("rh", "rh")]
    [InlineData("s", "s")]
    [InlineData("se", "set")]
    [InlineData("unl", "unlock")]
    [InlineData("unm", "unmap")]
    [InlineData(":::rec", "recompute")]
    [InlineData("  rec", "recompute")]
    public void Names_match_by_unique_prefix(string line, string name)
    {
        var (commands, calls) = Registry();
        var result = commands.Execute(line);
        Assert.True(result.Ok, result.Message);
        Assert.Equal(name, Assert.Single(calls).Name);
    }

    [Fact]
    public void An_ambiguous_prefix_lists_the_candidates()
    {
        var (commands, calls) = Registry();
        var result = commands.Execute("un");
        Assert.False(result.Ok);
        Assert.Contains("unlock", result.Message);
        Assert.Contains("unmap", result.Message);
        Assert.Empty(calls);
    }

    [Theory]
    [InlineData("re")]   // recompute는 3글자부터
    [InlineData("r")]    // rh, recompute
    public void A_prefix_shorter_than_min_length_is_an_error(string line)
    {
        var (commands, calls) = Registry();
        Assert.False(commands.Execute(line).Ok);
        Assert.Empty(calls);
    }

    [Fact]
    public void A_unique_prefix_shorter_than_min_length_is_not_a_command()
    {
        var commands = new ExCommands();
        commands.Register("recompute", 3, _ => ExResult.Success(), "");
        var result = commands.Execute("re");
        Assert.False(result.Ok);
        Assert.Contains("명령이 아닙니다", result.Message);
    }

    [Fact]
    public void Exact_name_wins_over_longer_names()
    {
        var (commands, calls) = Registry();
        commands.Execute("s/a/b/");
        Assert.Equal("s", Assert.Single(calls).Name);
    }

    [Theory]
    [InlineData("xyz")]
    [InlineData("recomputer")]
    public void Unknown_names_are_errors(string line)
    {
        var (commands, calls) = Registry();
        var result = commands.Execute(line);
        Assert.False(result.Ok);
        Assert.Contains("명령이 아닙니다", result.Message);
        Assert.Empty(calls);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(":")]
    public void An_empty_line_does_nothing(string line)
    {
        var (commands, calls) = Registry();
        var result = commands.Execute(line);
        Assert.True(result.Ok);
        Assert.Null(result.Message);
        Assert.Empty(calls);
    }

    [Theory]
    [InlineData("s/a/b/", null, "s", false, "/a/b/")]
    [InlineData("%s/a/b/g", "%", "s", false, "/a/b/g")]
    [InlineData("'<,'>s#a#b#", "'<,'>", "s", false, "#a#b#")]
    [InlineData("% s/a/b/", "%", "s", false, "/a/b/")]
    [InlineData("w!", null, "w", true, "")]
    [InlineData("rh _Line 0,0,0  1,0,0", null, "rh", false, "_Line 0,0,0  1,0,0")]
    [InlineData("rh   _Circle ", null, "rh", false, "_Circle ")]
    [InlineData("%", "%", "", false, "")]
    public void Lines_split_into_range_name_bang_and_args(string line, string? range, string name, bool bang, string args)
    {
        Assert.Equal((range, name, bang, args), ExCommands.Parse(line));
    }

    [Theory]
    [InlineData("1,2s/a/b/")]
    [InlineData("/x")]
    public void Unknown_ranges_are_not_parsed(string line)
    {
        Assert.Null(ExCommands.Parse(line));
        Assert.False(Registry().Commands.Execute(line).Ok);
    }

    [Fact]
    public void The_call_carries_range_bang_and_args()
    {
        var (commands, calls) = Registry();
        commands.Execute("'<,'>s/a/b/gi");
        commands.Execute("rec!");
        Assert.Equal(new ExCall("s", "/a/b/gi", "'<,'>", false), calls[0]);
        Assert.Equal(new ExCall("recompute", "", null, true), calls[1]);
    }

    [Fact]
    public void A_range_alone_is_an_error()
    {
        Assert.False(Registry().Commands.Execute("%").Ok);
    }

    [Fact]
    public void Exceptions_become_errors()
    {
        var commands = new ExCommands();
        commands.Register("boom", 1, _ => throw new InvalidOperationException("터짐"), "");
        var result = commands.Execute("boom");
        Assert.False(result.Ok);
        Assert.Contains("터짐", result.Message);
    }

    [Fact]
    public void Registering_again_replaces_the_command()
    {
        var commands = new ExCommands();
        commands.Register("w", 1, _ => ExResult.Success("old"), "");
        commands.Register("w", 1, _ => ExResult.Success("new"), "");
        Assert.Equal("new", commands.Execute("w").Message);
        Assert.Single(commands.Commands);
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("w1", 1)]
    [InlineData("w", 2)]
    public void Register_checks_name_and_min_length(string name, int minLength)
    {
        Assert.ThrowsAny<ArgumentException>(() => new ExCommands().Register(name, minLength, _ => ExResult.Success(), ""));
    }

    [Fact]
    public void Complete_lists_names_by_prefix_in_order()
    {
        var (commands, _) = Registry();
        Assert.Equal(new[] { "unlock", "unmap" }, commands.Complete("un"));
        Assert.Equal(new[] { "recompute", "rh" }, commands.Complete("r"));
        Assert.Equal(new[] { "s", "set" }, commands.Complete("s"));
        Assert.Empty(commands.Complete("x"));
        Assert.Equal(8, commands.Complete("").Count());
    }

    // ───────────── Tab 완성 ─────────────

    [Fact]
    public void Tab_cycles_candidates_and_back_to_the_original()
    {
        var (commands, _) = Registry();
        var completion = new ExCompletion();
        Assert.Equal("unlock", completion.Next(commands, "un"));
        Assert.Equal("[unlock]  unmap", completion.Info);
        Assert.Equal("unmap", completion.Next(commands, "unlock"));
        Assert.Equal("unlock  [unmap]", completion.Info);
        Assert.Equal("un", completion.Next(commands, "unmap"));
        Assert.Equal("unlock  unmap", completion.Info);
        Assert.Equal("unlock", completion.Next(commands, "un"));
    }

    [Fact]
    public void Shift_tab_cycles_backward()
    {
        var (commands, _) = Registry();
        var completion = new ExCompletion();
        Assert.Equal("unmap", completion.Next(commands, "un", backward: true));
        Assert.Equal("unlock", completion.Next(commands, "unmap", backward: true));
        Assert.Equal("un", completion.Next(commands, "unlock", backward: true));
    }

    [Fact]
    public void A_single_candidate_completes_and_ends()
    {
        var (commands, _) = Registry();
        var completion = new ExCompletion();
        Assert.Equal("recompute", completion.Next(commands, "rec"));
        Assert.False(completion.Active);
        Assert.Equal("", completion.Info);
    }

    [Theory]
    [InlineData("'<,'>s", "'<,'>s")]
    [InlineData("%se", "%set")]
    [InlineData(":lo", ":lock")]
    public void Tab_keeps_the_range_in_front(string line, string completed)
    {
        var (commands, _) = Registry();
        Assert.Equal(completed, new ExCompletion().Next(commands, line));
    }

    [Theory]
    [InlineData("s/a")]
    [InlineData("rh _Line")]
    [InlineData("xyz")]
    public void Tab_does_nothing_in_args_or_without_candidates(string line)
    {
        var (commands, _) = Registry();
        Assert.Null(new ExCompletion().Next(commands, line));
    }

    [Fact]
    public void Reset_starts_a_new_completion()
    {
        var (commands, _) = Registry();
        var completion = new ExCompletion();
        completion.Next(commands, "un");
        completion.Reset();
        Assert.Equal("s", completion.Next(commands, "s"));
    }

    // ───────────── 기록 ─────────────

    [Fact]
    public void History_walks_back_and_forward()
    {
        var history = new CommandHistory();
        history.Add("w");
        history.Add("rec");
        history.Add("s/a/b/");
        Assert.Equal("s/a/b/", history.Older(""));
        Assert.Equal("rec", history.Older("ignored"));
        Assert.Equal("w", history.Older(""));
        Assert.Null(history.Older(""));
        Assert.Equal("rec", history.Newer());
        Assert.Equal("s/a/b/", history.Newer());
        Assert.Equal("", history.Newer());
        Assert.Null(history.Newer());
    }

    [Fact]
    public void History_filters_by_the_typed_prefix()
    {
        var history = new CommandHistory();
        history.Add("s/a/b/");
        history.Add("w");
        history.Add("s/c/d/");
        Assert.Equal("s/c/d/", history.Older("s"));
        Assert.Equal("s/a/b/", history.Older(""));
        Assert.Null(history.Older(""));
        Assert.Equal("s/c/d/", history.Newer());
        Assert.Equal("s", history.Newer());
    }

    [Fact]
    public void History_moves_duplicates_to_the_end_and_skips_blank_lines()
    {
        var history = new CommandHistory();
        history.Add("w");
        history.Add("rec");
        history.Add("w");
        history.Add("  ");
        Assert.Equal(new[] { "rec", "w" }, history.Lines);
    }

    [Fact]
    public void History_keeps_the_last_fifty()
    {
        var history = new CommandHistory();
        for (var i = 0; i < 60; i++)
            history.Add($"rh {i}");
        Assert.Equal(50, history.Lines.Count);
        Assert.Equal("rh 10", history.Lines[0]);
        Assert.Equal("rh 59", history.Lines[^1]);
    }

    [Fact]
    public void History_reset_restarts_from_the_newest()
    {
        var history = new CommandHistory();
        history.Add("w");
        history.Add("rec");
        history.Older("");
        history.Older("");
        history.Reset();
        Assert.Null(history.Newer());
        Assert.Equal("rec", history.Older(""));
    }

    // ───────────── 입력줄 바꾸기 ─────────────

    [Fact]
    public void InputLine_replace_sets_the_whole_text()
    {
        var line = new InputLine();
        line.Open(":", "un");
        line.Composition = "가";
        Assert.Equal(InputLineResult.Changed, line.Replace("unlock"));
        Assert.Equal("unlock", line.Value);
        Assert.Equal("", line.Composition);
        Assert.Equal(InputLineResult.None, line.Replace("unlock"));
        line.Close();
        Assert.Equal(InputLineResult.None, line.Replace("x"));
    }

    // ───────────── : 키 ─────────────

    static Command[] Feed(KeyParser parser, string keys) => keys.SelectMany(parser.Feed).ToArray();

    [Fact]
    public void Colon_opens_the_command_line()
    {
        Assert.Equal(new Command[] { new CmdlinePromptCommand() }, Feed(new KeyParser(_ => 3), ":"));
    }

    [Fact]
    public void Colon_in_visual_mode_passes_the_selection_and_leaves_visual()
    {
        var parser = new KeyParser(_ => 3);
        Assert.Equal(new Command[] { new VisualCommand(true), new CmdlinePromptCommand(Visual: true) }, Feed(parser, "v:"));
        Assert.False(parser.Visual);
        // 다음 키는 일반 모드로 (비주얼 y는 바로 복사, 일반 y는 범위를 기다림)
        Assert.Equal(new Command[] { new OperatorCommand(Operator.Yank, WireRange.Island) }, Feed(parser, "yp"));
    }
}
