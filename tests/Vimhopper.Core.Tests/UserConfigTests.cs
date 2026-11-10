using System.IO;
using System.Linq;
using Vimhopper.Core;
using Xunit;

namespace Vimhopper.Core.Tests;

public class KeyNotationTests
{
    [Theory]
    [InlineData("<Space>w", " w")]
    [InlineData("<leader>w", " w")]
    [InlineData("<LEADER>", " ")]
    [InlineData("<Esc><CR><BS>", "\x1b\r\b")]
    [InlineData("<lt>a>", "<a>")]
    [InlineData("a<b", "a<b")]
    [InlineData("<", "<")]
    [InlineData("<>", "<>")]
    [InlineData("gg", "gg")]
    public void Parses_notation(string text, string keys)
    {
        Assert.True(KeyNotation.TryParse(text, out var parsed, out _));
        Assert.Equal(keys, parsed);
    }

    [Fact]
    public void Ctrl_uses_KeyParser_constants()
    {
        Assert.True(KeyNotation.TryParse("<C-o><c-I><C-h><C-=><C-->", out var keys, out _));
        Assert.Equal(new string(new[] { KeyParser.CtrlO, KeyParser.CtrlI, KeyParser.CtrlH, KeyParser.CtrlPlus, KeyParser.CtrlMinus }), keys);
        Assert.True(KeyNotation.TryParse("<Tab>", out keys, out _));
        Assert.Equal(KeyParser.CtrlI.ToString(), keys);
    }

    [Fact]
    public void Ctrl_without_constant_gets_its_own_key()
    {
        Assert.True(KeyNotation.TryParse("<C-s>", out var keys, out _));
        Assert.Equal(KeyNotation.Ctrl('s').ToString(), keys);
        Assert.NotEqual(KeyNotation.Ctrl('s'), KeyNotation.Ctrl('q'));
        Assert.Equal("<C-s>", KeyNotation.Format(keys));
    }

    [Fact]
    public void Unknown_name_is_an_error()
    {
        Assert.False(KeyNotation.TryParse("<Leadr>w", out _, out var error));
        Assert.Contains("<Leadr>", error);
    }

    [Theory]
    [InlineData("<Space>w<CR>")]
    [InlineData("<C-o><Esc><BS><lt>")]
    [InlineData("dd")]
    public void Format_round_trips(string text)
    {
        KeyNotation.TryParse(text, out var keys, out _);
        Assert.Equal(text, KeyNotation.Format(keys));
    }

    [Fact]
    public void Hint_style_uses_Enter()
    {
        Assert.Equal("<Space><Enter>", KeyNotation.Format(" \r", hintStyle: true));
    }
}

public class UserConfigTests
{
    [Fact]
    public void Map_sets_both_modes_nmap_only_normal()
    {
        var config = new UserConfig();
        Assert.True(config.Map("map", "<leader>a dd").Ok);
        Assert.True(config.Map("nnoremap", "<leader>b yy").Ok);
        Assert.True(config.Map("vmap", "<leader>c y").Ok);

        Assert.True(config.Mappings(MapMode.Normal).ContainsKey(" a"));
        Assert.True(config.Mappings(MapMode.Visual).ContainsKey(" a"));
        Assert.True(config.Mappings(MapMode.Normal)[" b"].Noremap);
        Assert.False(config.Mappings(MapMode.Visual).ContainsKey(" b"));
        Assert.False(config.Mappings(MapMode.Normal).ContainsKey(" c"));
        Assert.Equal("y", config.Mappings(MapMode.Visual)[" c"].Rhs);
    }

    [Fact]
    public void Rhs_keeps_inner_spaces_and_drops_trailing()
    {
        var config = new UserConfig();
        config.Map("nmap", "x  a b  ");
        Assert.Equal("a b", config.Mappings(MapMode.Normal)["x"].Rhs);
    }

    [Fact]
    public void Map_without_args_lists()
    {
        var config = new UserConfig();
        Assert.Equal("매핑이 없습니다", config.Map("map", "").Message);
        config.Map("nnoremap", "<leader>w :w<CR>");
        config.Map("vmap", "Q y");
        var list = config.Map("map", "").Message!;
        Assert.Contains("n  <Space>w  * :w<CR>", list);
        Assert.Contains("v  Q    y", list);
        Assert.DoesNotContain("Q", config.Map("nmap", "").Message);
        // lhs만 주면 그것으로 시작하는 매핑
        Assert.DoesNotContain("Q", config.Map("map", "<leader>").Message);
    }

    [Fact]
    public void Map_bad_notation_fails()
    {
        var config = new UserConfig();
        Assert.False(config.Map("map", "<Foo> x").Ok);
        Assert.False(config.Map("map", "x <Foo>").Ok);
        Assert.Empty(config.Mappings(MapMode.Normal));
    }

    [Fact]
    public void Unmap_removes_by_mode()
    {
        var config = new UserConfig();
        config.Map("map", "Q dd");
        Assert.True(config.Unmap("nunmap", "Q").Ok);
        Assert.False(config.Mappings(MapMode.Normal).ContainsKey("Q"));
        Assert.True(config.Mappings(MapMode.Visual).ContainsKey("Q"));
        Assert.False(config.Unmap("nunmap", "Q").Ok);
        Assert.True(config.Unmap("unmap", "Q").Ok);
        Assert.Empty(config.Mappings(MapMode.Visual));
        Assert.False(config.Unmap("unmap", "").Ok);
    }

