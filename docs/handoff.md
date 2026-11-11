# 핸드오프 노트

다음 세션(또는 다음 자동 개선 사이클)이 이어서 작업하기 위한 현재 상태 요약.
사이클별 상세 기록은 `docs/auto-log.md`, 보류 항목 전체는 `docs/backlog.md`,
자동 루프 규칙은 `.claude/loop.md` 참고.

---

## 현재 상태 (2026-11-12 기준)

- **브랜치**: `main`, working tree 깨끗함.
- **origin과의 차이**: 로컬 `main`이 origin보다 **12커밋 앞섬**, push 안 함(사용자가 아직 요청 안 함).
- **에이전트 팀 셋업**: `.claude/settings.json`에 `CLAUDE_CODE_EXPERIMENTAL_AGENT_TEAMS=1` 활성화됨(커밋 `87798c7`).
  `developer` 에이전트가 신설돼 planner → developer → (test-writer, code-reviewer) 흐름을
  team-lead 중계 없이 직접 SendMessage로 처리한다. `.claude/agents/{planner,developer,test-writer,code-reviewer}.md` 참고.
  **팀은 2회차부터 세션 내에 계속 떠 있는 상태(idle)로 유지된다** — 다음 사이클은 `Agent` 툴로 새로
  스폰하지 말고 `ListAgents`로 확인 후 기존 `planner`/`developer`/`test-writer`/`code-reviewer`에
  `SendMessage`로 말을 걸 것. (3회차에서 이걸 놓쳐 `planner-2`를 중복 스폰한 적이 있음 — 아래 참고.)

## 이번 사이클(3회차) 결과

- **처리 항목**: `PendingReportQueue`의 `Id`가 사실 파일 경로인데 이름이 추상적 (`docs/backlog.md` 「코드 품질 (Phase 6)」)
- **해결**: `PendingReportEntry.Id` → `FilePath`로 이름 변경, `RemoveAsync`/`RecordFailureAsync`의
  `id` 매개변수도 `filePath`로 변경 + `<param>` XML 문서 추가. 제어 흐름·로그 메시지는 무변경(순수 리네이밍).
- **브랜치 → main 병합**: `auto/2026-11-12-pending-report-filepath` → `--no-ff` 머지 완료(커밋 메시지 "merge: PendingReportEntry.Id를 FilePath로 이름 변경").
- **검증**: `dotnet build`(경고 0) / `dotnet test`(Agent 86 + Server 167, 전체 통과) — 머지 후 main에서도 재확인함.
- **기획서**: `docs/specs/2026-11-12-pending-report-filepath.md`
- **특이사항**: team-lead가 기존 팀을 재사용해야 했는데 실수로 `planner-2`를 중복 스폰함. 이미 작업
  지시까지 나간 상태라 산출물(품질 양호)을 그대로 채택하고 진행함 — 결과물엔 영향 없었으나 다음
  사이클에선 주의할 것(위 "에이전트 팀 셋업" 참고).
- **상세 리뷰 피드백**: `docs/auto-log.md`의 2026-11-12 항목.

## 다음에 할 일

1. **(권장) Phase 6 백로그 중 하나를 4회차로** — 둘 다 🟡 수준이고 둘 다 `Program.cs` 한 파일:
   - `AgentOptions`가 두 경로로 중복 구성됨 (`Program.cs`) — 테스트 0개, DI/Serilog 초기화 순서 제약 있어 신중히
   - Serilog `retainedFileCountLimit`이 "파일 개수" 기준이라 "보관 일수" 의도와 암묵적 전제가 있음 — 효과를 테스트로 직접 검증하기 어려움
   - 같은 파일이라 두 항목을 한 사이클에 묶는 것도 검토 가치 있음(planner-2 기획서의 판단 참고).
2. **고아 브랜치 정리 필요(사람 판단)**: `auto/20261111-pending-report-id-naming` — 커밋 없이 `74be051`에 멈춰 있음, 1회차 이전에 중단된 시도로 보임. 삭제할지 재사용할지 아직 결정 안 함.
3. **origin push 여부**: 로컬 12커밋을 언제 push할지 아직 안 정함.
4. 「정확성」 섹션의 `LicenseService`/`LicenseMatchingService` 공백 불일치 버그는 범위가 넓어(CRUD+매칭+CSV 전부 영향) Phase 6 범위 밖 — Phase 5 백로그 재검토 시 별도로 다룰 것.

## 알아두면 좋은 것

- 자동 루프는 `.claude/loop.md` 규칙상 한 번에 backlog 1항목만 처리하고, main에 직접 커밋/머지하지 않는다(머지는 사람이 한다).
  단, **에이전트 팀으로 수동 사이클을 돌릴 때는** 지난 2~3회차처럼 team-lead가 검증 후 직접 `--no-ff` 머지까지 담당하는 패턴이 자리잡음.
- 에이전트 팀 흐름에서 `docs/auto-log.md` 작성과 `auto/*` → `main` 머지는 team-lead(사용자와 직접 대화하는 세션)의 몫으로 남아있다 — developer는 건드리지 않는다.
- **팀 재사용**: 이전 사이클에서 스폰한 `planner`/`developer`/`test-writer`/`code-reviewer`는 세션이 끝나지 않는 한 idle로 계속 떠 있다. 새 사이클 시작 시 `ListAgents`로 먼저 확인하고, 떠 있으면 `Agent`로 새로 스폰하지 말고 `SendMessage`로 기존 팀원에게 말 걸 것.
