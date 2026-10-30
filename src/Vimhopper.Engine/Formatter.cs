using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Undo;
using Vimhopper.Core;

namespace Vimhopper;

/// <summary>정렬로 바뀐 것의 개수. 모두 0이면 이미 정렬된 상태였다.</summary>
internal readonly record struct FormatResult(int Moved, int RelaysAdded, int RelaysRemoved)
{
    public bool Changed => Moved > 0 || RelaysAdded > 0 || RelaysRemoved > 0;
}

/// <summary>
/// GH_Document 객체를 CanvasLayout 입력으로 바꾸고, 계획대로 객체를 옮기고 Relay를 다시 놓는다.
/// </summary>
/// <remarks>
/// 범위 안의 Relay는 사용자가 넣은 것도 모두 풀었다가 필요한 곳에만 다시 놓는다.
/// 단, 데이터 연산(flatten 등)이 걸렸거나 한쪽이 연결되지 않은 Relay는 풀면 결과가 바뀌므로 일반 객체로 둔다.
/// 모든 변경은 실행 취소 기록 하나로 남는다.
/// </remarks>
internal static class Formatter
{
    const string UndoName = "Vimhopper format";

    // 이보다 작게 어긋난 위치는 이미 맞는 것으로 본다
    const float Epsilon = 0.5f;

    public static FormatResult Format(GH_Document doc, IReadOnlyCollection<IGH_DocumentObject> objects, bool separateIslands)
    {
        var scope = objects.ToList();
        var ids = new HashSet<Guid>(scope.Select(o => o.InstanceGuid));
        var dissolvable = new HashSet<GH_Relay>(scope.OfType<GH_Relay>().Where(CanDissolve));
        var relay = RelayShape.Measure();
        var options = new LayoutOptions { RelayWidth = relay.Width, RelayHeight = relay.Height };

        var nodes = scope
            .Select(o =>
            {
                var b = o.Attributes.Bounds;
                return new LayoutNode(o.InstanceGuid, b.X, b.Y, b.Width, b.Height, o is GH_Relay r && dissolvable.Contains(r));
            })
            .ToList();

        var outputs = new Dictionary<Guid, IGH_Param>();
        var edges = new List<LayoutEdge>();
        foreach (var obj in scope)
        {
            foreach (var output in VimController.Params(obj, ParamSide.Output))
                outputs[output.InstanceGuid] = output;

            var top = obj.Attributes.Bounds.Y;
            foreach (var input in VimController.Params(obj, ParamSide.Input))
            {
                foreach (var source in input.Sources)
                {
                    var owner = Owner(source);
                    if (owner == obj || !ids.Contains(owner.InstanceGuid)) continue;
                    edges.Add(new LayoutEdge(
                        owner.InstanceGuid, source.InstanceGuid, source.Attributes.OutputGrip.Y - owner.Attributes.Bounds.Y,
                        obj.InstanceGuid, input.InstanceGuid, input.Attributes.InputGrip.Y - top));
                }
            }
        }

        var plan = CanvasLayout.Plan(nodes, edges, options, separateIslands);

        var kept = scope.Where(o => !(o is GH_Relay r && dissolvable.Contains(r))).ToList();
        var inputs = kept.SelectMany(o => VimController.Params(o, ParamSide.Input)).ToList();
        var wanted = inputs.ToDictionary(p => p, p => Wanted(p, plan, dissolvable, ids, outputs));

        var moves = new List<(IGH_DocumentObject Obj, float Dx, float Dy)>();
        foreach (var obj in kept)
        {
            var b = obj.Attributes.Bounds;
            var p = plan.Positions[obj.InstanceGuid];
            if (Math.Abs(p.X - b.X) >= Epsilon || Math.Abs(p.Y - b.Y) >= Epsilon)
                moves.Add((obj, p.X - b.X, p.Y - b.Y));
        }

        if (moves.Count == 0 && IsWiredAsPlanned(plan, dissolvable, inputs, wanted, outputs))
            return default;

        return Apply(doc, plan, relay, dissolvable, inputs, wanted, outputs, moves);
    }

