using System;
using System.Collections.Generic;
using System.Linq;
using Vimhopper.Core;
using Xunit;

namespace Vimhopper.Core.Tests;

public class GraphRangeTests
{
    /// <summary>이름으로 객체와 와이어(모두 출력 0 → 입력 0..)를 만드는 테스트용 그래프.</summary>
    sealed class Wires
    {
        readonly Dictionary<string, Guid> _ids = new();
        readonly HashSet<string> _relays = new();
        readonly List<(string From, string To, int Input)> _wires = new();

        public Wires Relay(string name)
        {
            _relays.Add(name);
            return this;
        }

        public Wires Wire(string from, string to, int input = 0)
        {
            Id(from);
            Id(to);
            _wires.Add((from, to, input));
            return this;
        }

        public Guid Id(string name) => _ids.TryGetValue(name, out var id) ? id : _ids[name] = Guid.NewGuid();

        public string[] Names(IEnumerable<Guid> ids) => ids.Select(id => _ids.First(p => p.Value == id).Key).ToArray();

        public CanvasGraph Graph() => new(_ids.Select(p =>
        {
            var name = p.Key;
            var inputCount = _wires.Where(w => w.To == name).Select(w => w.Input + 1).DefaultIfEmpty(0).Max();
            var inputs = Enumerable.Range(0, inputCount)
                .Select(i => (IReadOnlyList<ParamRef>)_wires.Where(w => w.To == name && w.Input == i)
                    .Select(w => new ParamRef(_ids[w.From], 0)).ToList())
                .ToList();
            var recipients = _wires.Where(w => w.From == name).Select(w => new ParamRef(_ids[w.To], w.Input)).ToList();
            var outputs = recipients.Count > 0
                ? new List<IReadOnlyList<ParamRef>> { recipients }
                : new List<IReadOnlyList<ParamRef>>();
            return new GraphNode(p.Value, 0, 0, 80, 40, _relays.Contains(name), inputs, outputs);
        }));
    }

    //  w ──→ w/2.0
    //  '2' → w/2.1
    //  w/2 ──→ Neg ──→ Domain.0
    //   └───────────→ Domain.1
    static Wires HalfWidthDomain() => new Wires()
        .Wire("w", "w/2", 0)
        .Wire("2", "w/2", 1)
        .Wire("w/2", "Neg")
        .Wire("Neg", "Domain", 0)
        .Wire("w/2", "Domain", 1);

    [Theory]
    [InlineData("Domain", true, 1, "Domain Neg w/2")]
    [InlineData("Domain", true, 2, "Domain Neg w/2 w 2")]
    [InlineData("Domain", true, null, "Domain Neg w/2 w 2")]
    [InlineData("w/2", false, 1, "w/2 Neg Domain")]
    [InlineData("w", false, 1, "w w/2")]
    [InlineData("w", false, null, "w w/2 Neg Domain")]
    [InlineData("w", true, null, "w")]
    public void Follows_every_wire_up_or_down_to_the_given_depth(string start, bool upstream, int? depth, string expected)
    {
        var wires = HalfWidthDomain();
        var range = GraphRange.Collect(wires.Graph(), wires.Id(start), new WireRange(upstream, depth));
        Assert.Equal(expected.Split(' ').OrderBy(n => n), wires.Names(range).OrderBy(n => n));
    }

    [Fact]
    public void Starts_with_the_cursor_and_goes_nearest_first()
    {
        var wires = HalfWidthDomain();
        var range = GraphRange.Collect(wires.Graph(), wires.Id("Domain"), new WireRange(true, null));
        var names = wires.Names(range);
        Assert.Equal("Domain", names[0]);
        Assert.True(Array.IndexOf(names, "w/2") < Array.IndexOf(names, "w"));
    }

    [Fact]
    public void Relays_are_not_counted_as_a_step()
    {
        // A → R1 → R2 → B → C (R1, R2는 풀리는 Relay)
        var wires = new Wires().Relay("R1").Relay("R2")
            .Wire("A", "R1").Wire("R1", "R2").Wire("R2", "B").Wire("B", "C");
        var range = GraphRange.Collect(wires.Graph(), wires.Id("A"), new WireRange(false, 1));
        Assert.Equal(new[] { "A", "B", "R1", "R2" }, wires.Names(range).OrderBy(n => n));
    }

    [Fact]
    public void Several_starts_share_one_range()
    {
        var wires = HalfWidthDomain();
        var range = GraphRange.Collect(wires.Graph(), new[] { wires.Id("w"), wires.Id("2") }, upstream: false, depth: 1);
        Assert.Equal(new[] { "2", "w", "w/2" }, wires.Names(range).OrderBy(n => n));
    }

    [Fact]
    public void Unknown_start_gives_nothing()
    {
        var wires = HalfWidthDomain();
        Assert.Empty(GraphRange.Collect(wires.Graph(), Guid.NewGuid(), new WireRange(true, null)));
    }
}
