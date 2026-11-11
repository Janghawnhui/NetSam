# 큐 항목의 `Id`를 `FilePath`로 이름 바꿔 "이건 파일 경로다"를 타입에 드러내기

- 작성일: 2026-11-12
- 대상 Phase: **Phase 6 (서비스화)**
- 담당 영역: `NetSamLite.Agent` / 재시도·큐
- 자동 개선 사이클: 3회차 (1회차 `docs/specs/2026-11-11-queue-envelope-null-guard.md`, 2회차 `docs/specs/2026-11-11-failed-folder-atomic-move.md`)
- 브랜치(권장): `auto/2026-11-12-pending-report-filepath`
  - 주의: 기존 고아 브랜치 `auto/20261111-pending-report-id-naming`은 **재사용하지도, 삭제하지도 않는다.** `docs/handoff.md` 「다음에 할 일」 2번대로 그 브랜치의 처분은 사람 판단 사항이다. 새 브랜치를 main에서 따서 작업한다.

---

## 배경

`docs/backlog.md` 「코드 품질 (Phase 6)」의 다음 항목을 구현한다.

> ### `PendingReportQueue`의 `Id`가 사실 파일 경로인데 이름이 추상적
> - **위치**: `src/NetSamLite.Agent/Sending/PendingReportQueue.cs` (`PendingReportEntry.Id`, `RemoveAsync`/`RecordFailureAsync`의 `id` 매개변수)
> - **현상**: `ListPendingAsync`가 반환하는 `Id`는 실제로는 큐 파일의 절대경로이고, `RemoveAsync`/`RecordFailureAsync`도 그 값을 그대로 `File.Exists`/`File.Delete`/`File.Move`에 쓴다. 호출부(`ReportDeliveryService`)가 "이게 파일 경로"라는 구현 세부사항에 암묵적으로 의존하게 됨.
> - **나중에 고려할 방향**: 필드명을 `Path`로 바꾸거나, XML 주석에 "이 Id는 파일 경로다"를 명시.
> - **기록일**: 2026-11-10 (Phase 6 code-reviewer 리뷰에서 🟡로 지적됨)

### 현재 코드 확인 — 이 항목이 이미 해결됐는지 판단

백로그가 제시한 두 방향 중 **"XML 주석에 명시"는 이미 되어 있다.**

- `src/NetSamLite.Agent/Sending/PendingReportQueue.cs:13`
  `/// <summary>ListPendingAsync가 반환하는 큐 항목 1건. Id는 파일 전체 경로다.</summary>`

그래서 "문서화" 쪽은 중복 작업이다. 그런데 **남아 있는 부분이 세 군데 있다.**

1. **메서드 매개변수 쪽에는 아무 설명이 없다.** `RemoveAsync(string id, ...)`(`:62`), `RecordFailureAsync(string id, ...)`(`:73`) 둘 다 XML 주석에 `id`가 무엇인지 없다. `RecordFailureAsync`에는 `<returns>`만 있다(`:72`). IntelliSense로 `RemoveAsync`를 호출하는 사람은 "어떤 id?"에 대한 답을 레코드 선언까지 따라가야만 얻는다.
2. **이름 자체는 그대로 `Id`다.** 백로그가 제시한 두 방향 중 더 강한 쪽(이름 변경)은 적용되지 않았다. `Id`라는 이름은 C#/EF 관례상 "불투명한 식별자(정수 PK, GUID)"를 강하게 암시하고, 이 프로젝트 안에도 실제로 그런 `Id`가 여러 개 있다(`LicenseEntity.Id`, `ProhibitedSoftwareEntity.Id`, `SoftwareChangeEventEntity.Id` — 전부 DB 정수 PK). 같은 솔루션에서 `Id`가 두 가지 전혀 다른 의미로 쓰이는 상황이다.
3. **그래서 호출부가 여전히 "암묵적 의존"이다.** `ReportDeliveryService.cs:40`과 `:44`는 `entry.Id`를 그대로 넘기지만, 그 값이 경로라는 사실은 코드에 나타나지 않는다. 테스트는 이미 이 사실에 의존하고 있다 — `PendingReportQueueTests.cs:187`의 `Path.GetFileName(entry.Id)`, `:247`의 `File.SetLastWriteTimeUtc(entry.Id, ...)`. 즉 **"Id는 사실 경로"라는 지식이 이미 코드 전체에 퍼져 있는데 이름만 그걸 숨기고 있다.**

