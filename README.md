# Vimhopper

Grasshopper 캔버스를 nvim처럼 키보드만으로 다루는 플러그인입니다.

> 상태: 프로토타입, [로드맵](docs/roadmap.md) 4텀(명령줄과 설정)까지 · 대상: Rhino 8 (.NET 7) · Grasshopper 1 · Rhino에서 실제 동작은 아직 검증 중입니다 ([검증 목록](docs/verification.md))

## 철학

- **손은 키보드에.** 컴포넌트 사이를 오가고, 와이어를 잇고, flatten / graft 같은 데이터 연산을 붙이는 일을 마우스 없이 합니다.
- **같은 키는 항상 같은 곳으로.** 이동은 직전에 어디서 왔는지와 무관하게 지금 선택된 객체만 보고 정해집니다. ([이동 규칙](docs/navigation.md))
- **정리는 포매터가.** 코드 포매터처럼, 캔버스 배치는 사용자가 하나하나 손대지 않아도 알아서 깔끔해집니다. 필요하면 Relay도 알아서 넣고 뺍니다. ([자동 정렬](docs/formatting.md))
- **정렬된 화면에서는 hjkl이 곧 방향.** 이동과 정렬이 같은 기준을 쓰므로, 정렬된 캔버스에서는 `h`/`l`이 수평 와이어를 따라 좌우로, `j`/`k`가 같은 열을 따라 위아래로 움직입니다.
- **조합되는 문법.** vim처럼 연산자(`y` `d` `=` `gc` `gy`)와 범위(`h` `l` `H` `L` `p`, 라벨·마크까지의 블록)를 조합하고, `.`으로 되풀이합니다. 키를 몰라도 잠시 멈추면 다음에 올 수 있는 키가 화면에 나옵니다.
- **Grasshopper를 방해하지 않기.** 마우스로 쓰는 기본 상태는 그대로 두고, Vim 모드에서도 `Ctrl+S`, `Ctrl+Z` 같은 기본 동작은 Grasshopper가 처리합니다. Vim 모드의 `Space`는 리더 키이고, 컴포넌트 검색은 `Space Space`입니다.

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

