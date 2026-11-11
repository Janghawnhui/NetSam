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

---

## 2026-11-11 — dead-letter 이동 원자화 (`failed/` 쓰기+삭제 2단계 제거)

- **backlog 항목**: `docs/backlog.md` 「코드 품질 (Phase 6)」 "`failed/`로 옮기는 과정이 원자적이지 않음(쓰기+삭제 2단계)" (2026-11-10 기록)
- **기획서**: `docs/specs/2026-11-11-failed-folder-atomic-move.md`
- **브랜치**: `auto/2026-11-11-failed-folder-atomic-move`
- **변경 파일**:
  - `src/NetSamLite.Agent/Sending/PendingReportQueue.cs`
  - `tests/NetSamLite.Agent.Tests/Sending/PendingReportQueueTests.cs`
- **리뷰 루프 횟수**: 1회 (code-reviewer 1차에서 🔴/🟡 없이 승인, 참고 1건만)
- **최종 결과**: 통과 (`dotnet build` 경고 0개, `dotnet test` Agent 83 / Server 167 전체 통과 — team-lead가 커밋 후 직접 재실행해 재검증)

### 구현 요약 (이번 사이클부터 팀 구성 변경)

이번 사이클부터 `developer` 에이전트를 신설해 planner → developer → (test-writer, code-reviewer) 흐름을
팀 내 직접 SendMessage로 돌렸다(team-lead가 중계하지 않음). team-lead는 사이클 전체를 배경에서
지켜보며 상태 알림만 받다가, developer가 완료 요약을 보낸 뒤에 빌드·테스트를 직접 재실행해 검증하고
이 로그와 아래 커밋만 담당했다.

`RecordFailureAsync`의 dead-letter 분기를 "새 위치에 쓰기(`WriteEnvelopeAsync`) → 원본 삭제(`RemoveQuietly`)"
2단계에서 `File.Move(id, failedPath, overwrite: true)` 한 번(이동만 먼저, 같은 볼륨 안에서 원자적) →
이동된 파일의 `FailureCount` 내용을 갱신하는 순서로 바꿔, 프로세스가 중간에 죽어도 리포트가
`queue/`와 `queue/failed/` 양쪽에 동시에 남는 창을 없앴다. `RemoveQuietly`는 다른 호출부
(`EnforceMaxQueuedReports`, `PruneExpired`)에서 여전히 쓰이므로 메서드 자체는 유지하고 이 분기에서만
호출을 제거했다.

### test-writer 피드백 요약

정상/경계/예외 테스트 3개 추가, 기존 13개 무변경 통과. "완료 기준 3번(.tmp 잔여 파일 없음)이 새
테스트에서 직접 검증 안 됨"을 지적해 developer가 기존 테스트에
`Directory.GetFiles(failedDirectory, "*.tmp").Should().BeEmpty()` 한 줄을 추가로 반영했다. 그 외
지적 없음 — 경쟁 상태(동시 다중 프로세스) 테스트는 기획서의 「안 할 것」 범위라 백로그 후보로만
언급하고 테스트는 작성하지 않았다.

### code-reviewer 피드백 요약

🔴/🟡 없음, 승인. `File.Move`를 내용 갱신보다 먼저 호출하는 순서가 기획서 의도(이동 자체를 원자적
경계로 삼음)와 맞는지 확인했고, 절대 규칙 2(읽기 전용) 위반 없음과 `File.Move` 실패 시 예외 전파
경로(`ReportDeliveryService` → `AgentWorker`의 사이클 단위 catch)가 변경 전과 동일함을 코드 추적으로
확인했다. 참고 1건은 사소한 수준으로 기록될 만한 🟡가 아니었다고 developer가 보고.

### planner 질문/답변

없음 — 기획서가 명확해서 developer가 범위 확인 질문 없이 그대로 구현했다.

### 그 외 특이사항

- `git branch --list "auto/*"`에 이번 사이클과 무관한 `auto/20261111-pending-report-id-naming`
  브랜치가 남아있었다(1회차 이전부터 존재, 커밋 없이 `74be051`에 그대로 멈춰 있음 — 이전에 그
  backlog 항목을 고르려다 중단된 흔적으로 보임). 이번 사이클 범위가 아니라 team-lead가 그대로
  두고 사용자에게만 보고했다. 다음 사이클에서 정리 여부를 사람이 결정.
