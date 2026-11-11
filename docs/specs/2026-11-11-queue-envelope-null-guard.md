# 큐 파일 역직렬화 시 `Report=null` 가드 추가

- 작성일: 2026-11-11
- 대상 Phase: **Phase 6 (서비스화)**
- 담당 영역: `NetSamLite.Agent` / 재시도·큐

---

## 배경

`docs/backlog.md` 「코드 품질 (Phase 6)」의 다음 항목을 구현한다.

> ### 구버전 큐 파일 포맷을 읽을 때 조용히 null Report로 역직렬화됨
> - **위치**: `src/NetSamLite.Agent/Sending/PendingReportQueue.cs` (`ReadEnvelopeAsync`)
> - **현상**: 큐 파일을 `InventoryReport`에서 `QueuedReportEnvelope(FailureCount, Report)` 래퍼로 바꿨는데, 구버전 포맷(래퍼 없이 `InventoryReport`를 바로 직렬화한 파일)을 `JsonSerializer.Deserialize<QueuedReportEnvelope>`로 읽으면 System.Text.Json이 예외 없이 `Report=null`인 객체를 만들어낸다. 이게 "깨진 파일" 경로로 안 빠지고 정상 항목처럼 흘러가 `HttpInventorySender`가 null 바디를 서버에 POST하게 됨 — 결국 `MaxQueueSendFailures`로 dead-letter 되긴 하지만 그 전까지 쓸모없는 재시도가 몇 번 낭비됨.
> - **나중에 고려할 방향**: `ReadEnvelopeAsync`에서 역직렬화 후 `envelope.Report is null`이면 "손상된 파일"과 동일하게 즉시 삭제/dead-letter 처리하는 가드 추가.
> - **기록일**: 2026-11-10 (Phase 6 code-reviewer 리뷰에서 🟡로 지적됨)

현재 코드 확인 결과 지적이 그대로 유효하다.

- `src/NetSamLite.Agent/Sending/PendingReportQueue.cs:147-160` `ReadEnvelopeAsync`는 `IOException`/`JsonException`만 "읽을 수 없는 파일"로 처리한다. JSON 문법이 맞고 프로퍼티 이름만 다른 파일은 예외가 나지 않는다.
- `src/NetSamLite.Agent/Sending/PendingReportQueue.cs:11` `QueuedReportEnvelope(int FailureCount, InventoryReport Report)`는 `Report`가 non-nullable로 선언돼 있지만, nullable 참조 타입은 컴파일 타임 힌트일 뿐이라 역직렬화 시 런타임에 null이 들어가는 것을 막지 못한다.
- 그 null이 `PendingReportEntry`(`:55`)에 담겨 `ReportDeliveryService.FlushQueueAsync`(`src/NetSamLite.Agent/Sending/ReportDeliveryService.cs:38`)로 흘러가 `sender.TrySendAsync(null, ...)`까지 내려간다.

이 문제는 "구버전 큐 파일"뿐 아니라 **JSON 문법은 맞지만 내용이 비어 있는 큐 파일 전반**(예: 사람이 실수로 만든 `{}`, 다른 도구가 덮어쓴 파일)에서도 똑같이 재현된다. 백로그가 "배포 전이라 구버전 파일이 없다"는 이유로 보류했었지만, 가드 자체는 구버전 호환과 무관한 **입력 검증**이고 변경 범위가 작아 지금 처리한다.

왜 이번 사이클에 이 항목을 골랐는가: Phase 6 범위 안이고, 손댈 소스 파일이 1개(+테스트 1개)이며, DB 스키마·HTTP 계약·설정 파일을 건드리지 않아 다른 작업과 의존성이 없다.

## 목표

- 큐 디렉터리에 `Report`가 없는(또는 null인) JSON 파일이 있어도, 그 항목이 **정상 큐 항목처럼 전송 경로로 흘러가지 않는다**.
- 해당 파일은 기존 "손상된 큐 파일"과 **완전히 같은 방식**으로 처리된다 — `ListPendingAsync`에서는 경고 로그를 남기고 건너뛰고, `RecordFailureAsync`에서는 삭제하고 `true`(포기)를 돌려준다.
- 결과적으로 `IInventorySender.TrySendAsync`에 null 리포트가 전달되는 경로가 사라진다.

## 범위

### 할 것

