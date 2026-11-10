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
            var ex = new ExCommands();
            ConfigCommands.Register(ex, Config, "vimhopperrc");
            foreach (var line in lines)
                Assert.True(ex.Execute(line).Ok, line);
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

        MapMode? Mode(char key) => InputOpen || !Parser.MapsKey(key) ? null : Parser.Visual ? MapMode.Visual : MapMode.Normal;

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
    public void Digit_inside_count_is_not_mapped()
    {
        // vim처럼 반복 횟수 중의 0은 매핑하지 않는다. 맨 앞의 0은 매핑된다
        var rig = new Rig("nmap 0 gg");
        rig.Feed("10j");
        Assert.Equal("10j", rig.SentText);
        var move = Assert.IsType<MoveCommand>(Assert.Single(rig.Commands));
        Assert.Equal(10, move.Count);
        rig.Sent.Clear();
        rig.Feed("0");
        Assert.Equal("gg", rig.SentText);
    }

    [Fact]
    public void Data_operators_after_index_beat_mappings()
    {
        var rig = new Rig("nnoremap s <Space>w", "nnoremap Q yy");
        rig.Feed("o1s");
        Assert.Equal("o1s", rig.SentText);
        Assert.Contains(rig.Commands, c => c is ParamOpCommand { Op: ParamOp.Simplify });
        // 데이터 연산자가 아닌 키는 o{n} 직후에도 매핑된다
        rig.Sent.Clear();
        rig.Feed("Q");
        Assert.Equal("yy", rig.SentText);
    }

    [Fact]
    public void Hints_after_count_keep_count_hints_and_mappings()
    {
        var rig = new Rig("nnoremap <leader>w yy");
        rig.Feed("2 ");
        Assert.True(rig.Remapper.Waiting);
        var builtIn = rig.Parser.HintsAfter(rig.Remapper.Buffered);
        Assert.Equal(rig.Parser.Hints.Select(h => h.Keys), builtIn.Select(h => h.Keys));
        Assert.Contains(builtIn, h => h.Keys == "j");
        var merged = KeyParser.WithMappings(builtIn, rig.Config.MappingHints(MapMode.Normal, rig.Remapper.Buffered));
        Assert.Equal("w", merged[0].Keys);
        Assert.Contains(merged, h => h.Keys == "j");
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

    // nmap a b, nmap b a, nmap bb x: a → b → a → b에서 bb를 기다리고, 시간 초과마다 다시 돈다
    [Fact]
    public void Cycle_that_waits_for_longer_lhs_stops_across_timeouts()
    {
        var rig = new Rig("nmap a b", "nmap b a", "nmap bb x");
        Assert.Null(rig.Feed("a"));
        Assert.True(rig.Remapper.Waiting);
        string? error = null;
        var timeouts = 0;
        while (rig.Remapper.Waiting && timeouts < 1000)
        {
            error = rig.Remapper.Timeout() ?? error;
            timeouts++;
        }
        Assert.False(rig.Remapper.Waiting);
        // 첫 시간 초과에서 b를 다시 기다리게 되므로 바로 고리
        Assert.Equal(1, timeouts);
        Assert.Contains("매핑이 너무 깊습니다", error);
        Assert.Empty(rig.Sent);
        Assert.Equal("", rig.Remapper.Buffered);

        // 버린 뒤에는 다시 처음부터 센다
        rig.Feed("j");
        Assert.Equal("j", rig.SentText);
    }

    // timeoutlen=0인 Engine처럼 기다릴 때마다 바로 Timeout을 부르는 고리도 끝난다
    [Fact]
    public void Cycle_with_zero_timeout_ends_without_recursion()
    {
        var rig = new Rig("nmap a b", "nmap b a", "nmap bb x", "set timeoutlen=0");
        Assert.Equal(0, rig.Config.TimeoutLen);
        var error = rig.Feed("a");
        var steps = 0;
        while (rig.Remapper.Waiting && steps++ < 1000)
            error = rig.Remapper.Timeout() ?? error;
        Assert.False(rig.Remapper.Waiting);
        Assert.Contains("매핑이 너무 깊습니다", error);
        Assert.Empty(rig.Sent);
    }

    // 키를 넘기면 센 수를 지우므로 시간 초과를 여러 번 거쳐도 정상 매핑은 막히지 않는다
    [Fact]
    public void Emitted_key_resets_expansion_count()
    {
        var rig = new Rig("nmap Q jj", "nmap QQ kk");
        for (var i = 0; i < KeyRemapper.MaxDepth * 2; i++)
        {
            Assert.Null(rig.Feed("Q"));
            Assert.Null(rig.Remapper.Timeout());
        }
        Assert.Equal(new string('j', KeyRemapper.MaxDepth * 4), rig.SentText);
    }

    // 기다리는 키가 매번 달라도 키를 넘기지 않고 시간 초과가 3번 지나면 멈춘다 (timeoutlen × 3 안에 오류)
    [Fact]
    public void Chain_without_emitting_stops_after_three_timeouts()
    {
        var rig = new Rig("nmap a b", "nmap bx 1", "nmap b c", "nmap cx 1", "nmap c d", "nmap dx 1",
            "nmap d e", "nmap ex 1", "nmap e f", "nmap fx 1", "nmap f g");
        rig.Feed("a");
        string? error = null;
        var timeouts = 0;
        while (rig.Remapper.Waiting && timeouts < 100)
        {
            error = rig.Remapper.Timeout();
            timeouts++;
        }
        Assert.Equal(KeyRemapper.MaxTimeouts, timeouts);
        Assert.Contains("매핑이 너무 깊습니다", error);
        Assert.Empty(rig.Sent);
    }

    // 시간 초과를 두 번 지나 키를 넘기면 정상 (넘기면 세기를 지움)
    [Fact]
    public void Chain_that_emits_within_limit_works()
    {
        var rig = new Rig("nmap a b", "nmap bx 1", "nmap b c", "nmap cx 1", "nmap c j");
        rig.Feed("a");
        Assert.Null(rig.Remapper.Timeout());
        Assert.True(rig.Remapper.Waiting);
        Assert.Null(rig.Remapper.Timeout());
        Assert.False(rig.Remapper.Waiting);
        Assert.Equal("j", rig.SentText);
        // 다시 해도 같다
        rig.Feed("a");
        rig.Remapper.Timeout();
        rig.Remapper.Timeout();
        Assert.Equal("jj", rig.SentText);
    }
}
