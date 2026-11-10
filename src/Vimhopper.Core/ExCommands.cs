using System;
using System.Collections.Generic;
using System.Linq;

namespace Vimhopper.Core;

/// <summary><c>:</c> 명령 한 번의 호출. Name은 등록된 전체 이름 (<c>:rec</c>로 불러도 <c>recompute</c>).</summary>
/// <param name="Args">이름(과 <c>!</c>) 뒤의 글. 앞 공백만 지운다 (<c>:s/a/b/</c>면 <c>/a/b/</c>).</param>
/// <param name="Range"><c>%</c>, <c>'&lt;,'&gt;</c>, 범위가 없으면 null.</param>
/// <param name="Bang">이름 바로 뒤에 <c>!</c>가 있었는지.</param>
public sealed record ExCall(string Name, string Args, string? Range, bool Bang);

/// <summary><c>:</c> 명령의 결과. Message는 상태 줄에 보일 글 (없으면 null).</summary>
public sealed record ExResult(bool Ok, string? Message = null)
{
    public static ExResult Success(string? message = null) => new(true, message);

    public static ExResult Error(string message) => new(false, message);
}

/// <summary>등록된 <c>:</c> 명령. MinLength는 불러낼 수 있는 가장 짧은 앞부분 길이 (vim의 <c>:s[ubstitute]</c>).</summary>
public sealed record ExCommandInfo(string Name, int MinLength, string Description);

/// <summary>
/// <c>:</c> 명령 모음 (vim의 Ex 명령). Engine이 명령을 등록하고, 입력줄의 글을 <see cref="Execute"/>로 실행한다.
/// 설정 파일(<c>vimhopperrc</c>)도 줄마다 이것으로 실행한다.
/// </summary>
/// <remarks>
/// 줄의 모양: <c>[범위][이름][!][ 인자]</c>. 범위는 <c>%</c>(문서 전체)나 <c>'&lt;,'&gt;</c>(비주얼 선택).
/// 이름은 영문자만이고 vim처럼 겹치지 않는 앞부분만 써도 된다 (<c>:rec</c> = <c>:recompute</c>).
/// 정확히 같은 이름이 먼저이고, 앞부분이 여러 명령에 맞으면 오류에 후보를 보여 준다.
/// </remarks>
public sealed class ExCommands
{
    public const string WholeDocument = "%";
    public const string VisualRange = "'<,'>";

    readonly Dictionary<string, (ExCommandInfo Info, Func<ExCall, ExResult> Run)> _commands = new(StringComparer.Ordinal);

    /// <summary>등록된 명령, 이름 순.</summary>
    public IEnumerable<ExCommandInfo> Commands =>
        _commands.Values.Select(c => c.Info).OrderBy(c => c.Name, StringComparer.Ordinal);

    /// <summary>명령을 등록한다. 같은 이름이 있으면 바꾼다.</summary>
    /// <param name="minLength">불러낼 수 있는 가장 짧은 앞부분 길이 (1 이상, 이름 길이 이하).</param>
    public void Register(string name, int minLength, Func<ExCall, ExResult> run, string description)
    {
        if (name.Length == 0 || !name.All(IsNameChar))
            throw new ArgumentException($"명령 이름은 영문자만: {name}", nameof(name));
        if (minLength < 1 || minLength > name.Length)
            throw new ArgumentOutOfRangeException(nameof(minLength));
        _commands[name] = (new ExCommandInfo(name, minLength, description), run);
    }

    /// <summary>
    /// <c>:</c> 뒤의 글 한 줄을 실행한다. 앞의 공백과 <c>:</c>는 무시하고, 빈 줄은 아무것도 하지 않는다.
    /// 명령이 던진 예외는 오류 결과로 바꾼다.
    /// </summary>
    public ExResult Execute(string line)
    {
        if (Parse(line) is not { } parsed)
            return ExResult.Error($"명령이 아닙니다: {line.Trim()}");
        var (range, name, bang, args) = parsed;
        if (name.Length == 0)
            return range == null && args.Length == 0
                ? ExResult.Success()
                : ExResult.Error($"명령이 아닙니다: {line.Trim()}");

        var (command, error) = Resolve(name);
        if (command is not { } c)
            return ExResult.Error(error!);
        try
        {
            return c.Run(new ExCall(c.Info.Name, args, range, bang));
        }
        catch (Exception e)
        {
            return ExResult.Error($":{c.Info.Name}: {e.Message}");
        }
    }