**결론: 항목은 "주석만 선반영된 채 미해결" 상태다.** 이번 사이클은 남은 쪽, 즉 **이름 변경 + 매개변수 문서화**를 처리한다.

### 왜 이번 사이클에 이 항목인가 (= 세 후보 중 가장 작고 안전한 이유)

`.claude/loop.md` 3번("가장 작고 안전한 항목 1개")을 기준으로 `docs/handoff.md`가 권장한 Phase 6 후보 3개를 비교했다.

| 후보 | 런타임 동작 변경 | 검증 가능성 | 판단 |
|---|---|---|---|
| **① `PendingReportEntry.Id` 네이밍** | **없음(0)** — 식별자 이름만 바뀐다. IL 수준에서 흐름이 동일하다. | **컴파일러가 전수 검증**한다. 놓친 호출부가 있으면 빌드가 깨진다. 기존 Agent 테스트 83개가 그대로 회귀망이 된다. | **채택** |
| ② `AgentOptions` 중복 구성 (`Program.cs`) | 있음 — DI/Serilog 초기화 순서를 건드린다 | **낮음.** `Program.cs`의 `RunAsService`는 top-level + `Host.CreateApplicationBuilder`라 현재 테스트가 하나도 없다. "두 경로로 구성"은 Serilog 설정이 `Build()` 전에 값을 필요로 해서 생기는 구조적 제약이라, 깔끔히 1회 바인딩으로 합치기 어렵고 어설프게 손대면 "로그가 안 남는다" 같은 조용한 회귀가 난다 | 보류 |
| ③ Serilog `retainedFileCountLimit` 전제 | 주석만 달면 없음 / `retainedFileTimeLimit`로 교체하면 있음 | **낮음.** ②와 같은 `Program.cs`, 역시 테스트 없음. 교체하면 패키지 버전 확인까지 필요하고, 효과("로그가 N일 뒤 지워진다")는 테스트로 확인할 수 없다 | 보류 |

- ①은 **런타임 동작 변경이 0이라 가장 안전하다.** 2회차(`File.Move` 원자화)처럼 파일 시스템 동작을 바꾸는 작업이 아니다.
- ②③은 둘 다 `Program.cs` 한 파일에 있어 **다음 사이클에 묶어서** 처리하는 편이 효율적이다(같은 파일을 두 사이클 연속 건드리지 않음).
- ①은 DB 스키마·HTTP 계약·`AgentOptions`·설정 파일·`appsettings.json`을 전혀 건드리지 않는다. 「절대 규칙 6」(현재 Phase 밖 금지)에도 걸리지 않는다 — Phase 6의 "재시도·로그" 범위 안이다.

## 목표

- `PendingReportQueue`의 공개 API만 보고도 **"이 문자열은 큐 파일의 경로다"가 즉시 드러난다.** 레코드 선언까지 추적하지 않아도 된다.
- 호출부(`ReportDeliveryService`)가 의존하는 사실이 **이름으로 명시**되어, 나중에 큐 저장 방식을 바꿀 때(예: SQLite 큐) "무엇이 깨지는지"가 컴파일 에러로 드러난다.
- 같은 솔루션 안에서 `Id`(= DB 정수 PK)와 큐 파일 경로가 **같은 이름을 공유하지 않는다.**
- **런타임 동작은 한 비트도 바뀌지 않는다.**

## 범위

### 할 것