1. `ReadEnvelopeAsync`에 역직렬화 후 검증 추가: 봉투 자체가 null이거나 `Report`가 null이면 "읽을 수 없는 파일"과 동일하게 `null`을 반환하고 경고 로그를 남긴다.
2. `QueuedReportEnvelope.Report`의 타입을 `InventoryReport?`로 바꿔, "디스크에서 읽은 값은 신뢰할 수 없다"는 사실을 타입에 드러낸다.
3. 위 변경으로 호출부에 nullable 경고(CS8604 등)가 생기지 않도록 `ReadEnvelopeAsync`의 반환 타입을 "검증이 끝난 값"으로 좁힌다.
   - 권장 형태: `private async Task<(int FailureCount, InventoryReport Report)?> ReadEnvelopeAsync(...)`. null 검사 뒤에 `return (envelope.FailureCount, envelope.Report);`를 하면 컴파일러의 null 흐름 분석이 narrowing을 해줘서 `!`(null 무시 연산자) 없이 경고가 사라진다.
   - `RecordFailureAsync`의 `envelope with { FailureCount = ... }`는 반환 타입이 바뀌면 `new QueuedReportEnvelope(failureCount + 1, report)` 형태로 바뀐다. 그 외 로직(상한 비교, failed 폴더 이동, 로그)은 그대로 둔다.
   - 더 나은 형태가 있다고 판단되면 developer가 조정해도 된다. 지켜야 할 조건은 **① 새 컴파일 경고 0개 ② `PendingReportEntry.Report`가 절대 null이 아님** 두 가지뿐이다.
4. 테스트 추가 (`tests/NetSamLite.Agent.Tests/Sending/PendingReportQueueTests.cs`).

### 안 할 것

- **`Report` 안쪽 필드까지 검증하지 않는다** (`Device`가 null인지, `DeviceId`가 빈 문자열인지 등). 이번 가드는 "봉투에 리포트가 아예 없다"까지만 본다. 리포트 내용 유효성 검증은 서버 책임이고, 별도 작업으로 둔다.
- **구버전 포맷을 읽어서 새 포맷으로 변환(마이그레이션)하지 않는다.** 운영 중인 인스턴스가 없어 되살릴 데이터가 없고, 변환 코드는 하위호환 shim이 된다. 구버전 파일은 "내용 없는 파일"과 같이 버린다.
- **건너뛴 파일을 `failed/`로 보존하지 않는다.** 현재 깨진 JSON 파일도 `ListPendingAsync`에서는 삭제하지 않고 그냥 건너뛰며, `QueueRetentionDays`가 지나면 `PruneExpired`가 지운다. 이 가드도 그 동작을 그대로 따른다 — 깨진 파일 보존/격리 정책 변경은 이 작업의 범위가 아니다.
- 같은 파일에 있는 다른 백로그 항목(`failed/` 이동의 원자성, `PendingReportEntry.Id` 네이밍)은 **건드리지 않는다.** 각각 별도 작업이다.
- `AgentOptions`에 새 설정값을 추가하지 않는다.

## 수정할 파일

| 파일 | 할 일 |
|---|---|
| `src/NetSamLite.Agent/Sending/PendingReportQueue.cs` | `QueuedReportEnvelope.Report`를 `InventoryReport?`로 변경. `ReadEnvelopeAsync`에 null 검증 추가 + 반환 타입을 검증 완료 형태로 좁힘. `ListPendingAsync`/`RecordFailureAsync` 호출부를 새 반환 타입에 맞게 조정. **동작 변경은 "null 리포트 차단" 하나뿐이고, 큐 순서·실패 누적·failed 이동·보관 기간 로직은 그대로 유지한다.** |
| `tests/NetSamLite.Agent.Tests/Sending/PendingReportQueueTests.cs` | 아래 「테스트 시나리오」의 3개 테스트 추가. 기존 테스트는 수정하지 않는다(수정이 필요해지면 그건 의도치 않은 동작 변경이라는 신호이므로 먼저 이 기획서로 되돌아와 상의한다). |

변경 파일은 2개로 「절대 규칙 7」(3개 이상)의 사전 승인 대상은 아니지만, 이 기획서를 승인 문서로 삼는다.

### 절대 규칙 점검

| 규칙 | 해당 여부 |
|---|---|
| 1 (회사 자산) | 해당 없음. 자체 코드와 System.Text.Json 공개 동작만 사용 |
| 2 (에이전트 읽기 전용) | 레지스트리·WMI 코드를 건드리지 않음. 파일 삭제는 **에이전트 자신이 만든 큐 파일**에 한정되고 기존 동작 그대로 |
| 3 (개인정보) | 수집 항목 변경 없음. 추가 로그에 리포트 내용을 찍지 않고 **파일 경로만** 남긴다 |
| 4 (비밀값) | 설정 파일 변경 없음 |
| 5 (`Win32_Product`) | 해당 없음 |
| 6 (현재 Phase) | Phase 6(서비스화 — 재시도·큐·로그) 범위 안 |

