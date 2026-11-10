using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Undo;
using Grasshopper.Kernel.Undo.Actions;
using Vimhopper.Core;

namespace Vimhopper;

// : 명령줄. 줄 해석·이름 완성·기록은 Core의 ExCommands·ExCompletion·CommandHistory, 여기서는 입력줄과 내장 명령
internal sealed partial class VimController
{
    /// <summary>: 입력 중. Visual은 비주얼 모드에서 열었을 때의 선택 ('&lt;,'&gt; 범위), 아니면 null.</summary>
    sealed class CmdlineSession
    {
        public CmdlineSession(IReadOnlyList<Guid>? visual) => Visual = visual;

        public IReadOnlyList<Guid>? Visual { get; }
        // 입력줄 끝에 보일 Tab 후보
        public string Info { get; set; } = "";
    }

    readonly ExCommands _ex = new();
    // 세션 동안의 : 기록 (문서가 바뀌어도 남음, 핫 리로드에는 사라짐)
    readonly CommandHistory _cmdHistory = new();
    readonly ExCompletion _completion = new();
    CmdlineSession? _cmdline;

    void RegisterExCommands()
    {
        _ex.Register("w", 1, Write, "문서 저장 (Ctrl+S)");
        _ex.Register("recompute", 3, Recompute, "다시 계산 (F5)");
        _ex.Register("lock", 3, call => SetSolver(call, enabled: false), "솔버 잠그기");
        _ex.Register("unlock", 3, call => SetSolver(call, enabled: true), "솔버 풀기");
        _ex.Register("rh", 2, RunRhino, "Rhino 명령 실행");
        _ex.Register("s", 1, SubstituteNickNames, "별명 바꾸기 :s/찾을/바꿀/[gi]");
    }

    /// <summary><c>:</c>: 명령줄을 연다. 비주얼 모드에서 열면 지금 선택을 기억하고 <c>'&lt;,'&gt;</c>를 미리 채운다.</summary>
    void OpenCmdline(bool visual)
    {
        if (Document is not { } doc) return;
        var selection = visual && _anchor != null
            ? doc.SelectedObjects().Select(o => o.InstanceGuid).ToList()
            : null;
        _cmdline = new CmdlineSession(selection);
        _completion.Reset();
        _cmdHistory.Reset();
        _input.Open(":", selection != null ? ExCommands.VisualRange : "", _savedIme,
            OnCmdlineChanged, OnCmdlineConfirmed, OnCmdlineCancelled);
    }

    void OnCmdlineChanged(string text)
    {
        _completion.Reset();
        _cmdHistory.Reset();
        if (_cmdline != null) _cmdline.Info = "";
    }

    void OnCmdlineConfirmed(string text)
    {
        if (_cmdline is not { } session) return;
        _cmdHistory.Add(text);
        // 명령이 '<,'> 선택을 읽을 수 있게 실행이 끝날 때까지 세션을 둔다
        var result = _ex.Execute(text);
        _cmdline = null;
        _status = result.Message ?? "";
        LeaveCmdlineVisual(session);
    }

    void OnCmdlineCancelled()
    {
        if (_cmdline is not { } session) return;
        _cmdline = null;
        LeaveCmdlineVisual(session);
    }

    // 비주얼 모드에서 열었으면 vim처럼 명령줄을 닫을 때 비주얼 모드를 나간다 (파서는 : 때 이미 나감)
    void LeaveCmdlineVisual(CmdlineSession session)
    {
        if (session.Visual != null && _anchor != null)
            SetVisual(false);
    }

    /// <summary>
    /// 입력줄이 열린 동안의 키. <c>:</c> 명령줄이면 <c>Tab</c> / <c>Shift+Tab</c> 이름 완성, <c>↑</c> / <c>↓</c>
    /// (<c>Ctrl+p</c> / <c>Ctrl+n</c>) 기록. 가져갔으면 true (Tab의 글자까지 삼킴), 나머지는 글자로 입력줄에 간다.
    /// </summary>
    bool CmdlineKey(Keys code, Keys modifiers)
    {
        if (_cmdline is not { } session || _input.Composing) return false;
        string? text;
        switch (code)
        {
            case Keys.Tab when (modifiers & ~Keys.Shift) == Keys.None:
                _cmdHistory.Reset();
                text = _completion.Next(_ex, _input.Text, backward: modifiers == Keys.Shift);
                break;
            case Keys.Up when modifiers == Keys.None:
            case Keys.P when modifiers == Keys.Control:
                _completion.Reset();
                text = _cmdHistory.Older(_input.Text);
                break;
            case Keys.Down when modifiers == Keys.None:
            case Keys.N when modifiers == Keys.Control:
                _completion.Reset();
                text = _cmdHistory.Newer();
                break;
            default:
                return false;
        }
        if (text != null)
            _input.SetText(text);
        session.Info = _completion.Info;
        _canvas.Refresh();
        return true;
    }

    // ───────────────────────── 내장 명령 ─────────────────────────

    static ExResult? RejectRange(ExCall call) =>
        call.Range != null ? ExResult.Error($":{call.Name}은 범위를 받지 않습니다") : null;

