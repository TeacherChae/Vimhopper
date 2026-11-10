# 0001. Core / Engine / 로더 셋으로 나누고 엔진은 핫 리로드

- 상태: 채택 (2026-10-26)
- 관련: [개발](../development.md), `src/Vimhopper.Core`, `src/Vimhopper.Engine`, `src/Vimhopper`

## 맥락

- `GH_Document`·`GH_Canvas`는 Rhino 없이 만들 수 없어서 Grasshopper를 쓰는 코드는 단위 테스트를 할 수 없습니다.
- `.gha`는 Rhino가 잠그므로 고칠 때마다 Rhino를 껐다 켜야 했습니다.

## 결정

| 프로젝트 | 대상 | 내용 |
|---|---|---|
| `Vimhopper.Core` | netstandard2.0, Grasshopper 비의존 | 키 파서, 그래프 이동·범위, 자동 정렬 계산, 점프 목록, 되풀이 기록. xUnit 테스트 |
| `Vimhopper.Engine` | net7.0-windows | 키 후킹, 명령 실행, 오버레이 그리기, 정렬 적용. Core의 결과를 문서에 반영 |
| `Vimhopper` (로더) | `.gha` | 엔진을 불러와 캔버스에 붙이기. Debug 빌드는 엔진을 임시 폴더로 복사해 언로드 가능한 `AssemblyLoadContext`에 올리고, dll이 바뀌면 갈아끼움 |

- 로직은 가능한 한 Core에 둡니다. Engine은 "Grasshopper 객체 ↔ Core의 그래프 모델" 변환과 실행만 합니다.
- 엔진이 캔버스·문서에 붙인 이벤트는 `VimController.Dispose`와 문서 변경 때 모두 해제합니다. 빠뜨리면 이전 엔진이 계속 키를 받습니다.

## 결과

- Rhino를 켜 둔 채 `scripts\watch.ps1`로 저장 → 빌드 → 반영. 로더를 고쳤을 때만 재시작.
- 테스트 585개가 Rhino 없이 돕니다. 대신 Engine 쪽(이벤트, 그리기, undo)은 [검증 목록](../verification.md)으로 사람이 Rhino에서 확인해야 합니다.
- Rhino 8은 .NET 7이지만 SDK 9로 빌드합니다. Rhino 9 (.NET 10) 대상 추가는 [로드맵](../roadmap.md#그-뒤)에.

## 버린 대안

| 대안 | 버린 이유 |
|---|---|
| 한 프로젝트 | 테스트 불가, 고칠 때마다 Rhino 재시작 |
| Grasshopper 객체를 흉내 낸 가짜로 Engine 테스트 | `GH_Document` 생성 자체가 Rhino를 요구 |
