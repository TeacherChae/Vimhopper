using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Windows.Forms;
using Grasshopper.GUI.Canvas;
using Rhino;

namespace Vimhopper;

/// <summary>
/// Vimhopper.Engine.dll을 불러와 캔버스에 붙인다.
/// </summary>
/// <remarks>
/// 핫 리로드 모드(Debug 빌드)에서는 엔진과 의존 dll을 임시 폴더로 복사해 언로드 가능한
/// AssemblyLoadContext에 올린다. 원본은 잠기지 않으므로 Rhino를 켜둔 채 빌드할 수 있고,
/// 빌드 폴더의 dll이 바뀌면 이전 엔진을 Dispose·언로드한 뒤 새 엔진으로 교체한다.
/// 로더는 엔진 타입을 직접 참조하지 않는다 (참조하면 기본 컨텍스트에 올라가 언로드할 수 없음).
/// </remarks>
internal sealed class EngineHost
{
    const string EngineFile = "Vimhopper.Engine.dll";
    const string EntryType = "Vimhopper.VimhopperEngine";

    // 빌드가 dll을 여러 번 나눠 쓰므로 마지막 변경 후 잠시 기다렸다가 리로드
    const int DebounceMilliseconds = 700;

    sealed class Slot
    {
        public Slot(GH_Canvas canvas) => Canvas = canvas;
        public GH_Canvas Canvas { get; }
        public Dictionary<string, object?> State { get; } = new();
        public IDisposable? Session { get; set; }
    }

    static readonly string ShadowRoot = Path.Combine(Path.GetTempPath(), "Vimhopper", "hot");

    readonly string _directory;
    readonly bool _hotReload;
    readonly List<Slot> _slots = new();
    MethodInfo? _attach;
    HotLoadContext? _context;
    FileSystemWatcher? _watcher;
    System.Windows.Forms.Timer? _debounce;

    public EngineHost(string directory, bool hotReload)
    {
        _directory = directory;
        _hotReload = hotReload;

        if (!hotReload)
        {
            _attach = FindAttach(Assembly.LoadFrom(Path.Combine(directory, EngineFile)));
            return;
        }

        CleanupShadows();
        LoadShadow(CopyToShadow());
        StartWatching();
        RhinoApp.WriteLine("Vimhopper: hot reload enabled");
    }

    public void Attach(GH_Canvas canvas)
    {
        if (_slots.Exists(s => s.Canvas == canvas))
            return;
        var slot = new Slot(canvas);
        _slots.Add(slot);
        slot.Session = Invoke(slot);
    }

    IDisposable? Invoke(Slot slot)
    {
        try
        {
            return (IDisposable?)_attach?.Invoke(null, new object[] { slot.Canvas, slot.State });
        }
        catch (TargetInvocationException e)
        {
            RhinoApp.WriteLine($"Vimhopper: engine failed to attach - {e.InnerException?.Message}");
            return null;
        }
    }

    static MethodInfo FindAttach(Assembly engine) =>
        engine.GetType(EntryType, throwOnError: true)!.GetMethod("Attach")
        ?? throw new MissingMethodException(EntryType, "Attach");

    // ───────────────────────── 핫 리로드 ─────────────────────────

    void StartWatching()
    {
        _debounce = new System.Windows.Forms.Timer { Interval = DebounceMilliseconds };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            Reload();
        };

        _watcher = new FileSystemWatcher(_directory, "Vimhopper.*.dll")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
        };
        _watcher.Changed += OnFileChanged;
        _watcher.Created += OnFileChanged;
        _watcher.Renamed += OnFileChanged;
        _watcher.EnableRaisingEvents = true;
    }

    // FileSystemWatcher는 스레드 풀에서 호출되므로 UI 스레드로 넘겨 타이머를 재시작
    void OnFileChanged(object sender, FileSystemEventArgs e) =>
        RhinoApp.InvokeOnUiThread(new Action(() =>
        {
            _debounce!.Stop();
            _debounce.Start();
        }));

    void Reload()
    {
        string shadow;
        try
        {
            shadow = CopyToShadow();
        }
        catch (IOException)
        {
            // 빌드가 아직 쓰는 중: 조금 뒤 다시 시도
            _debounce!.Start();
            return;
        }

        foreach (var slot in _slots)
        {
            slot.Session?.Dispose();
            slot.Session = null;
        }
        _attach = null;
        _context?.Unload();
        _context = null;

        try
        {
            LoadShadow(shadow);
        }
        catch (Exception e)
        {
            RhinoApp.WriteLine($"Vimhopper: reload failed - {e.Message}");
            return;
        }

        foreach (var slot in _slots)
            slot.Session = Invoke(slot);

        RhinoApp.WriteLine($"Vimhopper: engine reloaded ({DateTime.Now:HH:mm:ss})");
        CleanupShadows();
    }

    string CopyToShadow()
    {
        var shadow = Path.Combine(ShadowRoot, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(shadow);
        foreach (var file in Directory.GetFiles(_directory, "Vimhopper.*"))
        {
            var ext = Path.GetExtension(file);
            if (ext is ".dll" or ".pdb")
                File.Copy(file, Path.Combine(shadow, Path.GetFileName(file)), overwrite: true);
        }
        return shadow;
    }

    void LoadShadow(string shadow)
    {
        _context = new HotLoadContext(shadow);
        _attach = FindAttach(_context.LoadFromAssemblyPath(Path.Combine(shadow, EngineFile)));
    }

    // 이전 복사본 정리. 아직 언로드되지 않은 폴더는 잠겨 있으므로 실패는 무시
    void CleanupShadows()
    {
        if (!Directory.Exists(ShadowRoot)) return;
        var current = _context?.Directory;
        foreach (var dir in Directory.GetDirectories(ShadowRoot))
        {
            if (string.Equals(dir, current, StringComparison.OrdinalIgnoreCase)) continue;
            try { Directory.Delete(dir, recursive: true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// 임시 폴더의 Vimhopper dll만 이 컨텍스트에 올린다.
    /// Grasshopper, RhinoCommon, System.* 등은 null을 돌려 기본 컨텍스트와 공유한다 (타입 동일성 유지).
    /// </summary>
    sealed class HotLoadContext : AssemblyLoadContext
    {
        public HotLoadContext(string directory)
            : base($"Vimhopper.Engine {Path.GetFileName(directory)}", isCollectible: true) => Directory = directory;

        public string Directory { get; }

        protected override Assembly? Load(AssemblyName name)
        {
            var path = Path.Combine(Directory, name.Name + ".dll");
            return File.Exists(path) ? LoadFromAssemblyPath(path) : null;
        }
    }
}
