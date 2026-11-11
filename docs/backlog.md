# 백로그 — 지금은 안 고치고 메모만

## 정확성

### `LicenseService`/`LicenseMatchingService`도 ProhibitedSoftware와 같은 종류의 공백 불일치 버그를 가질 수 있음
- **위치**: `src/NetSamLite.Server/Licensing/LicenseService.cs`, `src/NetSamLite.Server/Licensing/LicenseMatchingService.cs`
- **현상**: 라이선스 매칭도 "정규화된 ProductName(SoftwareNormalizer 출력)" vs "관리자가 `LicenseService`로 입력한 원본 `License.ProductName`"을 비교하는 구조인데, `LicenseMatchingService.cs`의 `GroupBy(l => l.ProductName, StringComparer.OrdinalIgnoreCase)`는 대소문자만 흡수하고 앞뒤 공백/연속 공백은 못 흡수한다. 관리자가 `" Adobe Acrobat "`이나 `"Adobe  Acrobat"`(연속 공백)으로 라이선스를 등록하면 정규화 엔진이 출력하는 깨끗한 `"Adobe Acrobat"`과 영원히 매칭이 안 돼 "라이선스 미등록"으로 잘못 표시될 수 있음 — Phase 5에서 `ProhibitedSoftwareAlertService`에 있던 것과 같은 종류의 버그.
- **지금 안 고치는 이유**: License는 유니크 인덱스가 없어(의도적으로 여러 건 등록 허용) `ProhibitedSoftware`처럼 "DB 제약 추가"로 강제할 수 없고, 범위가 더 넓음(CRUD + 매칭 + CSV 리포트 전부 영향).
- **나중에 고려할 방향**: `ProhibitedSoftwareEntity.ComputeKey` 같은 공용 정규화 키 함수를 Shared나 Server 공통 위치로 빼서 `LicenseService.CreateAsync`/`UpdateAsync`에서 입력 시점에 Trim+공백축약을 적용하거나, `LicenseMatchingService`의 비교 쪽에서 같은 정규화를 적용.
- **기록일**: 2026-11-10 (Phase 5 "금지 SW 공백 불일치" 버그 수정 후 code-reviewer가 유사 패턴으로 지적)

## 코드 품질 (Phase 6)

### `failed/`로 옮기는 과정이 원자적이지 않음(쓰기+삭제 2단계)
- **위치**: `src/NetSamLite.Agent/Sending/PendingReportQueue.cs` (`RecordFailureAsync`)
- **현상**: `MaxQueueSendFailures`에 도달한 리포트를 `failed/`로 옮길 때 "새 위치에 쓰기(`WriteEnvelopeAsync`) → 원본 삭제(`RemoveQuietly`)" 순서로 진행된다. 그 사이 프로세스가 죽으면 같은 리포트가 `queue/`와 `failed/` 양쪽에 동시에 남을 수 있음(유실은 아니고 재시작 후 큐 쪽 사본이 한 번 더 전송 시도되는 중복 가능성만).
- **해결 방법**: 같은 볼륨 안에서는 `File.Move`가 원자적이므로, "쓰기+삭제" 대신 `File.Move(id, failedPath)` 한 번으로 바꾸면 이 틈 자체가 없어짐 — FailureCount를 갱신한 새 내용을 써야 하니, temp 파일에 갱신된 내용을 쓴 뒤 그 temp 파일을 바로 `failedPath`로 `File.Move`하면 됨(원본 `id` 파일은 그 과정에서 안 건드리고 마지막에 한 번만 지우는 대신, 처음부터 `id` 위치가 아니라 temp 위치에만 쓰고 거기서 곧장 `failedPath`로 이동).
- **기록일**: 2026-11-10 (Phase 6 code-reviewer 리뷰에서 🟡로 지적됨)

### `PendingReportQueue`의 `Id`가 사실 파일 경로인데 이름이 추상적
- **위치**: `src/NetSamLite.Agent/Sending/PendingReportQueue.cs` (`PendingReportEntry.Id`, `RemoveAsync`/`RecordFailureAsync`의 `id` 매개변수)
- **현상**: `ListPendingAsync`가 반환하는 `Id`는 실제로는 큐 파일의 절대경로이고, `RemoveAsync`/`RecordFailureAsync`도 그 값을 그대로 `File.Exists`/`File.Delete`/`File.Move`에 쓴다. 호출부(`ReportDeliveryService`)가 "이게 파일 경로"라는 구현 세부사항에 암묵적으로 의존하게 됨.
- **나중에 고려할 방향**: 필드명을 `Path`로 바꾸거나, XML 주석에 "이 Id는 파일 경로다"를 명시.
- **기록일**: 2026-11-10 (Phase 6 code-reviewer 리뷰에서 🟡로 지적됨)

