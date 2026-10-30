using System;
using System.Collections.Generic;
using System.Linq;

namespace Vimhopper.Core;

/// <summary>
/// 층 나누기: 모든 와이어가 왼쪽→오른쪽으로 한 층 이상 가면서 <c>가중치 × 건너는 층 수</c>의 합이 가장 작은 층 번호.
/// Graphviz dot과 ELK의 기본 방식 (Gansner 외, "A Technique for Drawing Directed Graphs", 1993의 Network Simplex).
/// </summary>
/// <remarks>
/// 순환이 없는 그래프를 받는다. 이어진 덩어리마다 따로 풀고 각 덩어리의 가장 왼쪽 층을 0으로 맞춘다.
/// 같은 입력에는 항상 같은 결과가 나온다 (객체·와이어 순서에만 의존).
/// </remarks>
public static class NetworkSimplex
{
    public readonly struct Edge
    {
        public Edge(int tail, int head, int weight = 1)
        {
            Tail = tail;
            Head = head;
            Weight = weight;
        }

        public int Tail { get; }
        public int Head { get; }
        public int Weight { get; }
    }

    public static int[] Rank(int count, IReadOnlyList<Edge> edges)
    {
        var rank = new int[count];
        foreach (var component in Components(count, edges))
            new Solver(component, edges, rank).Run();
        return rank;
    }

    /// <summary>와이어 방향을 무시하고 이어진 객체 묶음 (객체 번호 순서대로).</summary>
    static List<List<int>> Components(int count, IReadOnlyList<Edge> edges)
    {
        var parent = Enumerable.Range(0, count).ToArray();
        int Find(int i)
        {
            while (parent[i] != i)
                i = parent[i] = parent[parent[i]];
            return i;
        }

        foreach (var e in edges)
        {
            var a = Find(e.Tail);
            var b = Find(e.Head);
            if (a != b)
                parent[Math.Max(a, b)] = Math.Min(a, b);
        }
        return Enumerable.Range(0, count).GroupBy(Find).Select(g => g.ToList()).ToList();
    }

    /// <summary>덩어리 하나. 객체 번호는 덩어리 안 번호(0..n-1)로 바꿔서 푼다.</summary>
    sealed class Solver
    {
        readonly List<int> _nodes;
        readonly int[] _output;
        readonly List<Edge> _edges = new();
        readonly List<int>[] _incident;
        readonly int[] _rank;
        readonly bool[] _tree;
        readonly int[] _cut;
        readonly int[] _low;
        readonly int[] _lim;
        readonly int[] _parentEdge;

        public Solver(List<int> nodes, IReadOnlyList<Edge> all, int[] output)
        {
            _nodes = nodes;
            _output = output;
            var local = new Dictionary<int, int>();
            for (var i = 0; i < nodes.Count; i++)
                local[nodes[i]] = i;
            foreach (var e in all)
                if (local.TryGetValue(e.Tail, out var t) && local.TryGetValue(e.Head, out var h))
                    _edges.Add(new Edge(t, h, e.Weight));

            var n = nodes.Count;
            _incident = new List<int>[n];
            for (var i = 0; i < n; i++)
                _incident[i] = new List<int>();
            for (var k = 0; k < _edges.Count; k++)
            {
                _incident[_edges[k].Tail].Add(k);
                _incident[_edges[k].Head].Add(k);
            }

            _rank = new int[n];
            _tree = new bool[_edges.Count];
            _cut = new int[_edges.Count];
            _low = new int[n];
            _lim = new int[n];
            _parentEdge = new int[n];
        }

        int Slack(int e) => _rank[_edges[e].Head] - _rank[_edges[e].Tail] - 1;

        public void Run()
        {
            if (_nodes.Count > 1)
            {
                InitRank();
                FeasibleTree();
                Optimize();
            }

            var min = _rank.Min();
            for (var i = 0; i < _nodes.Count; i++)
                _output[_nodes[i]] = _rank[i] - min;
        }

        /// <summary>시작점: 소스로부터의 최장 거리 (모든 와이어가 한 층 이상).</summary>
        void InitRank()
        {
            var indegree = new int[_nodes.Count];
            foreach (var e in _edges)
                indegree[e.Head]++;
            var queue = new Queue<int>(Enumerable.Range(0, _nodes.Count).Where(i => indegree[i] == 0));
            while (queue.Count > 0)
            {
                var v = queue.Dequeue();
                foreach (var k in _incident[v])
                {
                    var e = _edges[k];
                    if (e.Tail != v) continue;
                    _rank[e.Head] = Math.Max(_rank[e.Head], _rank[v] + 1);
                    if (--indegree[e.Head] == 0)
                        queue.Enqueue(e.Head);
                }
            }
        }

        /// <summary>
        /// 여유(slack)가 0인 와이어만으로 모든 객체를 잇는 나무를 만든다. 나무 밖 객체와의 와이어 중 여유가
        /// 가장 작은 것이 팽팽해지도록 나무 전체를 옮기기를 반복한다.
        /// </summary>
        void FeasibleTree()
        {
            var inTree = new bool[_nodes.Count];
            var members = new List<int> { 0 };
            inTree[0] = true;

            void Grow()
            {
                var stack = new Stack<int>(members);
                while (stack.Count > 0)
                {
                    var v = stack.Pop();
                    foreach (var k in _incident[v])
                    {
                        var other = _edges[k].Tail == v ? _edges[k].Head : _edges[k].Tail;
                        if (inTree[other] || Slack(k) != 0) continue;
                        inTree[other] = true;
                        _tree[k] = true;
                        members.Add(other);
                        stack.Push(other);
                    }
                }
            }

            Grow();
            while (members.Count < _nodes.Count)
            {
                var best = -1;
                for (var k = 0; k < _edges.Count; k++)
                {
                    if (inTree[_edges[k].Tail] == inTree[_edges[k].Head]) continue;
                    if (best < 0 || Slack(k) < Slack(best))
                        best = k;
                }

                // 나무 쪽이 와이어의 출발이면 나무를 오른쪽으로, 도착이면 왼쪽으로
                var delta = inTree[_edges[best].Tail] ? Slack(best) : -Slack(best);
                foreach (var v in members)
                    _rank[v] += delta;
                Grow();
            }
        }