캔버스에서 `` ` ``(백틱, `1` 왼쪽 키)를 누르면 **Vim 모드**로 들어갑니다 (좌하단에 `-- VIM(normal) --`). 한 번 더 누르면 나옵니다.

| 하고 싶은 것 | 키 | 자세히 |
|---|---|---|
| 와이어를 따라 이동 | `h` 부모, `l` 자식, `j`/`k` 같은 열 위아래, `3l`처럼 숫자는 반복 | [키맵](docs/keymap.md#이동) · [규칙](docs/navigation.md) |
| 번호로 골라 이동 | `gi{n}` 입력 n의 출처, `go{n}` 출력 n을 받는 객체 | [키맵](docs/keymap.md#이동) |
| 덩어리 사이 이동 | `}` 다음 덩어리, `{` 이전 덩어리 (와이어로 이어진 묶음, vim의 문단) | [키맵](docs/keymap.md#덩어리-이동) |
| 화면의 객체로 바로 | `t` 누르면 객체마다 라벨, 라벨을 치면 그리로 (`o0 t{라벨} i1`로 먼 곳에 연결) | [키맵](docs/keymap.md#라벨-점프) |
| 이름으로 찾기 | `/` 이름·별명·종류 (한글 가능), `n` / `N` 다음·이전, `*` / `#` 같은 종류 | [키맵](docs/keymap.md#찾기) |
| 마크 | `ma`로 달고 `'a`로 돌아옴, `y'a`처럼 범위로도 | [키맵](docs/keymap.md#마크) |
| 왔던 곳으로 | `Ctrl+o` 뒤로, `Ctrl+i` 앞으로 (`gi` `go` `{` `}`, 라벨·찾기·마크 점프, 숫자 이동이 기록됨) | [키맵](docs/keymap.md#점프-목록) |
| 컴포넌트 넣기 | `Space Space`로 커서 객체 오른쪽에 검색창, 놓으면 커서가 그 컴포넌트로 | [키맵](docs/keymap.md#넣기) |
| 와이어 연결 | `o{n}`으로 출력 고르고 → 이동 → `i{n}`으로 입력에 연결 | [키맵](docs/keymap.md#연결) |
| 데이터 연산 | `o{n}` / `i{n}` 바로 뒤에 `f` flatten, `g` graft, `s` simplify, `r` reverse | [키맵](docs/keymap.md#데이터-연산자) |
| 범위에 적용 | `yL` 하류 전체 복사, `yt{라벨}` 라벨까지 블록 복사, `=2h` 상류 2단계 정렬, `dd` 삭제, `gcH` 상류 끄기, `dp` 덩어리 삭제, 비주얼 모드 `H`/`L` | [키맵](docs/keymap.md#연산자--범위) |
| 정렬 | `==` 지금 덩어리, `g=` 문서 전체 | [키맵](docs/keymap.md#정렬) · [규칙](docs/formatting.md) |
| 되풀이 | `.` 마지막 변경을 지금 객체에서, `3.` 범위 단계를 3으로 | [키맵](docs/keymap.md#되풀이) |
| 붙여넣기 | `p` 커서 오른쪽, `P` 왼쪽에 붙이고 커서를 붙인 객체로 | [키맵](docs/keymap.md#붙여넣기) |
| 오류·경고·느린 곳으로 | `]e` / `[e` 오류의 원인, `]w` 경고의 원인, `]s` 느린 순, `]g` 그룹 | [키맵](docs/keymap.md#다음이전---) |
| 데이터 모양 보기 | `K` 입력·출력의 가지·항목 수와 짝이 안 맞는 입력 힌트 | [키맵](docs/keymap.md#데이터-모양) |
| 그룹·별명 | `gGp` 덩어리를 그룹으로 묶고 이름 입력, `cn` 별명 바꾸기 | [키맵](docs/keymap.md#바꾸기) |
| 슬라이더 값 | `Ctrl+a` / `Ctrl+x` 한 단계 올리기·내리기, `5Ctrl+a` 다섯 단계, 토글은 뒤집기 | [키맵](docs/keymap.md#값-바꾸기) |
| 명령줄 | `:w` 저장, `:rec` 다시 계산, `:rh _Line` Rhino 명령, `:%s/old/new/g` 별명 일괄 바꾸기 | [키맵](docs/keymap.md#명령줄-) |
| 내 키 설정 | `%APPDATA%\Vimhopper\vimhopperrc`에 `nnoremap <leader>w :w<CR>`, `set timeoutlen=600` 같은 줄 | [키맵](docs/keymap.md#설정) |
| 되돌리기 | `u` | |
| 화면 | `zz` 가운데로, `zL` `zp`처럼 범위를 화면에 맞춤, `Ctrl+=` / `Ctrl+-`는 커서 객체 기준 | [키맵](docs/keymap.md#화면) |

Vim 모드에서는 커서 객체의 입력 와이어(파랑)와 출력 와이어(자주)가 굵게 강조되고, `h` / `l`이 갈 객체에 표시가 붙습니다. `d`, `g`, `y` 같은 접두어를 누르고 잠시 멈추면 다음 키 목록이 나옵니다.

```
o0f  l  i1     출력[0]을 flatten해서 고르고, 자식으로 가서 입력[1]에 연결
gi1  ==        입력[1]의 출처로 가서, 그 덩어리를 정렬
d2l  j  .      하류 2단계 삭제, 아래 객체로 가서 되풀이
}  gcp  Ctrl+o 다음 덩어리로 가서 통째로 끄고, 원래 자리로
o0  /sum Enter  i1  출력[0]을 고르고, 이름에 sum이 든 객체를 찾아 입력[1]에 연결
ma  …  y'a  p   마크를 달고, 나중에 마크까지의 블록을 복사해서 오른쪽에 붙임
```

## 문서

| 무엇 | 문서 |
|---|---|
| 쓰기 | [키맵](docs/keymap.md) (전체 키), [Rhino 검증 목록](docs/verification.md) (사람이 Rhino에서 확인할 것) |
| 정한 규칙 | [이동 (hjkl)](docs/navigation.md), [자동 정렬](docs/formatting.md), [결정 기록 (ADR)](docs/adr/README.md) |
| 계획 | [로드맵](docs/roadmap.md): 1텀 문법 → 2텀 찾기와 점프 → 3텀 읽기와 진단 → 4텀 명령줄과 설정 (끝) → 그 뒤 |
| 제안·배경 | [vim에서 더 가져올 것](docs/vim-ideas.md), [진단 도구](docs/diagnostics-ideas.md), [리더 키·검색·줌·명령줄](docs/leader-and-zoom-ideas.md), [자동 정렬 개선 조사](docs/formatting-research.md) |
| 조사 | [nvim의 철학과 와우 포인트](docs/research/nvim.md), [Grasshopper 사용자의 불편](docs/research/grasshopper-users.md) |
| 개발 | [개발](docs/development.md) (구조, 빌드, 테스트, 핫 리로드), [CLAUDE.md](CLAUDE.md) (코디네이터·구현·리뷰 에이전트, herdr로 병렬 실행) |
