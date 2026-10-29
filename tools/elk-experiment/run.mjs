// ELK로 여러 옵션 조합을 돌려 손으로 정리한 배치·지금 == 결과와 비교한다.
// 배경과 결과: docs/formatting-research.md
// usage: npm install && node run.mjs [배치 번호...]   (번호를 주면 그 배치를 열 순서로 출력)
import { createRequire } from "module";
import fs from "fs";
import path from "path";
import { fileURLToPath } from "url";

const dir = path.dirname(fileURLToPath(import.meta.url));
const require = createRequire(import.meta.url);
const ELK = require("elkjs/lib/elk.bundled.js");
const elk = new ELK();

const A = JSON.parse(fs.readFileSync(path.join(dir, "graph.json"), "utf8"));
const nodes = A.nodes, edges = A.edges;
const byId = Object.fromEntries(nodes.map(n => [n.id, n]));
const label = n => (n.nick && n.nick !== n.name && n.nick !== "None" ? n.nick : n.name);

// 결과 확인용 패널: 입력은 연결되고 출력은 아무 데도 안 가는 Panel
const hasIn = new Set(edges.map(e => e[2])), hasOut = new Set(edges.map(e => e[0]));
const monitors = new Set(nodes.filter(n => n.name === "Panel" && hasIn.has(n.id) && !hasOut.has(n.id)).map(n => n.id));

// 줄기: 끝 객체에서 연결된 맨 위 입력을 거슬러 오른 경로 중 가장 긴 것
function topSource(id) {
  const ins = edges.filter(e => e[2] === id).sort((a, b) => a[3] - b[3]);
  return ins[0];
}
let trunk = [];
for (const n of nodes) {
  if (hasOut.has(n.id) || monitors.has(n.id)) continue;
  const path = [];
  for (let e = topSource(n.id); e; e = topSource(e[0])) path.push(e);
  if (path.length > trunk.length) trunk = path;
}
const trunkSet = new Set(trunk);

// 지금 == 결과 (gy로 받은 좌표)
const current = {
  "29735118": [349, 1114], "2b7cff87": [349, 1150], "2846219a": [392, 1186], "e2ea9924": [596, 1076],
  "407ebb20": [739, 1182], "c51a3def": [742, 1112], "dd28f0c3": [753, 1036], "52cff86f": [843, 1034],
  "52aa8cda": [990, 1341], "8eb77d4d": [1060, 1170], "b7d66356": [1060, 1318], "d6a7dda4": [1155, 1090],
  "c3732b05": [1260, 1340], "1ead773a": [1260, 1492], "bdca036d": [1267, 1376], "eb3089ea": [1363, 1054],
  "7bd51a8e": [1467, 1708], "315f9d3f": [1467, 1744], "54219374": [1565, 1318], "637820cd": [1670, 1834],
  "c68116a5": [1765, 1734], "acba1489": [1766, 1318], "74abdbe1": [1870, 1824], "ebf6ac0e": [1963, 1744],
  "60be7820": [2070, 1749], "a33d4d70": [2175, 1757],
};
// 손으로 정리한 배치 (gy로 받은 좌표, 원본 객체 순서와 같은 순서)
const desired = [
  [624, 2483], [348, 2512], [348, 2543], [348, 2563], [613, 2541], [732, 2481], [990, 2311], [1202, 2311],
  [1023, 2543], [1085, 2719], [1085, 2521], [1297, 2675], [1195, 2763], [1300, 2529], [1660, 2311], [1417, 2685],
  [1756, 2685], [1421, 2531], [1538, 2563], [1984, 2311], [1755, 2521], [1876, 2629], [2077, 2531], [2080, 2625],
  [2183, 2536], [2321, 2544],
];
const layouts = [];
layouts.push({ name: "손으로 정리", pos: Object.fromEntries(nodes.map((n, i) => [n.id, desired[i]])) });
layouts.push({ name: "지금 == (Relay 3개는 생략)", pos: Object.fromEntries(nodes.map(n => [n.id, current[n.id.slice(0, 8)]])) });

// 와이어 가중치. tiers면 줄기 100 > 입력값에서 나가는 와이어 10 > 맨 위 입력 5 > 나머지 1
const isInputValue = id => byId[id].inputs.length === 0 || !hasIn.has(id);
function weight(e, tiers) {
  if (trunkSet.has(e)) return 100;
  if (!tiers) return 1;
  if (isInputValue(e[0])) return 10;
  if (topSource(e[2]) === e) return 5;
  return 1;
}