        /// <summary>
        /// 자르는 값(cut value)이 음수인 나무 와이어를 빼고, 같은 두 쪽을 잇는 와이어 중 여유가 가장 작은 것을
        /// 넣기를 반복한다. 음수가 없으면 최적.
        /// </summary>
        void Optimize()
        {
            Refresh();
            var limit = 10 * _nodes.Count + 100;
            for (var step = 0; step < limit; step++)
            {
                var leave = -1;
                for (var k = 0; k < _edges.Count; k++)
                    if (_tree[k] && _cut[k] < 0 && (leave < 0 || _cut[k] < _cut[leave]))
                        leave = k;
                if (leave < 0)
                    return;

                var enter = Enter(leave);
                if (enter < 0)
                    return;
                _tree[leave] = false;
                _tree[enter] = true;
                Refresh();
            }
        }

        /// <summary>
        /// 뺄 와이어가 나무를 출발 쪽과 도착 쪽으로 나눌 때, 도착 쪽 → 출발 쪽으로 가는 나무 밖 와이어 중
        /// 여유가 가장 작은 것.
        /// </summary>
        int Enter(int leave)
        {
            var tail = _edges[leave].Tail;
            var head = _edges[leave].Head;
            // 뿌리에서 먼 쪽(나무에서 아래쪽)의 부분 나무
            var sub = _lim[tail] < _lim[head] ? tail : head;
            var subIsTail = sub == tail;
            bool InSub(int v) => _low[sub] <= _lim[v] && _lim[v] <= _lim[sub];

            var best = -1;
            for (var k = 0; k < _edges.Count; k++)
            {
                if (_tree[k]) continue;
                var e = _edges[k];
                // 부분 나무가 출발 쪽이면 나머지 → 부분 나무, 도착 쪽이면 부분 나무 → 나머지
                var crosses = subIsTail
                    ? !InSub(e.Tail) && InSub(e.Head)
                    : InSub(e.Tail) && !InSub(e.Head);
                if (!crosses) continue;
                if (best < 0 || Slack(k) < Slack(best))
                    best = k;
            }
            return best;
        }

        /// <summary>나무 와이어가 모두 팽팽하도록 층을 다시 매기고, 번호(low/lim)와 자르는 값을 다시 계산.</summary>
        void Refresh()
        {
            var n = _nodes.Count;
            var children = new List<int>[n];
            for (var i = 0; i < n; i++)
                children[i] = new List<int>();

            // 뿌리 0에서 나무를 따라가며 층을 매기고 부모 와이어를 기록
            var order = new List<int>(n);
            var visited = new bool[n];
            var stack = new Stack<int>();
            stack.Push(0);
            visited[0] = true;
            _parentEdge[0] = -1;
            while (stack.Count > 0)
            {
                var v = stack.Pop();
                order.Add(v);
                foreach (var k in _incident[v])
                {
                    if (!_tree[k]) continue;
                    var e = _edges[k];
                    var other = e.Tail == v ? e.Head : e.Tail;
                    if (visited[other]) continue;
                    visited[other] = true;
                    _parentEdge[other] = k;
                    _rank[other] = e.Tail == v ? _rank[v] + 1 : _rank[v] - 1;
                    children[v].Add(other);
                    stack.Push(other);
                }
            }

            // 후위 순서 번호: 부분 나무는 [low, lim] 구간
            var next = 1;
            void Number(int v)
            {
                _low[v] = next;
                foreach (var c in children[v])
                    Number(c);
                _lim[v] = next++;
            }
            Number(0);

            // 자식부터: 부모 와이어의 자르는 값
            for (var i = order.Count - 1; i > 0; i--)
            {
                var v = order[i];
                _cut[_parentEdge[v]] = CutValue(v, _parentEdge[v]);
            }
        }

        /// <summary>
        /// 나무 와이어 f를 빼서 v 쪽 부분 나무와 나머지로 나눌 때, 출발 쪽 → 도착 쪽 와이어의 가중치 합에서
        /// 반대 방향 합을 뺀 값. v의 자식 와이어 값은 이미 계산되어 있어야 한다 (Graphviz x_cutval).
        /// </summary>
        int CutValue(int v, int f)
        {
            var dir = _edges[f].Tail == v ? 1 : -1;
            var sum = 0;
            foreach (var k in _incident[v])
            {
                var e = _edges[k];
                var other = e.Tail == v ? e.Head : e.Tail;
                var outside = !(_low[v] <= _lim[other] && _lim[other] <= _lim[v]);
                int value;
                if (outside)
                {
                    value = e.Weight;
                }
                else
                {
                    value = _tree[k] ? _cut[k] : 0;
                    value -= e.Weight;
                }

                int d;
                if (dir > 0)
                    d = e.Head == v ? 1 : -1;
                else
                    d = e.Tail == v ? 1 : -1;
                if (outside)
                    d = -d;
                sum += d < 0 ? -value : value;
            }
            return sum;
        }
    }
}
