using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Vimhopper.Core;

/// <summary>
/// 매핑의 키 표기(vim과 같음: <c>&lt;Space&gt;</c>, <c>&lt;leader&gt;</c>, <c>&lt;C-x&gt;</c> …)와 <see cref="KeyParser"/>가 받는 키 문자 사이의 변환.
/// </summary>
/// <remarks>
/// Ctrl 조합은 <see cref="KeyParser"/>의 <c>Ctrl{글자}</c> 상수를 그대로 쓴다 (리플렉션으로 모으므로 상수를 더하면 저절로 따라온다).
/// 상수가 없는 Ctrl+글자는 <see cref="UnboundCtrlBase"/>부터의 사용자 정의 영역 문자로 나타낸다 (매핑에만 쓰임).
/// </remarks>
public static class KeyNotation
{
    /// <summary>KeyParser에 상수가 없는 Ctrl+a..z를 나타내는 문자의 시작 (U+E100 + 글자 순서).</summary>
    public const char UnboundCtrlBase = '';

    // 글자 → KeyParser.Ctrl{글자} 상수
    static readonly Dictionary<char, char> CtrlLetters = typeof(KeyParser)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.IsLiteral && f.FieldType == typeof(char) && f.Name.Length == 5 && f.Name.StartsWith("Ctrl", StringComparison.Ordinal)
                    && f.Name[4] is >= 'A' and <= 'Z')
        .ToDictionary(f => char.ToLowerInvariant(f.Name[4]), f => (char)f.GetRawConstantValue()!);

    static readonly Dictionary<string, char> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["space"] = KeyParser.Space,
        ["leader"] = KeyParser.Space,
        ["esc"] = KeyParser.Escape,
        ["cr"] = KeyParser.Enter,
        ["enter"] = KeyParser.Enter,
        ["return"] = KeyParser.Enter,
        ["bs"] = KeyParser.Backspace,
        // vim처럼 Tab은 Ctrl+i와 같은 키
        ["tab"] = KeyParser.CtrlI,
        ["lt"] = '<',
        ["bar"] = '|',
        ["bslash"] = '\\',
    };

    /// <summary>Ctrl+글자(a-z)에 해당하는 키 문자.</summary>
    public static char Ctrl(char letter)
    {
        letter = char.ToLowerInvariant(letter);
        if (letter is < 'a' or > 'z')
            throw new ArgumentOutOfRangeException(nameof(letter));
        return CtrlLetters.TryGetValue(letter, out var c) ? c : (char)(UnboundCtrlBase + (letter - 'a'));
    }

    /// <summary>표기를 키 문자열로. 모르는 <c>&lt;이름&gt;</c>은 오류 (짝이 없는 <c>&lt;</c>는 글자 그대로).</summary>
    public static bool TryParse(string text, out string keys, out string? error)
    {
        var sb = new StringBuilder();
        error = null;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c != '<')
            {
                sb.Append(c);
                continue;
            }
            var close = text.IndexOf('>', i + 1);
            // <C->>처럼 이름이 >인 경우는 없으므로 가장 가까운 >까지
            var name = close > i + 1 ? text.Substring(i + 1, close - i - 1) : null;
            if (name == null || name.Any(char.IsWhiteSpace) || name.IndexOf('<') >= 0)
            {
                sb.Append(c);
                continue;
            }
            if (TryNamed(name, out var key))
            {
                sb.Append(key);
                i = close;
                continue;
            }
            keys = "";
            error = $"알 수 없는 키 표기: <{name}>";
            return false;
        }
        keys = sb.ToString();
        return true;
    }

    static bool TryNamed(string name, out char key)
    {
        if (Named.TryGetValue(name, out key))
            return true;
        if (name.Length == 3 && (name[0] is 'C' or 'c') && name[1] == '-')
        {
            var k = name[2];
            switch (k)
            {
                case >= 'a' and <= 'z' or >= 'A' and <= 'Z': key = Ctrl(k); return true;
                case '=' or '+': key = KeyParser.CtrlPlus; return true;
                case '-': key = KeyParser.CtrlMinus; return true;
                case '[': key = KeyParser.Escape; return true;
            }
        }
        key = default;
        return false;
    }

    /// <summary>키 문자열을 표기로 (<c>:map</c> 목록, 힌트).</summary>
    /// <param name="hintStyle">힌트 표기(<see cref="KeyHint"/>)처럼 Enter를 <c>&lt;Enter&gt;</c>로. 아니면 vim처럼 <c>&lt;CR&gt;</c>.</param>
    public static string Format(string keys, bool hintStyle = false)
    {
        var sb = new StringBuilder();
        foreach (var c in keys)
            sb.Append(Format(c, hintStyle));
        return sb.ToString();
    }

    static string Format(char c, bool hintStyle)
    {
        switch (c)
        {
            case KeyParser.Space: return "<Space>";
            case KeyParser.Escape: return "<Esc>";
            case KeyParser.Enter: return hintStyle ? "<Enter>" : "<CR>";
            case KeyParser.Backspace: return "<BS>";
            case KeyParser.CtrlPlus: return "<C-=>";
            case KeyParser.CtrlMinus: return "<C-->";
            case '<': return "<lt>";
        }
        foreach (var pair in CtrlLetters)
            if (pair.Value == c)
                return $"<C-{pair.Key}>";
        if (c >= UnboundCtrlBase && c < UnboundCtrlBase + 26)
            return $"<C-{(char)('a' + (c - UnboundCtrlBase))}>";
        return c.ToString();
    }
}
