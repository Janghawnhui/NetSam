# (후속) 백로그 항목 정리 — 큐 봉투 null 가드 반영

- 작성일: 2026-11-11
- 선행 기획서: `docs/specs/2026-11-11-queue-envelope-null-guard.md`
- 상태: **team-lead 승인 대기** (구현 작업 아님, 문서 정리 작업)

## 배경

위 기획서의 구현이 끝나고 code-reviewer 리뷰를 🔴 없이 통과했다. code-reviewer가 🟡로 "`docs/backlog.md`의 해당 항목이 해결됐으니 제거/해결 표시하자"고 제안했고, developer가 "team-lead 지시의 수정 파일 목록(2개)에 `docs/backlog.md`가 없는데 건드려도 되는지" 확인을 요청했다.

구현 확인 결과 백로그 항목은 실제로 해소됐다.

- `src/NetSamLite.Agent/Sending/PendingReportQueue.cs:11` — `QueuedReportEnvelope(int FailureCount, InventoryReport? Report)`
- `src/NetSamLite.Agent/Sending/PendingReportQueue.cs:148` — `ReadEnvelopeAsync`가 `Task<(int FailureCount, InventoryReport Report)?>` 반환
- `src/NetSamLite.Agent/Sending/PendingReportQueue.cs:161` — `if (envelope.Report is null)` 가드

## 판단

**이번 구현 작업의 범위 밖이다. developer는 `docs/backlog.md`를 건드리지 않는다.**

근거:

1. team-lead 지시가 수정 파일을 2개(`PendingReportQueue.cs`, `PendingReportQueueTests.cs`)로 명시했다. 승인된 범위를 구현 중에 스스로 넓히지 않는 것이 「절대 규칙 7」(계획 먼저)의 취지다.
2. `docs/backlog.md`는 CLAUDE.md 「📝 백로그」에서 **프로젝트 전체의 보류 항목 대장**으로 규정된 문서다. 특정 기능 작업의 산출물이 아니라 별도 관리 대상이다.
3. 다만 code-reviewer 지적 자체는 타당하다 — 해결된 항목을 그대로 두면 다음 사이클의 planner가 **이미 고쳐진 일을 다시 골라** 중복 작업을 하게 된다. 그래서 "하지 말자"가 아니라 "별도 작업으로 team-lead가 처리"가 맞다.

## 제안하는 처리 (team-lead 결정 사항)

`docs/backlog.md`의 「코드 품질 (Phase 6)」에서 **"구버전 큐 파일 포맷을 읽을 때 조용히 null Report로 역직렬화됨"** 항목(현재 14~19행)을 아래 둘 중 하나로 처리한다.

- **방안 A (권장): 항목 제거.** CLAUDE.md는 백로그를 "지금 당장 안 고치기로 한 항목" 목록으로 정의하므로, 고쳐진 항목은 목록에서 빠지는 게 정의에 맞다. 해결 경위는 커밋 메시지와 이 기획서에 남아 있어 추적이 가능하다.
- **방안 B: 「해결됨」 섹션으로 이동**하고 `- **해결일**: 2026-11-11 (기획서: docs/specs/2026-11-11-queue-envelope-null-guard.md)` 한 줄을 붙인다. 백로그에 해결 이력도 같이 쌓고 싶을 때 선택한다.

어느 쪽이든 **같은 파일에 남아 있는 나머지 Phase 6 항목 3개**(`failed/` 이동 원자성, `PendingReportEntry.Id` 네이밍, `AgentOptions` 중복 구성, Serilog `retainedFileCountLimit` 전제)는 **손대지 않는다.** 이번 구현으로 해결된 것이 아니므로 그대로 보류 상태를 유지한다.

## 주의

planner(나)는 `docs/specs/*.md`만 쓰기로 되어 있어 `docs/backlog.md`를 직접 수정하지 않는다. 이 문서는 그 결정 요청용이다.
