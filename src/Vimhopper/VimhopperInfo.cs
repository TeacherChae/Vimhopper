using System;
using System.IO;
using Grasshopper.Kernel;

namespace Vimhopper;

public sealed class VimhopperInfo : GH_AssemblyInfo
{
    public override string Name => "Vimhopper";
    public override string Description => "Vim-style keyboard navigation and wiring for the Grasshopper canvas.";
    public override Guid Id => new("3f6c1d2e-8b4a-4e7f-9a1c-5d2b7e0f4a63");
    public override string AuthorName => "Keon Chae";
    public override string Version => "0.1.0";
}

/// <summary>Grasshopper가 로드될 때 엔진을 불러와 캔버스에 붙인다.</summary>
public sealed class VimhopperLoader : GH_AssemblyPriority
{
#if DEBUG
    const bool HotReload = true;
#else
    const bool HotReload = false;
#endif

    static EngineHost? s_host;

    public override GH_LoadingInstruction PriorityLoad()
    {
        var directory = Path.GetDirectoryName(typeof(VimhopperLoader).Assembly.Location)!;
        s_host = new EngineHost(directory, HotReload);

        Grasshopper.Instances.CanvasCreated += s_host.Attach;
        if (Grasshopper.Instances.ActiveCanvas is { } canvas)
            s_host.Attach(canvas);
        return GH_LoadingInstruction.Proceed;
    }
}
