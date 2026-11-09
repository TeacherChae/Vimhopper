# Vimhopper — 에이전트 작업 규칙

Grasshopper 1 (Rhino 8) 캔버스를 nvim처럼 키보드로 다루는 플러그인. 구조와 빌드는 [docs/development.md](docs/development.md), 키는 [docs/keymap.md](docs/keymap.md), 계획은 [docs/roadmap.md](docs/roadmap.md).

## 역할

| 역할 | 누가 | 하는 일 |
|---|---|---|
| 코디네이터 | 메인 세션 | 로드맵·텀 계획, 작업 나누기, 키 배정, 브랜치 병합, 사용자에게 결정 묻기, 텀 끝 README 갱신 |
| 구현 | `.claude/agents/implementer.md` | 작업 하나를 자기 워크트리·브랜치에서 구현, 테스트, 커밋 |
| 리뷰 | `.claude/agents/reviewer.md` | 병합 전 브랜치 diff 검토 (버그, Grasshopper API, 키 충돌) |
| 사용자 | 사람 | 제품 방향 결정, **Rhino에서 실제 동작 확인** ([검증 목록](docs/verification.md)) |

사용자에게는 취향·제품 방향이나 Rhino에서만 알 수 있는 것만 묻는다. 나머지는 합리적인 기본값으로 정하고 문서에 남긴 뒤 진행한다.

## 흐름

1. 텀(마일스톤) 하나 = 로드맵의 한 단계. 브랜치 `term/{n}-{이름}`을 `main`에서 만든다.
2. 작업마다 `feature/…` 브랜치를 텀 브랜치에서 갈라 워크트리에서 구현한다. 같은 파일을 크게 고치는 작업은 동시에 돌리지 않는다.
3. 리뷰 후 코디네이터가 텀 브랜치에 `--no-ff`로 병합한다. 충돌은 코디네이터가 푼다.
4. 텀이 끝나면 README·로드맵·검증 목록을 갱신하고 `main`에 병합한다.

## 코드 규칙

- 로직은 가능한 한 `Vimhopper.Core`(Grasshopper 비의존)에 두고 xUnit 테스트를 쓴다. Engine은 Rhino 없이 테스트할 수 없다.
- 끝내기 전에 항상: `dotnet build Vimhopper.sln` (경고 0) 과 `dotnet test`.
- Engine에서 문서를 바꾸는 동작은 Grasshopper undo에 기록해서 `u` 한 번에 되돌아오게 한다 (`doc.UndoUtil.RecordEvent`).
- 캔버스·문서에 붙인 이벤트는 `VimController.Dispose`와 문서 변경 시 반드시 해제한다 (핫 리로드).
- 새 키는 `KeyParser`에 넣고 파서 테스트를 쓴다. 키 배정은 코디네이터가 정한 것을 따르고, 없으면 [키맵](docs/keymap.md)과 겹치지 않게 고른 뒤 보고한다.
- 코드 주석·문서는 한국어, 기존 문서 문체 (짧게, 표, 군더더기 없이).
- 커밋 메시지는 영어 명령형 제목 + 무엇을 왜 했는지 본문. 끝에 `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- 기능을 바꾸면 같은 커밋에서 `docs/keymap.md`를 고치고, Rhino에서 확인할 항목을 `docs/verification.md`에 더한다.
