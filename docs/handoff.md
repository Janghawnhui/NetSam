# 핸드오프 노트

다음 세션(또는 다음 자동 개선 사이클)이 이어서 작업하기 위한 현재 상태 요약.
사이클별 상세 기록은 `docs/auto-log.md`, 보류 항목 전체는 `docs/backlog.md`,
자동 루프 규칙은 `.claude/loop.md` 참고.

---

## 현재 상태 (2026-11-15 기준)

- **브랜치**: `feature/2026-11-12-phase7-signalr` (main 아님 — Phase 7은 아직 main에 머지 안 됨), working tree 깨끗함.
- **origin과의 차이**: 로컬 `main`이 origin보다 12커밋 이상 앞섬, push 안 함(사용자가 아직 요청 안 함). Phase 7 브랜치는 origin에 없음.
- **에이전트 팀 셋업**: `.claude/settings.json`에 `CLAUDE_CODE_EXPERIMENTAL_AGENT_TEAMS=1` 활성화됨(커밋 `87798c7`).
  `developer` 에이전트가 신설돼 planner → developer → (test-writer, code-reviewer) 흐름을
  team-lead 중계 없이 직접 SendMessage로 처리한다. `.claude/agents/{planner,developer,test-writer,code-reviewer}.md` 참고.
  **팀은 2회차부터 세션 내에 계속 떠 있는 상태(idle)로 유지된다** — 다음 사이클은 `Agent` 툴로 새로
  스폰하지 말고 `ListAgents`로 확인 후 기존 `planner`/`developer`/`test-writer`/`code-reviewer`에
  `SendMessage`로 말을 걸 것. (3회차에서 이걸 놓쳐 `planner-2`를 중복 스폰한 적이 있음 — 아래 참고.)
- **주의**: `phase7-planner`(Opus)가 **세션 사용량 한도에 걸려 응답 불가** (리셋 예정: 2026-11-12 06:30 KST 기준 — 다음 세션에서 리셋 여부 재확인 필요). 한도가 안 풀렸으면 7-C 진행 시에도 developer의 모호한 지점/planner 질문은 team-lead(사용자)에게 직접 묻는 방식으로 우회해야 한다.

## Phase 7 (SignalR 실시간 채널) 진행 상황 — 2026-11-15 기준

기획서: `docs/specs/2026-11-12-phase7-signalr-realtime.md`. 사용자 지시로 **7-A/7-B/7-C 각 단계가 끝날 때마다
test-writer·code-reviewer 검증 → 커밋 → 사용자에게 요약 보고 → 사용자가 "다음"이라고 할 때까지 다음 단계로
넘어가지 않는다.** team-lead가 이 게이트를 직접 집행한다(developer/planner는 기본적으로 사용자에게 바로
보고하려 하므로, 매 사이클 시작 시 "결과는 나(team-lead)에게 보고하라"고 명시적으로 재지시해야 함).

- **7-A (연결+토큰 인증)**: 완료·커밋 `f2ea56c`. 독립 검증 완료(build 0/0, test Agent 98 + Server 178).
- **7-B (하트비트+대시보드 상태 표시)**: 완료·커밋 `cfbc9c2`(구현) + `1381046`(test-writer 보강 테스트 1개).
  독립 재검증 완료 — `dotnet build` 경고 0/오류 0, `dotnet test` Agent 98/98 + Server 187/187.
  `RecordHeartbeatAsync`가 `LastHeartbeatUtc`만 갱신하고 `IsOnline`은 안 건드리는 것, `SendHeartbeatAsync`가
  항상 `GetAuthenticatedDeviceId()`(클레임) 쪽을 기록하고 바디의 `DeviceId`는 로그에만 쓰는 것을 코드에서
  직접 확인함. code-reviewer 승인(🔴/🟡 없음). 7-C 관련 파일은 손대지 않음(diff로 확인).
  - code-reviewer가 "7-C 진행해도 좋다"고 덧붙였으나 developer가 사용자 승인 없이는 넘어가지 않고 대기함 — 올바른 처신.
- **7-C ("지금 수집")**: **미착수.** 사용자의 "다음" 지시를 기다리는 중.
- **문서 공통 작업(`docs/backlog.md`, `docs/service-install.md`, `docs/learning-log.md`, `CLAUDE.md` 현재
  Phase 표시)**: 7-C까지 전부 끝난 뒤에 한 번에 처리 예정. 아직 안 함.
- **알려진 한계(의도된 트레이드오프, 버그 아님)**: 서버 프로세스가 크래시하면 `OnDisconnectedAsync`가
  안 불려서 해당 장치가 영구히 "온라인"으로 남는다(`NoDisconnectEvent_DeviceStaysOnlineForever_KnownLimitation`
  테스트로 고정됨). 완화책(서버 시작 시 stale online 행 스윕)은 Phase 8 후보로만 기록, 미구현.

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

0. **(최우선, 진행 중) Phase 7-C "지금 수집"** — 사용자가 "다음"이라고 하면 `phase7-developer`에게 기획서의
   7-C 「수정할 파일」목록만 구현하도록 지시. 흐름은 7-A/7-B와 동일(test-writer·code-reviewer 검증 →
   커밋 → team-lead에게 보고 → 사용자 승인 전까지 정지). `phase7-planner`는 한도 문제로 못 깨울 수 있으니
   모호한 점은 team-lead가 직접 판단하거나 사용자에게 물을 것. 7-C까지 끝나면 문서 공통 작업(위 Phase 7
   섹션 참고) 진행.
1. **(Phase 6 백로그, Phase 7과 무관, 우선순위 낮음) 백로그 중 하나를 다음 사이클로** — 둘 다 🟡 수준이고 둘 다 `Program.cs` 한 파일:
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
