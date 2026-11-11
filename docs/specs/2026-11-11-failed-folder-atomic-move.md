# dead-letter 이동을 `File.Move` 한 번으로 원자화

- 작성일: 2026-11-11
- 대상 Phase: **Phase 6 (서비스화)**
- 담당 영역: `NetSamLite.Agent` / 재시도·큐
- 자동 개선 사이클: 2회차 (1회차는 `docs/specs/2026-11-11-queue-envelope-null-guard.md`)

---

## 배경

`docs/backlog.md` 「코드 품질 (Phase 6)」의 다음 항목을 구현한다.

> ### `failed/`로 옮기는 과정이 원자적이지 않음(쓰기+삭제 2단계)
> - **위치**: `src/NetSamLite.Agent/Sending/PendingReportQueue.cs` (`RecordFailureAsync`)
> - **현상**: `MaxQueueSendFailures`에 도달한 리포트를 `failed/`로 옮길 때 "새 위치에 쓰기(`WriteEnvelopeAsync`) → 원본 삭제(`RemoveQuietly`)" 순서로 진행된다. 그 사이 프로세스가 죽으면 같은 리포트가 `queue/`와 `failed/` 양쪽에 동시에 남을 수 있음(유실은 아니고 재시작 후 큐 쪽 사본이 한 번 더 전송 시도되는 중복 가능성만).
> - **해결 방법**: 같은 볼륨 안에서는 `File.Move`가 원자적이므로, "쓰기+삭제" 대신 `File.Move(id, failedPath)` 한 번으로 바꾸면 이 틈 자체가 없어짐 — (…)
> - **기록일**: 2026-11-10 (Phase 6 code-reviewer 리뷰에서 🟡로 지적됨)

### 현재 코드 확인

`src/NetSamLite.Agent/Sending/PendingReportQueue.cs:92-95`의 dead-letter 경로가 지적 그대로다.

1. `Directory.CreateDirectory(FailedDirectory)`
2. `await WriteEnvelopeAsync(failedPath, updated, ...)` — `failed/`에 사본 생성
3. `RemoveQuietly(id)` — `queue/`의 원본 삭제

2번과 3번 **사이**에 프로세스가 죽으면(서비스 중지, 전원 차단) 같은 리포트가 양쪽에 남는다. 재시작 후 `ListPendingAsync`(`:50`)는 `QueueDirectory`만 훑으므로 큐 쪽 사본이 다시 살아나 한 번 더 전송된다 — 이미 `MaxQueueSendFailures`번 실패해 포기한 리포트인데, `SendWithRetryAsync`(`ReportDeliveryService.cs:59-80`)의 지수 백오프 재시도를 처음부터 다시 소모하고, 그동안 **큐 머리에서 뒤 항목들을 막는다**.

서버 쪽 중복 수신 자체는 위험하지 않다(`POST /api/v1/inventory`는 같은 `DeviceId` 스냅샷을 다시 받는 멱등 경로). 문제는 **포기한 리포트가 되살아나 재시도 예산과 큐 순서를 잠식한다**는 점이다.

### 왜 이번 사이클에 이 항목인가

- Phase 6 범위 안(재시도·큐)이며 「절대 규칙 6」에 걸리지 않는다.
- 손댈 소스 파일 1개 + 테스트 1개. DB 스키마·HTTP 계약·`AgentOptions`·설정 파일을 건드리지 않아 다른 작업과 의존성이 없다.
- 같은 파일의 1회차 작업(`Report=null` 가드)이 이미 merge돼 있어 충돌이 없고, 손대는 메서드(`RecordFailureAsync`)의 테스트가 이미 4개 있어 회귀를 바로 잡아낼 수 있다.

## 목표

- **불변식 확보**: 어떤 시점에 프로세스가 죽어도 하나의 리포트 파일이 `queue/`와 `queue/failed/`에 **동시에 존재하지 않는다.**
- dead-letter 이동이 파일 시스템 수준에서 "한 번의 `File.Move`"로 끝나, 중간 상태(사본 2개)가 존재할 수 있는 창 자체가 사라진다.
- 재시작 후 포기한 리포트가 되살아나 재시도 예산을 소모하거나 큐 머리를 막는 경로가 없어진다.