    [Fact]
    public void Options_have_current_defaults()
    {
        var config = new UserConfig();
        Assert.Equal(1000, config.TimeoutLen);
        Assert.Equal(400, config.HintDelay);
        Assert.Equal(60, config.ScrollOff);
        Assert.Equal(LabelJump.Alphabet, config.Labels);
        Assert.Equal("모든 옵션이 기본값입니다", config.Set("").Message);
    }

    [Fact]
    public void Set_value_show_and_reset()
    {
        var config = new UserConfig();
        Assert.True(config.Set("timeoutlen=500 hintdelay:200").Ok);
        Assert.Equal(500, config.TimeoutLen);
        Assert.Equal(200, config.HintDelay);
        Assert.Equal("timeoutlen=500", config.Set("timeoutlen?").Message);
        Assert.Equal("timeoutlen=500", config.Set("timeoutlen").Message);
        Assert.Equal("timeoutlen=500  hintdelay=200", config.Set("").Message);
        Assert.True(config.Set("timeoutlen&").Ok);
        Assert.Equal(1000, config.TimeoutLen);
        Assert.Contains("labels=", config.Set("all").Message);
    }

    [Theory]
    [InlineData("timeoutlen=abc")]
    [InlineData("scrolloff=-1")]
    [InlineData("labels=a")]
    [InlineData("labels=aab")]
    [InlineData("labels=aB")]
    [InlineData("nosuch=1")]
    [InlineData("notimeoutlen")]
    public void Set_rejects_bad_values(string args)
    {
        var config = new UserConfig();
        var (ok, message) = config.Set(args);
        Assert.False(ok);
        Assert.False(string.IsNullOrEmpty(message));
        Assert.Equal(1000, config.TimeoutLen);
        Assert.Equal(LabelJump.Alphabet, config.Labels);
    }

    [Fact]
    public void Set_labels()
    {
        var config = new UserConfig();
        Assert.True(config.Set("labels=fjdk").Ok);
        Assert.Equal("fjdk", config.Labels);
    }

    [Fact]
    public void Run_dispatches_config_commands()
    {
        var config = new UserConfig();
        Assert.True(config.Run(":nnoremap Q dd").Ok);
        Assert.True(config.Run("se scrolloff=10").Ok);
        Assert.Equal(10, config.ScrollOff);
        Assert.True(config.Run("nunmap Q").Ok);
        Assert.False(config.Run("w").Ok);
        Assert.Contains("w", config.Run("w").Message);
    }

    [Fact]
    public void Load_skips_comments_and_reports_line_numbers()
    {
        var config = new UserConfig();
        var lines = new[]
        {
            "\" 주석",
            "",
            "   ",
            ":nnoremap <leader>w :w<CR>",
            "set timeoutlen=300",
            "recompute",
            "set nosuch",
        };
        var errors = UserConfig.LoadLines(lines, config.Run);
        Assert.Equal(2, errors.Count);
        Assert.StartsWith("6행: ", errors[0]);
        Assert.StartsWith("7행: ", errors[1]);
        Assert.Equal(300, config.TimeoutLen);
        Assert.True(config.Mappings(MapMode.Normal).ContainsKey(" w"));
    }

    [Fact]
    public void Load_passes_line_without_colon()
    {
        var seen = new System.Collections.Generic.List<string>();
        UserConfig.LoadLines(new[] { "  :: set x" }, l => { seen.Add(l); return (true, null); });
        Assert.Equal(new[] { "set x" }, seen);
    }

    [Fact]
    public void Load_missing_file_is_silent()
    {
        var errors = UserConfig.LoadFile(Path.Combine(Path.GetTempPath(), "vimhopper-no-such-rc-" + System.Guid.NewGuid()), _ => (false, "x"));
        Assert.Empty(errors);
    }

    [Fact]
    public void Load_file_reads_utf8()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "\" 한글 주석\nnmap Q dd\n", System.Text.Encoding.UTF8);
            var config = new UserConfig();
            Assert.Empty(UserConfig.LoadFile(path, config.Run));
            Assert.True(config.Mappings(MapMode.Normal).ContainsKey("Q"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Mapping_hints_list_rest_of_lhs_and_rhs()
    {
        var config = new UserConfig();
        config.Map("nnoremap", "<leader>w :w<CR>");
        config.Map("nnoremap", "<leader>f<C-o> yy");
        config.Map("nnoremap", "<leader> dd");
        var hints = config.MappingHints(MapMode.Normal, " ");
        Assert.Equal(new[] { "(기다림)", "f<C-o>", "w" }, hints.Select(h => h.Keys));
        Assert.Equal("→ :w<CR>", hints[2].Description);
        Assert.Empty(config.MappingHints(MapMode.Visual, " "));
    }

    [Fact]
    public void Labels_option_feeds_label_jump()
    {
        Assert.Equal(new[] { "f", "j", "k" }, LabelJump.Labels(3, "fjk"));
        Assert.Equal(new[] { "f", "j", "kf", "kj" }, LabelJump.Labels(4, "fjk"));
        Assert.Equal(9, LabelJump.Labels(100, "fjk").Count);
    }
}