    /// <summary>이름이 <paramref name="prefix"/>로 시작하는 명령들, 이름 순 (<c>Tab</c> 완성).</summary>
    public IEnumerable<string> Complete(string prefix) =>
        _commands.Keys.Where(n => n.StartsWith(prefix, StringComparison.Ordinal)).OrderBy(n => n, StringComparer.Ordinal);

    /// <summary>
    /// 줄을 범위·이름·<c>!</c>·인자로 나눈다. 범위나 이름 자리가 아닌 글이 먼저 오면 null.
    /// </summary>
    public static (string? Range, string Name, bool Bang, string Args)? Parse(string line)
    {
        var i = SkipHead(line, 0);
        string? range = null;
        if (Matches(line, i, WholeDocument))
            range = WholeDocument;
        else if (Matches(line, i, VisualRange))
            range = VisualRange;
        if (range != null)
            i = SkipSpaces(line, i + range.Length);

        var start = i;
        while (i < line.Length && IsNameChar(line[i])) i++;
        var name = line.Substring(start, i - start);
        if (name.Length == 0 && i < line.Length)
            return null;

        var bang = i < line.Length && line[i] == '!';
        if (bang) i++;
        var args = line.Substring(i).TrimStart();
        return (range, name, bang, args);
    }

    /// <summary>
    /// <c>Tab</c> 완성에서 이름 앞까지(<c>:</c>, 공백, 범위)의 길이. 그 뒤가 이름 글자뿐이 아니면(인자를 쓰는 중) null.
    /// </summary>
    public static int? NameStart(string line)
    {
        var i = SkipHead(line, 0);
        if (Matches(line, i, WholeDocument))
            i = SkipSpaces(line, i + WholeDocument.Length);
        else if (Matches(line, i, VisualRange))
            i = SkipSpaces(line, i + VisualRange.Length);
        for (var j = i; j < line.Length; j++)
            if (!IsNameChar(line[j]))
                return null;
        return i;
    }

    // 정확히 같은 이름 → 앞부분이 맞고 MinLength를 채운 명령이 하나 → 그 명령. 아니면 오류
    (( ExCommandInfo Info, Func<ExCall, ExResult> Run)? Command, string? Error) Resolve(string name)
    {
        if (_commands.TryGetValue(name, out var exact))
            return (exact, null);
        var prefixed = _commands.Values
            .Where(c => c.Info.Name.StartsWith(name, StringComparison.Ordinal))
            .OrderBy(c => c.Info.Name, StringComparer.Ordinal)
            .ToList();
        var allowed = prefixed.Where(c => name.Length >= c.Info.MinLength).ToList();
        if (allowed.Count == 1)
            return (allowed[0], null);
        if (prefixed.Count > 1)
            return (null, $"모호한 명령: :{name} ({string.Join(", ", prefixed.Select(c => c.Info.Name))})");
        return (null, $"명령이 아닙니다: :{name}");
    }

    static bool IsNameChar(char c) => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z';

    static bool Matches(string line, int index, string token) =>
        index + token.Length <= line.Length && string.CompareOrdinal(line, index, token, 0, token.Length) == 0;

    static int SkipHead(string line, int i)
    {
        while (i < line.Length && (line[i] == ':' || char.IsWhiteSpace(line[i]))) i++;
        return i;
    }

    static int SkipSpaces(string line, int i)
    {
        while (i < line.Length && char.IsWhiteSpace(line[i])) i++;
        return i;
    }
}

