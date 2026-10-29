"""ghx에서 지정한 객체들의 그래프를 뽑는다: 크기, 그립 위치(객체 위쪽 기준), 와이어.
usage: python -I extract.py file.ghx out.json guid ...
Relay는 풀어서 앞뒤를 직결한다."""
import json
import sys
import xml.etree.ElementTree as ET

path, out_path, wanted = sys.argv[1], sys.argv[2], sys.argv[3:]
wanted_set = set(wanted)
root = ET.parse(path).getroot()


def items(chunk):
    out = {}
    its = chunk.find("items")
    if its is not None:
        for it in its.findall("item"):
            out.setdefault(it.get("name"), []).append(it)
    return out


def text(chunk, name):
    v = items(chunk).get(name)
    return v[0].text if v else None


def chunks(chunk, name=None):
    cs = chunk.find("chunks")
    if cs is None:
        return []
    return [c for c in cs.findall("chunk") if name is None or c.get("name") == name]


def bounds(chunk):
    attrs = chunks(chunk, "Attributes")
    if not attrs:
        return None
    b = items(attrs[0]).get("Bounds")
    if not b:
        return None
    b = b[0]
    return tuple(float(b.find(k).text) for k in ("X", "Y", "W", "H"))


def params(container):
    ins = chunks(container, "param_input")
    outs = chunks(container, "param_output")
    pdata = chunks(container, "ParameterData")
    if pdata:
        ins = chunks(pdata[0], "InputParam")
        outs = chunks(pdata[0], "OutputParam")
    return ins, outs


param_owner = {}  # param guid -> (object guid, index)
objs = {}
defobjs = [c for c in root.iter("chunk") if c.get("name") == "DefinitionObjects"][0]
for obj in chunks(defobjs, "Object"):
    container = chunks(obj, "Container")[0]
    gid = text(container, "InstanceGuid")
    ob = bounds(container)
    ins, outs = params(container)
    node = {"id": gid, "name": text(obj, "Name"), "nick": text(container, "NickName"),
            "x": ob[0] if ob else 0, "y": ob[1] if ob else 0,
            "w": ob[2] if ob else 0, "h": ob[3] if ob else 0, "inputs": [], "outputs": []}
    if not ins and not outs:
        # 단독 파라미터(슬라이더, 패널, Relay, Curve): 그립은 세로 가운데
        srcs = [i.text for i in items(container).get("Source", [])]
        if node["name"] != "Number Slider":
            node["inputs"].append({"dy": node["h"] / 2, "sources": srcs})
        node["outputs"].append({"dy": node["h"] / 2})
        param_owner[gid] = (gid, 0)
    for k, p in enumerate(ins):
        pb = bounds(p)
        node["inputs"].append({"dy": pb[1] + pb[3] / 2 - node["y"],
                               "sources": [i.text for i in items(p).get("Source", [])]})
        param_owner[text(p, "InstanceGuid")] = (gid, k)
    for k, p in enumerate(outs):
        pb = bounds(p)
        node["outputs"].append({"dy": pb[1] + pb[3] / 2 - node["y"]})
        param_owner[text(p, "InstanceGuid")] = (gid, k)
    objs[gid] = node

# 와이어 (출처 객체, 출력 번호) → (받는 객체, 입력 번호)
edges = []
for gid in wanted:
    node = objs[gid]
    for k, inp in enumerate(node["inputs"]):
        for s in inp["sources"]:
            if s in param_owner and param_owner[s][0] in wanted_set:
                src, oi = param_owner[s]
                edges.append([src, oi, gid, k])

# Relay 풀기: Relay로 들어오는 출처를 Relay가 보내던 곳에 직결
relays = [g for g in wanted if objs[g]["name"] == "Relay"]
for r in relays:
    srcs = [(e[0], e[1]) for e in edges if e[2] == r]
    if not srcs:  # 출처가 선택 밖이면 Relay 출처를 직접 찾는다
        for s in objs[r]["inputs"][0]["sources"]:
            if s in param_owner and param_owner[s][0] in wanted_set:
                srcs.append(param_owner[s])
    outs = [e for e in edges if e[0] == r]
    edges = [e for e in edges if r not in (e[0], e[2])]
    for src in srcs:
        for e in outs:
            edges.append([src[0], src[1], e[2], e[3]])

nodes = [objs[g] for g in wanted if g not in relays]
for n in nodes:
    for i in n["inputs"]:
        i.pop("sources", None)
json.dump({"nodes": nodes, "edges": edges}, open(out_path, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
print(f"{len(nodes)} nodes, {len(edges)} edges, relays dissolved: {len(relays)}")
