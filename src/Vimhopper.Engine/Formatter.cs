using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using Vimhopper.Core;

namespace Vimhopper;

/// <summary>GH_Document 객체를 CanvasLayout 입력으로 바꾸고, 계산된 위치로 옮긴다.</summary>
internal static class Formatter
{
    /// <param name="separateIslands">true면 와이어로 이어진 덩어리마다 따로 정렬한다 (문서 전체 정렬).</param>
    /// <returns>옮긴 객체 수. 이미 정렬되어 있으면 0이고 실행 취소 기록도 남기지 않는다.</returns>
    public static int Format(GH_Document doc, IReadOnlyCollection<IGH_DocumentObject> objects, bool separateIslands)
    {
        var ids = new HashSet<Guid>(objects.Select(o => o.InstanceGuid));
        var nodes = objects
            .Select(o =>
            {
                var b = o.Attributes.Bounds;
                return new LayoutNode(o.InstanceGuid, b.X, b.Y, b.Width, b.Height);
            })
            .ToList();

        var edges = new List<LayoutEdge>();
        foreach (var obj in objects)
        {
            var top = obj.Attributes.Bounds.Y;
            foreach (var input in VimController.Params(obj, ParamSide.Input))
            {
                foreach (var source in input.Sources)
                {
                    var owner = source.Attributes.GetTopLevel.DocObject;
                    if (owner == obj || !ids.Contains(owner.InstanceGuid)) continue;
                    edges.Add(new LayoutEdge(
                        owner.InstanceGuid, source.Attributes.OutputGrip.Y - owner.Attributes.Bounds.Y,
                        obj.InstanceGuid, input.Attributes.InputGrip.Y - top));
                }
            }
        }

        var positions = separateIslands
            ? CanvasLayout.ArrangeAll(nodes, edges)
            : CanvasLayout.Arrange(nodes, edges);

        var moves = new List<(IGH_DocumentObject Obj, float Dx, float Dy)>();
        foreach (var obj in objects)
        {
            var b = obj.Attributes.Bounds;
            var p = positions[obj.InstanceGuid];
            var dx = p.X - b.X;
            var dy = p.Y - b.Y;
            if (Math.Abs(dx) >= 0.5f || Math.Abs(dy) >= 0.5f)
                moves.Add((obj, dx, dy));
        }
        if (moves.Count == 0)
            return 0;

        doc.UndoUtil.RecordPivotEvent("Vimhopper format", moves.Select(m => m.Obj));
        foreach (var (obj, dx, dy) in moves)
        {
            // Pivot이 객체의 어느 점인지는 객체 종류마다 다르므로 영역의 이동량만큼 옮긴다
            var pivot = obj.Attributes.Pivot;
            obj.Attributes.Pivot = new PointF(pivot.X + dx, pivot.Y + dy);
            obj.Attributes.ExpireLayout();
            obj.Attributes.PerformLayout();
        }

        // 그룹 외곽선은 구성 객체 위치로 다시 계산되게 한다
        foreach (var group in doc.Objects.OfType<GH_Group>())
            group.Attributes.ExpireLayout();

        return moves.Count;
    }
}