function elkGraph(opts) {
  return {
    id: "root",
    layoutOptions: {
      "elk.algorithm": "layered", "elk.direction": "RIGHT",
      "elk.edgeRouting": "SPLINES",
      "elk.layered.spacing.nodeNodeBetweenLayers": "40",
      "elk.spacing.nodeNode": "20",
      "elk.layered.spacing.edgeNodeBetweenLayers": "10",
      ...opts.layout,
    },
    children: nodes.map(n => ({
      id: n.id, width: n.w, height: n.h,
      layoutOptions: {
        "elk.portConstraints": "FIXED_POS",
        ...(opts.comments && monitors.has(n.id) ? { "elk.commentBox": "true" } : {}),
      },
      ports: [
        ...n.inputs.map((p, k) => ({ id: `${n.id}:i${k}`, x: 0, y: p.dy, width: 0, height: 0, layoutOptions: { "elk.port.side": "WEST" } })),
        ...n.outputs.map((p, k) => ({ id: `${n.id}:o${k}`, x: n.w, y: p.dy, width: 0, height: 0, layoutOptions: { "elk.port.side": "EAST" } })),
      ],
    })),
    edges: edges.map((e, k) => ({
      id: `e${k}`, sources: [`${e[0]}:o${e[1]}`], targets: [`${e[2]}:i${e[3]}`],
      layoutOptions: opts.trunk ? { "elk.layered.priority.straightness": String(weight(e, opts.tiers)) } : {},
    })),
  };
}

const variants = [
  { name: "ELK: 최장 거리 열 나누기 (지금 방식에 가까움)", layout: { "elk.layered.layering.strategy": "LONGEST_PATH_SOURCE" } },
  { name: "ELK 기본 (Network Simplex 열 나누기 + BK 세로 배치)", layout: {} },
  { name: "ELK 기본 + 결과 패널을 주석 상자로", layout: {}, comments: true },
  { name: "ELK + 주석 상자 + 줄기 가중치 (BK)", layout: {}, comments: true, trunk: true },
  { name: "ELK + 주석 상자 + 줄기 가중치 (Network Simplex 세로 배치)", layout: { "elk.layered.nodePlacement.strategy": "NETWORK_SIMPLEX" }, comments: true, trunk: true },
  { name: "ELK + 주석 상자 + 줄기 가중치 (Linear Segments)", layout: { "elk.layered.nodePlacement.strategy": "LINEAR_SEGMENTS" }, comments: true, trunk: true },
  { name: "ELK + 주석 상자 + 단계별 가중치 (줄기 > 입력값 > 맨 위 입력, Network Simplex)", layout: { "elk.layered.nodePlacement.strategy": "NETWORK_SIMPLEX" }, comments: true, trunk: true, tiers: true },
  { name: "위와 같음 + 열 간격 55", layout: { "elk.layered.nodePlacement.strategy": "NETWORK_SIMPLEX", "elk.layered.spacing.nodeNodeBetweenLayers": "55" }, comments: true, trunk: true, tiers: true },
];
for (const v of variants) {
  const res = await elk.layout(elkGraph(v));
  layouts.push({ name: v.name, pos: Object.fromEntries(res.children.map(c => [c.id, [c.x, c.y]])) });
}

// ───── 지표 ─────
const gripY = (pos, id, side, k) => pos[id][1] + byId[id][side][k].dy;
function metrics(pos) {
  let straight = 0, trunkStraight = 0, slack = 0;
  for (const e of edges) {
    const d = Math.abs(gripY(pos, e[0], "outputs", e[1]) - gripY(pos, e[2], "inputs", e[3]));
    if (d < 1.5) { straight++; if (trunkSet.has(e)) trunkStraight++; }
    slack += d;
  }
  const flow = nodes.filter(n => !monitors.has(n.id));
  const ys = flow.flatMap(n => [pos[n.id][1], pos[n.id][1] + n.h]);
  const xs = nodes.flatMap(n => [pos[n.id][0], pos[n.id][0] + n.w]);
  const find = nick => nodes.find(n => label(n) === nick).id;
  const uz = find("Z"), ex = find("Extr");
  const uzGap = pos[ex][0] - (pos[uz][0] + byId[uz].w);
  // 줄기 와이어 양 끝 그립의 세로 범위
  const ty = trunk.flatMap(e => [gripY(pos, e[0], "outputs", e[1]), gripY(pos, e[2], "inputs", e[3])]);
  return {
    straight: `${straight}/${edges.length}`, trunk: `${trunkStraight}/${trunk.length}`,
    height: Math.round(Math.max(...ys) - Math.min(...ys)), width: Math.round(Math.max(...xs) - Math.min(...xs)),
    slack: Math.round(slack), uzGap: Math.round(uzGap), trunkSpread: Math.round(Math.max(...ty) - Math.min(...ty)),
  };
}

console.log("monitors:", [...monitors].map(id => label(byId[id]) + "<-" + label(byId[edges.find(e => e[2] === id)[0]])).join(", "));
console.log("trunk:", [byId[trunk.at(-1)[0]], ...trunk.slice().reverse().map(e => byId[e[2]])].map(label).join(" → "));
console.log("| 배치 | 수평 와이어 | 줄기 수평 | 흐름 높이 | 전체 폭 | 와이어 세로 차 합 | 줄기 높이 편차 | Unit Z→Extrude 거리 |");
for (const l of layouts) {
  const m = metrics(l.pos);
  l.metrics = m;
  console.log(`| ${l.name} | ${m.straight} | ${m.trunk} | ${m.height} | ${m.width} | ${m.slack} | ${m.trunkSpread} | ${m.uzGap} |`);
}