## 범위

### 할 것

`RecordFailureAsync`의 dead-letter 분기(`:92-99`)를 아래 순서로 바꾼다.

1. `Directory.CreateDirectory(FailedDirectory)` — 그대로 유지(`File.Move`의 대상 폴더가 없으면 실패하므로 반드시 먼저).
2. `File.Move(id, failedPath, overwrite: true)` — **이 한 줄이 "큐에서 사라짐"과 "failed에 나타남"을 동시에 만든다.** 같은 볼륨(하위 폴더)이므로 원자적이다.
   - `overwrite: true`를 쓰는 이유: 파일명에 `Guid`가 들어가므로(`NewFileName()`) 같은 이름이 `failed/`에 이미 있을 확률은 사실상 0이지만, 있을 때 `IOException`으로 전송 사이클 전체가 죽는 것보다 덮어쓰는 쪽이 안전하다. dead-letter 폴더는 "버린 리포트 보관소"이므로 덮어쓰기로 잃을 게 없다.
3. `await WriteEnvelopeAsync(failedPath, updated, cancellationToken)` — 이동이 끝난 **뒤** 파일 내용의 `FailureCount`를 최종값으로 갱신한다.
4. 기존 `logger.LogError(...)` 로그는 메시지·구조적 템플릿(`{FailureCount}`, `{Path}`) 그대로 유지한다.
5. `RemoveQuietly(id)` 호출을 이 분기에서 **삭제**한다(`File.Move`가 원본을 이미 치웠다). `RemoveQuietly` 자체는 다른 호출부(`:79`, `EnforceMaxQueuedReports`, `PruneExpired`)가 있으니 메서드를 지우지는 않는다.
6. 왜 "이동 먼저, 내용 갱신 나중"인지 **2~3줄 주석**으로 남긴다(학습 프로젝트 — 「절대 규칙 9」). 요지: 3번 직전에 죽으면 `failed/` 파일의 `FailureCount`가 1 작게 남지만, `failed/`는 `ListPendingAsync`가 읽지 않는 dead-letter이므로 그 값은 사람이 보는 참고값일 뿐이다. 반대 순서("내용 갱신 먼저 → 이동")로 하면 중간에 죽을 때 리포트가 `queue/`에 남아 **포기한 리포트가 한 사이클 더 재시도되는** 지금 문제의 변형이 남는다.
7. 테스트 추가 (`tests/NetSamLite.Agent.Tests/Sending/PendingReportQueueTests.cs`).

### 안 할 것

- **`File.Move` 실패에 대한 새 예외 처리를 추가하지 않는다.** 지금도 `WriteEnvelopeAsync`가 던지면 그대로 `ReportDeliveryService`로 전파되는 구조다. 동작을 동일하게 유지하고, 디스크 오류 정책은 별도 작업으로 둔다. (`overwrite: true`로 "대상 파일 이미 존재"만 미리 차단한다.)
- **`FailureCount`가 1 작게 남는 크래시 창을 없애려 하지 않는다.** 없애려면 파일 쓰기 2단계를 다시 들여와야 해서 이번 작업의 목적과 정면으로 어긋난다. dead-letter 파일의 참고값 오차는 받아들인다.
- **`failed/` 폴더를 읽는 기능(재처리·조회 API·CLI)을 만들지 않는다.** 현재 `failed/`는 `PruneExpired`만 접근하며, 그 설계를 바꾸지 않는다.
- **같은 파일의 다른 백로그 항목은 건드리지 않는다**: `PendingReportEntry.Id` 네이밍, `AgentOptions` 중복 구성, Serilog `retainedFileCountLimit` 전제 — 각각 별도 사이클이다.
- `AgentOptions`에 새 설정값을 추가하지 않는다.
- `EnqueueAsync`/`ListPendingAsync`/`RemoveAsync`/`PruneExpired`/`EnforceMaxQueuedReports`의 로직은 손대지 않는다.

## 수정할 파일

