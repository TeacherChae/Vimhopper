using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using Vimhopper.Core;

namespace Vimhopper;

// 바꾸기 c: cn 별명 바꾸기. 입력줄은 찾기(/)와 같은 InputLineBox
internal sealed partial class VimController
{
    const string RenamePrompt = "별명: ";

    /// <summary><c>cn</c>: 지금 별명을 미리 채운 입력줄을 연다. Enter면 <see cref="RenameCommand"/>를 실행한다 (<c>.</c>에 기록).</summary>
    bool OpenRename()
    {
        if (Selected is not { } obj)
        {
            _status = "선택된 객체가 없습니다";
            return false;
        }
        if (RenameError(obj) is { } error)
        {
            _status = error;
            return false;
        }
        _input.Open(RenamePrompt, obj.NickName ?? "", _savedIme, _ => { }, OnRenameConfirmed, () => _status = "별명 바꾸기 취소");
        return true;
    }

    // 빈 Enter는 그 종류의 기본 별명으로
    void OnRenameConfirmed(string text) => Execute(new RenameCommand(text.Length > 0 ? text : null));

    /// <summary>
    /// 커서 객체의 별명을 바꾼다 (<c>u</c> 한 번에 되돌아옴). <paramref name="nickName"/>이 null이면 Grasshopper 기본 별명.
    /// 별명은 계산 결과에 영향이 없으므로 다시 계산하지 않고 모양만 다시 잰다 (Grasshopper 메뉴의 별명 바꾸기와 같음).
    /// </summary>
    bool Rename(string? nickName)
    {
        if (Document is not { } doc || Selected is not { } obj)
        {
            _status = "선택된 객체가 없습니다";
            return false;
        }
        if (RenameError(obj) is { } error)
        {
            _status = error;
            return false;
        }

        var name = nickName ?? DefaultNickName(obj);
        if (name == null)
        {
            _status = "기본 별명을 찾을 수 없습니다: 별명을 그대로 둡니다";
            return false;
        }
        var old = obj.NickName ?? "";
        if (name == old)
        {
            _status = $"별명 그대로: {Show(name)}";
            return true;
        }

        doc.UndoUtil.RecordNickNameEvent("Vimhopper rename", obj);
        obj.NickName = name;
        obj.Attributes.ExpireLayout();
        _canvas.Refresh();
        _status = nickName == null ? $"기본 별명: {Show(name)}" : $"별명: {Show(old)} → {Show(name)}";
        return true;
    }

    static string Show(string nickName) => nickName.Length > 0 ? nickName : "(없음)";

    /// <summary>별명을 바꿀 수 없으면 그 이유. 스크리블·마크업은 별명 대신 글·모양을 보여 주므로 별명이 없는 것으로 친다.</summary>
    static string? RenameError(IGH_DocumentObject obj) => obj switch
    {
        GH_Scribble or GH_Markup => "별명이 없는 객체입니다",
        IGH_ActiveObject { MutableNickName: false } => "별명을 바꿀 수 없는 객체입니다",
        _ => null,
    };

    /// <summary>그 종류를 새로 놓을 때의 별명 (컴포넌트 서버에 등록된 설명). 등록되지 않은 종류면 null.</summary>
    static string? DefaultNickName(IGH_DocumentObject obj) =>
        Grasshopper.Instances.ComponentServer.EmitObjectProxy(obj.ComponentGuid)?.Desc?.NickName;
}
