# 자동 개선 사이클 로그

`docs/backlog.md`의 보류 항목을 하나씩 골라 구현·리뷰·테스트까지 자동으로 돌린 사이클의 기록.

---

## 2026-11-11 — 큐 봉투 역직렬화 `Report=null` 가드

- **backlog 항목**: `docs/backlog.md` 「코드 품질 (Phase 6)」 "구버전 큐 파일 포맷을 읽을 때 조용히 null Report로 역직렬화됨" (2026-11-10 기록)
- **기획서**: `docs/specs/2026-11-11-queue-envelope-null-guard.md`
- **브랜치**: `auto/2026-11-11-queue-envelope-null-guard`
- **변경 파일**:
  - `src/NetSamLite.Agent/Sending/PendingReportQueue.cs`
  - `tests/NetSamLite.Agent.Tests/Sending/PendingReportQueueTests.cs`
- **리뷰 루프 횟수**: 1회 (code-reviewer 1차에서 🔴 없이 통과, 🟡 2건 중 1건만 즉시 반영)
- **최종 결과**: 통과

### 구현 요약

`QueuedReportEnvelope.Report`를 `InventoryReport?`로 바꾸고, `ReadEnvelopeAsync`에서
역직렬화 후 `Report`가 null이면 "읽을 수 없는 파일"과 동일하게 경고 로그를 남기고
건너뛰도록 가드를 추가했다. 반환 타입을 `(int FailureCount, InventoryReport Report)?`
튜플로 좁혀 null 검증 이후 호출부(`ListPendingAsync`, `RecordFailureAsync`)에
CS8604 경고 없이 비-null 값만 전달되도록 했다. 큐 순서·실패 누적·failed 폴더 이동·
보관 기간 로직은 변경하지 않았다.

### test-writer 피드백 요약

developer 초안 테스트 3개(Theory 포함 5케이스)를 검증 — 보강 없이 그대로 승인.
기획서 완료 기준 1~6 모두 커버됨을 확인(구버전 포맷/`{}` 케이스, head-of-line
영향 없음, `RecordFailureAsync` true 반환 + 파일 삭제, 기존 11개 테스트 무변경 통과).
완료 기준 7번(경고 로그의 `{Path}` 구조적 로깅)은 `NullLogger` 사용으로 테스트에서
직접 검증하지 않았으나, 기존 테스트 컨벤션과 일치하여 범위 밖으로 판단.

### code-reviewer 피드백 요약

🔴 없음. 🟡 2건:
1. 봉투 자체가 null인 경우와 `Report`만 null인 경우가 같은 로그 메시지를 써서
   부정확함 — 같은 파일 내 수정이라 즉시 반영(두 케이스 로그 메시지 분리).
2. `docs/backlog.md`의 해당 항목이 해결됐는데 문서에 남아있음 — 이 파일은
   기획서의 「수정할 파일」 목록 밖이라 planner에게 질문했고, "team-lead가
   별도로 처리"하라는 답을 받아 이번 사이클에서는 건드리지 않았다. 후속 문서:
   `docs/specs/2026-11-11-queue-envelope-null-guard-followup.md` (team-lead가
   backlog 항목 제거 vs 「해결됨」 섹션 이동 중 선택).

### planner 질문/답변

- **질문**: `docs/backlog.md` 해당 항목을 이번 사이클에서 제거/해결 표시해도
  되는지, 범위 밖이라 보류해야 하는지.
- **답변**: 범위 밖. team-lead가 명시한 수정 파일 2개 외에는 구현 중 스스로
  범위를 넓히지 않는다(절대 규칙 7 취지). backlog.md는 프로젝트 전체 보류
  항목 대장이라 별도 관리 대상. 후속 문서를 새로 작성해 team-lead가 처리할
  두 방안(항목 제거/해결됨 섹션 이동)을 정리해두었다.