    /// <summary>풀어도 결과가 같은 Relay인지 (데이터 연산이 없고 입출력이 모두 연결됨).</summary>
    internal static bool CanDissolve(GH_Relay relay) =>
        relay.SourceCount > 0 && relay.Recipients.Count > 0 &&
        relay.DataMapping == GH_DataMapping.None && !relay.Reverse && !relay.Simplify;

    static IGH_DocumentObject Owner(IGH_Param param) => param.Attributes.GetTopLevel.DocObject;

    /// <summary>입력 p가 받아야 할 출력들. 풀리는 Relay는 그 너머의 출력으로 펼친 뒤 계획된 경로로 바꾼다.</summary>
    static List<PortRef> Wanted(
        IGH_Param p, LayoutPlan plan, HashSet<GH_Relay> dissolvable, HashSet<Guid> ids, Dictionary<Guid, IGH_Param> outputs)
    {
        var owner = Owner(p);
        var result = new List<PortRef>();
        foreach (var source in Expand(p.Sources, dissolvable, new HashSet<GH_Relay>()))
        {
            // 범위 밖에서 오는 와이어도 그대로 다시 이을 수 있게 등록
            outputs[source.InstanceGuid] = source;
            var sourceOwner = Owner(source);
            var route = ids.Contains(sourceOwner.InstanceGuid) && sourceOwner != owner
                ? plan.Route(new Connection(sourceOwner.InstanceGuid, source.InstanceGuid, owner.InstanceGuid, p.InstanceGuid))
                : PortRef.Real(sourceOwner.InstanceGuid, source.InstanceGuid);
            if (!result.Contains(route))
                result.Add(route);
        }
        return result;
    }

    static IEnumerable<IGH_Param> Expand(IEnumerable<IGH_Param> sources, HashSet<GH_Relay> dissolvable, HashSet<GH_Relay> visiting)
    {
        foreach (var source in sources)
        {
            if (source is GH_Relay relay && dissolvable.Contains(relay))
            {
                if (!visiting.Add(relay)) continue;
                foreach (var inner in Expand(relay.Sources, dissolvable, visiting))
                    yield return inner;
                visiting.Remove(relay);
            }
            else
            {
                yield return source;
            }
        }
    }

    /// <summary>
    /// 지금 문서의 Relay와 와이어가 계획과 같은지. 계획된 Relay마다 같은 자리에 기존 Relay가 있으면 그것으로 본다.
    /// </summary>
    static bool IsWiredAsPlanned(
        LayoutPlan plan, HashSet<GH_Relay> dissolvable, List<IGH_Param> inputs,
        Dictionary<IGH_Param, List<PortRef>> wanted, Dictionary<Guid, IGH_Param> outputs)
    {
        if (plan.Relays.Count != dissolvable.Count)
            return false;

        var remaining = dissolvable.ToList();
        var matched = new IGH_Param[plan.Relays.Count];
        foreach (var planned in plan.Relays)
        {
            var found = remaining.FirstOrDefault(r =>
                Math.Abs(r.Attributes.Bounds.X - planned.X) < Epsilon && Math.Abs(r.Attributes.Bounds.Y - planned.Y) < Epsilon);
            if (found == null)
                return false;
            remaining.Remove(found);
            matched[planned.Index] = found;
        }

        IGH_Param Resolve(PortRef r) => r.IsRelay ? matched[r.Relay] : outputs[r.Port];

        foreach (var planned in plan.Relays)
            if (!matched[planned.Index].Sources.SequenceEqual(new[] { Resolve(planned.Upstream) }))
                return false;
        foreach (var p in inputs)
            if (!p.Sources.SequenceEqual(wanted[p].Select(Resolve)))
                return false;
        return true;
    }