1. **`PendingReportEntry.Id` → `FilePath`로 이름 변경** (`PendingReportQueue.cs:14`).
   ```
   public sealed record PendingReportEntry(string FilePath, InventoryReport Report, int FailureCount);
   ```
   - **왜 백로그가 적은 `Path`가 아니라 `FilePath`인가**: 백로그의 "필드명을 `Path`로 바꾸거나"라는 제안과 **같은 취지의 변경**이지만, 이 파일과 테스트 파일은 `System.IO.Path`를 매우 자주 쓴다(`Path.Combine`, `Path.GetFileName`, `Path.GetTempPath`). 멤버 접근(`entry.Path`)은 문법상 모호하지 않아 컴파일은 되지만, 같은 화면에 `Path.Combine(...)`과 `entry.Path`가 섞여 읽는 사람이 한 번 멈춘다. `FilePath`는 그 혼동이 없고 "파일"이라는 정보를 한 단어 더 준다.
   - 만약 developer/code-reviewer가 백로그 문구 그대로 `Path`를 선호하면 **한 단어 차이이므로 그쪽으로 가도 된다.** 다만 이름을 바꿀 때는 아래 모든 호출부에 동일하게 적용한다.
2. **레코드 XML 주석을 새 이름에 맞게 갱신** (`:13`). 지금 문장이 "Id는 파일 전체 경로다"이므로 `FilePath`를 가리키도록 고치고, **"이 값을 그대로 `RemoveAsync`/`RecordFailureAsync`에 넘긴다"**는 한 줄을 덧붙인다(「절대 규칙 9」 — 왜 경로를 노출하는 설계인지 읽는 사람이 알게).
3. **`RemoveAsync`/`RecordFailureAsync`의 매개변수 이름 `id` → `filePath`** (`:62`, `:73`), 메서드 본문 안의 해당 참조도 전부 함께(`:64`, `:66`, `:75`, `:79`, `:88`, `:93`, `:98`).
   - 특히 `:93`의 `Path.Combine(FailedDirectory, Path.GetFileName(id))`와 `:98`의 `File.Move(id, failedPath, overwrite: true)`는 **"매개변수가 경로다"라는 전제 자체**이므로 이름이 바뀌면 코드가 자기 설명이 된다.
4. **두 메서드에 `<param>` XML 주석 추가**. 내용은 "`ListPendingAsync`가 반환한 `PendingReportEntry.FilePath`를 그대로 넘긴다" 수준의 한 줄. `RecordFailureAsync`의 기존 `<returns>` 문장은 **그대로 유지**한다.
5. **호출부 갱신**: `ReportDeliveryService.cs:40`, `:44`의 `entry.Id` → `entry.FilePath`. (위치 기반 생성 `new PendingReportEntry(path, ...)`(`:55`)는 위치 인자라 **수정 불필요**하다.)
6. **테스트의 식별자 갱신**(동작 변경 없음, 기계적):
   - `tests/NetSamLite.Agent.Tests/Sending/PendingReportQueueTests.cs` — `entry.Id`/`pendingBefore[0].Id` 12곳(`:62`, `:103`, `:104`, `:127`, `:162`, `:163`, `:187`, `:190`, `:247`, `:252`, `:268`, `:296`).
   - `tests/NetSamLite.Agent.Tests/Sending/ReportDeliveryServiceTests.cs` — `badEntry.Id` 1곳(`:157`).
   - 테스트 **이름** 중 `Id`를 의미로 쓰는 1개만 함께 고친다: `RecordFailureAsync_IdFileDoesNotExist_ReturnsTrueAndCreatesNothingInFailedFolder`(`:202`) → `RecordFailureAsync_QueueFileDoesNotExist_ReturnsTrueAndCreatesNothingInFailedFolder`. 나머지 테스트 이름은 건드리지 않는다.
7. **새 계약을 못박는 테스트 3개 추가**(아래 「테스트 시나리오」). 이름 변경만으로는 "`FilePath`가 정말 실존하는 큐 파일의 경로"라는 계약이 테스트로 고정되지 않으므로, 이번에 명시적으로 만든다. 이게 이번 작업이 단순 이름 치환을 넘어 남기는 가치다.