| 파일 | 할 일 |
|---|---|
| `src/NetSamLite.Agent/Sending/PendingReportQueue.cs` | `RecordFailureAsync`의 dead-letter 분기를 「할 것」 1~6번대로 교체. **`updatedFailureCount < maxFailures` 판정, `Math.Max(1, ...)` 방어, 반환값(`true`/`false`) 의미, 읽을 수 없는 파일 처리(`:76-81`)는 전부 그대로 유지한다.** 동작 변경은 "파일 이동 방식" 하나뿐이다. |
| `tests/NetSamLite.Agent.Tests/Sending/PendingReportQueueTests.cs` | 아래 「테스트 시나리오」의 3개 테스트 추가. 기존 13개 테스트는 수정하지 않는다 — 수정이 필요해지면 의도치 않은 동작 변경이라는 신호이므로 먼저 이 기획서로 돌아와 상의한다. |
| `docs/backlog.md` | **사이클이 통과한 뒤에만** 이 항목(「코드 품질 (Phase 6)」의 "`failed/`로 옮기는 과정이 원자적이지 않음")을 「해결됨」 섹션으로 옮기고, **해결일 `2026-11-11`**, **브랜치명**(`auto/2026-11-11-failed-folder-atomic-move` 형태), **기획서 경로 `docs/specs/2026-11-11-failed-folder-atomic-move.md`**, 한 줄 「해결 방법」을 1회차 항목과 같은 형식으로 함께 적는다. |
| `docs/auto-log.md` | `.claude/loop.md` 7번대로 이번 사이클 기록 추가(시각, 브랜치, 처리 항목, 변경 파일, 테스트 수, 리뷰 결과, "내가 배울 점" 3줄). |

소스 변경은 2개 파일, 문서 포함 4개다. 「절대 규칙 7」에 따라 **이 기획서를 사전 승인 문서로 삼는다.** 위 목록 밖의 파일을 건드려야 할 상황이 생기면 먼저 질문한다.

### 절대 규칙 점검

| 규칙 | 해당 여부 |
|---|---|
| 1 (회사 자산) | 해당 없음. 자체 코드와 .NET `File.Move` 공개 동작만 사용 |
| 2 (에이전트 읽기 전용) | 레지스트리·WMI 코드 미변경. 파일 조작은 **에이전트 자신이 만든 큐 파일**에 한정되며, 오히려 삭제 1회가 줄어든다 |
| 3 (개인정보) | 수집 항목·로그 내용 변경 없음(로그는 기존대로 파일 경로와 실패 횟수만) |
| 4 (비밀값) | 설정 파일 변경 없음 |
| 5 (`Win32_Product`) | 해당 없음 |
| 6 (현재 Phase) | Phase 6(서비스화 — 재시도·큐·로그) 범위 안 |

## 완료 기준

`dotnet build`(신규 경고 0개)와 `dotnet test` 통과 외에:

1. `MaxQueueSendFailures`에 도달하면 리포트가 `queue/`에서 사라지고 `queue/failed/`에 **정확히 1개** 존재한다(양쪽 동시 존재 없음).
2. `failed/`로 옮겨진 파일의 **내용이 보존된다** — 역직렬화하면 원본 `Report.Device.DeviceId`를 그대로 읽을 수 있고, `FailureCount`가 `MaxQueueSendFailures`와 같다.
3. 이동 후 `queue/`와 `queue/failed/` 어디에도 `*.tmp` 잔여 파일이 없다.
4. `RecordFailureAsync`의 반환값 의미가 그대로다 — 상한 미달이면 `false`(큐 유지), 도달하면 `true`(포기).
5. 소스에서 dead-letter 분기의 "사본 쓰기 → 원본 삭제" 2단계가 사라졌고, `File.Move`가 **원본 경로를 소스로** 호출된다(코드 리뷰 확인 항목).
6. 기존 `PendingReportQueueTests` 13개 테스트가 **수정 없이** 통과한다(순서 보장·`MaxQueuedReports`·`QueueRetentionDays`·`failed/` 보관 기간 정리·`Report=null` 가드 회귀 없음).
7. `dotnet build` 경고에 `File.Move` 관련 플랫폼/오버로드 경고가 없다(`overwrite` 오버로드는 .NET Core 3.0+ 지원).

## 테스트 시나리오

