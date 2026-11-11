# 핸드오프 노트

다음 세션(또는 다음 자동 개선 사이클)이 이어서 작업하기 위한 현재 상태 요약.
사이클별 상세 기록은 `docs/auto-log.md`, 보류 항목 전체는 `docs/backlog.md`,
자동 루프 규칙은 `.claude/loop.md` 참고.

---

## 현재 상태 (2026-11-12 기준)

- **브랜치**: `main`, working tree 깨끗함.
- **origin과의 차이**: 로컬 `main`이 origin보다 **11커밋 앞섬**, push 안 함(사용자가 아직 요청 안 함).
- **에이전트 팀 셋업**: `.claude/settings.json`에 `CLAUDE_CODE_EXPERIMENTAL_AGENT_TEAMS=1` 활성화됨(커밋 `87798c7`).
  `developer` 에이전트가 신설돼 planner → developer → (test-writer, code-reviewer) 흐름을
  team-lead 중계 없이 직접 SendMessage로 처리한다. `.claude/agents/{planner,developer,test-writer,code-reviewer}.md` 참고.

## 이번 사이클(2회차) 결과

- **처리 항목**: `failed/`로 옮기는 과정이 원자적이지 않음(쓰기+삭제 2단계) — `PendingReportQueue.RecordFailureAsync`
- **해결**: `File.Move(id, failedPath, overwrite: true)` 한 번(원자적 이동) → 이동 후 내용 갱신 순서로 교체.
  `queue/`와 `queue/failed/`에 동시에 존재하는 창 제거.
- **브랜치 → main 병합**: `auto/2026-11-11-failed-folder-atomic-move` → `--no-ff` 머지 완료(커밋 `447778b`).
- **검증**: `dotnet build`(경고 0) / `dotnet test`(Agent 83 + Server 167, 전체 통과) — 머지 후 main에서도 재확인함.
- **기획서**: `docs/specs/2026-11-11-failed-folder-atomic-move.md`
- **상세 리뷰 피드백**: `docs/auto-log.md`의 동일 날짜 두 번째 항목.

## 다음에 할 일

1. **(권장) Phase 6 백로그 중 하나를 3회차로** — 전부 🟡 수준, 의존성 낮음:
   - `PendingReportQueue`의 `Id`가 사실 파일 경로인데 이름이 추상적 (`docs/backlog.md` 「코드 품질 (Phase 6)」)
   - `AgentOptions`가 두 경로로 중복 구성됨 (`Program.cs`)
   - Serilog `retainedFileCountLimit`이 "파일 개수" 기준이라 "보관 일수" 의도와 암묵적 전제가 있음
2. **고아 브랜치 정리 필요(사람 판단)**: `auto/20261111-pending-report-id-naming` — 커밋 없이 `74be051`에 멈춰 있음, 1회차 이전에 중단된 시도로 보임. 삭제할지 재사용할지 아직 결정 안 함.
3. **origin push 여부**: 로컬 11커밋을 언제 push할지 아직 안 정함.
4. 「정확성」 섹션의 `LicenseService`/`LicenseMatchingService` 공백 불일치 버그는 범위가 넓어(CRUD+매칭+CSV 전부 영향) Phase 6 범위 밖 — Phase 5 백로그 재검토 시 별도로 다룰 것.

## 알아두면 좋은 것

- 자동 루프는 `.claude/loop.md` 규칙상 한 번에 backlog 1항목만 처리하고, main에 직접 커밋/머지하지 않는다(머지는 사람이 한다).
- 에이전트 팀 흐름에서 `docs/auto-log.md` 작성과 `auto/*` → `main` 머지는 team-lead(사용자와 직접 대화하는 세션)의 몫으로 남아있다 — developer는 건드리지 않는다.
