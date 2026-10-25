using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Vimhopper.Core;

namespace Vimhopper;

/// <summary>GH_Document의 와이어 연결을 최상위 객체 단위 그래프로 보여준다.</summary>
internal sealed class DocumentGraph : IGraph
{
    readonly Func<GH_Document?> _document;

    public DocumentGraph(Func<GH_Document?> document) => _document = document;

    public IReadOnlyList<Guid> Downstream(Guid id) =>
        Neighbors(id, obj => VimController.Params(obj, ParamSide.Output).SelectMany(p => p.Recipients));

    public IReadOnlyList<Guid> Upstream(Guid id) =>
        Neighbors(id, obj => VimController.Params(obj, ParamSide.Input).SelectMany(p => p.Sources));

    IReadOnlyList<Guid> Neighbors(Guid id, Func<IGH_DocumentObject, IEnumerable<IGH_Param>> linked)
    {
        if (_document()?.FindObject(id, true) is not { } obj)
            return Array.Empty<Guid>();

        // 화면 위→아래, 같은 높이면 왼쪽→오른쪽 순서 (j가 아래로 가도록)
        return linked(obj)
            .Select(p => p.Attributes.GetTopLevel.DocObject)
            .Where(o => o.InstanceGuid != id)
            .Distinct()
            .OrderBy(o => o.Attributes.Bounds.Y + o.Attributes.Bounds.Height / 2)
            .ThenBy(o => o.Attributes.Bounds.X)
            .Select(o => o.InstanceGuid)
            .ToList();
    }
}
