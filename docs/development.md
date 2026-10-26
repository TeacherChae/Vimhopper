# 개발

## 구조

```
src/Vimhopper.Core     Grasshopper 비의존 로직 — netstandard2.0
                         KeyParser       키 입력 → 명령 (상태 머신)
                         GraphNavigator  hjkl (연결 따라 이동)
                         Navigator       Ctrl+hjkl (화면 방향 이동)
                         CanvasLayout    자동 정렬 (LayeredLayout, WireGeometry)
src/Vimhopper.Engine   실제 플러그인 로직 — 키 후킹, 명령 실행, 오버레이 그리기, 정렬 적용
src/Vimhopper          얇은 로더 (.gha) — 엔진을 불러와 캔버스에 붙이고, Debug 빌드에서는 핫 리로드
tests/                 Vimhopper.Core 단위 테스트 (xUnit)
scripts/link-dev.ps1   개발용 .ghlink 생성
scripts/watch.ps1      저장할 때마다 엔진 자동 빌드
```

- 키 입력은 캔버스 HWND를 서브클래싱(`NativeWindow`)해서 Grasshopper보다 먼저 받습니다.
- 로직은 가능한 한 Core에 둡니다. Grasshopper 문서(`GH_Document`)는 Rhino 없이 만들 수 없어서 Engine은 단위 테스트를 할 수 없기 때문입니다.

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
