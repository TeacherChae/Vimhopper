using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Vimhopper;

/// <summary>
/// 캔버스 HWND를 서브클래싱해서 Grasshopper보다 먼저 키 입력을 받는다.
/// 콜백이 true를 돌려주면 해당 WM_KEYDOWN과 뒤따르는 WM_CHAR를 삼킨다.
/// </summary>
internal sealed class CanvasKeyHook : NativeWindow, IDisposable
{
    const int WM_KEYDOWN = 0x0100;
    const int WM_CHAR = 0x0102;
    const int VK_PROCESSKEY = 0xE5;

    readonly Control _control;
    readonly Func<Keys, bool> _onKeyDown;
    bool _swallowChar;

    public CanvasKeyHook(Control control, Func<Keys, bool> onKeyDown)
    {
        _control = control;
        _onKeyDown = onKeyDown;
        if (control.IsHandleCreated)
            AssignHandle(control.Handle);
        control.HandleCreated += OnHandleCreated;
        control.HandleDestroyed += OnHandleDestroyed;
    }

    public void Dispose()
    {
        _control.HandleCreated -= OnHandleCreated;
        _control.HandleDestroyed -= OnHandleDestroyed;
        ReleaseHandle();
    }

    void OnHandleCreated(object? sender, EventArgs e) => AssignHandle(_control.Handle);

    void OnHandleDestroyed(object? sender, EventArgs e) => ReleaseHandle();

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_KEYDOWN)
        {
            var vk = (int)m.WParam;
            // IME가 켜져 있으면 실제 키 대신 VK_PROCESSKEY가 온다
            if (vk == VK_PROCESSKEY)
                vk = (int)Ime.ImmGetVirtualKey(m.HWnd);

            _swallowChar = _onKeyDown((Keys)vk | Control.ModifierKeys);
            if (_swallowChar)
                return;
        }
        else if (m.Msg == WM_CHAR && _swallowChar)
        {
            _swallowChar = false;
            return;
        }

        base.WndProc(ref m);
    }
}

/// <summary>Vim 모드 동안 한글 IME를 끄기 위한 imm32 호출.</summary>
internal static class Ime
{
    [DllImport("imm32.dll")]
    public static extern IntPtr ImmAssociateContext(IntPtr hWnd, IntPtr hIMC);

    [DllImport("imm32.dll")]
    public static extern uint ImmGetVirtualKey(IntPtr hWnd);
}