### 안 할 것

- **`PendingReportQueue`의 공개 API 형태(시그니처 구조)를 바꾸지 않는다.** 즉 `RemoveAsync(PendingReportEntry entry)`처럼 **레코드 전체를 넘기도록 바꾸는 캡슐화 개선은 하지 않는다.** 그게 "호출부가 경로를 몰라도 되게" 하는 더 강한 해법이긴 하지만, 백로그가 제시한 방향(이름 변경/주석)을 넘어서는 설계 변경이고 `.claude/loop.md` 「절대 금지」의 "backlog에 없는 새 기능 구현 금지"에 가깝다. 필요하다고 판단되면 **별도 백로그 항목으로 제안만** 한다.
- **큐의 저장 방식을 바꾸지 않는다** — 파일 기반 유지. SQLite 큐, 인덱스 파일, 불투명 핸들 도입 전부 범위 밖.
- **`AgentOptions`에 설정값을 추가하지 않는다.**
- **`Program.cs`를 건드리지 않는다** — 후보 ②(`AgentOptions` 중복 구성)와 ③(Serilog 보관 전제)은 각각 다음 사이클이다.
- **`QueuedReportEnvelope`(internal) 쪽 이름은 건드리지 않는다** — `FailureCount`/`Report` 모두 이번 변경과 무관하다.
- **기존 테스트의 단정(assert)이나 시나리오를 바꾸지 않는다.** 식별자 치환 + 테스트 이름 1개 외에 기존 테스트에 손을 대야 하는 상황이 생기면, 그건 **의도치 않은 동작 변경이라는 신호**이므로 먼저 이 기획서로 돌아와 상의한다.
- **`src/NetSamLite.Server`/`NetSamLite.Web`의 `Id`(DB 정수 PK)는 전혀 건드리지 않는다.** 이번 변경은 `NetSamLite.Agent`의 큐 레코드 1개에 한정된다.

## 수정할 파일

| 파일 | 할 일 |
|---|---|
| `src/NetSamLite.Agent/Sending/PendingReportQueue.cs` | 「할 것」 1~4번. 레코드 속성명, 두 메서드의 매개변수명과 본문 참조, XML 주석. **`EnqueueAsync`/`ListPendingAsync`/`PruneExpired`/`EnforceMaxQueuedReports`/`ReadEnvelopeAsync`/`WriteEnvelopeAsync`/`RemoveQuietly`의 로직과 `RecordFailureAsync`의 제어 흐름(실패 횟수 증가, `Math.Max(1, ...)` 방어, dead-letter 분기, 반환값 의미)은 한 줄도 바꾸지 않는다.** |
| `src/NetSamLite.Agent/Sending/ReportDeliveryService.cs` | 「할 것」 5번. `entry.Id` → `entry.FilePath` 2곳(`:40`, `:44`)**만**. 전송 순서 로직·로그 메시지·주석은 그대로. |
| `tests/NetSamLite.Agent.Tests/Sending/PendingReportQueueTests.cs` | 「할 것」 6번(식별자 12곳 + 테스트 이름 1개) + 7번(신규 테스트 3개 추가). |
| `tests/NetSamLite.Agent.Tests/Sending/ReportDeliveryServiceTests.cs` | 「할 것」 6번. `badEntry.Id` → `badEntry.FilePath` 1곳(`:157`)**만**. |
| `docs/backlog.md` | **사이클이 빌드·테스트·리뷰를 모두 통과한 뒤에만** 「코드 품질 (Phase 6)」의 "`PendingReportQueue`의 `Id`가 사실 파일 경로인데 이름이 추상적" 항목을 「해결됨」 섹션으로 옮기고, **해결일 `2026-11-12`**, **브랜치명 `auto/2026-11-12-pending-report-filepath`**, **기획서 경로 `docs/specs/2026-11-12-pending-report-filepath.md`**, 한 줄 「해결 방법」을 1·2회차 항목과 같은 형식으로 함께 적는다. (「해결 방법」 예: "`PendingReportEntry.Id`를 `FilePath`로, `RemoveAsync`/`RecordFailureAsync`의 매개변수 `id`를 `filePath`로 이름 바꾸고 `<param>` 주석을 추가해 '이 문자열은 큐 파일 경로'라는 계약을 타입에 드러냄. 런타임 동작 변경 없음.") |
| `docs/auto-log.md` | `.claude/loop.md` 7번대로 이번 사이클 기록 추가(시각, 브랜치, 처리 항목, 변경 파일, 테스트 수, 리뷰 결과, "내가 배울 점" 3줄). |

