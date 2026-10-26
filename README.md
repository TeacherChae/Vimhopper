# Vimhopper

Grasshopper 캔버스를 nvim처럼 키보드만으로 다루는 플러그인입니다.

> 상태: 초기 프로토타입 (v0.1.0) · 대상: Rhino 8 (.NET 7) · Grasshopper 1 · Rhino에서 실제 동작은 아직 검증 중입니다

## 철학

- **손은 키보드에.** 컴포넌트 사이를 오가고, 와이어를 잇고, flatten / graft 같은 데이터 연산을 붙이는 일을 마우스 없이 합니다.
- **같은 키는 항상 같은 곳으로.** 이동은 직전에 어디서 왔는지와 무관하게 지금 선택된 객체만 보고 정해집니다. ([이동 규칙](docs/navigation.md))
- **정리는 포매터가.** 코드 포매터처럼, 캔버스 배치는 사용자가 하나하나 손대지 않아도 알아서 깔끔해집니다. 필요하면 Relay도 알아서 넣고 뺍니다. ([자동 정렬](docs/formatting.md))
- **정렬된 화면에서는 hjkl이 곧 방향.** 이동과 정렬이 같은 기준을 쓰므로, 정렬된 캔버스에서는 `h`/`l`이 수평 와이어를 따라 좌우로, `j`/`k`가 같은 열을 따라 위아래로 움직입니다.
- **Grasshopper를 방해하지 않기.** 마우스로 쓰는 기본 상태는 그대로 두고, Vim 모드에서도 `Space`(컴포넌트 검색), `Ctrl+S`, `Ctrl+Z` 같은 기본 동작은 Grasshopper가 처리합니다.

## 설치

아직 배포 패키지가 없어서 소스에서 빌드합니다. Rhino 8과 [.NET SDK 9 이상](https://dotnet.microsoft.com/download)이 필요합니다.

```powershell
git clone https://github.com/TeacherChae/Vimhopper.git
cd Vimhopper
dotnet build
scripts\link-dev.ps1      # Grasshopper가 빌드 폴더의 플러그인을 불러오도록 연결 (최초 1회)
```

Rhino와 Grasshopper를 다시 켜면 로드됩니다. 연결을 해제하려면 `scripts\link-dev.ps1 -Remove`를 실행합니다. 수정하면서 개발하는 방법(Rhino를 켜둔 채 반영하는 핫 리로드 등)은 [개발](docs/development.md)을 참고하세요.

## 사용법

캔버스에서 `Esc`를 누르면 **Vim 모드**로 들어갑니다 (좌하단에 `-- VIM --`). `a`로 나옵니다.

| 하고 싶은 것 | 키 | 자세히 |
|---|---|---|
| 와이어를 따라 이동 | `h` 부모, `l` 자식, `j`/`k` 같은 열 위아래 | [키맵](docs/keymap.md#이동) · [규칙](docs/navigation.md) |
| 와이어 연결 | `o{n}`으로 출력 고르고 → 이동 → `i{n}`으로 입력에 연결 | [키맵](docs/keymap.md#연결) |
| 데이터 연산 | `o{n}` / `i{n}` 바로 뒤에 `f` flatten, `g` graft, `s` simplify, `r` reverse | [키맵](docs/keymap.md#데이터-연산자) |
| 정렬 | `==` 지금 덩어리, `g=` 문서 전체 | [키맵](docs/keymap.md#정렬) · [규칙](docs/formatting.md) |
| 되돌리기 | `u` | |
| 화면 이동·확대 | `z` 가운데로, `Ctrl+=` / `Ctrl+-` | [키맵](docs/keymap.md#화면) |

```
o0f  l  i1     출력[0]을 flatten해서 고르고, 자식으로 가서 입력[1]에 연결
1h   ==        입력[1]의 출처로 가서, 그 덩어리를 정렬
```

## 문서

- [키맵](docs/keymap.md): 전체 키 목록
- [이동 (hjkl)](docs/navigation.md): 이동 규칙과 설계 배경
- [자동 정렬](docs/formatting.md): 정렬·Relay 규칙과 설계 배경
- [개발](docs/development.md): 구조, 빌드, 테스트, 핫 리로드
- [로드맵](docs/roadmap.md)
