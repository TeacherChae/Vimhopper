using System;
using System.IO;

namespace Vimhopper.Core;

/// <summary>
/// 설정 명령(<c>:map</c> 계열, <c>:unmap</c> 계열, <c>:set</c>, <c>:source</c>)을 <see cref="ExCommands"/>에 등록한다 (0023).
/// 설정 파일도 줄마다 <see cref="ExCommands.Execute"/>로 실행하므로 파일에는 어떤 <c>:</c> 명령이든 쓸 수 있다.
/// </summary>
/// <remarks>
/// 짧은 이름은 vim과 같다 (<c>:no</c>, <c>:nn</c>, <c>:se</c>, <c>:so</c> …). <c>:un</c>은 <c>:unlock</c>과 <c>:unmap</c> 사이에서
/// 모호하게 두고 (<c>:unl</c> / <c>:unm</c>), <c>:s</c>는 정확히 같은 이름인 <c>:substitute</c>가 먼저다.
/// </remarks>
public static class ConfigCommands
{
    /// <summary>설정 파일이 다른 파일을 <c>:source</c>로 읽을 수 있는 깊이 (자기 자신을 읽는 고리를 끊음).</summary>
    public const int MaxSourceDepth = 16;

    // 이름, 가장 짧은 앞부분 길이
    static readonly (string Name, int MinLength)[] MapNames =
    {
        ("map", 3), ("noremap", 2), ("nmap", 2), ("nnoremap", 2), ("vmap", 2), ("vnoremap", 2),
    };

    static readonly (string Name, int MinLength)[] UnmapNames =
    {
        ("unmap", 3), ("nunmap", 3), ("vunmap", 2),
    };

    /// <summary>
    /// 설정 명령을 등록한다. <paramref name="rcPath"/>는 인자 없는 <c>:source</c>가 읽을 파일이고,
    /// 상대 경로는 그 파일이 있는 폴더를 기준으로 한다.
    /// </summary>
    public static void Register(ExCommands ex, UserConfig config, string rcPath)
    {
        foreach (var (name, min) in MapNames)
            ex.Register(name, min, call => Reject(call) ?? FromTuple(config.Map(call.Name, call.Args)), MapDescription(name));
        foreach (var (name, min) in UnmapNames)
            ex.Register(name, min, call => Reject(call) ?? FromTuple(config.Unmap(call.Name, call.Args)), UnmapDescription(name));
        ex.Register("set", 2, call => Reject(call) ?? FromTuple(config.Set(call.Args)), "옵션 보기·바꾸기 :set name=value");

        var depth = 0;
        ex.Register("source", 2, call =>
        {
            if (Reject(call) is { } error) return error;
            var path = ResolvePath(call.Args, rcPath);
            if (!File.Exists(path))
                return ExResult.Error($"파일이 없습니다: {path}");
            if (depth >= MaxSourceDepth)
                return ExResult.Error($":source가 너무 깊습니다 ({MaxSourceDepth}): {path}");
            depth++;
            try
            {
                return Source(ex, path);
            }
            finally
            {
                depth--;
            }
        }, "설정 파일 읽기 (인자 없으면 vimhopperrc)");
    }

    /// <summary>
    /// 파일을 줄마다 <paramref name="ex"/>로 실행한다. 오류는 첫 번째 것과 개수를 묶어 하나의 결과로.
    /// 파일이 없으면 아무것도 하지 않고 성공 (Vim 모드에 처음 들어갈 때 읽는 기본 설정 파일).
    /// </summary>
    public static ExResult Source(ExCommands ex, string path)
    {
        var errors = UserConfig.LoadFile(path, line => ToTuple(ex.Execute(line)));
        var file = Path.GetFileName(path);
        if (errors.Count == 0)
            return ExResult.Success(File.Exists(path) ? $"읽음: {file}" : null);
        return ExResult.Error($"{file} {errors[0]}" + (errors.Count > 1 ? $" (오류 {errors.Count}개)" : ""));
    }

    /// <summary>
    /// <c>:source</c> 인자를 경로로. 비면 <paramref name="rcPath"/>, 따옴표를 벗기고 <c>%APPDATA%</c> 같은 환경 변수와 <c>~</c>를 펼친다.
    /// 상대 경로는 <paramref name="rcPath"/>의 폴더 기준 (Rhino의 작업 폴더는 정해져 있지 않으므로).
    /// </summary>
    public static string ResolvePath(string args, string rcPath)
    {
        var text = args.Trim();
        if (text.Length >= 2 && text[0] == '"' && text[text.Length - 1] == '"')
            text = text.Substring(1, text.Length - 2);
        if (text.Length == 0)
            return rcPath;
        text = Environment.ExpandEnvironmentVariables(text);
        if (text == "~" || text.StartsWith("~/", StringComparison.Ordinal) || text.StartsWith("~\\", StringComparison.Ordinal))
            text = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + text.Substring(1);
        if (Path.IsPathRooted(text))
            return text;
        var folder = Path.GetDirectoryName(rcPath);
        return string.IsNullOrEmpty(folder) ? text : Path.Combine(folder, text);
    }

    // 설정 명령은 범위와 !를 받지 않는다 (vim의 :map!은 입력 모드용이라 없음)
    static ExResult? Reject(ExCall call)
    {
        if (call.Range != null) return ExResult.Error($":{call.Name}은 범위를 받지 않습니다");
        if (call.Bang) return ExResult.Error($":{call.Name}!은 없습니다");
        return null;
    }

    static ExResult FromTuple((bool Ok, string? Message) result) =>
        result.Ok ? ExResult.Success(result.Message) : ExResult.Error(result.Message ?? "실패");

    static (bool Ok, string? Message) ToTuple(ExResult result) => (result.Ok, result.Message);

    static string MapDescription(string name) => name switch
    {
        "map" => "매핑 (일반·비주얼)",
        "noremap" => "다시 펼치지 않는 매핑 (일반·비주얼)",
        "nmap" => "일반 모드 매핑",
        "nnoremap" => "일반 모드, 다시 펼치지 않는 매핑",
        "vmap" => "비주얼 모드 매핑",
        _ => "비주얼 모드, 다시 펼치지 않는 매핑",
    };

    static string UnmapDescription(string name) => name switch
    {
        "unmap" => "매핑 지우기 (일반·비주얼)",
        "nunmap" => "일반 모드 매핑 지우기",
        _ => "비주얼 모드 매핑 지우기",
    };
}