## 완료 기준

`dotnet build`(경고 포함 신규 경고 0개)와 `dotnet test` 통과 외에:

1. 구버전 포맷 파일(래퍼 없이 `InventoryReport`를 직렬화한 JSON)이 큐에 있어도 `ListPendingAsync` 결과에 포함되지 않는다.
2. `{}`처럼 JSON 문법은 맞지만 `Report`가 없는 파일도 같이 걸러진다.
3. 같은 큐에 정상 파일이 함께 있으면 정상 파일은 영향 없이 반환된다(한 파일 때문에 큐 전체가 멈추지 않는다).
4. 걸러진 파일에 `RecordFailureAsync`를 호출하면 `true`를 반환하고 파일이 삭제된다 — 깨진 JSON 파일과 동일 동작.
5. `ListPendingAsync`가 반환하는 어떤 `PendingReportEntry`도 `Report`가 null이 아니다(→ `IInventorySender`에 null이 전달될 경로 없음).
6. 기존 `PendingReportQueueTests` 11개 테스트가 **수정 없이** 그대로 통과한다(순서 보장·MaxQueuedReports·QueueRetentionDays·원자적 쓰기 회귀 없음).
7. 걸러질 때 경고 로그가 남고, 그 로그에 파일 경로가 구조적 로깅(`{Path}`)으로 포함된다.

## 테스트 시나리오

파일: `tests/NetSamLite.Agent.Tests/Sending/PendingReportQueueTests.cs` (기존 `CreateReport` 헬퍼와 임시 디렉터리 패턴 재사용)

### 1. 정상 — 현재 포맷은 영향 없음

`ListPendingAsync_CurrentEnvelopeFormat_ReturnsReportWithFailureCount`

- `EnqueueAsync`로 리포트 1건을 넣고, `RecordFailureAsync`를 1번 호출해 `FailureCount=1`로 만든다.
- `ListPendingAsync` 결과가 1건이고 `Report.Device.DeviceId`와 `FailureCount=1`이 모두 보존된다.
- 의도: 가드 추가가 정상 경로를 건드리지 않았음을 확인.

### 2. 경계 — 구버전 포맷 / 내용 없는 JSON

`ListPendingAsync_LegacyFormatFileWithoutEnvelope_SkipsItButReturnsOtherValidReports`

- `EnqueueAsync`로 `"DEV-GOOD"` 1건을 넣는다.
- 큐 폴더에 `JsonSerializer.Serialize(CreateReport("DEV-LEGACY"))` 결과를 그대로 쓴 파일을 만든다(래퍼 없는 구버전 포맷). 파일명은 정렬상 먼저 오도록 `20200101T000000000Z_legacy.json`처럼 둬서, **앞에 있는 깨진 항목이 뒤의 정상 항목을 막지 않는지**까지 확인한다.
- `ListPendingAsync` 결과는 1건이고 `DeviceId == "DEV-GOOD"`.
- 같은 테스트(또는 `[Theory]` 데이터)로 `{}`, `{"FailureCount":2}` 케이스도 같이 검증한다 — 둘 다 걸러져야 한다.

> 참고: 이 테스트가 가드 **전**에는 실패하는지 확인하면 좋다. 가드가 없으면 결과가 2건이 되고 그중 하나의 `Report`가 null이다.

### 3. 예외 — 걸러진 파일의 실패 기록

`RecordFailureAsync_FileHasNoReportInEnvelope_ReturnsTrueAndRemovesFile`

- 큐 폴더에 `{}` 내용의 파일을 직접 만든다.
- `RecordFailureAsync(path, ...)` → `true` 반환, `File.Exists(path) == false`.
- 의도: 기존 `RecordFailureAsync_FileIsUnreadable_ReturnsTrueAndRemovesFile`와 동일한 보장을 "문법은 맞지만 비어 있는 파일"에도 적용. `true`를 돌려주는 게 중요한데, `false`면 `ReportDeliveryService.FlushQueueAsync`가 `return false`로 빠져 **이 파일 하나 때문에 큐 전체가 매 사이클 막힌다**(head-of-line blocking).
