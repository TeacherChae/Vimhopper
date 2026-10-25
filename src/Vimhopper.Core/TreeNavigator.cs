using System;
using System.Collections.Generic;

namespace Vimhopper.Core;

/// <summary>와이어 연결 그래프. 반환 목록은 화면상 위→아래 순서로 정렬되어 있어야 한다.</summary>
public interface IGraph
{
    /// <summary>이 객체의 출력을 받는 객체들 (자식).</summary>
    IReadOnlyList<Guid> Downstream(Guid id);

    /// <summary>이 객체의 입력에 연결된 객체들 (부모).</summary>
    IReadOnlyList<Guid> Upstream(Guid id);
}

/// <summary>
/// 와이어 연결을 따라 이동한다.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><c>l</c>: 자식으로 (맨 위 자식). <c>h</c>: 부모로 (맨 위 부모).</item>
/// <item><c>j</c>/<c>k</c>: 방금 건너온 객체(앵커)를 기준으로 한 형제 사이 이동.
/// <c>l</c>로 왔으면 같은 부모의 자식들, <c>h</c>로 왔으면 같은 자식의 부모들.</item>
/// <item>왔던 방향의 반대로 이동하면 원래 있던 객체로 정확히 돌아간다.</item>
/// </list>
/// </remarks>
public sealed class TreeNavigator
{
    readonly struct Step
    {
        public Step(Guid node, Guid? anchor, bool anchorDownstream, Direction via)
        {
            Node = node;
            Anchor = anchor;
            AnchorDownstream = anchorDownstream;
            Via = via;
        }

        public Guid Node { get; }
        public Guid? Anchor { get; }
        public bool AnchorDownstream { get; }
        public Direction Via { get; }
    }

    readonly IGraph _graph;
    readonly Stack<Step> _history = new();
    Guid? _anchor;
    bool _anchorDownstream;

    public TreeNavigator(IGraph graph) => _graph = graph;

    public Guid? Current { get; private set; }

    /// <summary>이동 기록을 지우고 주어진 객체에서 새로 시작한다.</summary>
    public void Reset(Guid? start)
    {
        Current = start;
        _history.Clear();
        _anchor = null;
    }

    /// <summary>이동했으면 새 객체, 갈 곳이 없으면 null (현재 위치 유지).</summary>
    public Guid? Move(Direction direction)
    {
        if (Current is not { } current)
            return null;

        return direction is Direction.Left or Direction.Right
            ? MoveAlongWire(current, direction)
            : MoveToSibling(current, direction == Direction.Down ? 1 : -1);
    }

    Guid? MoveAlongWire(Guid current, Direction direction)
    {
        var forward = direction == Direction.Right;
        var neighbors = forward ? _graph.Downstream(current) : _graph.Upstream(current);

        // 왔던 길을 되돌아가는 경우: 이전 상태 복원
        if (_history.Count > 0)
        {
            var last = _history.Peek();
            if (last.Via != direction && Contains(neighbors, last.Node))
            {
                _history.Pop();
                Current = last.Node;
                _anchor = last.Anchor;
                _anchorDownstream = last.AnchorDownstream;
                return Current;
            }
        }

        if (neighbors.Count == 0)
            return null;

        _history.Push(new Step(current, _anchor, _anchorDownstream, direction));
        _anchor = current;
        _anchorDownstream = forward;
        Current = neighbors[0];
        return Current;
    }

    Guid? MoveToSibling(Guid current, int step)
    {
        if (!HasValidAnchor(current))
            PickDefaultAnchor(current);
        if (_anchor is not { } anchor)
            return null;

        var siblings = _anchorDownstream ? _graph.Downstream(anchor) : _graph.Upstream(anchor);
        var index = IndexOf(siblings, current) + step;
        if (index < 0 || index >= siblings.Count)
            return null;

        Current = siblings[index];
        return Current;
    }

    bool HasValidAnchor(Guid current)
    {
        if (_anchor is not { } anchor) return false;
        var siblings = _anchorDownstream ? _graph.Downstream(anchor) : _graph.Upstream(anchor);
        return Contains(siblings, current);
    }

    // 건너온 기록이 없으면: 부모가 있으면 같은 부모의 자식들, 없으면(시작 객체) 같은 자식의 부모들
    void PickDefaultAnchor(Guid current)
    {
        _anchor = null;
        var parents = _graph.Upstream(current);
        if (parents.Count > 0)
        {
            _anchor = parents[0];
            _anchorDownstream = true;
            return;
        }

        var children = _graph.Downstream(current);
        if (children.Count > 0)
        {
            _anchor = children[0];
            _anchorDownstream = false;
        }
    }

    static bool Contains(IReadOnlyList<Guid> list, Guid id) => IndexOf(list, id) >= 0;

    static int IndexOf(IReadOnlyList<Guid> list, Guid id)
    {
        for (var i = 0; i < list.Count; i++)
            if (list[i] == id) return i;
        return -1;
    }
}
