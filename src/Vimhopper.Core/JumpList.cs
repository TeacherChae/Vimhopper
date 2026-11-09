using System;
using System.Collections.Generic;

namespace Vimhopper.Core;

/// <summary>
/// vim의 점프 목록 (<c>:help jumplist</c>). 위치는 객체의 InstanceGuid.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>점프할 때 떠나는 위치를 맨 끝에 더하고 Index를 끝(Count)에 둔다. 같은 위치가 앞에 있으면 지운다.</item>
/// <item><c>Ctrl+o</c>를 끝에서 처음 누르면 지금 위치를 먼저 더해서 <c>Ctrl+i</c>로 돌아올 수 있게 한다.</item>
/// <item><c>Ctrl+o</c> 뒤에 새로 점프해도 뒤쪽 항목은 지우지 않는다 (vim 기본, <c>jumpoptions=stack</c>이 아님).</item>
/// <item>없어진 위치(지운 객체)는 지우지 않고 건너뛴다. <c>u</c>로 되살아나면 다시 쓰인다.</item>
/// </list>
/// </remarks>
public sealed class JumpList
{
    /// <summary>vim과 같은 최대 길이. 넘치면 가장 오래된 항목부터 버린다.</summary>
    public const int MaxLength = 100;

    readonly List<Guid> _entries = new();

    /// <summary>오래된 것부터 나열한 위치들.</summary>
    public IReadOnlyList<Guid> Entries => _entries;

    /// <summary>지금 위치의 인덱스. Count면 목록 끝(가장 최근 점프 뒤)에 있다.</summary>
    public int Index { get; private set; }

    public void Clear()
    {
        _entries.Clear();
        Index = 0;
    }

    /// <summary>점프하기 전에 부른다. <paramref name="from"/>은 떠나는 위치.</summary>
    public void Record(Guid from)
    {
        Append(from);
        Index = _entries.Count;
    }

    /// <summary>
    /// <c>{count}Ctrl+o</c>: 있는 위치만 세어 count개 전으로. 갈 곳이 모자라면 아무것도 바꾸지 않고 null.
    /// </summary>
    /// <param name="current">지금 위치. 목록 끝에서 처음 돌아갈 때 저장된다.</param>
    /// <param name="exists">위치가 아직 있는지.</param>
    public Guid? Back(Guid? current, int count, Func<Guid, bool> exists)
    {
        if (count < 1) return null;
        if (Index < _entries.Count || current is not { } here)
            return Move(Find(Index, -count, exists));

        // 지금 위치를 넣으면 앞의 같은 위치가 빠져서 인덱스가 바뀔 수 있으므로 넣은 뒤 그 항목에서부터 센다.
        // 갈 곳이 모자라면 넣은 것도 되돌린다
        var saved = new List<Guid>(_entries);
        Append(here);
        if (Move(Find(_entries.Count - 1, -count, exists)) is { } target)
            return target;
        _entries.Clear();
        _entries.AddRange(saved);
        return null;
    }

    /// <summary><c>{count}Ctrl+i</c>: 있는 위치만 세어 count개 뒤로. 끝에 있거나 갈 곳이 모자라면 null.</summary>
    public Guid? Forward(int count, Func<Guid, bool> exists) =>
        count < 1 ? null : Move(Find(Index, count, exists));

    Guid? Move(int? index)
    {
        if (index is not { } i) return null;
        Index = i;
        return _entries[i];
    }

    // start에서 step 방향으로 있는 위치를 |steps|개 지난 인덱스
    int? Find(int start, int steps, Func<Guid, bool> exists)
    {
        var dir = Math.Sign(steps);
        var left = Math.Abs(steps);
        for (var i = start + dir; i >= 0 && i < _entries.Count; i += dir)
            if (exists(_entries[i]) && --left == 0)
                return i;
        return null;
    }

    void Append(Guid id)
    {
        _entries.Remove(id);
        _entries.Add(id);
        if (_entries.Count > MaxLength)
            _entries.RemoveAt(0);
    }
}
