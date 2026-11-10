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
    const int VK_HANGUL = 0x15;
    const int VK_HANJA = 0x19;
    const int WM_IME_SETCONTEXT = 0x0281;
    const int WM_IME_STARTCOMPOSITION = 0x010D;
    const int WM_IME_ENDCOMPOSITION = 0x010E;
    const int WM_IME_COMPOSITION = 0x010F;
    const int WM_IME_CHAR = 0x0286;
    const int GCS_COMPSTR = 0x0008;
    const int GCS_RESULTSTR = 0x0800;
    const long ISC_SHOWUICOMPOSITIONWINDOW = 0x80000000;

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

    /// <summary>
    /// 입력줄이 열린 동안의 글자 받기. null이 아니면 키는 Grasshopper에 넘기지 않고(콜백이 가져간 키는 글자까지 삼킴),
    /// 뒤따르는 WM_CHAR와 IME가 확정한 글자를 여기로 보낸다.
    /// </summary>
    public Action<char>? TextInput { get; set; }

    /// <summary>입력줄이 열린 동안 IME가 조합 중인 글자 (빈 문자열이면 조합 끝). 조합은 Grasshopper·IME 창 대신 HUD에 그린다.</summary>
    public Action<string>? Composition { get; set; }

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
        if (TextInput != null && WndProcText(ref m))
            return;

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

    // 입력줄이 열린 동안. 여기서 끝냈으면 true (일반 키 처리로 가지 않음)
    bool WndProcText(ref Message m)
    {
        switch (m.Msg)
        {
            case WM_KEYDOWN:
                var vk = (int)m.WParam;
                // IME가 처리하는 키(조합 중인 한글, 한/영, 한자)는 IME에 맡긴다
                if (vk is VK_PROCESSKEY or VK_HANGUL or VK_HANJA)
                {
                    base.WndProc(ref m);
                    return true;
                }
                // 백틱처럼 컨트롤러가 가져간 키는 글자도 삼키고, 나머지는 WM_CHAR로 입력줄에 들어간다
                _swallowChar = _onKeyDown((Keys)vk | Control.ModifierKeys);
                return true;

            case WM_CHAR:
                if (_swallowChar)
                    _swallowChar = false;
                else
                    TextInput!((char)(long)m.WParam);
                return true;

            // DefWindowProc가 조합을 처리하지 않게 하므로 보통은 오지 않지만, 오면 확정 글자다
            case WM_IME_CHAR:
                TextInput!((char)(long)m.WParam);
                return true;

            // 기본 조합 창을 띄우지 않는다 (조합 중인 글자는 HUD 입력줄에 그림)
            case WM_IME_SETCONTEXT:
                m.LParam = (IntPtr)((long)m.LParam & ~ISC_SHOWUICOMPOSITIONWINDOW);
                base.WndProc(ref m);
                return true;
            case WM_IME_STARTCOMPOSITION:
                return true;
            case WM_IME_ENDCOMPOSITION:
                Composition?.Invoke("");
                return true;

            case WM_IME_COMPOSITION:
                var flags = (long)m.LParam;
                // 한 메시지에 확정(앞 음절)과 새 조합(다음 음절)이 같이 올 수 있다. 확정을 먼저
                if ((flags & GCS_RESULTSTR) != 0)
                    foreach (var c in Ime.Read(m.HWnd, GCS_RESULTSTR))
                        TextInput!(c);
                if ((flags & GCS_COMPSTR) != 0)
                    Composition?.Invoke(Ime.Read(m.HWnd, GCS_COMPSTR));
                return true;
        }
        return false;
    }
}

/// <summary>Vim 모드 동안 한글 IME를 끄고, 입력줄에서 조합 글자를 읽기 위한 imm32 호출.</summary>
internal static class Ime
{
    [DllImport("imm32.dll")]
    public static extern IntPtr ImmAssociateContext(IntPtr hWnd, IntPtr hIMC);

    [DllImport("imm32.dll")]
    public static extern uint ImmGetVirtualKey(IntPtr hWnd);

    [DllImport("imm32.dll")]
    static extern IntPtr ImmGetContext(IntPtr hWnd);

    [DllImport("imm32.dll")]
    static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);

    [DllImport("imm32.dll", CharSet = CharSet.Unicode)]
    static extern int ImmGetCompositionStringW(IntPtr hIMC, int dwIndex, byte[]? lpBuf, int dwBufLen);

    [DllImport("imm32.dll")]
    static extern bool ImmNotifyIME(IntPtr hIMC, int dwAction, int dwIndex, int dwValue);

    const int NI_COMPOSITIONSTR = 0x0015;
    const int CPS_CANCEL = 0x0004;

    /// <summary>조합 중인 글자(GCS_COMPSTR) 또는 확정된 글자(GCS_RESULTSTR).</summary>
    public static string Read(IntPtr hWnd, int index)
    {
        var himc = ImmGetContext(hWnd);
        if (himc == IntPtr.Zero) return "";
        try
        {
            var length = ImmGetCompositionStringW(himc, index, null, 0);
            if (length <= 0) return "";
            var buffer = new byte[length];
            ImmGetCompositionStringW(himc, index, buffer, length);
            return System.Text.Encoding.Unicode.GetString(buffer);
        }
        finally
        {
            ImmReleaseContext(hWnd, himc);
        }
    }

    /// <summary>조합 중인 글자를 버린다 (입력줄을 닫을 때, 남은 조합이 캔버스로 새지 않게).</summary>
    public static void CancelComposition(IntPtr hWnd)
    {
        var himc = ImmGetContext(hWnd);
        if (himc == IntPtr.Zero) return;
        ImmNotifyIME(himc, NI_COMPOSITIONSTR, CPS_CANCEL, 0);
        ImmReleaseContext(hWnd, himc);
    }
}