    /// <remarks>
    /// 실행 취소 기록 안의 동작 순서: Relay 추가 → 와이어 → 기존 Relay 삭제 → 이동.
    /// 실행 취소는 역순이므로 기존 Relay가 먼저 되살아난 뒤 와이어가 그것을 다시 가리키고,
    /// 다시 실행은 정순이므로 새 Relay가 먼저 생긴 뒤 와이어가 그것을 가리킨다.
    /// </remarks>
    static FormatResult Apply(
        GH_Document doc, LayoutPlan plan, RelayShape shape, HashSet<GH_Relay> dissolvable,
        List<IGH_Param> inputs, Dictionary<IGH_Param, List<PortRef>> wanted,
        Dictionary<Guid, IGH_Param> outputs, List<(IGH_DocumentObject Obj, float Dx, float Dy)> moves)
    {
        var actions = new List<IGH_UndoAction>();

        // 1. 새 Relay
        var created = plan.Relays.Select(r => shape.Create(r.X, r.Y)).ToList();
        foreach (var r in created)
            doc.AddObject(r, false);
        if (created.Count > 0)
            actions.AddRange(doc.UndoUtil.CreateAddObjectEvent(UndoName, created).Actions);

        // 2. 와이어
        IGH_Param Resolve(PortRef r) => r.IsRelay ? created[r.Relay] : outputs[r.Port];
        var rewired = inputs.Where(p => !p.Sources.SequenceEqual(wanted[p].Select(Resolve))).ToList();
        var wireTargets = rewired.Concat(created).ToList();
        if (wireTargets.Count > 0)
            actions.AddRange(doc.UndoUtil.CreateWireEvent(UndoName, wireTargets).Actions);

        foreach (var planned in plan.Relays)
            created[planned.Index].AddSource(Resolve(planned.Upstream));
        foreach (var p in rewired)
        {
            p.RemoveAllSources();
            foreach (var source in wanted[p])
                p.AddSource(Resolve(source));
        }

        // 3. 기존 Relay (이제 아무도 가리키지 않는다)
        if (dissolvable.Count > 0)
        {
            actions.AddRange(doc.UndoUtil.CreateRemoveObjectEvent(UndoName, dissolvable).Actions);
            doc.RemoveObjects(dissolvable, false);
        }

        // 4. 이동
        if (moves.Count > 0)
            actions.AddRange(doc.UndoUtil.CreatePivotEvent(UndoName, moves.Select(m => m.Obj)).Actions);
        foreach (var (obj, dx, dy) in moves)
        {
            // Pivot이 객체의 어느 점인지는 객체 종류마다 다르므로 영역의 이동량만큼 옮긴다
            var pivot = obj.Attributes.Pivot;
            obj.Attributes.Pivot = new PointF(pivot.X + dx, pivot.Y + dy);
            obj.Attributes.ExpireLayout();
            obj.Attributes.PerformLayout();
        }

        doc.UndoUtil.RecordEvent(UndoName, actions);

        // 그룹 외곽선은 구성 객체 위치로 다시 계산되게 한다
        foreach (var group in doc.Objects.OfType<GH_Group>())
            group.Attributes.ExpireLayout();

        // 와이어가 바뀐 곳은 다시 계산 (Relay는 데이터를 그대로 넘기므로 결과는 같다)
        if (rewired.Count > 0)
        {
            foreach (var p in rewired)
                Owner(p).ExpireSolution(false);
            doc.NewSolution(false);
        }

        return new FormatResult(moves.Count, created.Count, dissolvable.Count);
    }

    /// <summary>Relay의 크기와, Pivot에서 영역 왼쪽 위까지의 거리. Rhino 버전마다 다를 수 있어 직접 잰다.</summary>
    readonly struct RelayShape
    {
        RelayShape(float width, float height, float left, float top)
        {
            Width = width;
            Height = height;
            Left = left;
            Top = top;
        }

        public float Width { get; }
        public float Height { get; }
        public float Left { get; }
        public float Top { get; }

        public static RelayShape Measure()
        {
            var probe = new GH_Relay();
            probe.CreateAttributes();
            probe.Attributes.Pivot = PointF.Empty;
            probe.Attributes.PerformLayout();
            var b = probe.Attributes.Bounds;
            return new RelayShape(b.Width, b.Height, b.X, b.Y);
        }

        /// <summary>영역 왼쪽 위가 (x, y)인 새 Relay (문서에는 아직 넣지 않음).</summary>
        public GH_Relay Create(float x, float y)
        {
            var relay = new GH_Relay();
            relay.CreateAttributes();
            relay.Attributes.Pivot = new PointF(x - Left, y - Top);
            relay.Attributes.PerformLayout();
            return relay;
        }
    }
}