### `AgentOptions`가 두 경로로 중복 구성됨
- **위치**: `src/NetSamLite.Agent/Program.cs` (`RunAsService`)
- **현상**: Serilog/HttpClient 설정에 쓸 `AgentOptions`를 `builder.Configuration.GetSection(...).Get<AgentOptions>()`로 한 번 수동 바인딩하고, DI의 `IOptions<AgentOptions>`용으로 `builder.Services.Configure<AgentOptions>(...)`를 또 호출한다. 지금은 같은 설정 소스(appsettings.json의 "Agent" 섹션)라 동작은 일치하지만, 둘 중 하나만 건드리면 드리프트가 생기기 쉬운 구조.
- **나중에 고려할 방향**: Host 빌드 전에 `IOptions<AgentOptions>`를 한 번만 바인딩해서 양쪽에서 재사용하도록 통일.
- **기록일**: 2026-11-10 (Phase 6 code-reviewer 리뷰에서 🟡로 지적됨)

### Serilog `retainedFileCountLimit`은 "파일 개수" 기준이라 "보관 일수" 의도와 암묵적 전제가 있음
- **위치**: `src/NetSamLite.Agent/Program.cs` (`RunAsService`의 `LoggerConfiguration`), `src/NetSamLite.Agent/AgentOptions.cs`의 `LogRetentionDays` 주석
- **현상**: `rollingInterval: Day` + `retainedFileCountLimit: LogRetentionDays`는 "하루에 로그 파일이 정확히 1개"라는 전제에서만 "N일 보관"과 "최근 N개 파일 보관"이 일치한다. 나중에 `rollOnFileSizeLimit` 등을 추가해 하루에 여러 파일이 생기게 되면 실제 보관 기간이 의도(며칠)보다 짧아진다.
- **나중에 고려할 방향**: 주석에 이 전제를 명시, 또는 Serilog의 날짜 기반 정리 로직(`Serilog.Sinks.File`의 `retainedFileTimeLimit`, 최신 버전에서 지원)으로 교체.
- **기록일**: 2026-11-10 (Phase 6 code-reviewer 리뷰에서 🟡로 지적됨)

## 성능 개선

### `LicenseMatchingService`가 리포트 호출마다 전체 재정규화함
- **위치**: `src/NetSamLite.Server/Licensing/LicenseMatchingService.cs`
- **현상**: `GET /api/v1/licensing/report`를 호출할 때마다 모든 디바이스의 `InstalledSoftwareSnapshot`을 `SoftwareNormalizer`로 처음부터 다시 정규화한다. O(디바이스 수 × 설치 SW 수 × 정규화 규칙 수) 규모 연산이 매 호출마다 반복됨.
- **지금 안 고치는 이유**: 정규화 결과를 저장하는 테이블이 없어서(Phase 3가 순수 인메모리 엔진) 매번 다시 계산하는 게 "정규화 규칙이 바뀌어도 항상 최신 규칙으로 판정"이라는 정확성을 보장하는 가장 단순한 방법이었음. 지금 데이터량(디바이스 수백 대 이하)에서는 체감 영향 없음.
- **나중에 고려할 방향**: Phase 5(대시보드)에서 리포트를 자주 조회하게 되면 ① 정규화 결과 캐싱(규칙 버전/해시가 바뀔 때만 무효화) ② `SoftwareChangeEventEntity` 기반 증분 재계산 중 하나를 검토.
- **기록일**: 2026-11-10 (Phase 4 code-reviewer 리뷰에서 🟡로 지적됨)

