using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using Vimhopper.Core;

namespace Vimhopper;

// ] / [: 다음·이전 오류 원인, 경고 원인, 느린 객체, 그룹. 대상 고르기와 순서는 Core의 BracketNav와 Search,
// 여기서는 키를 누를 때 문서에서 메시지·계산 시간·그룹을 읽는다 (계산 이벤트를 구독하지 않음)
internal sealed partial class VimController
{
    // 계산 시간을 그려 줄 느린 객체 수
    const int SlowLabelCount = 5;
    // 상태줄에 붙일 메시지의 최대 길이
    const int StatusMessageLength = 80;

    static MessageLevel LevelOf(IGH_ActiveObject obj) => obj.RuntimeMessageLevel switch
    {
        GH_RuntimeMessageLevel.Error => MessageLevel.Error,
        GH_RuntimeMessageLevel.Warning => MessageLevel.Warning,
        _ => MessageLevel.None,
    };

    static double Milliseconds(IGH_ActiveObject obj) => obj.ProcessorTime.TotalMilliseconds;

    static List<(Guid Id, double Milliseconds)> SolveTimes(GH_Document doc) =>
        doc.Objects.OfType<IGH_ActiveObject>().Select(o => (o.InstanceGuid, Milliseconds(o))).ToList();

    void BracketMove(BracketTarget target, bool forward, int count)
    {
        if (Document is not { } doc) return;
        var graph = DocumentGraph.Build(doc);
        var order = Search.Order(graph);

        IReadOnlyList<Guid> list = order;
        Func<Guid, bool> matches;
        Func<SearchHit, IGH_DocumentObject, string> describe;
        switch (target)
        {
            case BracketTarget.Error or BracketTarget.Warning:
            {
                var level = target == BracketTarget.Error ? MessageLevel.Error : MessageLevel.Warning;
                var what = level == MessageLevel.Error ? "오류" : "경고";
                var levels = doc.Objects.OfType<IGH_ActiveObject>().ToDictionary(o => o.InstanceGuid, LevelOf);
                var roots = BracketNav.RootCauses(graph, id => levels.TryGetValue(id, out var l) ? l : MessageLevel.None, level);
                if (roots.Count == 0)
                {
                    _status = $"{what} 없음";
                    return;
                }
                var total = levels.Values.Count(l => l == level);
                matches = roots.Contains;
                describe = (hit, obj) =>
                    BracketNav.Describe(hit, $"{what} 원인", $"({what} {total}개)", forward) + FirstMessage(obj, level);
                break;
            }
            case BracketTarget.Slow:
            {
                var times = SolveTimes(doc);
                list = BracketNav.Slowest(times, order);
                if (list.Count == 0)
                {
                    _status = "계산 시간이 기록된 객체 없음";
                    return;
                }
                var ms = times.ToDictionary(t => t.Id, t => t.Milliseconds);
                matches = _ => true;
                describe = (hit, obj) =>
                    BracketNav.Describe(hit, "느린 객체", $"{BracketNav.FormatTime(ms[hit.Target])} {obj.NickName}", forward);
                break;
            }
            default:
            {
                var starts = BracketNav.GroupStarts(order, doc.Objects.OfType<GH_Group>()
                    .Select(g => (g, g.ObjectsRecursive().Select(o => o.InstanceGuid))));
                if (starts.Count == 0)
                {
                    _status = "그룹 없음";
                    return;
                }
                matches = starts.ContainsKey;
                describe = (hit, _) =>
                    BracketNav.Describe(hit, "그룹", GroupName(starts[hit.Target]), forward);
                break;
            }
        }

        // ]s / [s도 커서의 순위에서 센다. 커서가 순위에 없으면 Search.Next가 ]s는 가장 느린 객체, [s는 가장 빠른 객체에서 시작한다
        var next = Search.Next(list, matches, _current, forward, count);
        if (next == null || doc.FindObject(next.Target, true) is not { } found)
        {
            _status = "대상 객체를 찾을 수 없습니다";
            return;
        }
        if (next.Target != _current)
            RecordJump();
        Select(found);
        _status = describe(next, found);
    }

    static string GroupName(GH_Group group) =>
        string.IsNullOrWhiteSpace(group.NickName) || group.NickName == "Group" ? "(이름 없음)" : group.NickName;

    // 대상의 첫 메시지 (": Data conversion failed …"). 길면 자른다
    static string FirstMessage(IGH_DocumentObject obj, MessageLevel level)
    {
        if (obj is not IGH_ActiveObject active) return "";
        var messages = active.RuntimeMessages(level == MessageLevel.Error ? GH_RuntimeMessageLevel.Error : GH_RuntimeMessageLevel.Warning);
        if (messages == null || messages.Count == 0) return "";
        var text = $"{active.NickName}: {messages[0].Replace('\n', ' ').Replace('\r', ' ')}";
        if (text.Length > StatusMessageLength) text = text.Substring(0, StatusMessageLength - 1) + "…";
        return "   " + text;
    }

    /// <summary>
    /// Vim 모드인 동안 가장 느린 객체 몇 개의 오른쪽 위에 계산 시간을 그린다 (화면 좌표라 줌과 무관하게 같은 크기).
    /// 그릴 때마다 읽지만 객체마다 속성 하나라 가볍다.
    /// </summary>
    void DrawSlowTimes()
    {
        if (!_vim || Document is not { } doc) return;
        var top = doc.Objects.OfType<IGH_ActiveObject>()
            .Select(o => (Obj: o, Ms: Milliseconds(o)))
            .Where(x => x.Ms > 0)
            .OrderByDescending(x => x.Ms)
            .Take(SlowLabelCount)
            .ToList();
        if (top.Count == 0) return;

        var g = _canvas.Graphics;
        var saved = g.Transform;
        g.ResetTransform();
        using var back = new SolidBrush(Color.FromArgb(200, 120, 40, 160));
        foreach (var (obj, ms) in top)
        {
            var b = obj.Attributes.Bounds;
            var corner = _canvas.Viewport.ProjectPoint(new PointF(b.Right, b.Top));
            var text = BracketNav.FormatTime(ms);
            var size = g.MeasureString(text, _hintFont);
            var rect = new RectangleF(corner.X - size.Width, corner.Y - size.Height - 2, size.Width, size.Height);
            g.FillRectangle(back, rect);
            g.DrawString(text, _hintFont, Brushes.White, rect.X, rect.Y);
        }
        g.Transform = saved;
    }
}
