# 0002. 캔버스 HWND 서브클래싱으로 Grasshopper보다 먼저 키를 받음

- 상태: 채택 (2026-10-26)
- 관련: `src/Vimhopper.Engine/CanvasKeyHook.cs`, [키맵 — 모드](../keymap.md#모드)

## 맥락

Grasshopper의 키 이벤트는 Grasshopper가 이미 처리한 뒤에 오거나(`Space` 검색창, `Delete` 등) 아예 오지 않습니다. vim처럼 글자 키를 가져가려면 그보다 먼저 받아야 합니다.

## 결정

- 캔버스 HWND를 `NativeWindow`로 서브클래싱해서 `WndProc`에서 키 메시지를 먼저 받습니다.
- Vim 모드에서만 가져가고, Grasshopper 모드에서는 모두 넘깁니다.
- Vim 모드에서 가져가는 `Ctrl` 조합: `Ctrl+hjkl`, `Ctrl+r`, `Ctrl+o` / `Ctrl+i`, `Ctrl+=` / `Ctrl+-`. 나머지 `Ctrl`/`Alt` 조합, `Delete`, 방향키, PageUp/Down, F키는 Grasshopper로.
- Vim 모드에서는 캔버스의 IME를 끄고 모드를 나가면 되돌립니다 (한글 입력 중 `h`가 `ㅗ`가 되지 않게).
- Ctrl 키는 파서 안에서 사용 영역 문자(U+E000…)로 바꿔 넣습니다 (`KeyParser.CtrlO` 등).

## 결과

- Grasshopper 기본 `Ctrl+O`(열기)는 Vim 모드에서 못 씁니다.
- **확인 안 됨**: 메뉴 단축키(`Ctrl+O`)가 `WndProc`보다 먼저 처리될 수 있습니다. 그러면 `IMessageFilter`나 `ProcessCmdKey`로 바꿔야 합니다 ([검증 목록](../verification.md)).
- IME를 끄므로 Vim 모드 안의 텍스트 입력(앞으로의 `/` 찾기, `:` 명령줄)은 한글을 받을 수 없습니다. 필요하면 입력줄이 열린 동안만 IME를 켭니다.

## 버린 대안

| 대안 | 버린 이유 |
|---|---|
| 캔버스 `KeyDown` 이벤트 | Grasshopper 자신의 키 처리(`Space` 검색창 등)보다 먼저 받고 막는다는 보장이 없음 |
| 전역 키보드 훅 | Rhino 전체에 영향, 포커스 판정이 따로 필요 |