소스 2개 + 테스트 2개 + 문서 2개 = **6개 파일**이다. 「절대 규칙 7」(파일 3개 이상이면 먼저 승인)에 따라 **이 기획서를 사전 승인 문서로 삼는다.** 위 목록 밖의 파일을 건드려야 하는 상황이 생기면 작업을 멈추고 먼저 질문한다.

### 절대 규칙 점검

| 규칙 | 해당 여부 |
|---|---|
| 1 (회사 자산) | 해당 없음. 자체 코드의 식별자 이름 변경만 |
| 2 (에이전트 읽기 전용) | 레지스트리·WMI 코드 미변경. 파일 조작 코드의 **동작**도 미변경(이름만 바뀜) |
| 3 (개인정보) | 수집 항목 변경 없음. 로그 템플릿(`{Path}`, `{FailureCount}`)과 출력 내용도 그대로 |
| 4 (비밀값) | `appsettings.json`·설정 파일 미변경 |
| 5 (`Win32_Product`) | 해당 없음 |
| 6 (현재 Phase) | Phase 6(서비스화 — 재시도·큐·로그) 범위 안 |

## 완료 기준

`dotnet build`(신규 경고 0개)와 `dotnet test`(Agent 83 + 신규 3 = 86, Server 167, 전체 통과) 외에:

1. `src/NetSamLite.Agent` 전체에서 `PendingReportEntry`의 속성을 `.Id`로 접근하는 코드가 **0곳**이다. (`grep -rn "entry\.Id" src/NetSamLite.Agent` → 결과 없음)
2. `tests/NetSamLite.Agent.Tests` 전체에서도 큐 항목을 `.Id`로 접근하는 코드가 **0곳**이다.
3. `PendingReportEntry` 선언에 `FilePath`가 있고, XML 주석이 **새 이름**을 가리킨다("Id는 ..."이라는 낡은 문장이 남아 있지 않다).
4. `RemoveAsync`와 `RecordFailureAsync` **둘 다** 매개변수명이 `filePath`이고, `<param>` XML 주석이 있다. `RecordFailureAsync`의 기존 `<returns>` 문장은 그대로 남아 있다.
5. **`git diff`에 동작 변경이 전혀 없다** — 변경 내용이 (a) 식별자 이름, (b) XML/일반 주석 문구, (c) 테스트 이름 1개, (d) 신규 테스트 3개뿐이다. 제어 흐름 변경(`if`/`for`/`return`), 메서드 추가·삭제, 호출 순서 변경, 로그 메시지 문자열 변경이 **하나도 없다.** (code-reviewer가 명시적으로 확인할 항목)
6. 기존 Agent 테스트 **전부**(`PendingReportQueueTests` 15개, `ReportDeliveryServiceTests` 등 포함 83개)가 **식별자 치환과 테스트 이름 1개 외의 수정 없이** 통과한다 — 순서 보장, `MaxQueuedReports`, `QueueRetentionDays`, `failed/` 정리, `Report=null` 가드, dead-letter 원자적 이동(2회차 결과)에 회귀가 없다.
7. 신규 테스트 3개가 **이름 변경 전 코드에서는 애초에 컴파일되지 않는다**(= 새 이름을 실제로 검증한다).
8. `dotnet build` 출력에 `System.IO.Path`와의 이름 충돌 관련 경고·에러가 없다(`FilePath`를 쓰면 애초에 발생할 수 없지만, 만약 `Path`로 가기로 결정했다면 이 항목을 반드시 확인한다).

