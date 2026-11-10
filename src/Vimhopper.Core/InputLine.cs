namespace Vimhopper.Core;

/// <summary>입력줄에 글자 하나를 넣은 결과.</summary>
public enum InputLineResult
{
    /// <summary>바뀐 것 없음 (모르는 제어 문자).</summary>
    None,
    /// <summary>글이 바뀜 (incsearch를 다시 계산).</summary>
    Changed,
    /// <summary>Enter: 확정. 입력줄은 닫힌다.</summary>
    Confirmed,
    /// <summary>Esc, 또는 빈 줄에서 Backspace (vim과 같음): 취소. 입력줄은 닫힌다.</summary>
    Cancelled,
}

/// <summary>
/// HUD 아래쪽 한 줄 입력 (vim의 명령줄). 찾기 <c>/</c>가 쓰고, 4텀의 <c>:</c> 명령줄도 쓴다.
/// 글자는 키가 아니라 WM_CHAR로 받은 문자라서 한글(IME 결과)도 그대로 들어온다.
/// </summary>
/// <remarks>
/// 커서는 늘 줄 끝에 있다 (줄 안 이동은 아직 없음). 조합 중인 한글(<see cref="Composition"/>)은 Engine이 IME에서 받아 넣고,
/// 확정되면 IME가 결과 글자를 <see cref="Feed"/>로 보낸다.
/// </remarks>
public sealed class InputLine
{
    public const char CtrlU = '\x15';

    /// <summary>앞에 보일 글자 (<c>/</c>, <c>:</c>). 0이면 입력줄이 닫혀 있다.</summary>
    public char Prompt { get; private set; }

    public bool Active => Prompt != '\0';

    /// <summary>앞에 그릴 글 (<c>/</c>, <c>그룹 이름: </c>). 닫혀 있으면 빈 글.</summary>
    public string PromptText { get; private set; } = "";

    /// <summary>확정된 글.</summary>
    public string Text { get; private set; } = "";

    /// <summary>IME가 조합 중인 글자 (한글 한 음절 등). 확정 전이라 <see cref="Text"/>에는 없다.</summary>
    public string Composition { get; set; } = "";

    /// <summary>조합 중인 글자까지 합친 글 (incsearch, Enter 때 쓰는 글).</summary>
    public string Value => Text + Composition;

    public void Open(char prompt) => Open(prompt.ToString());

    /// <summary>글 프롬프트로 연다 (<c>gG</c>의 <c>그룹 이름: </c>). <see cref="Prompt"/>는 그 첫 글자.</summary>
    public void Open(string prompt)
    {
        Prompt = prompt.Length > 0 ? prompt[0] : ' ';
        PromptText = prompt;
        Text = "";
        Composition = "";
    }

    public void Close()
    {
        Prompt = '\0';
        PromptText = "";
        Text = "";
        Composition = "";
    }

    /// <summary>
    /// 문자 하나. <c>\r</c> 확정, <c>\x1b</c> 취소, <c>\b</c> 한 글자 지우기(빈 줄이면 취소), <c>Ctrl+U</c> 모두 지우기.
    /// 그 밖의 제어 문자는 무시한다. 확정·취소해도 <see cref="Value"/>는 <see cref="Close"/> 전까지 남는다.
    /// </summary>
    public InputLineResult Feed(char c)
    {
        if (!Active) return InputLineResult.None;
        switch (c)
        {
            case '\r':
                return InputLineResult.Confirmed;
            case '\x1b':
                return InputLineResult.Cancelled;
            case '\b':
                if (Text.Length == 0 && Composition.Length == 0)
                    return InputLineResult.Cancelled;
                if (Text.Length > 0)
                    Text = Text.Substring(0, Text.Length - 1);
                return InputLineResult.Changed;
            case CtrlU:
                if (Text.Length == 0) return InputLineResult.None;
                Text = "";
                return InputLineResult.Changed;
        }
        if (char.IsControl(c))
            return InputLineResult.None;
        Text += c;
        return InputLineResult.Changed;
    }

    /// <summary>붙여넣기 (Ctrl+V): 첫 줄만, 제어 문자는 빼고.</summary>
    public InputLineResult Insert(string text)
    {
        if (!Active) return InputLineResult.None;
        var line = text.Split('\r', '\n')[0];
        var added = false;
        foreach (var c in line)
        {
            if (char.IsControl(c)) continue;
            Text += c;
            added = true;
        }
        return added ? InputLineResult.Changed : InputLineResult.None;
    }
}
