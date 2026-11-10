using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Vimhopper.Core;

/// <summary>매핑이 걸리는 모드.</summary>
public enum MapMode { Normal, Visual }

/// <summary>사용자 매핑 하나. <see cref="Lhs"/> / <see cref="Rhs"/>는 키 문자열, <see cref="RhsText"/>는 적은 그대로의 표기.</summary>
public sealed record KeyMapping(MapMode Mode, string Lhs, string Rhs, bool Noremap, string RhsText);

/// <summary>
/// 사용자 매핑(<c>:map</c> 계열)과 옵션(<c>:set</c>), 설정 파일(<c>vimhopperrc</c>) 읽기 (<see href="../../docs/adr/0023-cmdline-config.md">0023</see>).
/// </summary>
/// <remarks>
/// 명령줄과 따로 컴파일되도록 문자열을 받고 <c>(Ok, Message)</c>를 돌려준다. 명령줄 등록은 코디네이터가 묶는다.
/// </remarks>
public sealed class UserConfig
{
    /// <summary>매핑 명령 이름. <c>map</c> / <c>noremap</c>은 일반·비주얼 모두.</summary>
    public static readonly IReadOnlyList<string> MapCommands = new[] { "map", "noremap", "nmap", "nnoremap", "vmap", "vnoremap" };

    /// <summary>매핑 지우기 명령 이름.</summary>
    public static readonly IReadOnlyList<string> UnmapCommands = new[] { "unmap", "nunmap", "vunmap" };

    readonly Dictionary<string, KeyMapping> _normal = new(StringComparer.Ordinal);
    readonly Dictionary<string, KeyMapping> _visual = new(StringComparer.Ordinal);
    readonly Dictionary<string, Option> _options;

    public UserConfig()
    {
        _options = new Option[]
        {
            Option.Number("timeoutlen", 1000, "매핑 키를 기다리는 시간 (ms)"),
            Option.Number("hintdelay", 400, "다음 키 힌트를 띄우기까지 (ms)"),
            Option.Number("scrolloff", 60, "커서 객체와 화면 가장자리 사이 여백 (화면 px)"),
            new Option("labels", LabelJump.Alphabet, "라벨 점프 글자", ValidateLabels),
        }.ToDictionary(o => o.Name, StringComparer.Ordinal);
    }

    // ───────────────────────── 옵션 ─────────────────────────

    /// <summary><c>timeoutlen</c>: 매핑의 앞부분을 친 뒤 다음 키를 기다리는 시간 (ms).</summary>
    public int TimeoutLen => _options["timeoutlen"].Int;

    /// <summary><c>hintdelay</c>: 입력 중인 명령에서 다음 키 힌트를 띄우기까지 (ms).</summary>
    public int HintDelay => _options["hintdelay"].Int;

    /// <summary><c>scrolloff</c>: 커서 객체를 화면 안에 둘 때 가장자리와 띄울 여백 (화면 px).</summary>
    public int ScrollOff => _options["scrolloff"].Int;

    /// <summary><c>labels</c>: 라벨 점프 글자 (앞쪽이 가까운 객체에).</summary>
    public string Labels => _options["labels"].Value;

    /// <summary>옵션 이름들 (완성용).</summary>
    public IEnumerable<string> OptionNames => _options.Keys;

    /// <summary>
    /// <c>:set</c>. 인자를 공백으로 나눠 차례로: <c>name=value</c>(또는 <c>name:value</c>) 바꾸기, <c>name</c> / <c>name?</c> 보이기,
    /// <c>name&amp;</c> 기본값으로, <c>noname</c>은 불 옵션용(지금은 없음). 인자가 없으면 기본값과 다른 옵션, <c>all</c>은 모두.
    /// 하나가 틀리면 그 앞까지만 적용하고 멈춘다 (vim과 같음).
    /// </summary>
    public (bool Ok, string? Message) Set(string args)
    {
        var tokens = args.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            var changed = _options.Values.Where(o => o.Value != o.Default).ToList();
            return (true, changed.Count == 0 ? "모든 옵션이 기본값입니다" : string.Join("  ", changed.Select(o => o.Show())));
        }
        if (tokens.Length == 1 && tokens[0] == "all")
            return (true, string.Join("  ", _options.Values.Select(o => o.Show())));

