using System.Collections.Generic;
using System.Linq;
using Vimhopper.Core;
using Xunit;

namespace Vimhopper.Core.Tests;

public class KeyRemapperTests
{
    // 매핑 앞단 + 실제 파서. 파서에 넘어간 키와 나온 명령을 모은다
    sealed class Rig
    {
        public readonly UserConfig Config = new();
        public readonly KeyParser Parser = new(_ => 3);
        public readonly KeyRemapper Remapper;
        public readonly List<char> Sent = new();
        public readonly List<Command> Commands = new();
        public bool InputOpen;

        public Rig(params string[] lines)
        {
            foreach (var line in lines)
                Assert.True(Config.Run(line).Ok, line);
            Remapper = new KeyRemapper(Config, Mode, key =>
            {
                Sent.Add(key);
                if (InputOpen) return;
                foreach (var c in Parser.Feed(key))
                {
                    Commands.Add(c);
                    // / 를 입력줄이 여는 것처럼
                    if (c is SearchPromptCommand) InputOpen = true;
                }
            });
        }

        MapMode? Mode() => InputOpen || !Parser.AcceptsMappings ? null : Parser.Visual ? MapMode.Visual : MapMode.Normal;

        public string? Feed(string keys)
        {
            string? error = null;
            foreach (var k in keys)
                error = Remapper.Feed(k) ?? error;
            return error;
        }

        public string SentText => new(Sent.ToArray());
    }

    [Fact]
    public void Unmapped_keys_pass_through()
    {
        var rig = new Rig();
        rig.Feed("dd");
        Assert.Equal("dd", rig.SentText);
        Assert.IsType<DeleteCommand>(Assert.Single(rig.Commands));
    }

    [Fact]
    public void Full_match_expands()
    {
        var rig = new Rig("nnoremap Q dd");
        rig.Feed("Q");
        Assert.Equal("dd", rig.SentText);
        Assert.IsType<DeleteCommand>(Assert.Single(rig.Commands));
        Assert.False(rig.Remapper.Waiting);
    }

    [Fact]
    public void Prefix_waits_then_expands()
    {
        var rig = new Rig("nnoremap <leader>w yy");
        rig.Feed(" ");
        Assert.True(rig.Remapper.Waiting);
        Assert.Equal("<Space>", rig.Remapper.Pending);
        Assert.Empty(rig.Sent);
        rig.Feed("w");
        Assert.Equal("yy", rig.SentText);
        Assert.False(rig.Remapper.Waiting);
    }

    [Fact]
    public void Non_match_flushes_buffered_keys()
    {
        var rig = new Rig("nnoremap <leader>w yy");
        rig.Feed("  ");
        Assert.Equal("  ", rig.SentText);
        Assert.IsType<InsertComponentCommand>(rig.Commands.First());
    }

    [Fact]
    public void Timeout_flushes_or_takes_shorter_match()
    {
        var rig = new Rig("nnoremap <leader>w yy");
        rig.Feed(" ");
        rig.Remapper.Timeout();
        Assert.Equal(" ", rig.SentText);
        Assert.False(rig.Remapper.Waiting);

        var both = new Rig("nnoremap Q dd", "nnoremap QQ yy");
        both.Feed("Q");
        Assert.True(both.Remapper.Waiting);
        both.Remapper.Timeout();
        Assert.Equal("dd", both.SentText);
        both.Sent.Clear();
        both.Feed("QQ");
        Assert.Equal("yy", both.SentText);
    }

    [Fact]
    public void Timeout_without_waiting_does_nothing()
    {
        var rig = new Rig();
        Assert.Null(rig.Remapper.Timeout());
        Assert.Empty(rig.Sent);
    }

    [Fact]
    public void Noremap_rhs_is_not_remapped()
    {
        var rig = new Rig("nnoremap d y", "nnoremap Q dd");
        rig.Feed("Q");
        Assert.Equal("dd", rig.SentText);
        Assert.IsType<DeleteCommand>(Assert.Single(rig.Commands));
    }

    [Fact]
    public void Map_rhs_is_remapped()
    {
        var rig = new Rig("nmap Q W", "nnoremap W yy");
        rig.Feed("Q");
        Assert.Equal("yy", rig.SentText);
    }

