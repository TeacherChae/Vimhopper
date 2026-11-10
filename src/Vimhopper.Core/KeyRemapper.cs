using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Vimhopper.Core;

/// <summary>
/// 사용자 매핑을 펼쳐서 <see cref="KeyParser"/>에 넘기는 앞단 (vim의 typeahead).
/// </summary>
/// <remarks>
/// 친 키가 어떤 lhs의 앞부분이면 모아 두고, lhs와 맞으면 rhs로 바꾸고, 맞지 않으면 모은 키를 그대로 흘려보낸다.
/// 더 긴 lhs가 있어 기다리는 중이면(<see cref="Waiting"/>) Engine이 <c>timeoutlen</c> 뒤에 <see cref="Timeout"/>을 부른다:
/// 그때까지 맞은 가장 긴 lhs를 펼치고, 없으면 그대로 흘려보낸다.
/// <c>noremap</c>의 rhs는 다시 매핑하지 않고, <c>map</c>의 rhs는 다시 매핑한다 (rhs가 lhs로 시작하면 첫 키는 빼고, vim과 같음).
/// 키를 하나 넘길 때마다 모드를 다시 물으므로 rhs 안의 <c>v</c> 뒤 키는 비주얼 매핑을 따른다.
/// 모드가 <c>null</c>이면(입력줄이 열림, 명령 입력 중) 키를 매핑하지 않고 그대로 넘긴다.
/// </remarks>
public sealed class KeyRemapper
{
    /// <summary>키 하나·시간 초과 한 번에 펼칠 수 있는 매핑 수. 넘으면 재귀 매핑으로 보고 남은 키를 버린다.</summary>
    public const int MaxDepth = 100;

    readonly UserConfig _config;
    readonly Func<MapMode?> _mode;
    readonly Action<char> _sink;
    // 아직 넘기지 않은 키와 다시 매핑할 수 있는지
    readonly List<(char Key, bool Remap)> _queue = new();

    /// <param name="mode">지금 매핑이 걸리는 모드. <c>null</c>이면 매핑하지 않는다.</param>
    /// <param name="sink">펼친 키를 하나씩 받는 곳 (KeyParser, 입력줄이 열렸으면 입력줄).</param>
    public KeyRemapper(UserConfig config, Func<MapMode?> mode, Action<char> sink)
    {
        _config = config;
        _mode = mode;
        _sink = sink;
    }

    /// <summary>더 긴 lhs를 기다리며 키를 모아 두고 있는지. Engine은 이때 <c>timeoutlen</c> 타이머를 건다.</summary>
    public bool Waiting { get; private set; }

    /// <summary>모아 둔 키 (HUD 표시, 힌트용).</summary>
    public string Buffered => Waiting ? new string(_queue.Select(e => e.Key).ToArray()) : "";

    /// <summary>모아 둔 키의 표기 (HUD).</summary>
    public string Pending => KeyNotation.Format(Buffered);

    /// <summary>
    /// 평소에는 받지 않는 키(<c>;</c>, <c>Ctrl+s</c> 등)를 가로챌지: 모아 둔 키에 이어 어떤 lhs의 앞부분이 되면 true.
    /// false인데 기다리는 중이었다면 Engine은 <see cref="Timeout"/>으로 모은 키를 먼저 흘려보내고 그 키는 Grasshopper에 넘긴다.
    /// </summary>
    public bool Accepts(char key) =>
        _mode() is { } mode && _config.HasPrefix(mode, Buffered + key);

    /// <summary>키 하나. 오류(재귀 매핑)면 메시지.</summary>
    public string? Feed(char key)
    {
        _queue.Add((key, true));
        return Process(timedOut: false);
    }

    /// <summary><c>timeoutlen</c>이 지남: 기다리던 키를 맞은 만큼 펼치거나 그대로 흘려보낸다.</summary>
    public string? Timeout() => Waiting ? Process(timedOut: true) : null;

    /// <summary>모아 둔 키를 버린다 (Vim 모드 나가기, 문서 바뀜).</summary>
    public void Reset()
    {
        _queue.Clear();
        Waiting = false;
    }

    string? Process(bool timedOut)
    {
        Waiting = false;
        var expansions = 0;
        while (_queue.Count > 0)
        {
            var (key, remap) = _queue[0];
            if (!remap || _mode() is not { } mode)
            {
                Emit();
                continue;
            }

            // 맨 앞의 다시 매핑할 수 있는 키들만 lhs와 맞춘다
            var run = new StringBuilder();
            foreach (var entry in _queue)
            {
                if (!entry.Remap) break;
                run.Append(entry.Key);
            }
            var typed = run.ToString();
            var table = _config.Mappings(mode);
            KeyMapping? best = null;
            var longer = false;
            foreach (var m in table.Values)
            {
                if (typed.StartsWith(m.Lhs, StringComparison.Ordinal) && (best == null || m.Lhs.Length > best.Lhs.Length))
                    best = m;
                else if (m.Lhs.Length > typed.Length && m.Lhs.StartsWith(typed, StringComparison.Ordinal))
                    longer = true;
            }

            // 더 긴 lhs가 될 수 있으면 다음 키를 기다린다 (뒤에 이미 다시 매핑하지 않는 키가 있으면 더 올 키가 없음)
            if (longer && !timedOut && typed.Length == _queue.Count)
            {
                Waiting = true;
                return null;
            }
            timedOut = false;

            if (best == null)
            {
                Emit();
                continue;
            }
            if (++expansions > MaxDepth)
            {
                _queue.Clear();
                return $"매핑이 너무 깊습니다 (재귀 매핑?): {KeyNotation.Format(best.Lhs)}";
            }
            _queue.RemoveRange(0, best.Lhs.Length);
            var startsWithLhs = best.Rhs.StartsWith(best.Lhs, StringComparison.Ordinal);
            _queue.InsertRange(0, best.Rhs.Select((c, i) => (c, !best.Noremap && !(i == 0 && startsWithLhs))));
        }
        return null;
    }

    void Emit()
    {
        var key = _queue[0].Key;
        _queue.RemoveAt(0);
        _sink(key);
    }
}
