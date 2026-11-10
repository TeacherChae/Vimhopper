# Vimhopper — 에이전트 작업 규칙

Grasshopper 1 (Rhino 8) 캔버스를 nvim처럼 키보드로 다루는 플러그인. 구조와 빌드는 [docs/development.md](docs/development.md), 키는 [docs/keymap.md](docs/keymap.md), 계획은 [docs/roadmap.md](docs/roadmap.md), 이미 정한 것과 이유는 [docs/adr/](docs/adr/README.md). 작업 전에 ADR 목록을 훑고, ADR을 뒤집는 변경은 사용자에게 먼저 묻는다.

## 역할

| 역할 | 누가 | 하는 일 |
|---|---|---|
| 코디네이터 | 메인 세션 | 로드맵·텀 계획, 작업 나누기, 키 배정, 브랜치 병합, 사용자에게 결정 묻기, 텀 끝 README 갱신 |
| 구현 | `.claude/agents/implementer.md`를 따르는 에이전트 | 작업 하나를 자기 워크트리·브랜치에서 구현, 테스트, 커밋 |
| 리뷰 | `.claude/agents/reviewer.md`를 따르는 에이전트 | 병합 전 브랜치 diff 검토 (버그, Grasshopper API, 키 충돌) |
| 사용자 | 사람 | 제품 방향 결정, **Rhino에서 실제 동작 확인** ([검증 목록](docs/verification.md)) |

사용자에게는 취향·제품 방향이나 Rhino에서만 알 수 있는 것만 묻는다. 나머지는 합리적인 기본값으로 정하고 문서에 남긴 뒤 진행한다.

## 흐름

1. 텀(마일스톤) 하나 = 로드맵의 한 단계. 브랜치 `term/{n}-{이름}`을 `main`에서 만든다.
2. 작업마다 `feature/…` 브랜치를 텀 브랜치에서 갈라 워크트리에서 구현한다. 같은 파일을 크게 고치는 작업은 동시에 돌리지 않는다.
3. 리뷰 후 코디네이터가 텀 브랜치에 `--no-ff`로 병합한다. 충돌은 코디네이터가 푼다.
4. 기능별 리뷰와 별도로, 텀 브랜치 전체(`main...term/{n}`)를 한 번 더 리뷰한다 (기능끼리 키를 가로채는지, 병합 실수).
5. 텀이 끝나면 README·로드맵·검증 목록을 갱신하고 `main`에 병합한 뒤 `origin`에 푸시한다 (Rhino 검증을 기다리지 않음. 나중에 나온 문제는 `fix/` 작업으로).

## herdr로 실행하기

코디네이터가 herdr 창 안에 있으면(`HERDR_ENV=1`) 구현·리뷰 에이전트를 herdr로 띄운다. 아니면 Claude Code 서브에이전트로.

| 단계 | 명령 |
|---|---|
| 작업마다 워크스페이스 | `herdr worktree create --branch feature/x --base term/{n}-… --label x --no-focus` → JSON에서 root pane ID |
| 구현 에이전트 | `herdr agent start impl-x --kind claude --pane <id> -- --permission-mode auto`, 프롬프트에 implementer.md를 따르라고 |
| 리뷰 에이전트 | 같은 워크스페이스에서 `pane split --direction right --no-focus` → `agent start review-x --kind claude …` |
| 지시·대기 | `agent prompt <이름> "…"` (병렬이면 `--wait` 없이) → `agent wait <이름>` → `agent read <이름> --source recent-unwrapped` |
| 정리 | 병합 뒤 `herdr worktree remove --workspace <id>` (내가 만든 것만) |

- `blocked`이면 화면을 읽고 사용자에게 묻는다. 승인 창에 임의로 답하지 않는다.
- 리뷰 지적은 같은 구현 에이전트에게 보내 고치게 한다.
- 병렬 브랜치를 합칠 때 `KeyParser`의 `switch`에서 `return;`이 빠져 case가 이어지는 충돌이 자주 난다 (2·3텀). 병합 뒤 빌드로 확인한다.

## 코드 규칙

- 로직은 가능한 한 `Vimhopper.Core`(Grasshopper 비의존)에 두고 xUnit 테스트를 쓴다. Engine은 Rhino 없이 테스트할 수 없다.
- 끝내기 전에 항상: `dotnet build Vimhopper.sln` (경고 0) 과 `dotnet test`.
- Engine에서 문서를 바꾸는 동작은 Grasshopper undo에 기록해서 `u` 한 번에 되돌아오게 한다 (`doc.UndoUtil.RecordEvent`).
- 캔버스·문서에 붙인 이벤트는 `VimController.Dispose`와 문서 변경 시 반드시 해제한다 (핫 리로드).
- 새 키는 `KeyParser`에 넣고 파서 테스트를 쓴다. 키 배정은 코디네이터가 정한 것을 따르고, 없으면 [키맵](docs/keymap.md)과 겹치지 않게 고른 뒤 보고한다.
- 코드 주석·문서는 한국어, 기존 문서 문체 (짧게, 표, 군더더기 없이).
- 문서에 `\v` `\1` 같은 백슬래시 글을 셸·Python으로 쓰면 제어 문자로 바뀌기 쉽다 (4텀에 세 번). 커밋 전에 바뀐 `.md`에 제어 문자가 없는지 확인한다.
- 커밋 메시지는 영어 명령형 제목 + 무엇을 왜 했는지 본문. 끝에 `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- 기능을 바꾸면 같은 커밋에서 `docs/keymap.md`를 고치고, Rhino에서 확인할 항목을 `docs/verification.md`에 더한다.
- 키 배정 규칙, 동작 기준처럼 나중에 다시 논의될 만한 결정은 `docs/adr/`에 다음 번호로 남긴다 ([템플릿](docs/adr/template.md)).
