# Grasshopper 사용자 조사: 많이 쓰는 기능과 불편한 점

> 조사 문서입니다 (2026-11 기준). McNeel 포럼(discourse.mcneel.com), food4rhino, 블로그·강의 자료를 훑어 정리했습니다. 포럼의 좋아요 수는 수집한 페이지에 보이지 않아 **답글 수·조회수**를 수요의 근거로 썼습니다. reddit은 검색에 잡히지 않아 빠졌습니다.

## 요약

- 가장 자주 나오는 불만은 **스파게티 와이어**와 **데이터 트리 짝 맞춤**입니다. 포럼 Grasshopper 카테고리 질문의 약 2/3가 데이터·리스트 논리라는 사용자 추정이 있습니다 ([근거](#3-불편한-점-순위)).
- 그다음은 **남이(또는 한 달 전의 내가) 만든 정의를 읽기 어렵다**는 것과 **경고·null의 원인을 못 찾는다**는 것입니다.
- 키보드 조작 요청은 꾸준하지만 McNeel은 단일 키 단축키를 "가까운 시일 안에는 없다"고 답했습니다(Rhino 명령줄과 충돌). **Vimhopper가 메우는 빈자리가 정확히 여기입니다.**
- 플러그인 생태계는 문제마다 따로 있습니다(Telepathy = 와이어 줄이기, Bifocals = 이름 표시, Pancake/MetaHopper = 상류 선택·병목, Traceback = 원인 추적). Vimhopper는 이것들을 **키보드 한 문법(이동 + 연산자 + 범위)** 으로 묶을 수 있습니다.

## 1. 많이 쓰는 기본 기능과 숨은 단축키

| 기능 | 조작 | Vimhopper와의 관계 |
|---|---|---|
| 와이어 추가 연결 | `Shift`+드래그 | `I{n}`과 같음 |
| 와이어 끊기 | `Ctrl`+드래그 | `di{n}` / `do{n}` |
| 와이어 전부 옮기기 | `Ctrl+Shift`+드래그 (그립에서) | **없음** → 후보 ([4번](#4-불편한-점별-vimhopper-기능-제안)) |
| 와이어 끝으로 점프 | `Ctrl+←` / `Ctrl+→`, `Shift`를 더하면 연결된 것 모두 선택 | `h`/`l`, `vH`/`vL`과 겹침. Grasshopper가 처리하는 방향키이므로 충돌 없음 |
| 리본에서 컴포넌트 위치 찾기 | `Ctrl+Alt`+클릭 (info mode) | 관계 없음 |
| 복사하며 이동 | 이동 중 `Alt` | `yy` + 붙여넣기 |
| 화면 가운데 붙여넣기 | `Ctrl+Shift+V` | |
| 캔버스 벌리기 | 빈 곳에서 `Alt`+드래그 | 정렬(`==`)이 대신함 |
| 미리보기 끄기 / 켜기 | `Ctrl+Q`, 선택 반전 `Ctrl+Shift+I` | **없음** → `gp{범위}` 후보 |
| 켜기 / 끄기 | `Ctrl+E` (Enable/Disable) | `gc{범위}` |
| 그룹 | `Ctrl+G` | **없음** → `gG{범위}` 후보 |
| 고해상도 캡처 | `Ctrl+Shift+Q` | |
| 컴포넌트 검색 | `Space` / 더블클릭 (GH2 beta는 `?`) | Grasshopper가 그대로 처리 |
| 와이어 숨기기 | `W` 뒤 `H` (Rhino 8 초기에 깨졌다가 수정) | |
| 저장된 뷰 | Canvas Navigator(Named Views) | marks 후보 |
| 그 밖에 | Data Recorder, Panel / Param Viewer, Profiler 위젯, 단축키 편집(File → Interface) | 진단·프로파일 제안과 연결 |

출처: [bimcorner: 8 shortcuts](https://bimcorner.com/8-grasshopper-shortcuts-which-you-should-know/), [grasshopper3d: hotkeys](https://www.grasshopper3d.com/forum/topics/what-hotkeys-and-shortcuts-are-available-in-grasshopper), [Hide Wires 단축키 버그](https://discourse.mcneel.com/t/rhino-8-grasshopper-hide-wires-keyboard-shortcut-not-working/168350)

**공식 단축키의 한계** (키보드 요청이 계속 나오는 이유)

- [Wish: adding keyboard shortcuts in GH2](https://discourse.mcneel.com/t/wish-adding-keyboard-shortcuts-in-grasshopper-2/140532) (답글 10): flatten/graft/reparameterize, 입력 모두 끊기, 정렬, 선택한 두 컴포넌트 자동 연결, 이름 바꾸기를 키로 원함. David Rutten: Ctrl/Shift 조합은 언젠가, **단일 키는 Rhino 명령줄과 충돌해서 "가까운 시일 안에는 없음"** (RH-68139).
- [View 메뉴 단축키](https://discourse.mcneel.com/t/keyboard-shortcuts-for-grasshopper-view-menu/115382) (RH-62147), [줌 메뉴 단축키](https://discourse.mcneel.com/t/grasshopper-ui-feature-request-zoom-menu-keyboard-shortcut-capability/195177), [검색을 Tab으로·커서 위치에 놓기](https://discourse.mcneel.com/t/request-make-the-hotkey-for-invoking-the-radial-menu-in-grasshopper-great-again/81836), [GH2 Suggestions: interface, shortcuts](https://discourse.mcneel.com/t/grasshopper2-suggestions-interface-shortcuts-components/85865) (답글 6, 조회 1,357).
- Vimhopper는 Vim 모드라는 별도 상태로 들어가서 이 충돌을 피합니다. Rutten이 막혔다고 한 지점을 모드로 푼 셈이라, 소개 문구로도 쓸 만합니다.

## 2. 캔버스·생산성 플러그인

| 플러그인 | 해결하는 문제 | 수요 근거 | Vimhopper와 겹침 |
|---|---|---|---|
| **Telepathy** | 무선 송수신(키 이름으로 캔버스 어디로든 데이터 전달), 닉네임 찾아 바꾸기 | food4rhino 약 14,900 다운로드. 와이어 줄이기 질문마다 추천됨. 오래 관리되지 않아 [개조판](https://discourse.mcneel.com/t/telepathy-s/217423)이 나옴 | 낮음. 포매터의 Relay와 목적이 같음(긴 와이어 정리) |
| **Bifocals** / Bedrock **OverSight** | 아이콘 모드에서 컴포넌트 풀네임 라벨 표시 | 강의 자료([TU Delft](https://interactivetextbooks.tudelft.nl/rhino-grasshopper/Grasshopper_Rhino_course/4_Troubleshooting/Troubleshooting_Grasshopper/Bifocals/%21index.html))에서 필수로 소개 | 중간. 커서 객체 이름 표시로 일부 대체 가능 |
| **Pancake** | 상류·하류·연결 전체 선택, 병목(성능) 분석, 파일 관련 도구 | [Connectivity Analysis](https://makemove.gitbook.io/pancake/features/connectivity-analysis) | **높음**. `vH`/`vL`, `yH` 등이 같은 일을 키보드로 함 |
| **MetaHopper** | Grasshopper로 Grasshopper 조작(종류·그룹별 선택, 무거운 컴포넌트 목록) | food4rhino 491표(본 것 중 최다) | 중간. 선택·병목 탐색 |
| **AutoGraph**, OCD Tools, Sheepdog | 자동 배치 | [Canvas-organization plugin](https://discourse.mcneel.com/t/canvas-organization-plugin/219431) (2026-05)에서 추천. AutoGraph는 Rhino 4/5 빌드만 있음 | **높음**. `==` / `g=`가 같은 문제, 현재 쓸 만한 대안이 사실상 없음 |
| **Snapping Gecko** | 이동할 때 자석 가이드로 정렬 | "GH를 다시 만든다면 기본으로 넣자"는 [의견](https://discourse.mcneel.com/t/if-grasshopper-was-rebuilt-for-2026-what-should-change-or-stay/211626) | 중간. 포매터가 상위 호환 |
| Wires Renderer | 와이어를 직각·색으로 그림 | Rhino 8에서 안 불러와진다는 [보고](https://discourse.mcneel.com/t/can-wires-renderer-plugin-turn-native/213227) | 낮음 |
| Wire cutters + Pie Menu | Blender식 제스처로 와이어 자르기·Relay 넣기·출력 합치기 | [2026-10 공개](https://discourse.mcneel.com/t/wire-cutters-pie-menu-for-grasshopper/223751), 반응 적음 | 중간. 마우스 제스처 vs 키보드 |
| **GHPick** | 뷰포트 지오메트리를 클릭하면 만든 컴포넌트로 이동 | [2026-09 공개](https://discourse.mcneel.com/t/ghpick-pick-grasshopper-components-directly-from-the-rhino-viewport/222932), 사용자 피드백 활발 | 낮음. 점프 목적지로 연결 가능 |
| **Traceback** | 빨간 컴포넌트의 원인을 상류에서 추적·설명 | [2026-07 공개](https://discourse.mcneel.com/t/traceback-grasshopper-error-explainer-root-cause-tracer/221202) | **높음**. [진단 도구](../diagnostics-ideas.md) 2번과 같음 |
| Heteroptera | 타이머·카운터·이벤트 등 유틸 | 쓰기 어렵다는 [의견](https://discourse.mcneel.com/t/ideas-for-grasshopper-plugins-components/157996) | 없음 |
| Elefront, Human UI, LunchBox, Wombat, Squid/Jackalope | Rhino 객체 속성·베이크 / 사용자 UI / 범용 유틸 | 널리 쓰이나 캔버스 조작과 무관 | 없음 |
| Hops, Data Input/Output | 정의를 함수처럼 나눠 호출, 파일 분리 | 팀 작업 [조언](https://discourse.mcneel.com/t/using-grasshopper-effectively-in-teams/101961/3) | 없음 |
| AI: **Ant**, Raven, GHPT, Physalia, HOGER | 자연어로 정의 생성·수정·설명 | [Ant](https://discourse.mcneel.com/t/ant-ai-a-context-aware-ai-assistant-for-grasshopper/218539) Rhino 블로그 소개(2026-04), food4rhino 약 300 다운로드 | 낮음. 다만 `gy` 참조 복사가 AI에 정의 일부를 넘기는 통로가 될 수 있음 |

GH2 (Rhino 9 beta, [2026-07 공개](https://discourse.mcneel.com/t/rhino-beta-feature-grasshopper-2-beta/221336)): 파라미터마다 데이터 수식자 13개(flatten/graft/sort/clean), 값을 이름으로 내보내는 "Shout"(와이어 없는 전역값), 줌 단계별 정보 표시, 버전 비교. 키보드는 검색 `?` 정도. **GH2에서도 키보드 조작은 비어 있습니다.**

## 3. 불편한 점 순위

등장 빈도 순입니다(조사한 스레드·자료에서 반복해서 나온 정도로 판단).

| 순위 | 불편한 점 | 근거 |
|---|---|---|
| 1 | **스파게티 와이어, 정리가 안 됨** | [Wish: Alternative wire path styles](https://discourse.mcneel.com/t/wish-alternative-wire-path-styles/95163) ("가장 큰 고생은 깔끔한 정의를 만드는 것"), [Is everyone this messy?](https://discourse.mcneel.com/t/is-everyone-this-messy-with-it/177558) (답글 4. 공용 값(공차·각도)이 캔버스를 가로질러 엉킨다, 숨긴 와이어는 동료가 싫어함), [Hall of Shame](https://discourse.mcneel.com/t/whats-your-largest-grasshopper-script-the-hall-of-shame/60594?page=2), [wire bus·색 요청](https://discourse.mcneel.com/t/ideas-for-grasshopper-plugins-components/157996), Telepathy 다운로드 수 |
| 2 | **데이터 트리 짝 맞춤** (graft/flatten/Path Mapper) | "카테고리 문제의 약 2/3가 데이터·리스트 논리" ([Tom_P](https://discourse.mcneel.com/t/if-grasshopper-was-rebuilt-for-2026-what-should-change-or-stay/211626)), "문제의 90%는 데이터 트리 조작 실수" ([bimcorner](https://bimcorner.com/6-rules-how-to-work-with-grasshopper-data-tree/)), [graft/flatten을 언제 쓰나](https://discourse.mcneel.com/t/how-to-decide-whether-to-graft-flatten-and-other-sub-buttons-in-modeling/111363). 에러 없이 조용히 틀림 |
| 3 | **남의(또는 예전 내) 정의를 읽기 어려움, 문서화 부족** | [Struggling with GH Definitions](https://discourse.mcneel.com/t/struggling-with-gh-definitions-design-principles-or-best-practices/202906) (답글 16. "몇 주 지나면 못 읽는다", 주석 습관이 사람마다 다름), "주석은 극히 드물다", 이름 표시 vs 아이콘 [논쟁](https://discourse.mcneel.com/t/why-the-icons-use-in-grasshopper-tutorials-and-not-the-full-names-display-mode/55799/21), [ShapeDiver](https://shapediver.com/blog/3-reasons-why-your-grasshopper-definitions-should-be-clean-documented) |
| 4 | **경고·null의 원인 추적** | 상류 하나가 실패하면 하류가 줄줄이 주황색, null이 경고 없이 퍼짐([Lunchbox CSV](https://discourse.mcneel.com/t/lunchbox-create-csv-component-outputs-null-with-no-error/203176), [Mesh Clash](https://discourse.mcneel.com/t/mesh-clash-does-not-handle-null-values-properly/214316)), [어디서 틀렸는지 모르겠다](https://discourse.mcneel.com/t/cant-figure-out-where-the-definition-is-going-wrong/59470), Traceback 등장. 정의 전체의 에러 목록 기능은 기본에 없음 |
| 5 | **큰 정의에서 길 찾기** (상류·하류, 특정 컴포넌트, 구역) | 기본은 하류만 한 단계씩, 상류 검색 없음([grasshopper3d](https://www.grasshopper3d.com/xn/detail/2985220:Comment:1372685)), 클릭 가능한 목차 요청·Named View 이름 바꾸기/계층 요청([Ideas for plugins](https://discourse.mcneel.com/t/ideas-for-grasshopper-plugins-components/157996)), GHPick |
| 6 | **느린 컴포넌트 찾기** | [How to find bottleneck with profiler](https://discourse.mcneel.com/t/how-to-find-performance-bottleneck-with-profiler-widget/58399) (20개 이상 글, 2페이지). "느린 컴포넌트를 찾느라 오래 화면을 헤맨다". Profiler는 3ms 미만을 숨기고 시간이 오래된 값일 수 있음. Rutten: "GH1은 개발 도구에 신경을 덜 썼다. 더 나은 프로파일러·디버거가 필요" |
| 7 | **키보드로 할 수 없음** | [1번의 요청들](#1-많이-쓰는-기본-기능과-숨은-단축키), Esc로 계산 취소 요청 |
| 8 | **클러스터·재사용 관리** | 중첩 클러스터 인스턴스가 동기화되지 않음, User Object가 갱신되지 않음([User objects do not update](https://discourse.mcneel.com/t/user-objects-do-not-update/214430), [Updating UserObjects](https://discourse.mcneel.com/t/updating-userobjects-how-to-update-other-instances/68301)), "같은 클러스터 인스턴스를 모두 강조해 달라"(Rutten: "흐린 오버레이가 좋겠다"), 프로젝트마다 다시 손봐야 함 |
| 9 | **이름·닉네임 일괄 변경** | [Find & Replace for nick names](https://discourse.mcneel.com/t/wish-find-replace-function-for-component-nick-names/53831) 2018년 등록(RH-43761), **2026-09에 다시 요청**. Telepathy가 큰 정의에서 느림 |
| 10 | **버전 관리·diff** | [Version Control for GH](https://discourse.mcneel.com/t/version-control-for-gh/128645), [changelog 만들기](https://discourse.mcneel.com/t/generating-changelogs-between-saved-versions-is-there-a-way/163969), [두 버전 비교](https://discourse.mcneel.com/t/understanding-old-gh-code-compare-two-versions-of-a-gh-file/220526) (답글 6, 2026-07. ".ghx diff는 되지만 무시할 것이 너무 많다"), 실험 도구 [VVD](https://github.com/oderby/VVD), 상용 GraphHop |
| 11 | 협업 | [branch·collaborate 방법](https://discourse.mcneel.com/t/how-to-manage-share-branch-and-collaborate-on-grasshopper-definitions/122509), 화면 공유로 같이 편집하는 정도가 현실 |

## 4. 불편한 점별 Vimhopper 기능 제안

가치는 **수요 × Vimhopper만 할 수 있는 정도**로 매겼습니다 (상 / 중 / 하). 비용은 대략입니다.

| # | 불편한 점 | 지금 있는 것 | 제안 | 가치 | 비용 |
|---|---|---|---|---|---|
| 1 | 스파게티 와이어 | `==` / `g=` 자동 정렬, Relay 자동 | **커서 흐름 강조**: 커서의 상류·하류 와이어만 진하게, 나머지는 흐리게 (토글). 정렬하지 않은 남의 정의에서도 효과가 큼. 다음으로 `Ctrl+Shift` 드래그에 해당하는 **와이어 옮기기** `mo{n}` → `i{n}` (출력의 와이어 전부를 다른 출력으로) | 상 | 하 |
| 2 | 데이터 트리 짝 맞춤 | `f` `g` `s` `r` 연산자 | [진단 도구](../diagnostics-ideas.md) 3번 `K`(트리 모양 보기)와 1번(짝 맞춤 힌트) + 빠른 수정. 와이어 위 `{3;10}` inlay hint. 경쟁 플러그인이 없음 | **최상** | 중~상 |
| 3 | 읽기 어려움·문서화 | | **범위 연산자로 그룹 만들기** `gG{범위}`(`gGH` 상류를 그룹으로, 이름 입력), 커서 객체의 풀네임·설명 상시 표시(Bifocals 대체), 그룹 사이 이동 `]g` / `[g` (그룹을 목차처럼) | 상 | 하~중 |
| 4 | 경고·null 원인 | | `]e` / `[e` (원인으로만 이동) + 근본 원인 묶기 ([진단 도구](../diagnostics-ideas.md) 2번). null이 처음 생긴 곳으로 점프 `gn` | 상 | 중 |
| 5 | 길 찾기 | hjkl, `gi{n}` / `go{n}`, `vH` / `vL` | **캔버스 검색** `/`(이름·닉네임·종류) + `n` / `N`, **marks** `m{a}` / `'{a}`(Named View보다 가벼운 위치 저장, 파일에 저장), **jumplist** `Ctrl+o` / `Ctrl+i`(`gi1`로 멀리 갔다가 돌아오기). [리더 키·검색](../leader-and-zoom-ideas.md)과 연결 | 상 | 중 |
| 6 | 느린 컴포넌트 | | `]s` / `[s` 느린 순서로 이동, 화면에 상위 N개 시간 표시. Profiler 값이 오래된 경우를 구분 | 중 | 하 |
| 7 | 키보드 | 전부 | `.` 반복([vim에서 더 가져올 것](../vim-ideas.md) 2번), 미리보기 연산자 `gp{범위}`(`Ctrl+Q`), 이름 바꾸기 `cn`. Rutten 스레드의 요청 목록(flatten/graft, 입력 끊기, 자동 연결)은 이미 대부분 있음 → **README에 "공식 단축키로 안 되는 것" 비교표**를 두면 홍보 효과 | 상 | 하 |
| 8 | 클러스터 | | **범위를 클러스터로** `gC{범위}`(`gCH` 상류 원뿔을 클러스터로. 수동으로 가장 귀찮은 작업), 같은 클러스터 인스턴스 사이 이동 `*` / `#`(vim의 같은 단어 찾기) | 중 | 중 |
| 9 | 닉네임 일괄 변경 | | `:s/old/new/` 범위 치환(`:'<,'>s`처럼 비주얼 선택이나 범위에만). 명령줄 모드가 필요하므로 그 첫 사용처 | 중 | 중 |
| 10 | 버전 관리·diff | `gy` 참조(경로:GUID) | 문서를 **diff하기 좋은 텍스트로 내보내기**(객체·와이어·설정만, 위치·썸네일·뷰 제외, 위상 순서로 정렬). 두 파일 비교 결과를 열면 `]c` / `[c`로 바뀐 객체 사이 이동(vim diff 모드) | 중 | 중~상 |
| 11 | 협업 | `gy` | `gy` 참조를 붙여넣으면 그 객체로 점프(`:e 경로:GUID`처럼). 리뷰 코멘트·이슈·AI 프롬프트에 참조를 주고받는 통로 | 하~중 | 하 |

### 추천 순서

1. **커서 흐름 강조**와 **`/` 검색 + jumplist**: 비용이 낮고, 정렬하지 않은 남의 정의(3번·5번 불편)에서도 바로 쓸모가 있어 처음 써 보는 사용자를 붙잡습니다.
2. **`gG{범위}` 그룹, `gC{범위}` 클러스터**: 이미 있는 범위 문법에 연산자만 더하면 됩니다. Pancake·MetaHopper가 메뉴로 하는 일을 키 두세 개로 합니다.
3. **진단 `]e` + `K`**: 2번·4번 불편은 수요가 가장 크고 경쟁이 적습니다(Traceback 정도). [진단 도구](../diagnostics-ideas.md)의 시작 순서를 따릅니다.
4. diff 내보내기, `:s` 치환은 명령줄 모드가 생긴 뒤에.

## 주의할 점

- 포럼 수치는 수집 시점의 답글·조회수이고, 좋아요 수는 확인하지 못했습니다. 순위는 정량 통계가 아니라 반복 등장 정도입니다.
- "와이어를 숨기는" 해법(Telepathy, Shout)은 동료가 디버깅하기 어렵다는 반대가 꾸준합니다. Vimhopper는 숨기기보다 **정렬 + 강조**로 가는 편이 이 의견과 맞습니다.
- GH2(Rhino 9)는 아직 beta이고 키보드 지원이 약합니다. GH1 사용자층은 한동안 유지될 것으로 보이지만, 데이터 수식자처럼 GH2 개념이 바뀌는 부분은 나중에 대응이 필요합니다.
