using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using Vimhopper.Core;

namespace Vimhopper;

// 그룹: gG{범위}, gGG, 비주얼 gG. 객체를 모은 뒤 입력줄로 이름을 받아 GH_Group을 만든다
internal sealed partial class VimController
{
    const string GroupPrompt = "그룹 이름: ";

    /// <summary>이름을 기다리는 그룹. 묶을 객체는 입력줄을 열 때 정해 둔다.</summary>
    sealed class GroupSession
    {
        public GroupSession(IReadOnlyList<Guid> ids, WireRange? range)
        {
            Ids = ids;
            Range = range;
        }

        public IReadOnlyList<Guid> Ids { get; }
        // . 되풀이에 기록할 범위 (gGG·비주얼 gG는 null = 커서 객체)
        public WireRange? Range { get; }
        public string Info => Ids.Count == 1 ? "객체 1개" : $"객체 {Ids.Count}개";
    }

    GroupSession? _group;

    /// <summary><c>gGG</c>, 비주얼 <c>gG</c>, 또는 <c>.</c>이 되풀이하는 이름 붙은 그룹.</summary>
    bool Group(GroupCommand command)
    {
        if (command.Range is { } range)
            return ApplyOperator(Operator.Group, range, command.Name);
        return Document is { } doc && GroupObjects(doc, Targets(), null, command.Name);
    }

    /// <summary>
    /// name이 있으면 바로 묶고 (<c>.</c> 되풀이), 없으면 이름 입력줄을 연다. 입력줄을 연 것만으로는 문서가 바뀌지 않는다.
    /// </summary>
    /// <param name="range">확정 뒤 <c>.</c>에 기록할 원래 범위 (<c>'a</c>면 마크 그대로).</param>
    bool GroupObjects(GH_Document doc, List<IGH_DocumentObject> objects, WireRange? range, string? name)
    {
        if (objects.Count == 0)
        {
            _status = "선택된 객체가 없습니다";
            return false;
        }
        if (name != null)
        {
            CreateGroup(doc, objects, name);
            return true;
        }

        _group = new GroupSession(objects.Select(o => o.InstanceGuid).ToList(), range);
        _input.Open(GroupPrompt, _savedIme, _ => { }, OnGroupConfirmed, OnGroupCancelled);
        return true;
    }

    void OnGroupConfirmed(string name)
    {
        if (_group is not { } session || Document is not { } doc) return;
        _group = null;
        // 입력하는 동안 지워진 객체는 빼고
        var objects = session.Ids.Select(id => doc.FindObject(id, true)).OfType<IGH_DocumentObject>().ToList();
        if (objects.Count == 0)
        {
            _status = "묶을 객체가 없습니다";
            return;
        }
        CreateGroup(doc, objects, name);
        // vim이 넣은 글을 되풀이하듯 . 은 같은 이름으로 묻지 않고 묶는다
        _repeat.Record(new GroupCommand(session.Range, name));
    }

    void OnGroupCancelled()
    {
        _group = null;
        _status = "";
    }

    /// <summary>객체들을 새 GH_Group으로 묶는다. 다른 그룹에 든 객체도 그대로 둔 채 겹쳐 묶는다 (Grasshopper가 허용).</summary>
    void CreateGroup(GH_Document doc, List<IGH_DocumentObject> objects, string name)
    {
        // 색은 Grasshopper 기본 그룹 색 (Ctrl+G와 같음, 스킨을 따른다)
        var group = new GH_Group { NickName = name, Colour = GH_Skin.group_back };
        group.CreateAttributes();
        foreach (var obj in objects)
            group.AddObject(obj.InstanceGuid);
        doc.AddObject(group, false);
        doc.UndoUtil.RecordAddObjectEvent("Vimhopper group", group);
        group.ExpireCaches();
        group.Attributes.ExpireLayout();

        var what = objects.Count == 1 ? objects[0].NickName : $"{objects.Count} objects";
        _status = name.Length > 0 ? $"grouped {what} as {name}" : $"grouped {what}";
    }

    /// <summary>이름 입력 중: 묶일 객체를 점선 테두리로 (캔버스 좌표).</summary>
    void DrawGroupPending()
    {
        if (!_vim || _group is not { } session || Document is not { } doc) return;
        var g = _canvas.Graphics;
        using var pen = new Pen(Color.MediumSeaGreen, 2f) { DashStyle = DashStyle.Dash };
        foreach (var id in session.Ids)
        {
            if (doc.FindObject(id, true) is not { } obj) continue;
            var b = obj.Attributes.Bounds;
            b.Inflate(4, 4);
            g.DrawRectangle(pen, b.X, b.Y, b.Width, b.Height);
        }
    }
}
