using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Vimhopper.Core;
using Xunit;

namespace Vimhopper.Core.Tests;

/// <summary>설정 명령을 : 명령줄에 등록 (ConfigCommands).</summary>
public class ConfigCommandsTests
{
    // 엔진처럼 내장 명령과 설정 명령을 함께 등록한다. 내장 명령은 받은 호출을 남긴다
    sealed class Rig
    {
        public readonly UserConfig Config = new();
        public readonly ExCommands Ex = new();
        public readonly List<ExCall> Calls = new();

        public Rig(string rcPath = "vimhopperrc")
        {
            foreach (var (name, min) in new[] { ("w", 1), ("recompute", 3), ("lock", 3), ("unlock", 3), ("rh", 2), ("s", 1) })
                Ex.Register(name, min, call => { Calls.Add(call); return ExResult.Success(call.Name); }, name);
            ConfigCommands.Register(Ex, Config, rcPath);
        }
    }

    static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "vimhopper-test-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Set_forwards_and_returns_message()
    {
        var rig = new Rig();
        Assert.True(rig.Ex.Execute("set timeoutlen=500").Ok);
        Assert.Equal(500, rig.Config.TimeoutLen);
        Assert.Equal("timeoutlen=500", rig.Ex.Execute("se timeoutlen?").Message);
        var bad = rig.Ex.Execute("set nosuch");
        Assert.False(bad.Ok);
        Assert.Contains("nosuch", bad.Message);
    }

    [Fact]
    public void Nnoremap_with_leader_and_cmdline_rhs()
    {
        var rig = new Rig();
        Assert.True(rig.Ex.Execute("nnoremap <leader>w :w<CR>").Ok);
        var mapping = rig.Config.Mappings(MapMode.Normal)[" w"];
        Assert.Equal(":w\r", mapping.Rhs);
        Assert.True(mapping.Noremap);
        Assert.False(rig.Config.Mappings(MapMode.Visual).ContainsKey(" w"));
    }

    [Theory]
    [InlineData("map Q dd", true, true, false)]
    [InlineData("no Q dd", true, true, true)]
    [InlineData("nm Q dd", true, false, false)]
    [InlineData("nn Q dd", true, false, true)]
    [InlineData("nno Q dd", true, false, true)]
    [InlineData("vm Q dd", false, true, false)]
    [InlineData("vn Q dd", false, true, true)]
    public void Map_abbreviations_choose_modes(string line, bool normal, bool visual, bool noremap)
    {
        var rig = new Rig();
        Assert.True(rig.Ex.Execute(line).Ok, line);
        Assert.Equal(normal, rig.Config.Mappings(MapMode.Normal).ContainsKey("Q"));
        Assert.Equal(visual, rig.Config.Mappings(MapMode.Visual).ContainsKey("Q"));
        var any = rig.Config.Mappings(normal ? MapMode.Normal : MapMode.Visual)["Q"];
        Assert.Equal(noremap, any.Noremap);
    }

    [Fact]
    public void Map_without_rhs_lists()
    {
        var rig = new Rig();
        rig.Ex.Execute("nnoremap <leader>w :w<CR>");
        var list = rig.Ex.Execute("map");
        Assert.True(list.Ok);
        Assert.Contains("<Space>w", list.Message);
        Assert.Contains(":w<CR>", list.Message);
    }

    [Fact]
    public void Unmap_abbreviations()
    {
        var rig = new Rig();
        rig.Ex.Execute("map Q dd");
        Assert.True(rig.Ex.Execute("nun Q").Ok);
        Assert.False(rig.Config.Mappings(MapMode.Normal).ContainsKey("Q"));
        Assert.True(rig.Ex.Execute("vu Q").Ok);
        Assert.False(rig.Config.Mappings(MapMode.Visual).ContainsKey("Q"));
        rig.Ex.Execute("map Q dd");
        Assert.True(rig.Ex.Execute("unm Q").Ok);
        Assert.Empty(rig.Config.Mappings(MapMode.Normal));
        Assert.False(rig.Ex.Execute("unm Q").Ok);
    }

