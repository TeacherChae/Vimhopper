using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using Vimhopper.Core;

namespace Vimhopper;

/// <summary>GH_Document의 와이어 연결을 최상위 객체 단위 탐색 그래프로 옮긴다.</summary>
internal static class DocumentGraph
{
    /// <summary>
    /// 컴포넌트와 파라미터(슬라이더, 패널, Relay 포함)만 담는다. 그룹·스크리블 등은 순회 대상이 아니다.
    /// </summary>
    public static CanvasGraph Build(GH_Document doc)
    {
        var nodes = new List<GraphNode>();
        foreach (var obj in doc.Objects.OfType<IGH_ActiveObject>())
        {
            var b = obj.Attributes.Bounds;
            var inputs = VimController.Params(obj, ParamSide.Input)
                .Select(p => (IReadOnlyList<ParamRef>)p.Sources.Select(s => Ref(s, ParamSide.Output)).ToList())
                .ToList();
            var outputs = VimController.Params(obj, ParamSide.Output)
                .Select(p => (IReadOnlyList<ParamRef>)p.Recipients.Select(r => Ref(r, ParamSide.Input)).ToList())
                .ToList();
            var isRelay = obj is GH_Relay relay && Formatter.CanDissolve(relay);
            nodes.Add(new GraphNode(obj.InstanceGuid, b.X, b.Y, b.Width, b.Height, isRelay, inputs, outputs));
        }
        return new CanvasGraph(nodes);
    }

    /// <summary>파라미터를 (최상위 객체, 그 객체에서의 입력/출력 번호)로.</summary>
    static ParamRef Ref(IGH_Param param, ParamSide side)
    {
        var owner = param.Attributes.GetTopLevel.DocObject;
        var index = 0;
        var list = VimController.Params(owner, side);
        for (var i = 0; i < list.Count; i++)
            if (list[i] == param) index = i;
        return new ParamRef(owner.InstanceGuid, index);
    }
}
