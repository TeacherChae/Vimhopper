# 개발

## 구조

```
src/Vimhopper.Core     Grasshopper 비의존 로직 — netstandard2.0
  입력                   KeyParser (키 → 명령, 상태 머신), KeyParserHints (다음 키 힌트 표), Commands (명령 종류)
  이동                   GraphNavigator (hjkl), Navigator (Ctrl+hjkl), JumpList (Ctrl+o/i), LabelJump (t 라벨)
  찾기·마크              Search (/ n * # 순서), InputLine (HUD 한 줄 입력: / gG cn :), MarkStore (m ')
  명령줄                 ExCommands (: 줄 해석·이름 앞부분·Tab 완성·기록), Substitution (:s)
  범위                   GraphRange (h l H L p), VisualRange (비주얼·t·' 블록)
  변경                   RepeatRegister (.), PastePlacement (p P 위치), SliderStep (Ctrl+a/x 한 단계)
  화면                   ViewFit (z 맞춤, Ctrl+= 기준점)
  진단                   BracketNav (] [ 원인 판정·느린 순·그룹), DataShape (K 트리 모양과 재사용 힌트)
  설정                   UserConfig (매핑·옵션·vimhopperrc), KeyRemapper (매핑 펼치기, 파서 앞단), KeyNotation (<C-x> 표기)
  정렬                   CanvasLayout (공개 API), LayeredLayout, NetworkSimplex, WireGeometry
src/Vimhopper.Engine   Grasshopper에 적용하는 층 — Rhino 없이 테스트할 수 없음
                         CanvasKeyHook          캔버스 키 후킹, 입력줄의 IME
                         VimController          명령 실행, 오버레이·HUD 그리기. 기능별 partial:
                                                .Search .Bracket .DataShape .Group .Rename .Increment .Cmdline .Config
                         InputLineBox           입력줄 그리기·키 처리
                         WireHighlight          연결 강조
                         Formatter              정렬 결과를 문서에 적용 (undo)
                         DocumentGraph          GH_Document → Core 그래프
src/Vimhopper          얇은 로더 (.gha) — 엔진을 불러와 캔버스에 붙이고, Debug 빌드에서는 핫 리로드
tests/                 Vimhopper.Core 단위 테스트 (xUnit)
scripts/link-dev.ps1   개발용 .ghlink 생성
scripts/watch.ps1      저장할 때마다 엔진 자동 빌드
```

- 키 입력은 캔버스 HWND를 서브클래싱(`NativeWindow`)해서 Grasshopper보다 먼저 받습니다.
- 큰 기능의 Engine 코드는 `VimController.{기능}.cs` partial로 뗍니다. 병렬 작업이 `VimController.cs`에서 덜 부딪힙니다.
- 로직은 가능한 한 Core에 둡니다. Grasshopper 문서(`GH_Document`)는 Rhino 없이 만들 수 없어서 Engine은 단위 테스트를 할 수 없기 때문입니다.

## 브랜치

텀(로드맵의 한 단계)마다 `main`에서 `term/{n}-{이름}`을 만들고, 작업 브랜치는 텀 브랜치에서 가릅니다 (`main` → `term/2-jump` → `feature/label-jump`). 텀 밖의 작업은 `main`에서 바로 가릅니다. 흐름은 [CLAUDE.md](../CLAUDE.md)와 [0018](adr/0018-term-workflow.md).

| 접두사 | 용도 | 예 |
|---|---|---|
| `feature/` | 새 기능 | `feature/visual-mode` |
| `fix/` | 버그 수정 | `fix/relay-undo` |
| `hotfix/` | `main`에 바로 들어가야 하는 급한 수정 | `hotfix/loader-crash` |
| `refactor/` | 동작 변화 없는 구조 정리 | `refactor/key-parser-states` |
| `chore/` | 빌드, 스크립트, 문서 정리 등 | `chore/build-warnings` |

## 빌드와 테스트

필요한 것: .NET SDK 9 이상 (대상은 Rhino 8의 .NET 7이지만 SDK 9로 빌드됩니다). Grasshopper·RhinoCommon 라이브러리는 첫 빌드 때 NuGet에서 자동으로 받습니다.

```powershell
scripts\link-dev.ps1      # 최초 1회: Grasshopper가 빌드 폴더를 직접 로드하도록 연결
dotnet build              # 전체 빌드 → src/Vimhopper/bin/Debug/net7.0-windows/
dotnet test
```

C#은 컴파일 언어라서 `.cs` 소스를 `.dll`로 바꾸는 빌드가 필요합니다. `.gha`는 이름만 바뀐 `.dll`이고, Grasshopper는 `Libraries` 폴더의 `.gha`를 플러그인으로 불러옵니다. `link-dev.ps1`은 그 폴더에 빌드 폴더를 가리키는 `.ghlink`를 만들어, 빌드할 때마다 복사할 필요가 없게 합니다.

## 핫 리로드

Debug 빌드의 로더는 엔진(`Vimhopper.Engine.dll`, `Vimhopper.Core.dll`)을 임시 폴더로 복사해서 언로드 가능한 `AssemblyLoadContext`에 올립니다. 원본 dll이 잠기지 않으므로 **Rhino를 켜둔 채 빌드할 수 있고**, 빌드 폴더의 dll이 바뀌면 이전 엔진을 떼어내고 새 엔진으로 바꿉니다. Vim 모드 여부와 선택 객체는 리로드 후에도 유지되고, Rhino 명령창에 `Vimhopper: engine reloaded`가 표시됩니다.

```powershell
scripts\watch.ps1         # 파일을 저장하면 자동 빌드 → Rhino에 자동 반영
```

- **재시작이 필요한 경우**: 로더(`src/Vimhopper`)를 고쳤을 때. 이때는 Rhino를 닫고 `dotnet build` 합니다.
- 엔진의 `VimController`는 캔버스에 연결한 이벤트를 모두 `Dispose`에서 해제해야 합니다. 빠뜨리면 이전 버전이 계속 키를 받습니다.
- Release 빌드는 핫 리로드 없이 엔진을 일반 방식으로 로드합니다.