### Server/Web이 같은 SQLite 파일에 동시 쓰기 — 잠금 경합 가능성
- **위치**: `src/NetSamLite.Server/Program.cs`, `src/NetSamLite.Web/Program.cs` (둘 다 `AddDbContext<NetSamLiteDbContext>(UseSqlite(connectionString))`로 같은 `netsamlite.db` 파일을 각자 엶)
- **현상**: WAL 모드나 `busy_timeout` 조정이 없어 기본 rollback-journal 모드로 동작. Agent가 `POST /inventory`로 쓰는 동시에 Web에서 라이선스/금지SW CRUD를 하면 `SQLITE_BUSY`(database is locked) 예외가 간헐적으로 날 수 있음.
- **최초 마이그레이션 레이스(더 심각한 변형)**: Server와 Web 둘 다 시작 시 `Database.Migrate()`를 호출한다. 한 번도 마이그레이션된 적 없는 빈 DB에서 두 프로세스를 동시에 띄우면, SQLite에는 EF Core 마이그레이션용 분산 락(SQL Server의 `sp_getapplock`류)이 없어서 한쪽이 `SQLITE_BUSY`로 **부팅 자체가 실패**할 수 있다. 마이그레이션이 이미 한 번 적용된 뒤에는(펜딩 마이그레이션 없음) 위험 없음 — "최초 동시 기동" 시나리오에 한정.
- **지금 안 고치는 이유**: 단일 사용자 학습 환경이라 동시 쓰기/동시 최초 기동 빈도가 낮아 체감 확률이 낮음.
- **나중에 고려할 방향**: WAL 모드 + busy_timeout 상향, 마이그레이션 적용을 Server(또는 별도 초기화 스크립트) 하나만 담당하게 하고 Web은 재시도(지수 백오프)로 대기, 또는 Web이 Server에 HTTP로만 쓰기 요청을 위임하도록 재설계.
- **기록일**: 2026-11-10 (Phase 5 code-reviewer 리뷰에서 🟡로 지적됨, 최초 마이그레이션 레이스는 다음 리뷰에서 추가 지적)

### `NetSamLite.Web`의 Blazor 페이지가 전부 `CancellationToken.None`을 하드코딩
- **위치**: `src/NetSamLite.Web/Components/Pages/*.razor` (Devices, DeviceDetail, SoftwareInventory, Licensing, ProhibitedSoftware 전부)
- **현상**: CLAUDE.md 컨벤션("CancellationToken 전달")과 어긋남. Blazor 컴포넌트 생명주기에 기본 제공되는 CT가 없어서 완전히 피하긴 어려움.
- **나중에 고려할 방향**: 컴포넌트에 `CancellationTokenSource`를 두고 `OnInitializedAsync`/`Dispose`에서 연계하는 패턴 검토.
- **기록일**: 2026-11-10 (Phase 5 code-reviewer 리뷰에서 🟡로 지적됨)

### `NetSamLite.Server`에 전역 예외 처리 미들웨어가 없음
- **위치**: `src/NetSamLite.Server/Program.cs`
- **현상**: Web에는 `UseExceptionHandler`/`UseHsts`가 있는데 Server에는 없어서, 예기치 못한 예외가 그대로 기본 500으로 처리되고 Development 환경이면 스택트레이스까지 노출될 수 있음.
- **나중에 고려할 방향**: `app.Environment.IsDevelopment()` 분기로 운영 환경에서 스택트레이스 노출을 막는 처리 추가.
- **기록일**: 2026-11-10 (Phase 5 code-reviewer 리뷰에서 🟡로 지적됨)

### `ProhibitedSoftwareAlertService`에 `LicenseMatchReport.FailedCount` 같은 실패 건수 노출이 없음
- **위치**: `src/NetSamLite.Server/Policy/ProhibitedSoftwareAlertService.cs`, `Policy/ProhibitedSoftwareAlert.cs`
- **현상**: `LicenseMatchingService`와 동일한 항목 단위 try/catch로 정규화 실패를 건너뛰지만, `LicenseMatchReport.FailedCount`에 대응하는 필드가 없어 실패가 조용히 누락됨. "알림 없음 = 안전"으로 오인될 위험.
- **나중에 고려할 방향**: 알림 응답에도 실패 건수를 노출.
- **기록일**: 2026-11-10 (Phase 5 code-reviewer 리뷰에서 🟡로 지적됨)

## 코드 품질

### POST/PUT `/api/v1/licenses`의 유효성 검증 코드 중복
- **위치**: `src/NetSamLite.Server/Program.cs` (POST/PUT 핸들러 각각에 동일한 검증 블록)
- **현상**: `ProductName`/`LicenseType`/`Quantity` 검증 4줄이 두 핸들러에 그대로 복사돼 있어, 한쪽만 수정되면 드리프트가 생길 수 있음.
- **나중에 고려할 방향**: `static bool IsValidLicenseRequest(LicenseRequest r)` 같은 로컬 함수/헬퍼로 추출.
- **기록일**: 2026-11-10 (Phase 4 code-reviewer 리뷰에서 🟡로 지적됨)