    [Fact]
    public void Un_stays_ambiguous_and_s_stays_substitute()
    {
        var rig = new Rig();
        var un = rig.Ex.Execute("un Q");
        Assert.False(un.Ok);
        Assert.Contains("unlock", un.Message);
        Assert.Contains("unmap", un.Message);
        Assert.True(rig.Ex.Execute("unl").Ok);
        Assert.Equal("unlock", rig.Calls.Last().Name);

        Assert.True(rig.Ex.Execute("s/a/b/").Ok);
        Assert.Equal("s", rig.Calls.Last().Name);
        Assert.Equal("모든 옵션이 기본값입니다", rig.Ex.Execute("se").Message);
    }

    [Fact]
    public void Config_commands_reject_range_and_bang()
    {
        var rig = new Rig();
        Assert.False(rig.Ex.Execute("%set timeoutlen=1").Ok);
        Assert.False(rig.Ex.Execute("map! Q dd").Ok);
        Assert.Empty(rig.Config.Mappings(MapMode.Normal));
        Assert.Equal(1000, rig.Config.TimeoutLen);
    }

    [Fact]
    public void Source_runs_any_command_and_reports_first_error()
    {
        var dir = TempDir();
        try
        {
            var rc = Path.Combine(dir, "vimhopperrc");
            File.WriteAllLines(rc, new[]
            {
                "\" 주석",
                "set timeoutlen=500",
                "nnoremap <leader>w :w<CR>",
                ":lock",
                "rh _Line",
                "nosuchcommand",
                "set nosuch",
            });
            var rig = new Rig(rc);
            var result = rig.Ex.Execute("source");
            Assert.False(result.Ok);
            Assert.Contains("6행", result.Message);
            Assert.Contains("오류 2개", result.Message);
            Assert.Equal(500, rig.Config.TimeoutLen);
            Assert.True(rig.Config.Mappings(MapMode.Normal).ContainsKey(" w"));
            Assert.Equal(new[] { "lock", "rh" }, rig.Calls.Select(c => c.Name));
            Assert.Equal("_Line", rig.Calls[1].Args);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Source_with_path_relative_to_rc_folder()
    {
        var dir = TempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "extra"), "set scrolloff=7\n");
            var rig = new Rig(Path.Combine(dir, "vimhopperrc"));
            var result = rig.Ex.Execute("so extra");
            Assert.True(result.Ok, result.Message);
            Assert.Equal(7, rig.Config.ScrollOff);
            Assert.True(rig.Ex.Execute($"so \"{Path.Combine(dir, "extra")}\"").Ok);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Source_missing_file_is_error_but_startup_load_is_silent()
    {
        var dir = TempDir();
        try
        {
            var rc = Path.Combine(dir, "vimhopperrc");
            var rig = new Rig(rc);
            Assert.False(rig.Ex.Execute("source").Ok);
            var startup = ConfigCommands.Source(rig.Ex, rc);
            Assert.True(startup.Ok);
            Assert.Null(startup.Message);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Source_stops_self_recursion()
    {
        var dir = TempDir();
        try
        {
            var rc = Path.Combine(dir, "vimhopperrc");
            File.WriteAllLines(rc, new[] { "set scrolloff=3", "source" });
            var rig = new Rig(rc);
            var result = rig.Ex.Execute("source");
            Assert.False(result.Ok);
            Assert.Equal(3, rig.Config.ScrollOff);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Resolve_path_expands_environment()
    {
        Environment.SetEnvironmentVariable("VIMHOPPER_TEST_DIR", @"C:\vh");
        Assert.Equal(@"C:\vh\rc", ConfigCommands.ResolvePath("%VIMHOPPER_TEST_DIR%\\rc", @"D:\x\vimhopperrc"));
        Assert.Equal(@"D:\x\vimhopperrc", ConfigCommands.ResolvePath("  ", @"D:\x\vimhopperrc"));
        Assert.Equal(Path.Combine(@"D:\x", "more"), ConfigCommands.ResolvePath("more", @"D:\x\vimhopperrc"));
    }
}
