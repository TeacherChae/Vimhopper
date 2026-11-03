// usage: dotnet run --project tools/layout-bench -- [--no-results] [graph.json] [out.json]
// graph.json의 원래 배치를 덩어리 하나로 정렬한 좌표를 { "id": [x, y] }로 쓴다.
// 결과 확인용 패널 판정은 run.mjs와 같다: 입력은 연결되고 출력은 아무 데도 안 가는 Panel.
// --no-results면 결과 패널도 흐름에 넣는다 (결과 패널 빼기 이전과 비교용).
using System.Text.Json;
using Vimhopper.Core;

var noResults = args.Contains("--no-results");
var files = args.Where(a => !a.StartsWith("--")).ToList();
var input = files.Count > 0 ? files[0] : "tools/elk-experiment/graph.json";
var output = files.Count > 1 ? files[1] : "tools/elk-experiment/vimhopper.json";

using var doc = JsonDocument.Parse(File.ReadAllText(input));
var root = doc.RootElement;

var edgeList = root.GetProperty("edges").EnumerateArray()
    .Select(e => (From: e[0].GetString()!, Out: e[1].GetInt32(), To: e[2].GetString()!, In: e[3].GetInt32()))
    .ToList();
var hasIn = edgeList.Select(e => e.To).ToHashSet();
var hasOut = edgeList.Select(e => e.From).ToHashSet();

var ids = new Dictionary<string, Guid>();
var grips = new Dictionary<(string Node, string Side, int Index), float>();
var nodes = new List<LayoutNode>();
foreach (var n in root.GetProperty("nodes").EnumerateArray())
{
    var id = n.GetProperty("id").GetString()!;
    ids[id] = Guid.Parse(id);
    foreach (var side in new[] { "inputs", "outputs" })
    {
        var k = 0;
        foreach (var p in n.GetProperty(side).EnumerateArray())
            grips[(id, side, k++)] = p.GetProperty("dy").GetSingle();
    }
    var isResult = !noResults && n.GetProperty("name").GetString() == "Panel" && hasIn.Contains(id) && !hasOut.Contains(id);
    nodes.Add(new LayoutNode(
        ids[id], n.GetProperty("x").GetSingle(), n.GetProperty("y").GetSingle(),
        n.GetProperty("w").GetSingle(), n.GetProperty("h").GetSingle(), isResult: isResult));
}

// 포트 id는 (객체, 방향, 번호)마다 하나
var ports = new Dictionary<(string, string, int), Guid>();
Guid Port(string node, string side, int index) =>
    ports.TryGetValue((node, side, index), out var g) ? g : ports[(node, side, index)] = Guid.NewGuid();

var edges = edgeList
    .Select(e => new LayoutEdge(
        ids[e.From], Port(e.From, "outputs", e.Out), grips[(e.From, "outputs", e.Out)],
        ids[e.To], Port(e.To, "inputs", e.In), grips[(e.To, "inputs", e.In)]))
    .ToList();

var plan = CanvasLayout.Plan(nodes, edges);
var result = ids.ToDictionary(p => p.Key, p => new[] { plan.Positions[p.Value].X, plan.Positions[p.Value].Y });
File.WriteAllText(output, JsonSerializer.Serialize(new { positions = result, relays = plan.Relays.Count }));
Console.WriteLine($"wrote {output} ({result.Count} objects, {plan.Relays.Count} relays)");