### 테스트에서 `ProductName` 경계 케이스(공백만, 매우 긴 문자열)가 비대칭적으로 부족
- **위치**: `tests/NetSamLite.Server.Tests/Licensing/LicenseEndpointsTests.cs`
- **현상**: `LicenseType`은 빈 값/공백/null 3케이스를 다 테스트하는데 `ProductName`은 빈 값만 테스트됨. 실제 버그는 아님(`IsNullOrWhiteSpace`가 이미 공백을 걸러내고, 길이 제한이 없어 긴 문자열도 SQLite에서는 그냥 저장됨) — "길이 제한 없음"이 의도된 설계라는 걸 명시적으로 검증하는 테스트가 없을 뿐.
- **나중에 고려할 방향**: 위 길이 제약 항목과 같이 처리할 때 테스트도 함께 추가.
- **기록일**: 2026-11-10 (Phase 4 code-reviewer 리뷰에서 🟡로 지적됨)

### `LicenseMatchingService`가 스냅샷 전체가 정규화 실패하는 극단 케이스를 테스트하지 않음
- **위치**: `tests/NetSamLite.Server.Tests/Licensing/LicenseMatchingServiceNormalizationFailureTests.cs`
- **현상**: 정상 1건 + 실패 1건 혼합 시나리오만 테스트돼 있음. 코드 추적상 전체가 실패해도 `matched`/`unlicensedProducts`/`excludedFromLicensing`/`unclassifiedSoftware`가 모두 빈 리스트로 안전하게 반환되는 것으로 확인됐으나(🟢), 이 경로를 직접 검증하는 회귀 테스트는 없음.
- **나중에 고려할 방향**: 전원 실패 케이스 테스트 1개 추가.
- **기록일**: 2026-11-10 (Phase 4 code-reviewer 리뷰에서 🟡로 지적됨)

## SQL Server 전환 시 필수

### `LicenseEntity.ProductName`/`LicenseType`에 컬럼 길이 제약이 없음
- **위치**: `src/NetSamLite.Server/Data/Entities/LicenseEntity.cs`, `src/NetSamLite.Server/Data/NetSamLiteDbContext.cs`, 관련 마이그레이션
- **현상**: `MaxLength`/`HasMaxLength` 지정이 전혀 없어 SQLite에서는 `TEXT`(길이 무제한)로 생성됨. `ProductName`에는 인덱스(`HasIndex`)가 걸려 있음.
- **왜 지금은 괜찮은가**: SQLite는 컬럼 길이 제약이 없어 문제가 안 됨.
- **왜 SQL Server 전환 시 반드시 고쳐야 하는가**: SQL Server로 옮기면 길이 미지정 `string` 컬럼이 `nvarchar(max)`로 매핑되는데, `nvarchar(max)` 컬럼에는 일반 B-tree 인덱스를 걸 수 없어 `HasIndex(l => l.ProductName)`가 마이그레이션/인덱스 생성 단계에서 그대로 깨진다. **SQLite → SQL Server 전환 작업을 시작하기 전에 반드시** `ProductName`/`LicenseType`에 적정 길이(예: 200자)를 지정해야 함.
- **기록일**: 2026-11-10 (Phase 4 code-reviewer 리뷰에서 🟡로 지적됨)

## 해결됨

### 구버전 큐 파일 포맷을 읽을 때 조용히 null Report로 역직렬화됨
- **위치**: `src/NetSamLite.Agent/Sending/PendingReportQueue.cs` (`ReadEnvelopeAsync`)
- **현상**: 큐 파일을 `InventoryReport`에서 `QueuedReportEnvelope(FailureCount, Report)` 래퍼로 바꿨는데, 구버전 포맷(래퍼 없이 `InventoryReport`를 바로 직렬화한 파일)을 `JsonSerializer.Deserialize<QueuedReportEnvelope>`로 읽으면 System.Text.Json이 예외 없이 `Report=null`인 객체를 만들어낸다. 이게 "깨진 파일" 경로로 안 빠지고 정상 항목처럼 흘러가 `HttpInventorySender`가 null 바디를 서버에 POST하게 됨 — 결국 `MaxQueueSendFailures`로 dead-letter 되긴 하지만 그 전까지 쓸모없는 재시도가 몇 번 낭비됨.
- **기록일**: 2026-11-10 (Phase 6 code-reviewer 리뷰에서 🟡로 지적됨)
- **해결일**: 2026-11-11
- **브랜치**: `auto/2026-11-11-queue-envelope-null-guard`
- **기획서**: `docs/specs/2026-11-11-queue-envelope-null-guard.md`
- **해결 방법**: `QueuedReportEnvelope.Report`를 `InventoryReport?`로 바꾸고, `ReadEnvelopeAsync`에서 역직렬화 후 `Report`가 null이면 "읽을 수 없는 파일"과 동일하게 경고 로그를 남기고 건너뛰도록 가드를 추가함.
