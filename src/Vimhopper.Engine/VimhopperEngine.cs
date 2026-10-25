using System;
using System.Collections.Generic;
using Grasshopper.GUI.Canvas;

namespace Vimhopper;

/// <summary>
/// 로더(Vimhopper.gha)가 리플렉션으로 호출하는 진입점.
/// 로더와 엔진은 서로 다른 AssemblyLoadContext에 있을 수 있으므로 BCL·Grasshopper 타입만 주고받는다.
/// </summary>
public static class VimhopperEngine
{
    /// <param name="canvas">붙일 캔버스.</param>
    /// <param name="state">리로드를 거쳐도 유지되는 상태 저장소 (Vim 모드 여부, 선택 객체 등).</param>
    /// <returns>Dispose하면 캔버스에서 완전히 떨어진다.</returns>
    public static IDisposable Attach(GH_Canvas canvas, IDictionary<string, object?> state) =>
        new VimController(canvas, state);
}