// ───── 그림 ─────
function svg(l) {
  const xs = nodes.flatMap(n => [l.pos[n.id][0], l.pos[n.id][0] + n.w]);
  const ys = nodes.flatMap(n => [l.pos[n.id][1], l.pos[n.id][1] + n.h]);
  const x0 = Math.min(...xs) - 20, y0 = Math.min(...ys) - 20;
  const W = Math.max(...xs) - x0 + 20, H = Math.max(...ys) - y0 + 20;
  const parts = [];
  for (const e of edges) {
    const s = byId[e[0]], t = byId[e[2]];
    const x1 = l.pos[e[0]][0] + s.w, y1 = gripY(l.pos, e[0], "outputs", e[1]);
    const x2 = l.pos[e[2]][0], y2 = gripY(l.pos, e[2], "inputs", e[3]);
    const c = Math.max(Math.abs(x2 - x1) * 0.5, 20);
    const flat = Math.abs(y1 - y2) < 1.5;
    parts.push(`<path d="M${x1 - x0},${y1 - y0} C${x1 - x0 + c},${y1 - y0} ${x2 - x0 - c},${y2 - y0} ${x2 - x0},${y2 - y0}" class="wire${trunkSet.has(e) ? " trunk" : ""}${flat ? " flat" : ""}"/>`);
  }
  for (const n of nodes) {
    const [x, y] = l.pos[n.id];
    const cls = monitors.has(n.id) ? "node monitor" : n.inputs.length === 0 || n.name === "Panel" && !hasIn.has(n.id) ? "node input" : "node";
    parts.push(`<g><rect x="${x - x0}" y="${y - y0}" width="${n.w}" height="${n.h}" rx="3" class="${cls}"/><text x="${x - x0 + n.w / 2}" y="${y - y0 + n.h / 2 + 4}">${label(n)}</text></g>`);
  }
  return `<svg viewBox="0 0 ${W} ${H}" width="${W}" height="${H}">${parts.join("")}</svg>`;
}
const html = `<!doctype html><meta charset="utf-8"><title>ELK 정렬 실험</title>
<style>
:root{--bg:#fff;--fg:#1d1d1f;--muted:#6e6e73;--node:#e8e8ed;--stroke:#8e8e93;--input:#dbe9ff;--monitor:#fff3cd;--wire:#9a9aa0;--trunk:#d9480f;--flat:#2b8a3e}
@media (prefers-color-scheme:dark){:root{--bg:#161618;--fg:#f2f2f7;--muted:#98989d;--node:#2c2c2e;--stroke:#636366;--input:#1f3a5f;--monitor:#4a3f1a;--wire:#636366;--trunk:#ff8a50;--flat:#63e6be}}
body{background:var(--bg);color:var(--fg);font:14px system-ui,sans-serif;margin:24px}
section{margin:0 0 40px}h2{font-size:16px;margin:0 0 4px}.m{color:var(--muted);margin:0 0 8px}
.scroll{overflow-x:auto;border:1px solid var(--stroke);border-radius:8px}
svg text{font-size:10px;fill:var(--fg);text-anchor:middle}
.node{fill:var(--node);stroke:var(--stroke)}.input{fill:var(--input)}.monitor{fill:var(--monitor)}
.wire{fill:none;stroke:var(--wire);stroke-width:1.5}.trunk{stroke:var(--trunk);stroke-width:2.5}.flat{stroke-dasharray:none}
.wire.flat:not(.trunk){stroke:var(--flat)}
</style>
<h1>ELK 정렬 실험: panel_dividing</h1>
<p class="m">주황: 줄기 와이어 · 초록: 수평인 와이어 · 파랑 상자: 입력값 · 노랑 상자: 결과 확인용 패널. Relay는 모두 풀고 직결해서 그렸습니다.</p>
${layouts.map(l => `<section><h2>${l.name}</h2><p class="m">수평 와이어 ${l.metrics.straight} · 줄기 수평 ${l.metrics.trunk} · 흐름 높이 ${l.metrics.height} · Unit Z→Extrude ${l.metrics.uzGap}px</p><div class="scroll">${svg(l)}</div></section>`).join("")}`;
fs.writeFileSync(path.join(dir, "compare.html"), html);

// 지정한 배치를 열(x) 순서로 텍스트 출력
for (const pick of process.argv.slice(2)) {
  const l = layouts[Number(pick)];
  console.log(`\n== ${l.name}`);
  const x0 = Math.min(...nodes.map(n => l.pos[n.id][0])), y0 = Math.min(...nodes.map(n => l.pos[n.id][1]));
  for (const n of [...nodes].sort((a, b) => l.pos[a.id][0] + a.w - l.pos[b.id][0] - b.w || l.pos[a.id][1] - l.pos[b.id][1]))
    console.log(`${String(Math.round(l.pos[n.id][0] - x0)).padStart(5)}..${String(Math.round(l.pos[n.id][0] + n.w - x0)).padEnd(5)} y=${String(Math.round(l.pos[n.id][1] - y0)).padStart(4)}  ${monitors.has(n.id) ? "[결과패널] " : ""}${label(n)}`);
}
console.log("wrote compare.html");
