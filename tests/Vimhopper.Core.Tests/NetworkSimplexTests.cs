using System;
using System.Collections.Generic;
using System.Linq;
using Vimhopper.Core;
using Xunit;

namespace Vimhopper.Core.Tests;

public class NetworkSimplexTests
{
    static NetworkSimplex.Edge E(int tail, int head, int weight = 1) => new(tail, head, weight);

    static int Length(int[] rank, IEnumerable<NetworkSimplex.Edge> edges) =>
        edges.Sum(e => e.Weight * (rank[e.Head] - rank[e.Tail]));

    [Fact]
    public void Every_edge_goes_at_least_one_layer_right()
    {
        var edges = new[] { E(0, 1), E(1, 2), E(0, 2), E(3, 2) };
        var rank = NetworkSimplex.Rank(4, edges);

        Assert.All(edges, e => Assert.True(rank[e.Head] - rank[e.Tail] >= 1));
        Assert.Equal(0, rank.Min());
    }

    [Fact]
    public void Small_branch_moves_next_to_where_it_is_used()
    {
        // 긴 사슬 0 → 1 → 2 → 3 → 4, 슬라이더 5 → Unit Z 6 → 4 (Extrude)
        // 최장 거리면 6은 1층이지만, 와이어 길이 합을 줄이면 4 바로 앞(3층)
        var rank = NetworkSimplex.Rank(7, new[] { E(0, 1), E(1, 2), E(2, 3), E(3, 4), E(5, 6), E(6, 4) });

        Assert.Equal(rank[4] - 1, rank[6]);
        Assert.Equal(rank[6] - 1, rank[5]);
    }

    [Fact]
    public void Sources_sit_just_before_their_leftmost_consumer()
    {
        // 0 → 1 → 2 → 3, 입력값 4 → 1과 3
        var rank = NetworkSimplex.Rank(5, new[] { E(0, 1), E(1, 2), E(2, 3), E(4, 1), E(4, 3) });

        Assert.Equal(rank[1] - 1, rank[4]);
    }

    [Fact]
    public void Heavy_edges_are_kept_short()
    {
        // 0 → 1 → 2 와 0 → 2 (무거움): 두 경로가 모두 짧을 수는 없지만 0 → 2는 최소 2층
        var edges = new[] { E(0, 1), E(1, 2), E(0, 2, 10) };
        var rank = NetworkSimplex.Rank(3, edges);

        Assert.Equal(2, rank[2] - rank[0]);
    }

    [Fact]
    public void Components_are_solved_separately_from_layer_zero()
    {
        var rank = NetworkSimplex.Rank(5, new[] { E(0, 1), E(1, 2), E(3, 4) });

        Assert.Equal(new[] { 0, 1, 2, 0, 1 }, rank);
    }

    [Fact]
    public void Single_and_isolated_nodes_are_on_layer_zero()
    {
        Assert.Equal(new[] { 0 }, NetworkSimplex.Rank(1, Array.Empty<NetworkSimplex.Edge>()));
        Assert.Equal(new[] { 0, 0, 1 }, NetworkSimplex.Rank(3, new[] { E(1, 2) }));
    }

    public static IEnumerable<object[]> Seeds => Enumerable.Range(1, 40).Select(s => new object[] { s });

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Result_is_optimal_on_small_random_graphs(int seed)
    {
        // 객체 7개 이하: 가능한 층 번호를 모두 따져 본 최솟값과 같아야 한다
        var random = new Random(seed);
        var count = random.Next(2, 8);
        var edges = new List<NetworkSimplex.Edge>();
        for (var a = 0; a < count; a++)
            for (var b = a + 1; b < count; b++)
                if (random.NextDouble() < 0.35)
                    edges.Add(E(a, b, random.Next(1, 4)));

        var rank = NetworkSimplex.Rank(count, edges);
        Assert.All(edges, e => Assert.True(rank[e.Head] - rank[e.Tail] >= 1));
        Assert.Equal(BruteForce(count, edges), Length(rank, edges));
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Result_is_optimal_with_minimum_lengths_and_zero_weights(int seed)
    {
        // 세로 위치 계산처럼 최소 길이 0~3, 가중치 0~3인 간선
        var random = new Random(seed);
        var count = random.Next(2, 7);
        var edges = new List<NetworkSimplex.Edge>();
        for (var a = 0; a < count; a++)
            for (var b = a + 1; b < count; b++)
                if (random.NextDouble() < 0.45)
                    edges.Add(new NetworkSimplex.Edge(a, b, random.Next(0, 4), random.Next(0, 4)));

        var rank = NetworkSimplex.Rank(count, edges);
        Assert.All(edges, e => Assert.True(rank[e.Head] - rank[e.Tail] >= e.MinLength));
        Assert.Equal(BruteForce(count, edges, maxRank: 10), Length(rank, edges));
    }

    [Fact]
    public void Same_input_gives_same_result()
    {
        var random = new Random(7);
        var edges = new List<NetworkSimplex.Edge>();
        for (var a = 0; a < 60; a++)
            for (var b = a + 1; b < 60; b++)
                if (random.NextDouble() < 0.06)
                    edges.Add(E(a, b));

        Assert.Equal(NetworkSimplex.Rank(60, edges), NetworkSimplex.Rank(60, edges));
    }

    static int BruteForce(int count, List<NetworkSimplex.Edge> edges, int? maxRank = null)
    {
        var best = int.MaxValue;
        var rank = new int[count];
        void Try(int i)
        {
            if (i == count)
            {
                if (edges.All(e => rank[e.Head] - rank[e.Tail] >= e.MinLength))
                    best = Math.Min(best, Length(rank, edges));
                return;
            }
            for (var r = 0; r < (maxRank ?? count); r++)
            {
                rank[i] = r;
                Try(i + 1);
            }
        }
        Try(0);
        return best;
    }
}