        var shown = new List<string>();
        foreach (var token in tokens)
        {
            var eq = token.IndexOfAny(new[] { '=', ':' });
            var name = eq >= 0 ? token.Substring(0, eq) : token.TrimEnd('?', '&');
            if (!_options.TryGetValue(name, out var option))
            {
                if (name.StartsWith("no", StringComparison.Ordinal) && _options.ContainsKey(name.Substring(2)))
                    return (false, Join(shown, $"불 옵션이 아닙니다: {name.Substring(2)}"));
                return (false, Join(shown, $"알 수 없는 옵션: {name}"));
            }
            if (eq >= 0)
            {
                var value = token.Substring(eq + 1);
                if (option.Validate(value) is { } error)
                    return (false, Join(shown, $"{error}: {token}"));
                option.Value = value;
            }
            else if (token.EndsWith("&", StringComparison.Ordinal))
                option.Value = option.Default;
            else
                shown.Add(option.Show());
        }
        return (true, shown.Count > 0 ? string.Join("  ", shown) : null);
    }

    static string Join(List<string> shown, string error) =>
        shown.Count > 0 ? string.Join("  ", shown) + "  " + error : error;

    static string? ValidateLabels(string value)
    {
        if (value.Length < 2)
            return "라벨 글자는 두 개 이상이어야 합니다";
        if (value.Any(c => c is < 'a' or > 'z'))
            return "라벨 글자는 영문 소문자만 됩니다";
        if (value.Distinct().Count() != value.Length)
            return "라벨 글자가 겹칩니다";
        return null;
    }

    sealed class Option
    {
        public Option(string name, string @default, string description, Func<string, string?> validate)
        {
            Name = name;
            Default = @default;
            Value = @default;
            Description = description;
            Validate = validate;
        }

        public static Option Number(string name, int @default, string description) =>
            new(name, @default.ToString(), description,
                v => int.TryParse(v, out var n) && n >= 0 && n <= 100000 ? null : "0 이상의 숫자가 필요합니다");

        public string Name { get; }
        public string Default { get; }
        public string Value { get; set; }
        public string Description { get; }
        public Func<string, string?> Validate { get; }
        public int Int => int.Parse(Value);
        public string Show() => $"{Name}={Value}";
    }

    // ───────────────────────── 매핑 ─────────────────────────

    /// <summary>한 모드의 매핑 (lhs 키 문자열 → 매핑).</summary>
    public IReadOnlyDictionary<string, KeyMapping> Mappings(MapMode mode) => mode == MapMode.Normal ? _normal : _visual;

    /// <summary>
    /// <c>:map</c> 계열. <paramref name="name"/>은 <see cref="MapCommands"/> 중 하나, <paramref name="args"/>는 <c>{lhs} {rhs}</c>.
    /// 인자가 없으면 그 모드의 매핑 목록, lhs만 있으면 lhs로 시작하는 매핑 목록. rhs 끝의 공백은 버린다 (공백은 <c>&lt;Space&gt;</c>로).
    /// </summary>
    public (bool Ok, string? Message) Map(string name, string args)
    {
        if (!TryMapModes(name, out var modes, out var noremap))
            return (false, $"매핑 명령이 아닙니다: {name}");

        args = args.Trim();
        var split = args.IndexOfAny(new[] { ' ', '\t' });
        var lhsText = split < 0 ? args : args.Substring(0, split);
        var rhsText = split < 0 ? "" : args.Substring(split + 1).Trim();

        if (!KeyNotation.TryParse(lhsText, out var lhs, out var error))
            return (false, error);
        if (rhsText.Length == 0)
            return (true, List(modes, lhs));
        if (!KeyNotation.TryParse(rhsText, out var rhs, out error))
            return (false, error);

        foreach (var mode in modes)
            Table(mode)[lhs] = new KeyMapping(mode, lhs, rhs, noremap, rhsText);
        return (true, null);
    }

    /// <summary><c>:unmap</c> / <c>:nunmap</c> / <c>:vunmap</c> <c>{lhs}</c>. 어느 모드에도 없으면 오류.</summary>
    public (bool Ok, string? Message) Unmap(string name, string args)
    {
        MapMode[] modes = name switch
        {
            "unmap" => new[] { MapMode.Normal, MapMode.Visual },
            "nunmap" => new[] { MapMode.Normal },
            "vunmap" => new[] { MapMode.Visual },
            _ => Array.Empty<MapMode>(),
        };
        if (modes.Length == 0)
            return (false, $"매핑 지우기 명령이 아닙니다: {name}");
        args = args.Trim();
        if (args.Length == 0)
            return (false, "지울 매핑을 적어 주세요");
        if (!KeyNotation.TryParse(args, out var lhs, out var error))
            return (false, error);

        var removed = false;
        foreach (var mode in modes)
            removed |= Table(mode).Remove(lhs);
        return removed ? (true, null) : (false, $"그런 매핑이 없습니다: {args}");
    }

    Dictionary<string, KeyMapping> Table(MapMode mode) => mode == MapMode.Normal ? _normal : _visual;

    static bool TryMapModes(string name, out MapMode[] modes, out bool noremap)
    {
        noremap = name.EndsWith("noremap", StringComparison.Ordinal);
        modes = name switch
        {
            "map" or "noremap" => new[] { MapMode.Normal, MapMode.Visual },
            "nmap" or "nnoremap" => new[] { MapMode.Normal },
            "vmap" or "vnoremap" => new[] { MapMode.Visual },
            _ => Array.Empty<MapMode>(),
        };
        return modes.Length > 0;
    }

    // vim처럼 줄마다 "n  <Space>w  * :w<CR>" (*는 noremap)
    string List(MapMode[] modes, string prefix)
    {
        var lines = modes
            .SelectMany(m => Table(m).Values)
            .Where(m => m.Lhs.StartsWith(prefix, StringComparison.Ordinal))
            .OrderBy(m => m.Lhs, StringComparer.Ordinal)
            .ThenBy(m => m.Mode)
            .Select(m => $"{(m.Mode == MapMode.Normal ? 'n' : 'v')}  {KeyNotation.Format(m.Lhs)}  {(m.Noremap ? "*" : " ")} {m.RhsText}")
            .ToList();
        return lines.Count == 0 ? "매핑이 없습니다" : string.Join("\n", lines);
    }

    /// <summary>lhs가 <paramref name="keys"/>로 시작하는 매핑이 있는지 (같은 것 포함).</summary>
    public bool HasPrefix(MapMode mode, string keys) =>
        keys.Length > 0 && Table(mode).Keys.Any(l => l.StartsWith(keys, StringComparison.Ordinal));

    /// <summary>
    /// 매핑의 앞부분 <paramref name="typed"/>를 친 뒤의 힌트: 남은 lhs와 rhs. lhs가 정확히 <paramref name="typed"/>인 매핑은
    /// 기다리면 실행되므로 <c>(기다림)</c>으로 맨 앞에.
    /// </summary>
    public IReadOnlyList<KeyHint> MappingHints(MapMode mode, string typed)
    {
        var hints = new List<KeyHint>();
        if (Table(mode).TryGetValue(typed, out var exact))
            hints.Add(new KeyHint("(기다림)", "→ " + exact.RhsText));
        hints.AddRange(Table(mode).Values
            .Where(m => m.Lhs.Length > typed.Length && m.Lhs.StartsWith(typed, StringComparison.Ordinal))
            .OrderBy(m => m.Lhs, StringComparer.Ordinal)
            .Select(m => new KeyHint(KeyNotation.Format(m.Lhs.Substring(typed.Length), hintStyle: true), "→ " + m.RhsText)));
        return hints;
    }

    // ───────────────────────── 설정 파일 ─────────────────────────

    /// <summary>
    /// 명령줄 없이 설정 명령 한 줄을 실행한다 (<c>map</c> 계열, <c>unmap</c> 계열, <c>set</c> / <c>se</c>). 앞의 <c>:</c>는 무시한다.
    /// 다른 명령은 오류. 명령줄이 붙으면 명령줄의 실행 함수를 <see cref="LoadFile"/>에 넘긴다.
    /// </summary>
    public (bool Ok, string? Message) Run(string line)
    {
        line = line.Trim().TrimStart(':').TrimStart();
        var end = 0;
        while (end < line.Length && char.IsLetter(line[end]))
            end++;
        var name = line.Substring(0, end);
        var args = line.Substring(end);
        if (name.Length == 0)
            return (false, $"명령이 아닙니다: {line}");
        if (MapCommands.Contains(name))
            return Map(name, args);
        if (UnmapCommands.Contains(name))
            return Unmap(name, args);
        if (name is "set" or "se")
            return Set(args);
        return (false, $"알 수 없는 명령: {name}");
    }

    /// <summary>
    /// 설정 파일을 줄마다 <paramref name="execute"/>로 실행한다. 파일이 없으면 아무것도 하지 않는다.
    /// </summary>
    /// <returns>오류마다 <c>"{줄 번호}행: {메시지}"</c>.</returns>
    public static IReadOnlyList<string> LoadFile(string path, Func<string, (bool Ok, string? Message)> execute)
    {
        if (!File.Exists(path))
            return Array.Empty<string>();
        string[] lines;
        try
        {
            lines = File.ReadAllLines(path, Encoding.UTF8);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new[] { $"읽을 수 없습니다: {e.Message}" };
        }
        return LoadLines(lines, execute);
    }

    /// <summary>
    /// 줄마다 실행한다. 빈 줄과 <c>"</c>로 시작하는 줄(주석)은 건너뛰고, 앞의 공백과 <c>:</c>는 떼고 넘긴다.
    /// 오류가 난 줄이 있어도 다음 줄을 계속 실행한다 (vim과 같음).
    /// </summary>
    public static IReadOnlyList<string> LoadLines(IEnumerable<string> lines, Func<string, (bool Ok, string? Message)> execute)
    {
        var errors = new List<string>();
        var number = 0;
        foreach (var raw in lines)
        {
            number++;
            var line = raw.Trim().TrimStart(':').Trim();
            if (line.Length == 0 || line[0] == '"')
                continue;
            var (ok, message) = execute(line);
            if (!ok)
                errors.Add($"{number}행: {message ?? "실패"}");
        }
        return errors;
    }
}