/// <summary>
/// <c>:</c> 입력줄의 <c>↑</c> / <c>↓</c> 기록 (vim의 명령줄 기록). 최근 것이 끝, 같은 줄은 한 번만 (다시 쓰면 끝으로).
/// 기록을 넘기는 동안은 넘기기 시작할 때의 글로 시작하는 줄만 고른다 (vim과 같음).
/// </summary>
public sealed class CommandHistory
{
    readonly List<string> _lines = new();
    readonly int _capacity;
    // 넘기는 중이면 지금 위치 (_lines.Count면 처음 글)와 처음 글
    int _position = -1;
    string _prefix = "";

    public CommandHistory(int capacity = 50) => _capacity = capacity;

    public IReadOnlyList<string> Lines => _lines;

    public void Add(string line)
    {
        Reset();
        if (line.Trim().Length == 0) return;
        _lines.Remove(line);
        _lines.Add(line);
        if (_lines.Count > _capacity)
            _lines.RemoveAt(0);
    }

    /// <summary>넘기기를 끝낸다 (글을 고쳤을 때, 입력줄을 열 때).</summary>
    public void Reset() => _position = -1;

    /// <summary><c>↑</c>: 더 오래된 맞는 줄. 없으면 null (입력줄은 그대로).</summary>
    /// <param name="current">지금 입력줄의 글. 넘기기를 시작할 때만 쓴다.</param>
    public string? Older(string current)
    {
        if (_position < 0)
        {
            _prefix = current;
            _position = _lines.Count;
        }
        for (var i = _position - 1; i >= 0; i--)
            if (_lines[i].StartsWith(_prefix, StringComparison.Ordinal))
            {
                _position = i;
                return _lines[i];
            }
        return null;
    }

    /// <summary><c>↓</c>: 더 최근의 맞는 줄. 끝을 넘으면 넘기기 시작할 때의 글. 넘기는 중이 아니면 null.</summary>
    public string? Newer()
    {
        if (_position < 0) return null;
        for (var i = _position + 1; i < _lines.Count; i++)
            if (_lines[i].StartsWith(_prefix, StringComparison.Ordinal))
            {
                _position = i;
                return _lines[i];
            }
        _position = -1;
        return _prefix;
    }
}

/// <summary>
/// <c>:</c> 입력줄의 <c>Tab</c> / <c>Shift+Tab</c> 명령 이름 완성. 후보가 여럿이면 차례로 돌고, 한 바퀴 끝에 처음 글로 돌아온다 (vim의 wildmenu).
/// 후보가 하나면 바로 채우고 끝난다.
/// </summary>
public sealed class ExCompletion
{
    IReadOnlyList<string> _candidates = Array.Empty<string>();
    // 지금 고른 후보. _candidates.Count면 처음 글
    int _index;
    string _head = "";
    string _original = "";

    /// <summary>후보를 도는 중인지.</summary>
    public bool Active => _candidates.Count > 0;

    /// <summary>입력줄 오른쪽에 보일 후보 목록. 고른 것은 <c>[ ]</c>로 감싼다. 도는 중이 아니면 빈 글.</summary>
    public string Info => Active
        ? string.Join("  ", _candidates.Select((c, i) => i == _index ? $"[{c}]" : c))
        : "";

    public void Reset()
    {
        _candidates = Array.Empty<string>();
        _index = 0;
    }

    /// <summary>다음(<paramref name="backward"/>면 이전) 후보로 바꾼 줄. 완성할 것이 없으면 null.</summary>
    public string? Next(ExCommands commands, string line, bool backward = false)
    {
        if (!Active)
        {
            if (ExCommands.NameStart(line) is not { } start)
                return null;
            var candidates = commands.Complete(line.Substring(start)).ToList();
            if (candidates.Count == 0)
                return null;
            _head = line.Substring(0, start);
            if (candidates.Count == 1)
                return _head + candidates[0];
            _candidates = candidates;
            _original = line;
            _index = backward ? candidates.Count - 1 : 0;
            return _head + _candidates[_index];
        }

        var n = _candidates.Count + 1;
        _index = (_index + (backward ? n - 1 : 1)) % n;
        return _index == _candidates.Count ? _original : _head + _candidates[_index];
    }
}
