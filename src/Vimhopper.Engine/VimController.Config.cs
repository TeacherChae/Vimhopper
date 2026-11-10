using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using Vimhopper.Core;

namespace Vimhopper;

// 사용자 매핑·옵션·설정 파일 (0023). 키는 매핑 앞단(KeyRemapper)을 거쳐 파서로 간다
internal sealed partial class VimController
{
    readonly UserConfig _config = new();
    KeyRemapper _remap = null!;
    // 매핑의 앞부분을 친 뒤 timeoutlen이 지나면 모은 키를 펼치거나 흘려보낸다
    readonly Timer _mapTimer = new();
    // 설정 파일은 이 엔진에서 Vim 모드에 처음 들어갈 때 한 번 읽는다 (핫 리로드하면 다시 읽음)
    bool _rcLoaded;

    /// <summary>설정 파일 위치: <c>%APPDATA%\Vimhopper\vimhopperrc</c>.</summary>
    static string RcPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vimhopper", "vimhopperrc");

    void InitConfig()
    {
        _remap = new KeyRemapper(_config, MapModeFor, FeedParser);
        _mapTimer.Tick += OnMapTimer;
    }

    void DisposeConfig()
    {
        _mapTimer.Stop();
        _mapTimer.Tick -= OnMapTimer;
        _mapTimer.Dispose();
    }

    void ResetMappingState()
    {
        _mapTimer.Stop();
        _remap.Reset();
    }

    // 입력줄이 열렸거나 명령 입력 중(d 다음, 반복 횟수 뒤 숫자, o{n} 뒤 f g s r n)이면 매핑하지 않는다
    MapMode? MapModeFor(char key) =>
        _input.Active || !_parser.MapsKey(key) ? null : _parser.Visual ? MapMode.Visual : MapMode.Normal;

    /// <summary>
    /// 설정 파일을 읽는다. 줄마다 <c>:</c> 명령줄과 같은 <see cref="ExCommands"/>로 실행하므로 어떤 명령이든 쓸 수 있다.
    /// 오류는 첫 번째 것만 상태줄에 한 번 보인다. 파일이 없으면 조용히 넘어간다.
    /// </summary>
    void LoadRc()
    {
        _rcLoaded = true;
        var result = ConfigCommands.Source(_ex, RcPath);
        if (!result.Ok)
            _status = result.Message ?? "";
    }

    /// <summary>키 하나를 매핑 앞단에 넣는다. 더 긴 lhs를 기다리면 timeoutlen 타이머를 건다.</summary>
    void FeedMapped(char key)
    {
        _mapTimer.Stop();
        if (_remap.Feed(key) is { } error)
            _status = error;
        StartMapTimer();
    }

    void StartMapTimer()
    {
        if (!_remap.Waiting) return;
        if (_config.TimeoutLen == 0)
        {
            OnMapTimer(null, EventArgs.Empty);
            return;
        }
        _mapTimer.Interval = _config.TimeoutLen;
        _mapTimer.Start();
    }

    void OnMapTimer(object? sender, EventArgs e)
    {
        _mapTimer.Stop();
        if (!_vim) return;
        HideHints();
        if (_remap.Timeout() is { } error)
            _status = error;
        StartMapTimer();
        ScheduleHints();
        _canvas.Refresh();
    }

    // 펼친 키 하나. rhs가 입력줄을 열었으면 남은 키는 입력줄에 친다 (Ctrl 조합은 버림)
    void FeedParser(char key)
    {
        if (_input.Active)
        {
            if (key is < '' or > '')
                _input.Type(key);
            return;
        }
        foreach (var command in _parser.Feed(key))
            Execute(command);
    }

    /// <summary>
    /// 평소에는 Grasshopper에 넘기는 키(<c>;</c>, <c>,</c>, <c>-</c>, <c>Tab</c>, <c>Ctrl+s</c> 등)가 매핑의 앞부분이면 가로챈다.
    /// 매핑을 기다리던 중 맞지 않는 키가 오면 모은 키를 먼저 흘려보내고 그 키는 Grasshopper에 넘긴다.
    /// </summary>
    char? MappedExtraKey(Keys code, Keys modifiers)
    {
        if (TranslateExtra(code, modifiers) is { } key && _remap.Accepts(key))
            return key;
        FlushMapped();
        return null;
    }

    /// <summary>
    /// 매핑을 기다리며 모아 둔 키를 지금 흘려보낸다 (timeoutlen이 지난 것처럼). Grasshopper에 넘길 키가 오면
    /// 그 키보다 먼저 처리되도록 부른다 (순서 유지).
    /// </summary>
    void FlushMapped()
    {
        if (!_remap.Waiting) return;
        _mapTimer.Stop();
        if (_remap.Timeout() is { } error)
            _status = error;
        StartMapTimer();
        _canvas.Refresh();
    }

    // 미국 자판 기준. :는 명령줄이 쓰므로 뺀다
    static char? TranslateExtra(Keys code, Keys modifiers)
    {
        var shift = (modifiers & Keys.Shift) != 0;
        if ((modifiers & Keys.Control) != 0)
            return code is >= Keys.A and <= Keys.Z && !shift ? KeyNotation.Ctrl((char)('a' + (code - Keys.A))) : null;
        return code switch
        {
            Keys.OemSemicolon when !shift => ';',
            Keys.Oemcomma => shift ? '<' : ',',
            Keys.OemPeriod when shift => '>',
            Keys.OemMinus => shift ? '_' : '-',
            Keys.Oemplus when shift => '+',
            Keys.OemPipe or Keys.OemBackslash => shift ? '|' : '\\',
            Keys.OemQuotes when shift => '"',
            Keys.OemQuestion when shift => '?',
            Keys.D1 when shift => '!',
            Keys.D2 when shift => '@',
            Keys.D4 when shift => '$',
            Keys.D5 when shift => '%',
            Keys.D6 when shift => '^',
            Keys.D7 when shift => '&',
            Keys.D9 when shift => '(',
            Keys.D0 when shift => ')',
            Keys.Tab when !shift => KeyParser.CtrlI,
            _ => null,
        };
    }

    /// <summary>
    /// 지금 보일 다음 키 힌트. 매핑의 앞부분을 모아 두는 중이면 그 키로 시작하는 매핑(→ rhs)과,
    /// 매핑 없이 그 키를 쳤을 때의 원래 힌트를 함께 보인다.
    /// </summary>
    IReadOnlyList<KeyHint> CurrentHints()
    {
        var typed = _remap.Buffered;
        if (typed.Length == 0 || MapModeFor(typed[0]) is not { } mode)
            return _parser.Hints;
        return KeyParser.WithMappings(_parser.HintsAfter(typed), _config.MappingHints(mode, typed));
    }
}