파일: `tests/NetSamLite.Agent.Tests/Sending/PendingReportQueueTests.cs`
기존 `CreateReport` 헬퍼와 테스트별 임시 디렉터리 패턴을 재사용한다.
`src/NetSamLite.Agent/NetSamLite.Agent.csproj:23`에 `InternalsVisibleTo Include="NetSamLite.Agent.Tests"`가 이미 있으므로, `internal` 타입인 `QueuedReportEnvelope`를 테스트에서 직접 `JsonSerializer.Deserialize<QueuedReportEnvelope>`로 쓸 수 있다(`JsonDocument`로 수동 파싱할 필요 없음).

### 1. 정상 — 이동이 내용과 실패 횟수를 보존한다

`RecordFailureAsync_ReachesMaxFailures_MovedFileKeepsReportAndFinalFailureCount`

- `MaxQueueSendFailures = 2`로 큐를 만들고 `CreateReport("DEV-DEAD")`를 `EnqueueAsync`.
- `RecordFailureAsync`를 2번 호출(두 번째가 `true` 반환).
- 검증:
  - `ListPendingAsync()`가 비어 있다.
  - `queue/` 직하의 `*.json`이 0개(= 양쪽 동시 존재 없음).
  - `failed/`의 `*.json`이 1개이고, 그 파일을 `QueuedReportEnvelope`로 역직렬화하면 `Report!.Device.DeviceId == "DEV-DEAD"`, `FailureCount == 2`.
- 의도: 기존 테스트(`...ReturnsTrueMovesFileToFailedFolderAndRemovesFromQueue`)는 파일 **개수**만 봤다. 이동 방식을 "쓰기"에서 "`File.Move` + 내용 갱신"으로 바꾸면 내용 보존이 깨질 수 있는 지점이므로 내용까지 못박는다.

### 2. 경계 — `failed/`에 같은 이름의 파일이 이미 있을 때

`RecordFailureAsync_FailedFolderAlreadyHasFileWithSameName_OverwritesItWithoutThrowing`

- `MaxQueueSendFailures = 1`로 설정(첫 실패에 바로 dead-letter — 경계값 1 자체도 함께 검증).
- `EnqueueAsync(CreateReport("DEV-NEW"))` 후 `ListPendingAsync`로 `entry.Id`(= 파일 경로)를 얻는다.
- `queue/failed/` 폴더를 미리 만들고, **`Path.GetFileName(entry.Id)`와 똑같은 이름**으로 아무 내용(예: `"{}"`)의 파일을 심어둔다.
- `RecordFailureAsync(entry.Id, ...)` 호출.
- 검증: 예외가 나지 않고 `true` 반환, `queue/` 직하 `*.json` 0개, `failed/` `*.json` 1개, 그 내용의 `Report!.Device.DeviceId == "DEV-NEW"`(심어둔 `{}`가 아니라 새 리포트로 덮였다), `FailureCount == 1`.
- 의도: `overwrite: true` 없이 `File.Move`를 쓰면 이 테스트가 `IOException`으로 실패한다 — 오버로드 선택을 테스트로 고정한다.

### 3. 예외 — 원본 파일이 사라진 경우 `failed/`에 쓰레기를 만들지 않는다

`RecordFailureAsync_IdFileDoesNotExist_ReturnsTrueAndCreatesNothingInFailedFolder`

- 큐 폴더만 만들고(`Directory.CreateDirectory`), 존재하지 않는 파일 경로를 직접 조립한다(예: `Path.Combine(_queueDirectory, "20200101T000000000Z_missing.json")`).
- `RecordFailureAsync(missingPath, ...)` 호출.
- 검증: `true` 반환(= 호출부가 이 항목을 건너뛰어 큐 머리가 막히지 않는다), `queue/failed/` 폴더가 생기지 않았거나 생겼더라도 `*.json`이 0개.
- 의도: 없는 파일은 `ReadEnvelopeAsync`의 `IOException` 경로(`FileNotFoundException`은 `IOException` 파생)로 걸러져 dead-letter 분기에 **도달조차 하지 않아야** 한다. 이 경로가 깨지면 `File.Move`가 `FileNotFoundException`을 던져 전송 사이클 전체가 죽는다.
