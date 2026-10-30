# 자동 정렬 개선 조사

> 진행 중인 조사 기록입니다. 아직 코드는 바꾸지 않았습니다. 지금 규칙은 [자동 정렬](formatting.md)에 있고, 이 문서는 그 규칙을 어떻게 바꿀지에 대한 근거입니다. 다음 작업은 맨 아래 [다음 단계](#다음-단계)부터 이어가면 됩니다.

## 요약

실제 정의 파일 하나를 사용자가 손으로 정리한 배치와 지금 `==` 결과를 비교하고, 회로도·데이터 흐름 도구의 배치 방법을 조사한 뒤 ELK로 실험했습니다. 결론은 세 가지입니다.

1. **열 나누기를 Network Simplex로** 바꿉니다 (와이어 길이의 합 최소화). 입력값만 받는 컴포넌트가 왼쪽 끝으로 가는 문제가 예외 규칙 없이 풀립니다.
2. **결과 확인용 패널은 흐름에서 빼고** 정렬이 끝난 뒤 덩어리 위쪽 한 줄에 놓습니다.
3. **세로 위치를 와이어 가중치 최적화로** 정합니다. "줄기"(끝에서 맨 위 입력을 거슬러 오른 경로)에 큰 가중치를 주면 줄기가 한 줄로 펴지고 곁가지는 저절로 아래로 갑니다.

사용자가 확정한 것:

- Relay는 지금처럼 **모두 풀고 다시 계산**합니다 (손으로 넣은 Relay도 유지하지 않음).
- 제안 1(결과 패널 빼기), 2(입력값만 받는 가지 당기기), 4(간격)는 승인. 2는 실험 결과 Network Simplex 열 나누기로 대체할 수 있습니다.
- 3(줄기)은 판정 방법을 묻고 실험으로 확인한 상태이며, 구현 방식은 아직 정하지 않았습니다.

## 표본

사용자의 정의 파일 `panel_dividing.ghx`(저장소 밖, 사용자 로컬)에서 같은 부분을 세 가지 배치로 비교했습니다. 객체 26개, 와이어 30개 (Relay 1개를 풀어서 직결한 뒤 기준).

```
Crv ─────────────┐
장축 판재 너비 ───┤
판재 최소·최대 너비 → Dom ─┤
                 판재 탐색(Python) ─ width → w/2 → Neg → -w/2..w/2 → Rectangle → Orient → Extrude → Cap
                   │  │                 ↑'2'(30×20 패널)          ↑슬라이더          ↑     ↑
                   │  └ count → Divide ┐                                         │     Unit Z ← 슬라이더
                   └ C ──────→ Shatter ┴→ Eval(← 슬라이더) → HFrame ─────────────┘(Orient.B)
결과 확인용 패널 4개: ← Python.gap, ← w/2, ← -w/2..w/2, ← Rectangle
```

세 배치의 좌표와 그래프(크기, 그립 위치, 와이어)는 `tools/elk-experiment/`에 들어 있습니다 ([재현](#재현)). 원본 파일에서 연결이 없는 69×20 패널(`c81c5a67`)은 사용자 요청으로 뺐습니다.

주의: `.ghx` 파일에 저장된 "손으로 정리" 복사본의 좌표는 사용자가 `gy`로 보내준 최종 좌표와 다릅니다 (저장 후 더 다듬음). 실험은 `gy` 좌표를 씁니다.

## 손으로 정리한 배치의 규칙

### 지금 규칙과 같은 것

| 관찰 | 예 |
|---|---|
| 열은 오른쪽 끝 기준 정렬 (출력 그립이 한 줄) | Dom·Crv의 오른쪽 끝 674, Shatter·Neg·슬라이더·패널 약 1362 |
| 각 컴포넌트는 연결된 맨 위 입력의 와이어가 수평 | w/2 → Neg → Domain.A, Rectangle.X (맨 위 Plane은 비어 있음), Orient.G, Extrude.B, Cap |
| 입력값은 쓰이는 곳 바로 앞 열, 받는 그립과 같은 높이 | 판재 최소·최대 너비가 Dom의 A·B 그립에 맞음 (그립 간격 20 = 슬라이더 높이라 **둘이 딱 붙음**), Crv → Python.C, 30×20 패널 → w/2.B |
| 자리가 겹치면 아래로 밀림 | Eval.L 슬라이더는 그립 높이에 Shatter가 있어 그 아래로 |

### 지금 규칙과 다른 것

1. **결과 확인용 패널(입력만 있고 출력은 아무 데도 안 가는 Panel)을 흐름에서 빼서 맨 위 한 줄에 모음.** 4개 모두 위쪽 끝이 같은 높이(y=2311)이고, 가로로는 대체로 출처의 다음 열에 놓임.
2. **입력값만 받는 컴포넌트도 쓰이는 곳 바로 앞으로.** `슬라이더 → Unit Z → Extrude.D`에서 Unit Z가 Extrude 바로 앞 열(Orient 바로 아래)에 있음.
3. **줄기는 한 줄, 곁가지는 그 아래.** 줄기(Crv → Python → w/2 → Neg → Domain → Rectangle → Orient → Extrude → Cap)가 y≈2520~2560 한 줄이고, 곁가지(Python.C → Divide·Shatter → Eval → HFrame)는 그 아래 줄. 합류하는 와이어(HFrame → Orient.B)만 기울어짐.
4. **열 간격 약 55px.** 사용자 환경은 모든 컴포넌트를 그룹(여백 10px)으로 감싸므로, 지금 간격 40px이면 그룹 외곽선 사이가 20px뿐.

## 지금 `==` 결과의 문제

| | 지금 `==` | 손으로 정리 |
|---|---|---|
| 흐름 높이 | 784 | 302 |
| 줄기 | w/2 1318 → Neg 1492 → Domain 이후 1744로 계단처럼 내려감 | 한 줄 |
| 곁가지 | 줄기보다 위 | 줄기 아래 |
| Unit Z → Extrude | 1267px (1열에 놓여 화면을 가로지름) | 39px |
| 새 Relay | 3개 | - |

원인:

- **결과 패널이 줄기 자리를 차지함 (가장 큰 원인).** Neg는 맨 위 입력(w/2.R, 약 1340) 높이에 와야 하는데, 같은 열의 그 자리를 Eval.L 슬라이더와 w/2 결과 패널(1376~1476)이 먼저 차지해 1492로 밀림. 뒤따르는 객체는 맨 위 입력을 수평으로 맞추므로 밀린 높이를 따라가고, 또 밀려 1744까지 내려감. 3열은 160px 결과 패널이 열 너비를 정해 w/2·Relay는 왼쪽, Divide는 오른쪽에 붙어 들쭉날쭉해짐.
- **곁가지가 위로 감.** Python 출력 순서가 out, C, width, count, gap이라 곁가지(C)가 줄기(width)보다 위 그립에서 출발하고, "맨 위 입력 수평" 규칙이 그 높이를 따라감. 같은 열에서 겹치면 "위쪽이 자리를 가짐"이라 줄기가 밀림.
- **Unit Z가 왼쪽 끝으로 감.** 컴포넌트는 소스로부터의 최장 거리로 열을 정하고, 입력값만 쓰이는 곳 앞으로 당기기 때문.
- 새 Relay 3개에도 그룹이 씌워져 있었음. Vimhopper가 아니라 사용자 환경의 자동 그룹 플러그인 동작으로 보임.

## 줄기 판정

**끝 객체(출력이 아무 데도 안 가는 객체, 결과 패널 제외)에서 시작해 연결된 맨 위 입력의 출처로 거슬러 오른 경로 중 가장 긴 것.**

- 표본에서 손으로 정리한 배치의 줄기와 정확히 같은 경로가 나옴 (실험 스크립트가 자동 계산).
- 지금 규칙의 "맨 위 입력 수평"과 같은 기준이라 일관됨. Grasshopper 컴포넌트는 대개 주 형상을 첫 입력에 둠 (Extrude.B, Orient.G, Divide.C).
- 결정적이고 배치와 무관하므로 멱등성을 해치지 않음.

| 애매한 경우 | 처리 방안 (미확정) |
|---|---|
| 끝 객체가 여럿 | 가장 긴 경로가 줄기, 나머지는 보조 줄기로 차례로 아래에. 길이가 같으면 지금 화면에서 위에 있는 쪽 먼저 |
| 곁가지 안의 순서 | 곁가지에도 같은 규칙을 재귀 적용 |
| 입력 순서에 의미가 없는 컴포넌트 (Merge, Entwine, 임의 입력의 Python) | 맨 위 입력이 주 흐름이라는 보장이 없음. 이 방법의 한계 |

## 다른 분야 조사

데이터 흐름·회로도 도구는 같은 문제(입출력 단자 순서가 고정된 방향 그래프의 계층 배치)를 다룹니다.

| 도구 | 참고할 점 |
|---|---|
| [ELK Layered](https://eclipse.dev/elk/reference/algorithms/org-eclipse-elk-layered.html) (Eclipse Layout Kernel, Kiel 대학 KIELER 연구) | 우리와 같은 Sugiyama 5단계. 포트 위치 고정(`portConstraints: FIXED_POS`), 주석 상자, 와이어별 곧음 우선순위. [단계별 개요](https://eclipse.dev/elk/blog/posts/2025/25-08-21-layered.html), [논문](https://arxiv.org/pdf/2311.00533) |
| [netlistsvg](https://github.com/shuckc/netlistsvg) | 디지털 회로도(Yosys 넷리스트)를 ELK로 배치. 회로도에서도 ELK가 쓰인다는 사례 |
| Graphviz dot | 열 나누기와 세로 위치를 모두 Network Simplex(가중치 × 길이 합 최소화)로 풂. [`group`](https://www.graphviz.org/docs/attrs/group/) 속성: 같은 그룹 양 끝의 와이어는 가중치 100배 + 교차 벌점으로 곧게 ([설명](https://forum.graphviz.org/t/a-question-about-the-group-attribute-what-does-straight-mean/548)), [`weight`](https://graphviz.org/docs/attrs/weight/) |
| [Blueprint Assist](https://blueprintassist.github.io/miscellaneous/settings) (Unreal) | 실행 흐름(exec 와이어)이 명시적 줄기이고 파라미터 노드를 주변에 붙임 (`ParameterStyle`). 경유점(knot) 자동 생성·병합. 주석 상자 여백을 정렬에 반영하라는 설정 (`CommentNodePadding`) |
| [LabVIEW Clean Up Diagram](https://www.ni.com/docs/en-AS/bundle/labview/page/automatically-cleaning-up-the-block-diagram.html) | 입력 컨트롤은 왼쪽 끝, 표시기(결과 확인용)는 오른쪽 끝으로 보내는 옵션 |
| [Simulink arrangeSystem](https://www.mathworks.com/help/simulink/slref/simulink.blockdiagram.arrangesystem.html) | 신호선을 곧게 펴는 것이 목표. 알고리즘은 비공개 |
| [AutoGraph](https://www.food4rhino.com/en/app/autograph) (Grasshopper) | 파라미터 기반 자동 배치. Rhino 6까지만 확인됨 |

우리 문제와의 대응:

- **결과 패널 ↔ ELK 주석 상자** ([`commentBox`](https://eclipse.dev/elk/reference/options/org-eclipse-elk-commentBox.html)): 열 배치에서 빠지고 연결된 노드 근처에 라벨처럼 놓임. 주석이 어느 노드에 속하는지 추론하는 연구도 있음 ([On Comments in Visual Languages](https://macau.uni-kiel.de/receive/macau_mods_00002008?lang=en), [VL/HCC 2014](https://rtsys.informatik.uni-kiel.de/~biblio/downloads/papers/vlhcc14.pdf)).
- **Unit Z 문제 ↔ 열 나누기 전략**: 지금 방식은 ELK의 `LONGEST_PATH_SOURCE` + 입력값 예외. ELK·Graphviz 기본값 `NETWORK_SIMPLEX`는 와이어 길이 합을 최소화해 작은 가지를 쓰이는 곳 앞으로 보냄. 더 가벼운 대안은 최장 거리 결과를 후처리하는 [노드 승급(Nikolov–Tarassov)](https://pure.ul.ie/en/publications/graph-layering-by-promotion-of-nodes/).
- **줄기 ↔ 와이어 가중치**: ELK [`priority.straightness`](https://eclipse.dev/elk/reference/options/org-eclipse-elk-layered-priority-straightness.html)(세로 배치 단계에서 평가), Graphviz `group`. 세로 배치 방법은 Brandes–Köpf(ELK 기본), Linear Segments(Sander), Network Simplex(Gansner 외).
- **간격과 그룹 ↔ 주석 상자 여백** (Blueprint Assist `CommentNodePadding`).
- 이미 같은 방향인 것: Relay 자동 배치 ↔ Blueprint Assist 경유점, 그립 순서 고정 ↔ ELK 포트 고정.
- 데이터 흐름 레이아웃의 원류 연구: [Spönemann, On the Automatic Layout of Data Flow Diagrams](https://rtsys.informatik.uni-kiel.de/~biblio/downloads/theses/msp-dt.pdf), [KIELER와 Ptolemy II](https://macau.uni-kiel.de/receive/macau_mods_00001846?lang=en).

확인하지 못한 것: Graphviz 원 논문(Gansner 외 1993 "A Technique for Drawing Directed Graphs")의 세로 위치 가중치가 실제 노드끼리 1, 한쪽 가상 노드 2, 양쪽 가상 노드 8이라는 기억은 이번 검색으로 확인하지 못했습니다. 구현 전에 원문을 확인해야 합니다.

## ELK 실험

### 방법

- ghx에서 객체 크기와 그립 위치(각 파라미터 영역의 세로 가운데)를 읽어 ELK 그래프로 만듦. 포트는 위치 고정(`FIXED_POS`), 입력은 왼쪽, 출력은 오른쪽.
- Relay는 풀어서 출처와 받는 곳을 직결.
- 공통 옵션: `layered`, 방향 오른쪽, 열 간격 40(지금과 같게), 열 안 간격 20.
- 결과 패널 판정: 입력이 연결되어 있고 출력이 아무 데도 안 가는 Panel (표본에서 4개).
- 가중치를 줄 때 `priority.straightness`: 줄기 100, 나머지 1. "단계별"은 줄기 100 > 입력값에서 나가는 와이어 10 > 맨 위 입력 5 > 나머지 1.

### 지표

- **줄기 수평**: 줄기 와이어 8개 중 양 끝 그립 높이 차가 1.5px 미만인 것
- **수평 와이어**: 전체 30개 중 같은 기준
- **흐름 높이**: 결과 패널을 뺀 객체들의 세로 범위
- **Unit Z → Extrude**: 두 객체 사이 가로 거리

### 결과

| 배치 | 줄기 수평 | 수평 와이어 | 흐름 높이 | Unit Z → Extrude |
|---|---|---|---|---|
| 손으로 정리 | 8/8 | 15/30 | 302 | 39 |
| 지금 `==` (Relay 3개는 빼고 직결로 계산) | 5/8 | 10/30 | 784 | 1267 |
| ELK, 최장 거리 열 나누기 (지금 방식에 가까움) | 1/8 | 9/30 | 547 | 1162 |
| ELK 기본 (Network Simplex 열 나누기 + Brandes–Köpf 세로) | 1/8 | 9/30 | 361 | 88 |
| + 결과 패널을 주석 상자로 | 1/8 | 9/30 | 370 | 88 |
| + 줄기 가중치, Brandes–Köpf 세로 | 3/8 | 7/30 | 391 | 88 |
| + 줄기 가중치, Linear Segments 세로 | 5/8 | 8/30 | 416 | 88 |
| **+ 줄기 가중치, Network Simplex 세로** | **8/8** | 10/30 | 382 | 88 |
| + 단계별 가중치, Network Simplex 세로 | 8/8 | 11/30 | 382 | 88 |
| 위와 같음 + 열 간격 55 | 8/8 | 11/30 | 382 | 103 |

### 해석

1. **Network Simplex 열 나누기만으로 Unit Z 문제가 풀림** (1162 → 88). 흐름 높이도 547 → 361. 제안 2(입력값만 받는 가지 당기기)를 예외 규칙으로 만들 필요가 없음.
2. **줄기를 펴는 것은 세로 배치 방법에 달림.** 같은 가중치로 Brandes–Köpf 3/8, Linear Segments 5/8, Network Simplex 8/8. Network Simplex 결과는 곁가지(Divide, Shatter, Eval, HFrame)도 손으로 정리한 배치처럼 줄기 아래로 감. "줄기 먼저, 곁가지 아래"를 순서로 강제하지 않아도 가중치로 나옴.
3. **결과 패널은 흐름에서 빼는 것이 맞지만 놓는 위치는 우리 규칙이 필요.** ELK 주석 상자는 연결된 객체 바로 위에 패널마다 다른 높이로 놓음. "다음 열, 맨 위 한 줄"은 후처리로 직접 놓아야 함.

### 아직 손으로 정리한 배치와 다른 것

- **입력값끼리 붙지 못함.** 판재 최소·최대 너비(높이 20)가 Dom 그립(간격 20)에 둘 다 맞으려면 간격 0이어야 하는데, ELK는 열 안 최소 간격 20을 지켜 하나만 맞춤. 손으로 정리한 배치의 수평 와이어(15)가 ELK(11)보다 많은 주된 이유. **입력값끼리는 간격 0 허용**이 필요.
- **w/2.B로 가는 30×20 패널이 Python과 같은 열에 들어가 맨 위로 밀림.** 손으로 정리한 배치에서는 Python과 w/2 사이에 별도 열이 있으나, 그 열은 원래 Relay가 있던 자리. Relay를 푼 상태에서 어떻게 할지 미정.
- 열 간격 55는 구조를 바꾸지 않고 폭만 늘림.

## 다음 단계

구현 순서 제안 (사용자와 아직 확정하지 않음). ELK를 플러그인에 직접 넣지 않고(Java/JS라 Rhino 플러그인에는 무거움) 개념만 C#으로 옮깁니다.

1. ~~**결과 패널 빼기**~~ → **완료** ([규칙 6번](formatting.md#결과-패널))
   - `LayoutNode.IsResult` 플래그, 판정은 `Formatter.IsResult`(Panel, 받는 와이어 있음, 보내는 와이어 없음).
   - 출처의 다음 열, 덩어리 위쪽 한 줄. 같은 자리를 원하는 패널이 둘 이상이면 오른쪽으로 나란히 (출처 그립이 위인 것부터).
   - 표본 결과 ([진행 기록](#단계별-진행-기록)): 흐름 높이 784 → 424, 줄기 수평 5/8 → 7/8.
2. ~~**열 나누기를 Network Simplex로**~~ → **완료** (`src/Vimhopper.Core/NetworkSimplex.cs`, [규칙 1번](formatting.md#규칙))
   - 가중치는 모두 1. "입력값은 쓰이는 곳 바로 앞" 규칙이 예외 없이 포함됨을 테스트로 확인.
   - 작은 무작위 그래프 40개에서 모든 층 배정을 따져 본 최솟값과 같음을 확인 (`NetworkSimplexTests`). 객체 1000개·와이어 3000개에 약 0.3초.
   - `CanvasLayout.Layers`(hjkl의 층 계산)도 같은 코드를 쓰므로 `j`/`k`가 같은 열로 보는 객체가 정렬 결과와 함께 바뀜.
   - 대가: 무작위 그래프에서 와이어가 양 끝 열의 이웃을 가로지르는 비율 4.2% → 6.3% ([배경](formatting.md#층-나누는-규칙)).
   - 표본 결과: 줄기 수평 8/8, 흐름 높이 278(손으로 정리 302보다 낮음), Unit Z → Extrude 40px, 새 Relay 2개.
3. **세로 위치를 가중치 최적화로** (`PlaceRows`, `AlignLayer`; 지금은 맨 위 입력 수평 + 겹치면 위쪽 우선)
   - 줄기 판정 추가, 가중치: 줄기 > 입력값 > 맨 위 입력 > 나머지.
   - Network Simplex 세로 배치(Gansner 외)를 구현하거나, 지금 방식을 유지하되 겹칠 때 우선순위를 가중치 순으로 바꾸는 가벼운 방안부터 시험.
   - 입력값끼리는 간격 0 허용.
4. **간격**: `LayoutOptions.ColumnGap`(지금 40)을 약 55로, 또는 그룹 외곽선 기준으로 계산.
5. 각 단계마다 `tools/elk-experiment`와 같은 지표로 손으로 정리한 배치와 비교. Core의 무작위 그래프 테스트(멱등성, Relay 보장)는 그대로 통과해야 함.

열린 질문:

- Relay를 푼 뒤 입력값에 별도 열을 줄지 (30×20 패널 문제)
- 결과 패널이 같은 열에 여럿일 때, 덩어리가 여럿일 때(`g=`) 위쪽 줄을 덩어리마다 둘지
- 끝 객체가 여러 개인 정의에서 보조 줄기 순서
- 다른 표본에서도 같은 경향인지. 지금은 표본이 하나뿐.

## 단계별 진행 기록

`tools/layout-bench`로 지금 작업 트리의 `CanvasLayout`을 표본에 돌린 결과입니다 ([재현](#재현)). 결과 패널을 흐름에 넣고 돌리면(`--no-results`) Rhino에서 잰 "지금 `==`"과 지표가 정확히 같아서, Rhino 없이 비교할 수 있음을 확인했습니다.

| 단계 | 수평 와이어 | 줄기 수평 | 흐름 높이 | Unit Z → Extrude | 새 Relay |
|---|---|---|---|---|---|
| 손으로 정리 | 15/30 | 8/8 | 302 | 39 | - |
| 시작 (`--no-results`) | 10/30 | 5/8 | 784 | 1267 | 3 |
| 1. 결과 패널 빼기 | 9/30 | 7/8 | 424 | 984 | 5 |
| 2. 열 나누기 Network Simplex | 10/30 | 8/8 | 278 | 40 | 2 |

1단계 뒤 남은 것: 줄기가 `-w/2..w/2 → Rectangle`에서 한 번 내려가고(줄기 높이 편차 267), Unit Z는 여전히 왼쪽 끝 열입니다 (2단계 대상). 새 Relay가 3개에서 5개로 늘었는데, 흐름이 납작해지면서 긴 와이어가 객체 위를 지나는 곳이 생긴 것으로 보입니다.

2단계 뒤: 줄기가 한 줄로 펴지고(줄기 높이 편차 63, 손으로 정리와 같음) 곁가지가 줄기 아래로 갔습니다. 3단계(세로 위치 가중치)에서 기대했던 효과가 열 나누기만으로 나왔습니다. 남은 차이는 수평 와이어 수(10 vs 15)로, 주된 원인은 [입력값끼리 붙지 못함](#아직-손으로-정리한-배치와-다른-것)입니다.

### 교차 증가 원인 (2단계)

2단계 뒤 무작위 그래프 30개(객체 900개)에서 와이어가 양 끝 열의 이웃을 가로지르는 곳이 51 → 74로 늘었습니다 (와이어 1226 → 1169개 중 4.2% → 6.3%).

| | 최장 거리 | Network Simplex |
|---|---|---|
| 교차 | 51 | 74 |
| 출발 열의 이웃 | 8 | 20 |
| 도착 열의 이웃 | 43 | 54 |
| 이웃한 열 사이 와이어에서 | 51 | 74 (모두) |
| 새 Relay | 210 | 153 |

- **늘어난 23개 중 19개가 seed 3개(4, 10, 13)에 몰림.** 이 seed들은 정렬 높이도 크게 늘었습니다 (seed 13: 1355 → 2318, seed 10: 2125 → 4791). 나머지 27개 seed는 합쳐서 +4.
- **seed 13 상세**: 2열에 위쪽 덩어리(y 30~190)와 아래쪽 덩어리(y 1580~) 사이 1400px 빈 띠가 생기고, 위에서 아래 덩어리로 가는 와이어가 거의 수직으로 내려가며 도착 열의 이웃을 가로지름.
- **빈 띠의 원인: 세로 위치 맞추기(`PlaceRows`)의 가라앉음.** 입력이 없는 객체는 자식에, 자식은 맨 위 입력의 출처에 맞추므로 `슬라이더 → 그것을 맨 위 입력으로 받는 컴포넌트` 같은 쌍은 서로만 기준으로 삼아 어디에도 고정되지 않습니다. 같은 열에서 겹치면 아래로만 밀리고(`Math.Max(Target, floor)`) 위로 돌아오는 힘이 없어서, 반복할 때마다 묶음째 내려갑니다. 원래 있던 약점이고, Network Simplex가 이런 묶음(입력값에서 시작하는 작은 가지)을 쓰이는 곳 옆, 즉 흐름 한가운데 열로 보내면서 더 자주 부딪히게 됐습니다.
- **시도했다가 되돌린 것**: 기준을 따라가면 자기 자신으로 돌아오는 고리마다 입력 없는 정점 하나를 열 안에서 가능한 한 위에 놓기. 무작위 그래프의 높이는 크게 줄었지만(seed 13: 2318 → 912) 교차는 79로 오히려 늘었고(촘촘해진 만큼 이웃이 가까워짐), 표본에서는 슬라이더가 받는 그립 높이를 떠나 흐름 높이 278 → 692, 줄기 수평 8/8 → 7/8로 나빠졌습니다.
- **결론**: 남은 교차는 이미 알려진 한계(열 안에는 Relay를 놓을 수 없음)와 가라앉음이 겹친 것입니다. 가라앉음은 겹칠 때 아래로만 미는 탐욕적 방식 자체의 문제라, 3단계(세로 위치를 전체 최적화로)에서 함께 풀립니다. 그때까지 테스트 상한은 7%.

## 재현

```powershell
dotnet run --project tools/layout-bench -c Release   # 작업 트리의 정렬 결과 → tools/elk-experiment/vimhopper.json
cd tools/elk-experiment
npm install           # elkjs 0.9.3
node run.mjs          # 지표 표 출력 + compare.html (같은 축척 비교 그림). vimhopper.json이 있으면 "Vimhopper 작업 트리" 줄이 추가됨
node run.mjs 0 9      # 0번(손으로 정리), 9번 배치를 열 순서로 출력
```

- `graph.json`: 원래 배치의 그래프 (Relay 풀린 상태, 객체 크기·그립 위치·와이어). `extract.py`로 만들었습니다.
  `python -I extract.py <파일.ghx> graph.json <InstanceGuid ...>`
- `run.mjs`: 손으로 정리한 배치와 지금 `==` 결과의 좌표가 들어 있음 (`gy`로 받은 값). 새 표본을 추가하려면 같은 방식으로 그래프를 뽑고 좌표를 넣으면 됩니다.
- `compare.html`: 줄기는 주황, 수평 와이어는 초록, 입력값은 파랑, 결과 패널은 노랑.