    /// <summary><c>:w</c>: Grasshopper의 Ctrl+S처럼 저장한다. 한 번도 저장하지 않은 문서는 경로를 물을 수 없으므로 오류.</summary>
    ExResult Write(ExCall call)
    {
        if (RejectRange(call) is { } error) return error;
        if (Document is not { } doc) return ExResult.Error("문서가 없습니다");
        if (call.Args.Length > 0) return ExResult.Error(":w는 파일 이름을 받지 않습니다 (다른 이름으로 저장은 Grasshopper에서)");
        if (!doc.IsFilePathDefined)
            return ExResult.Error("저장한 적 없는 문서입니다: Grasshopper에서 한 번 저장하세요 (Ctrl+S)");
        return new GH_DocumentIO(doc).Save()
            ? ExResult.Success($"저장함: {Path.GetFileName(doc.FilePath)}")
            : ExResult.Error($"저장하지 못했습니다: {doc.FilePath}");
    }

    /// <summary><c>:recompute</c>: F5처럼 모든 객체를 다시 계산한다.</summary>
    ExResult Recompute(ExCall call)
    {
        if (RejectRange(call) is { } error) return error;
        if (Document is not { } doc) return ExResult.Error("문서가 없습니다");
        if (!GH_Document.EnableSolutions)
            return ExResult.Error("솔버가 잠겨 있습니다 (:unlock)");
        doc.NewSolution(true);
        return ExResult.Success("다시 계산함");
    }

    /// <summary><c>:lock</c> / <c>:unlock</c>: Grasshopper의 솔버 잠금 (모든 문서에 걸리는 전역 설정). 풀면 그동안 만료된 것을 계산한다.</summary>
    ExResult SetSolver(ExCall call, bool enabled)
    {
        if (RejectRange(call) is { } error) return error;
        if (GH_Document.EnableSolutions == enabled)
            return ExResult.Success(enabled ? "솔버가 이미 풀려 있습니다" : "솔버가 이미 잠겨 있습니다");
        GH_Document.EnableSolutions = enabled;
        if (enabled)
            Document?.NewSolution(false);
        _canvas.Refresh();
        return ExResult.Success(enabled ? "솔버 풀림" : "솔버 잠김");
    }

    /// <summary><c>:rh {명령}</c>: 줄의 나머지를 Rhino 명령줄에 친 것처럼 실행한다 (<c>RhinoApp.RunScript</c>).</summary>
    ExResult RunRhino(ExCall call)
    {
        if (RejectRange(call) is { } error) return error;
        if (call.Args.Trim().Length == 0) return ExResult.Error("사용법: :rh {Rhino 명령}");
        return Rhino.RhinoApp.RunScript(call.Args, true)
            ? ExResult.Success($"Rhino: {call.Args}")
            : ExResult.Error($"Rhino 명령을 실행하지 못했습니다: {call.Args}");
    }

    /// <summary>
    /// <c>:s/찾을/바꿀/[gi]</c>: 별명을 바꾼다. 범위 없음 = 커서 객체, <c>%</c> = 문서 전체, <c>'&lt;,'&gt;</c> = 비주얼 선택.
    /// 바뀐 객체 모두를 undo 기록 하나에 담아 <c>u</c> 한 번에 되돌린다. <c>.</c>으로 되풀이하지 않는다 (vim과 같음).
    /// </summary>
    ExResult SubstituteNickNames(ExCall call)
    {
        if (Substitution.Parse(call.Args, out var parseError) is not { } sub)
            return ExResult.Error(parseError!);
        if (Document is not { } doc) return ExResult.Error("문서가 없습니다");

        List<IGH_DocumentObject> targets;
        switch (call.Range)
        {
            case null:
                if (Selected is not { } obj) return ExResult.Error("커서 객체가 없습니다");
                if (RenameError(obj) is { } error) return ExResult.Error(error);
                targets = new() { obj };
                break;
            case ExCommands.WholeDocument:
                targets = doc.Objects.Where(o => RenameError(o) == null).ToList();
                break;
            default:
                if (_cmdline?.Visual is not { } visual) return ExResult.Error("비주얼 선택이 없습니다 (비주얼 모드에서 :)");
                targets = visual.Select(id => doc.FindObject(id, true))
                    .Where(o => o != null && RenameError(o) == null).ToList()!;
                break;
        }

        var changes = new List<(IGH_DocumentObject Obj, string NickName)>();
        foreach (var obj in targets)
        {
            var old = obj.NickName ?? "";
            var name = sub.Apply(old);
            if (name != old)
                changes.Add((obj, name));
        }
        if (changes.Count == 0)
            return ExResult.Error($"맞는 별명이 없습니다: {sub.Pattern}");

        // 별명 바꾸기 전에 기록한다 (GH_NickNameAction이 지금 별명을 저장)
        doc.UndoUtil.RecordEvent("Vimhopper :s", changes.Select(c => (IGH_UndoAction)new GH_NickNameAction(c.Obj)));
        foreach (var (obj, name) in changes)
        {
            obj.NickName = name;
            obj.Attributes.ExpireLayout();
        }
        _canvas.Refresh();
        return ExResult.Success($"{changes.Count}개 바꿈");
    }
}
