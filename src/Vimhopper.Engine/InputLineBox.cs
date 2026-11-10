using System;
using System.Drawing;
using System.Windows.Forms;
using Grasshopper.GUI.Canvas;
using Vimhopper.Core;

namespace Vimhopper;

/// <summary>
/// HUD 아래쪽 한 줄 입력 (vim의 명령줄). 찾기 <c>/</c>가 쓰고, 4텀의 <c>:</c> 명령줄도 이것을 쓴다.
/// 글의 상태는 Core의 <see cref="InputLine"/>, 여기서는 키 후킹·IME·그리기만 한다.
/// </summary>
/// <remarks>
/// 열린 동안은 키를 파서 대신 글자(WM_CHAR)로 받고, Vim 모드가 꺼 둔 캔버스 IME를 다시 켜서 한글을 받는다.
/// 조합 중인 한글은 IME 창 대신 이 줄에 주황 밑줄로 그린다. 닫으면 남은 조합을 버리고 IME를 다시 끈다.
/// </remarks>
internal sealed class InputLineBox : IDisposable
{
    readonly GH_Canvas _canvas;
    readonly CanvasKeyHook _hook;
    readonly InputLine _line = new();
    // 한글 설명·NickName이 깨지지 않게 한글 글꼴로
    readonly Font _font = new("Malgun Gothic", 10f, FontStyle.Bold);

    Action<string>? _changed;
    Action<string>? _confirmed;
    Action? _cancelled;

    public InputLineBox(GH_Canvas canvas, CanvasKeyHook hook)
    {
        _canvas = canvas;
        _hook = hook;
    }

    public bool Active => _line.Active;

    /// <summary>확정된 글 (조합 중인 글자 빼고).</summary>
    public string Text => _line.Text;

    /// <summary>IME가 한글을 조합하는 중인지.</summary>
    public bool Composing => _line.Composition.Length > 0;

    /// <summary>글을 통째로 바꾼다 (<c>:</c>의 Tab 완성·기록). 바뀜 콜백은 부르지 않는다 (사용자가 고친 것이 아님).</summary>
    public void SetText(string text)
    {
        if (!_line.Active) return;
        if (_line.Composition.Length > 0)
            Ime.CancelComposition(_canvas.Handle);
        _line.Replace(text);
    }

    /// <summary>입력줄을 연다.</summary>
    /// <param name="ime">Vim 모드에 들어갈 때 떼어 둔 캔버스 IME. 열린 동안만 다시 붙인다 (없으면 한글을 못 받음).</param>
    /// <param name="changed">글이 바뀔 때마다 (조합 중인 글자 포함).</param>
    /// <param name="confirmed">Enter. 입력줄은 이미 닫혀 있다.</param>
    /// <param name="cancelled">Esc, 빈 줄에서 Backspace. 입력줄은 이미 닫혀 있다.</param>
    public void Open(char prompt, IntPtr ime, Action<string> changed, Action<string> confirmed, Action cancelled) =>
        Open(prompt.ToString(), "", ime, changed, confirmed, cancelled);

    /// <summary>입력줄을 연다. <paramref name="text"/>는 미리 채울 글 (<c>cn</c>의 지금 별명), 커서는 그 끝.</summary>
    public void Open(string prompt, string text, IntPtr ime, Action<string> changed, Action<string> confirmed, Action cancelled)
    {
        Close();
        _line.Open(prompt, text);
        _changed = changed;
        _confirmed = confirmed;
        _cancelled = cancelled;
        _hook.TextInput = OnChar;
        _hook.Composition = OnComposition;
        if (ime != IntPtr.Zero)
            Ime.ImmAssociateContext(_canvas.Handle, ime);
    }

    /// <summary>콜백 없이 닫는다 (Vim 모드 나가기, 문서 바뀜, 핫 리로드). 열려 있지 않으면 아무것도 안 한다.</summary>
    public void Close()
    {
        if (!_line.Active) return;
        // 조합 취소 메시지가 캔버스로 새지 않게 후킹을 풀기 전에 버린다
        if (_line.Composition.Length > 0)
            Ime.CancelComposition(_canvas.Handle);
        _hook.TextInput = null;
        _hook.Composition = null;
        Ime.ImmAssociateContext(_canvas.Handle, IntPtr.Zero);
        _line.Close();
        _changed = null;
        _confirmed = null;
        _cancelled = null;
    }

    public void Dispose()
    {
        Close();
        _font.Dispose();
    }

    void OnChar(char c)
    {
        // Ctrl+V: 클립보드 첫 줄 붙여넣기
        const char ctrlV = '\x16';
        var result = c == ctrlV
            ? Clipboard.ContainsText() ? _line.Insert(Clipboard.GetText()) : InputLineResult.None
            : _line.Feed(c);
        Handle(result);
    }

    void OnComposition(string text)
    {
        if (!_line.Active || _line.Composition == text) return;
        _line.Composition = text;
        Handle(InputLineResult.Changed);
    }

    void Handle(InputLineResult result)
    {
        switch (result)
        {
            case InputLineResult.Changed:
                _changed?.Invoke(_line.Value);
                break;
            case InputLineResult.Confirmed:
                var value = _line.Value;
                var confirmed = _confirmed;
                Close();
                confirmed?.Invoke(value);
                break;
            case InputLineResult.Cancelled:
                var cancelled = _cancelled;
                Close();
                cancelled?.Invoke();
                break;
            default:
                return;
        }
        _canvas.Refresh();
    }

    /// <summary>
    /// 모드 표시줄 자리에 입력줄을 그린다 (화면 좌표). 확정된 글은 흰색, 조합 중인 글자는 주황 밑줄, 끝에 커서와 <paramref name="info"/>.
    /// </summary>
    /// <returns>그린 영역 (위에 다른 패널을 얹을 때).</returns>
    public RectangleF Draw(Graphics g, float left, float bottom, string info)
    {
        using var format = StringFormat.GenericTypographic;
        var text = _line.Prompt + _line.Text;
        var composition = _line.Composition;
        var tail = info.Length > 0 ? "    " + info : "";

        var textWidth = g.MeasureString(text, _font, PointF.Empty, format).Width;
        var compWidth = composition.Length > 0 ? g.MeasureString(composition, _font, PointF.Empty, format).Width : 0;
        var tailWidth = tail.Length > 0 ? g.MeasureString(tail, _font, PointF.Empty, format).Width : 0;
        const float pad = 6, caret = 6;
        var height = _font.GetHeight(g) + 4;

        var rect = new RectangleF(left, bottom - height, pad * 2 + textWidth + compWidth + caret + tailWidth, height);
        using (var back = new SolidBrush(Color.FromArgb(200, 30, 30, 30)))
            g.FillRectangle(back, rect);

        var x = rect.X + pad;
        var y = rect.Y + 2;
        g.DrawString(text, _font, Brushes.White, x, y, format);
        x += textWidth;
        if (composition.Length > 0)
        {
            g.DrawString(composition, _font, Brushes.Orange, x, y, format);
            using var underline = new Pen(Color.Orange, 1f);
            g.DrawLine(underline, x, rect.Bottom - 3, x + compWidth, rect.Bottom - 3);
            x += compWidth;
        }
        // 커서 (늘 줄 끝)
        using (var pen = new Pen(Color.White, 1.5f))
            g.DrawLine(pen, x + 1, rect.Y + 4, x + 1, rect.Bottom - 4);
        x += caret;
        if (tail.Length > 0)
            g.DrawString(tail, _font, Brushes.LightGray, x, y, format);
        return rect;
    }
}
