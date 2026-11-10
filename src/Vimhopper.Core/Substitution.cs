using System;
using System.Text;
using System.Text.RegularExpressions;

namespace Vimhopper.Core;

/// <summary>
/// <c>:s/찾을/바꿀/[gi]</c>의 인자 (별명 바꾸기). 찾을 글은 .NET 정규식, 바꿀 글은 .NET 치환 문법(<c>$1</c>)에 vim의 <c>\1</c>도 받는다.
/// </summary>
/// <remarks>
/// 구분자는 vim처럼 첫 글자 (<c>:s#a#b#</c>). 영문자·숫자·공백·<c>\</c>·<c>"</c>·<c>|</c>는 쓸 수 없다.
/// 구분자 앞의 <c>\</c>는 구분자 글자 그대로 (<c>:s/a\/b/c/</c>는 <c>a/b</c>를 찾음). 뒤쪽 구분자는 빼도 된다 (<c>:s/a/b</c>, <c>:s/a</c>는 지우기).
/// 플래그: <c>g</c> 별명 안의 모든 맞는 곳 (없으면 첫 곳만), <c>i</c> 대소문자 무시.
/// </remarks>
public sealed class Substitution
{
    public const string Usage = "사용법: :s/찾을/바꿀/[gi]";

    Substitution(Regex pattern, string replacement, bool global)
    {
        Pattern = pattern;
        Replacement = replacement;
        Global = global;
    }

    public Regex Pattern { get; }

    /// <summary>.NET 치환 문법으로 바꾼 바꿀 글 (<c>\1</c> → <c>${1}</c>).</summary>
    public string Replacement { get; }

    public bool Global { get; }

    /// <summary>별명 하나를 바꾼 결과. 맞는 곳이 없으면 그대로.</summary>
    public string Apply(string input) => Pattern.Replace(input, Replacement, Global ? -1 : 1);

    /// <summary><c>:s</c> 뒤의 글을 읽는다. 실패하면 null과 <paramref name="error"/>.</summary>
    public static Substitution? Parse(string args, out string? error)
    {
        error = null;
        if (args.Length == 0)
        {
            error = Usage;
            return null;
        }
        var delimiter = args[0];
        if (char.IsLetterOrDigit(delimiter) || char.IsWhiteSpace(delimiter) || delimiter is '\\' or '"' or '|')
        {
            error = $"구분자로 쓸 수 없는 글자: {delimiter} ({Usage})";
            return null;
        }

        var i = 1;
        var pattern = ReadPart(args, delimiter, ref i, out var closed);
        if (pattern.Length == 0)
        {
            error = "찾을 패턴이 비었습니다";
            return null;
        }
        var replacement = closed ? ReadPart(args, delimiter, ref i, out closed) : "";
        var flags = closed ? args.Substring(i) : "";

        var options = RegexOptions.CultureInvariant;
        var global = false;
        foreach (var f in flags)
        {
            switch (f)
            {
                case 'g': global = true; break;
                case 'i': options |= RegexOptions.IgnoreCase; break;
                case ' ' or '\t': break;
                default:
                    error = $"모르는 플래그: {f} (g, i만)";
                    return null;
            }
        }

        try
        {
            var regex = new Regex(pattern, options, TimeSpan.FromSeconds(1));
            return new Substitution(regex, ToNetReplacement(replacement), global);
        }
        catch (ArgumentException e)
        {
            error = $"패턴 오류: {e.Message}";
            return null;
        }
    }

    // 구분자까지 읽는다. \구분자는 구분자 글자, 다른 \x는 그대로 둔다 (정규식·치환이 해석). closed: 구분자로 끝났는지
    static string ReadPart(string args, char delimiter, ref int i, out bool closed)
    {
        var part = new StringBuilder();
        while (i < args.Length)
        {
            var c = args[i];
            if (c == delimiter)
            {
                i++;
                closed = true;
                return part.ToString();
            }
            if (c == '\\' && i + 1 < args.Length)
            {
                var next = args[i + 1];
                if (next == delimiter)
                    part.Append(next);
                else
                    part.Append(c).Append(next);
                i += 2;
                continue;
            }
            part.Append(c);
            i++;
        }
        closed = false;
        return part.ToString();
    }

    // vim의 \0~\9는 ${n}, \\는 \. 나머지(.NET의 $1, $$ 등)는 그대로
    static string ToNetReplacement(string replacement)
    {
        var result = new StringBuilder();
        for (var i = 0; i < replacement.Length; i++)
        {
            var c = replacement[i];
            if (c == '\\' && i + 1 < replacement.Length)
            {
                var next = replacement[i + 1];
                if (next is >= '0' and <= '9')
                {
                    result.Append("${").Append(next).Append('}');
                    i++;
                    continue;
                }
                if (next == '\\')
                {
                    result.Append('\\');
                    i++;
                    continue;
                }
            }
            result.Append(c);
        }
        return result.ToString();
    }
}