## 테스트 시나리오

파일: `tests/NetSamLite.Agent.Tests/Sending/PendingReportQueueTests.cs`
기존 `CreateReport` 헬퍼와 테스트별 임시 디렉터리(`_queueDirectory`) 패턴을 그대로 재사용한다. 새 섹션 주석(`// ----- FilePath 계약 -----`)을 두고 그 아래 모은다.

### 1. 정상 — `FilePath`가 큐 폴더 안에 실제로 존재하는 파일을 가리킨다

`ListPendingAsync_ReturnedEntry_FilePathPointsToExistingFileInQueueDirectory`

- `EnqueueAsync(CreateReport("DEV-1"))` 후 `ListPendingAsync`로 항목 1건을 얻는다.
- 검증:
  - `File.Exists(entry.FilePath)`가 `true`.
  - `Path.GetDirectoryName(entry.FilePath)`가 `_queueDirectory`와 같다(= 경로가 큐 폴더 직하를 가리킨다).
  - `Path.GetExtension(entry.FilePath)`가 `".json"`.
- 의도: 지금까지 **암묵적**이었던 "반환값은 실존하는 큐 파일의 경로"라는 계약을 테스트로 고정한다. 나중에 누가 `FilePath`에 파일명만 넣거나 상대경로를 넣으면 여기서 깨진다.

### 2. 경계 — dead-letter로 옮겨진 뒤에는 `FilePath`로 더 이상 조회되지 않는다

`ListPendingAsync_AfterEntryMovedToFailedFolder_NoEntryFilePathPointsIntoFailedFolder`

- `MaxQueueSendFailures = 1`로 큐를 만들고(경계값 1 — 첫 실패에 바로 dead-letter) `EnqueueAsync(CreateReport("DEV-DEAD"))`.
- `ListPendingAsync`로 얻은 `entry.FilePath`를 보관해 둔 뒤 `RecordFailureAsync(entry.FilePath, ...)` 호출.
- 검증:
  - 보관해 둔 원래 `FilePath`의 파일이 더 이상 존재하지 않는다(`File.Exists` → `false`) — `File.Move`로 옮겨졌으므로.
  - `ListPendingAsync()`가 비어 있다 → **`failed/` 안의 파일 경로가 `FilePath`로 새어 나오지 않는다.**
- 의도: `FilePath`의 유효 범위가 "`QueueDirectory` 직하, 그리고 그 항목이 아직 큐에 있는 동안"임을 못박는다. 이름이 구체적이 될수록 "그 경로를 호출부가 저장해 뒀다가 나중에 쓰면?"이라는 오해가 생길 수 있는데, 그 수명을 테스트로 명시한다.

### 3. 예외 — 이미 사라진 `FilePath`로 `RemoveAsync`를 불러도 예외가 안 난다

`RemoveAsync_FilePathDoesNotExist_DoesNotThrow`

- 큐 폴더만 만들고(`Directory.CreateDirectory(_queueDirectory)`), 존재하지 않는 경로를 직접 조립한다(예: `Path.Combine(_queueDirectory, "20200101T000000000Z_gone.json")`).
- `await _sut.RemoveAsync(missingPath, CancellationToken.None)` 호출.
- 검증: 예외가 발생하지 않는다(FluentAssertions의 `await act.Should().NotThrowAsync()` 또는 단순 호출 후 큐가 비어 있음 확인).
- 의도: `RemoveAsync`의 `File.Exists` 가드(`:64`)가 의도된 방어임을 고정한다. 실제로 가능한 시나리오다 — `PruneExpired`가 보관 기간 만료로 파일을 지운 직후 `ReportDeliveryService`가 같은 경로로 `RemoveAsync`를 부를 수 있다. 이 가드가 사라지면 전송 사이클 전체가 `FileNotFoundException`으로 죽는다. 기존 테스트에는 이 경로를 직접 검증하는 것이 없었다(`RecordFailureAsync` 쪽에만 있었다).
