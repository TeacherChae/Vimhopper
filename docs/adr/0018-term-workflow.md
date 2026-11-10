# 0018. 텀 단위 개발, 병렬 구현 + 리뷰, Rhino 검증 전에 main 병합

- 상태: 채택 (2026-11-09)
- 관련: [CLAUDE.md](../../CLAUDE.md), [로드맵](../roadmap.md), [검증 목록](../verification.md), `.claude/agents/implementer.md`, `.claude/agents/reviewer.md`

## 맥락

사용자가 기능을 하나씩 기획해 전달하는 방식은 느렸습니다. 사용자는 제품 방향과 Rhino 확인만 맡고, 기획·구현·리뷰는 에이전트가 진행하기로 했습니다.

## 결정

- 로드맵을 텀(마일스톤)으로 나눕니다: 1텀 문법(끝) → 2텀 찾기와 점프 → 3텀 읽기와 진단 → 4텀 명령줄과 설정 (사용자가 순서 결정).
- 역할: 코디네이터(계획, 키 배정, 병합, 사용자 질문), 구현(작업 하나를 자기 워크트리·`feature/` 브랜치에서), 리뷰(병합 전 diff), 사용자(방향, Rhino 확인).
- 브랜치: `main` → `term/{n}-{이름}` → `feature/…`. 리뷰 후 텀 브랜치에 `--no-ff`. 같은 파일을 크게 고치는 작업은 동시에 돌리지 않습니다 (`VimController.cs`, `KeyParser.cs`, `KeyParserHints.cs`, `keymap.md`는 거의 모든 작업이 건드리므로 충돌은 코디네이터가 풉니다).
- 텀 끝: 통합 리뷰 → README·로드맵·검증 목록 갱신 → `main` 병합 → `origin` 푸시. **Rhino 검증을 기다리지 않습니다** (사용자 결정). 나중에 나온 문제는 `fix/` 작업.
- 사용자에게는 취향·제품 방향이나 Rhino에서만 알 수 있는 것만 묻습니다. 나머지는 기본값으로 정하고 ADR·문서에 남깁니다.
- 작업마다 완료 조건: `dotnet build Vimhopper.sln` 경고 0, `dotnet test` 통과, 키맵·검증 목록 갱신.

## 결과

- 실행 도구(Claude Code 서브에이전트, herdr 등)가 바뀌어도 이 흐름과 CLAUDE.md는 그대로 씁니다. 에이전트 역할 설명은 `.claude/agents/`에 있습니다.
- `main`에 Rhino에서 확인 안 된 코드가 들어갑니다. [검증 목록](../verification.md)이 사용자가 확인할 항목의 유일한 출처입니다.