    [Fact]
    public void Rhs_starting_with_lhs_does_not_remap_first_key()
    {
        var rig = new Rig("nmap j jl");
        rig.Feed("j");
        Assert.Equal("jl", rig.SentText);
    }

    [Fact]
    public void Recursive_mapping_errors_and_drops()
    {
        var rig = new Rig("nmap a b", "nmap b a");
        var error = rig.Feed("a");
        Assert.Contains("재귀", error);
        Assert.Empty(rig.Sent);
        Assert.False(rig.Remapper.Waiting);
        // 그 뒤로도 보통 키는 그대로
        rig.Feed("h");
        Assert.Equal("h", rig.SentText);
    }

    [Fact]
    public void Visual_mappings_apply_only_in_visual()
    {
        var rig = new Rig("vnoremap Q d", "nnoremap Q yy");
        rig.Feed("vQ");
        Assert.Equal("vd", rig.SentText);
        rig.Sent.Clear();
        rig.Feed("Q");
        Assert.Equal("yy", rig.SentText);
    }

    [Fact]
    public void Mode_is_reevaluated_inside_rhs()
    {
        // rhs의 v 뒤 Q는 비주얼 매핑을 따른다
        var rig = new Rig("nmap S vQ", "vnoremap Q d");
        rig.Feed("S");
        Assert.Equal("vd", rig.SentText);
    }

    [Fact]
    public void No_mapping_while_command_is_pending()
    {
        // d 다음의 Q는 연산자 범위라 매핑하지 않는다
        var rig = new Rig("nnoremap Q yy");
        rig.Feed("dQ");
        Assert.Equal("dQ", rig.SentText);
    }

    [Fact]
    public void Count_before_mapping_is_kept()
    {
        var rig = new Rig("nnoremap Q l");
        rig.Feed("3Q");
        Assert.Equal("3l", rig.SentText);
        var move = Assert.IsType<MoveCommand>(Assert.Single(rig.Commands));
        Assert.Equal(3, move.Count);
    }

    [Fact]
    public void No_mapping_while_input_line_is_open()
    {
        // rhs가 입력줄을 열면 나머지 키는 매핑하지 않고 입력줄로 간다
        var rig = new Rig("nnoremap Q /QQ<CR>", "nnoremap <CR> dd");
        rig.Feed("Q");
        Assert.Equal("/QQ\r", rig.SentText);
        rig.Feed("Q");
        Assert.Equal("/QQ\rQ", rig.SentText);
    }

    [Fact]
    public void Accepts_only_mapping_prefixes()
    {
        var rig = new Rig("nnoremap ;w yy");
        Assert.True(rig.Remapper.Accepts(';'));
        Assert.False(rig.Remapper.Accepts(','));
        rig.Feed(";");
        Assert.True(rig.Remapper.Accepts('w'));
        Assert.False(rig.Remapper.Accepts('x'));
        rig.Feed("v");
        Assert.False(rig.Remapper.Accepts(';'));
    }

    [Fact]
    public void Reset_drops_buffered_keys()
    {
        var rig = new Rig("nnoremap <leader>w yy");
        rig.Feed(" ");
        rig.Remapper.Reset();
        Assert.False(rig.Remapper.Waiting);
        Assert.Equal("", rig.Remapper.Pending);
        rig.Feed("w");
        Assert.Equal("w", rig.SentText);
    }

    [Fact]
    public void Hints_merge_mappings_with_built_in()
    {
        var rig = new Rig("nnoremap <leader>w yy", "nnoremap <leader><Space> dd");
        rig.Feed(" ");
        var builtIn = rig.Parser.HintsAfter(rig.Remapper.Buffered);
        Assert.Equal(new KeyParser(_ => 3).HintsAfter(" ").Select(h => h.Keys), builtIn.Select(h => h.Keys));
        var merged = KeyParser.WithMappings(builtIn, rig.Config.MappingHints(MapMode.Normal, rig.Remapper.Buffered));
        Assert.Equal(new[] { "<Space>", "w" }, merged.Select(h => h.Keys));
        Assert.Equal("→ dd", merged[0].Description);
    }
}
