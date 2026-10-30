using System;
using System.Collections.Generic;
using System.Linq;

namespace Vimhopper.Core;

/// <summary>
/// 모든 간선이 <c>번호[도착] - 번호[출발] ≥ 최소 길이</c>를 지키면서 <c>가중치 × (번호[도착] - 번호[출발])</c>의 합이
/// 가장 작은 정수 번호. Graphviz dot과 ELK의 기본 방식 (Gansner 외, "A Technique for Drawing Directed Graphs", 1993의
/// Network Simplex). 층 나누기(최소 길이 1)와 세로 위치(보조 그래프)에 쓴다.
/// </summary>
/// <remarks>
/// 순환이 없는 그래프를 받는다. 이어진 덩어리마다 따로 풀고 각 덩어리의 가장 작은 번호를 0으로 맞춘다.
/// 같은 입력에는 항상 같은 결과가 나온다 (객체·간선 순서에만 의존).
/// </remarks>
public static class NetworkSimplex
{
    public readonly struct Edge
    {
        public Edge(int tail, int head, int weight = 1, int minLength = 1)
        {
            Tail = tail;
            Head = head;
            Weight = weight;
            MinLength = minLength;
        }

        public int Tail { get; }
        public int Head { get; }
        public int Weight { get; }
        public int MinLength { get; }
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
        readonly Edge[] _edges;
        readonly int[][] _incident;
        readonly int[] _rank;
        readonly bool[] _tree;
        readonly int[] _cut;
        readonly int[] _low;
        readonly int[] _lim;
        readonly int[] _parentEdge;

        // Refresh의 깊이 우선 탐색용 (반복마다 새로 만들지 않도록)
        readonly bool[] _visited;
        readonly int[] _stackNode;
        readonly int[] _stackNext;
        readonly int[] _postOrder;

        public Solver(List<int> nodes, IReadOnlyList<Edge> all, int[] output)
        {
            _nodes = nodes;
            _output = output;
            var local = new Dictionary<int, int>();
            for (var i = 0; i < nodes.Count; i++)
                local[nodes[i]] = i;
            var edges = new List<Edge>();
            foreach (var e in all)
                if (local.TryGetValue(e.Tail, out var t) && local.TryGetValue(e.Head, out var h))
                    edges.Add(new Edge(t, h, e.Weight, e.MinLength));
            _edges = edges.ToArray();

            var n = nodes.Count;
            var incident = new List<int>[n];
            for (var i = 0; i < n; i++)
                incident[i] = new List<int>();
            for (var k = 0; k < _edges.Length; k++)
            {
                incident[_edges[k].Tail].Add(k);
                incident[_edges[k].Head].Add(k);
            }
            _incident = incident.Select(l => l.ToArray()).ToArray();

            _rank = new int[n];
            _tree = new bool[_edges.Length];
            _cut = new int[_edges.Length];
            _low = new int[n];
            _lim = new int[n];
            _parentEdge = new int[n];
            _visited = new bool[n];
            _stackNode = new int[n];
            _stackNext = new int[n];
            _postOrder = new int[n];
        }

        int Slack(int e) => _rank[_edges[e].Head] - _rank[_edges[e].Tail] - _edges[e].MinLength;

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

        /// <summary>시작점: 소스로부터의 최장 거리 (모든 간선이 최소 길이 이상).</summary>
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
                    _rank[e.Head] = Math.Max(_rank[e.Head], _rank[v] + e.MinLength);
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

            // 새로 들어온 정점에서만 팽팽한 와이어를 따라 넓힌다. 나무를 옮기면 나무 안 와이어는 그대로 팽팽하고,
            // 나무 밖과의 와이어 중 새로 팽팽해진 것은 다음 반복의 최소 여유 탐색(여유 0)에서 잡힌다.
            void Grow(int from)
            {
                var stack = new Stack<int>();
                stack.Push(from);
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

            Grow(0);
            while (members.Count < _nodes.Count)
            {
                var best = -1;
                var bestSlack = int.MaxValue;
                for (var k = 0; k < _edges.Length; k++)
                {
                    if (inTree[_edges[k].Tail] == inTree[_edges[k].Head]) continue;
                    var slack = Slack(k);
                    if (slack < bestSlack)
                    {
                        best = k;
                        bestSlack = slack;
                        if (slack == 0)
                            break;
                    }
                }

                // 나무 쪽이 와이어의 출발이면 나무를 오른쪽으로, 도착이면 왼쪽으로
                var tailInTree = inTree[_edges[best].Tail];
                if (bestSlack != 0)
                {
                    var delta = tailInTree ? bestSlack : -bestSlack;
                    foreach (var v in members)
                        _rank[v] += delta;
                }

                var added = tailInTree ? _edges[best].Head : _edges[best].Tail;
                inTree[added] = true;
                _tree[best] = true;
                members.Add(added);
                Grow(added);
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
            var start = 0;
            for (var step = 0; step < limit; step++)
            {
                var leave = Leave(ref start);
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

        // 뺄 와이어 후보를 이만큼 더 본 뒤 그중 가장 음수인 것을 고른다 (Graphviz의 Search_size)
        const int SearchSize = 30;

        /// <summary>
        /// 자르는 값이 음수인 나무 와이어. 지난번 멈춘 자리부터 돌아가며 찾고(같은 와이어를 되풀이해 빼고 넣는
        /// 순환을 피함), 처음 찾은 뒤 <see cref="SearchSize"/>개까지 더 보고 가장 음수인 것을 고른다.
        /// </summary>
        int Leave(ref int start)
        {
            var best = -1;
            var found = 0;
            for (var i = 0; i < _edges.Length; i++)
            {
                var k = (start + i) % _edges.Length;
                if (!_tree[k] || _cut[k] >= 0) continue;
                if (best < 0 || _cut[k] < _cut[best])
                    best = k;
                if (++found >= SearchSize)
                {
                    start = k + 1;
                    return best;
                }
            }
            if (best >= 0)
                start = best + 1;
            return best;
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
            for (var k = 0; k < _edges.Length; k++)
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
        /// <remarks>
        /// 반복마다 불리므로 할당 없이 미리 만든 배열로 한 번의 깊이 우선 탐색에 모두 처리한다.
        /// 후위 순서로 끝나는 정점은 자식이 모두 끝났으므로 그때 부모 와이어의 자르는 값을 계산할 수 있다.
        /// </remarks>
        void Refresh()
        {
            var n = _nodes.Count;
            Array.Clear(_visited, 0, n);
            var top = 0;
            _stackNode[0] = 0;
            _stackNext[0] = 0;
            _visited[0] = true;
            _parentEdge[0] = -1;
            var next = 1;
            _low[0] = next;

            while (top >= 0)
            {
                var v = _stackNode[top];
                var incident = _incident[v];
                var i = _stackNext[top];
                var child = -1;
                for (; i < incident.Length; i++)
                {
                    var k = incident[i];
                    if (!_tree[k]) continue;
                    var e = _edges[k];
                    var other = e.Tail == v ? e.Head : e.Tail;
                    if (_visited[other]) continue;
                    _visited[other] = true;
                    _parentEdge[other] = k;
                    _rank[other] = e.Tail == v ? _rank[v] + e.MinLength : _rank[v] - e.MinLength;
                    child = other;
                    i++;
                    break;
                }
                _stackNext[top] = i;

                if (child >= 0)
                {
                    top++;
                    _stackNode[top] = child;
                    _stackNext[top] = 0;
                    _low[child] = next;
                    continue;
                }

                // v의 부분 나무가 끝남: 후위 번호
                _lim[v] = next++;
                _postOrder[next - 2] = v;
                top--;
            }

            // 자르는 값은 모든 번호가 정해진 뒤에 (다른 쪽 끝이 부분 나무 안인지 번호로 판단하므로).
            // 후위 순서면 자식의 값이 먼저 계산된다
            for (var i = 0; i < n; i++)
            {
                var v = _postOrder[i];
                if (_parentEdge[v] >= 0)
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
