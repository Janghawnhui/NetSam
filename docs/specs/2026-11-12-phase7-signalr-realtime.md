# Phase 7 — SignalR 실시간 채널 (에이전트↔서버 상시 연결 · 하트비트 · "지금 수집")

- 작성일: 2026-11-12
- 갱신 1: 2026-11-12 — **열린 질문 4개와 Hub 계약이 모두 확정됨**(아래 「확정된 결정」). 더 이상 선택지가 남아 있지 않다.
- 갱신 2: 2026-11-12 — 7-A 구현 중 developer 질의로 **`tests/NetSamLite.Server.Tests/NetSamLite.Server.Tests.csproj`에 테스트 전용 패키지 1개 추가**를 「수정할 파일」에 반영하고, 시나리오 B의 "장치 사칭 불가" 테스트 범위를 확정했다(아래 「시나리오 B」).
- 대상 Phase: **Phase 7 (SignalR 실시간 채널)** — 신규 Phase 착수 작업
- 담당 영역: `NetSamLite.Shared` (계약) / `NetSamLite.Server` (Hub·인증·연결 추적) / `NetSamLite.Agent` (연결·하트비트·수집 트리거) / `NetSamLite.Web` (상태 표시·버튼)
- 브랜치(권장): `feature/2026-11-12-phase7-signalr`
- 출처: `docs/backlog.md`/`docs/proposals.md`가 아니라 **2026-11-12 사용자와의 대화**에서 범위·핵심 결정이 직접 정해짐 (`docs/proposals.md`는 존재하지 않음)

> **이 문서는 「절대 규칙 7」(파일 3개 이상 수정 시 사전 승인)에 따른 승인용 문서다.**
> 수정 파일이 28개 내외이고 새 DB 마이그레이션과 새 NuGet 패키지 참조(제품 1개 + 테스트 1개)가 들어가므로,
> 단계 7-A/7-B/7-C마다 빌드·테스트 통과를 확인하고 넘어간다.
> 목록 밖의 파일을 건드려야 하는 상황이 생기면 작업을 멈추고 먼저 질문한다.

---

## 배경

### 왜 지금 이 작업인가

이번 작업은 기존 자동 개선 사이클(1~3회차)처럼 `docs/backlog.md`의 🟡 항목을 고른 것이 **아니다.**
**2026-11-12 사용자와의 대화**에서 Phase 7~10 로드맵과 Phase 7의 범위·핵심 설계 결정이 직접 정해졌고,
그 결과가 이미 `CLAUDE.md`에 반영돼 있다. 이 기획서는 그 결정을 구현 가능한 단위로 옮긴 것이다.

### CLAUDE.md 개정 내용 요약 (이번 대화의 산출물)

1. **「진행 단계」가 Phase 6 → Phase 10까지 확장되고, 현재 Phase가 7로 바뀌었다.**
   Phase 7의 완료 기준은 `CLAUDE.md`에 "에이전트↔서버 Hub 연결, 인증(mTLS/토큰), 끊김 시 지수 백오프 재연결, 대시보드에 연결 상태 표시"로 적혀 있다.
2. **「절대 규칙 2」가 "에이전트는 읽기 전용"에서 "능동 조치는 서버 서명 정책에 한정"으로 개정되었다.**
   - 레지스트리·WMI **조회 전용 원칙은 유지**되고 **레지스트리 쓰기는 영구 금지**(Phase 10 이후에도 변경 불가)다.
   - 프로세스 종료·방화벽 규칙 변경은 서버가 발급한 **서명된 정책(PolicyDocument + 서명)** 을 로컬에서 검증했을 때만 실행한다. 서명 누락·불일치·버전 다운그레이드(replay)는 즉시 거부하고 사유를 감사 로그에 남긴다.
   - **기본 모드 = 감사(Audit)**, Enforce 전환은 Phase별 명시적 플래그로만.
   - **보호 프로세스 목록**은 서버가 정책 생성 시점에 강제 제외(서버 쪽 1차 방어), **긴급 해제(Kill switch)** 는 네트워크 단절 시에도 로컬에서 동작, **모든 조치는 감사 로그**(서버 전송 + 로컬 파일 이중 기록).
   - **「Phase 9 진입 게이트」가 새로 추가되었다**: *대시보드/운영자 인증이 구현되어 있지 않으면 Phase 9(프로세스 감지·차단)를 시작하지 않는다. Phase 7의 `collect-now`는 조회성 명령이라 localhost 전용 제한으로 충분하지만, Phase 9부터는 "인증 미비 시 임의 명령 주입"이 실제 차단 기능에 직결되므로 운영자 인증이 먼저 갖춰져야 한다.*
     → **이번 Phase에 주는 함의**: `collect-now`를 **localhost 전용 제한**으로 막는 것이 CLAUDE.md에 명시된 정식 해법이 되었다(확정 결정 3). 운영자 인증은 Phase 7 범위 밖이지만 **Phase 9의 선결 과제로 못박혔으므로 backlog 항목이 아니다.**
   - **Phase 7에 대한 함의**: 개정된 2번은 "능동 조치의 조건"을 정의한 것이고, 그 조건(서명 정책·검증·감사 로그)은 Phase 8 이후에 만든다. **따라서 Phase 7에서는 능동 조치를 단 하나도 구현하지 않는다.** 이번 Phase에서 추가되는 유일한 서버→에이전트 명령인 "지금 수집"은 **기존 조회성 수집 파이프라인을 1회 당겨 돌리는 트리거**일 뿐이며, 프로세스/방화벽/레지스트리를 건드리지 않으므로 능동 통제가 아니다.
3. **「Phase 7~10 위험 요소·테스트 방법」 표가 새로 생겼다.** Phase 7 행의 내용이 이 기획서의 「완료 기준」에 직접 반영되어 있고, Phase 9 행에는 위 진입 게이트가 **"진입 전제: 대시보드/운영자 인증 구현 완료"** 로 다시 명시되어 있다.
   > Phase 7 — 주요 위험: SYSTEM 권한 프로세스가 외부 제어 채널을 여는 것 자체가 공격 표면 확대. 인증 미비 시 임의 명령 주입
   > Phase 7 — 테스트 방법: Windows Sandbox 내 에이전트 → 호스트의 서버로 연결 확인. 서버 포트를 방화벽으로 막아 재연결/백오프 동작 확인

### 현재 코드가 놓인 상태 (실제로 읽고 확인한 것)

| 확인한 것 | 사실 | 이번 설계에 주는 제약 |
|---|---|---|
| `src/NetSamLite.Agent/Program.cs` | `--once`는 DI 없이 직접 수집기를 조립하고, 서비스 모드(`RunAsService`)는 `Host.CreateApplicationBuilder` + `AddWindowsService` + Serilog + `AddHostedService<AgentWorker>` | Hub 연결은 **`AgentWorker`와 나란히 또 하나의 `BackgroundService`** 로 얹는 게 가장 자연스럽다. `--once` 경로는 **전혀 건드리지 않는다**(오프라인 1회 수집 도구로 남긴다) |
| `src/NetSamLite.Agent/AgentWorker.cs` | jitter 후 `수집 → ReportDeliveryService.DeliverAsync` 를 `CollectionIntervalMinutes` 간격으로 반복 | "지금 수집"이 이 루프와 **동시에** 돌면 `ReportDeliveryService`가 보장하는 "큐를 오래된 것부터 먼저 비우고 새 리포트는 그 다음" 순서가 깨진다 → **직렬화 장치가 필요**(아래 「설계 상세」 3) |
| `src/NetSamLite.Agent/Sending/ReportDeliveryService.cs` | 큐 플러시 → 성공 시에만 신규 리포트 전송. 순서 보장이 명시적 설계 의도(클래스 주석) | "지금 수집"은 **이 서비스를 그대로 재사용**한다. 전송 경로를 따로 만들지 않는다 |
| `src/NetSamLite.Shared/*.csproj` | **PackageReference가 0개**, ProjectReference도 0개 | 계약을 Shared에 두더라도 **SignalR 패키지를 Shared에 넣지 않는다.** `IAgentClient`는 `Task` 반환 메서드만 가진 평범한 인터페이스여서 패키지 없이 선언 가능하고, 강타입 Hub(`Hub<IAgentClient>`)는 **서버 쪽에서만** SignalR을 참조하면 성립한다 |
| `src/NetSamLite.Server/NetSamLite.Server.csproj` | `Microsoft.NET.Sdk.Web` | **SignalR은 ASP.NET Core 공유 프레임워크에 포함**되어 있어 서버에는 패키지 추가가 **필요 없다.** 제품 코드에서는 에이전트만 `Microsoft.AspNetCore.SignalR.Client`를 추가한다 |
| `tests/NetSamLite.Server.Tests/NetSamLite.Server.Tests.csproj` | `Microsoft.AspNetCore.Mvc.Testing`, `EntityFrameworkCore.Sqlite`, FluentAssertions, xunit. **SignalR.Client는 없다** | 실제 Hub 연결을 맺는 통합 테스트(시나리오 B)를 위해 **테스트 전용 `Microsoft.AspNetCore.SignalR.Client` 1개를 추가**한다. `Mvc.Testing`이 이미 같은 성격(테스트 전용 인프라 패키지)으로 들어와 있어 선례가 있다 |
| `src/NetSamLite.Web/Program.cs`, `NetSamLite.Web.csproj` | Web은 **Server 프로젝트를 참조**해 `DeviceDirectoryService` 등을 in-process DI로 쓰고, **같은 SQLite 파일을 직접 연다**. Server로 가는 `HttpClient`는 **없다** | **치명적 제약**: Hub는 Server **프로세스**에 살아 있고 Web은 **별도 프로세스**다. Web에서 `IHubContext`를 주입해도 그건 "Web 프로세스 안의 아무도 연결되지 않은 Hub"이므로 "지금 수집"이 에이전트에 도달하지 않는다 → **Web → (HTTP) → Server → `IHubContext` → 에이전트** 경로가 필수(아래 「설계 상세」 2) |
| `src/NetSamLite.Web/Components/Pages/Devices.razor` | `DirectoryService.ListAsync(CancellationToken.None)`를 `OnInitializedAsync`에서 호출해 테이블 한 장을 그리는 단순 패턴 | 연결 상태 컬럼과 "지금 수집" 버튼은 **이 페이지에 그대로 얹는다.** 새 페이지를 만들지 않는다 |
| `src/NetSamLite.Server/Program.cs` | 인증·인가 미들웨어가 **전혀 없다**(모든 엔드포인트 익명) | 장치 토큰 인증은 **Hub에만** 적용한다. `collect-now`는 운영자용이라 토큰 대신 **localhost 제한**으로 막는다(확정 결정 3). 기존 엔드포인트의 인증은 이번 범위 밖 |
| `src/NetSamLite.Server/Properties/launchSettings.json` | 기본 `http://localhost:5136` (http 프로파일), https는 7257 | **베어러 토큰을 평문 HTTP로 보내는 문제**가 생긴다 → 아래 「안 할 것」과 「완료 기준」에서 명시적으로 다룬다 |

---

## 목표

이 작업이 끝나면 다음이 가능해진다.

1. 에이전트 서비스가 기동하면 서버 Hub에 **상시 연결**을 유지하고, 끊기면 **무한 지수 백오프 + jitter(최대 60초 간격)** 로 스스로 다시 붙는다. 서버를 재시작하거나 방화벽으로 포트를 막았다 풀어도 사람이 손대지 않고 복구된다.
2. 서버는 연결한 상대가 **등록된 장치임을 토큰으로 검증**한다. 토큰 없이/틀린 토큰으로는 Hub에 붙을 수 없고, 서버 DB에는 **토큰 평문이 존재하지 않는다**(해시만 저장).
3. 대시보드 `장치 목록` 화면에서 장치별 **온라인/오프라인**과 **마지막 확인 시각(하트비트)** 을 볼 수 있다.
4. 대시보드의 **"지금 수집"** 버튼을 누르면 해당 PC의 에이전트가 Hub로 명령을 받아 **즉시 1회** 기존 수집·보고 파이프라인(`ISoftwareCollector` + `IHardwareCollector` + `ReportDeliveryService`)을 실행하고, 결과(성공/이미 수집 중/실패)가 대시보드에 돌아온다. 다음 주기(최대 60분)를 기다릴 필요가 없다.
5. 학습 목표상 더 중요한 것: **"SYSTEM 권한 서비스가 외부에서 명령을 받는 채널"을 안전하게 여는 최소 요건**(장치 단위 인증, 평문 비밀값 미저장, 명령 종류를 조회성으로 제한, 명령 엔드포인트의 localhost 제한, 버전 협상)을 직접 만들어 보고, Phase 8(정책 배포)이 그 위에 얹힐 수 있는 뼈대를 남긴다.

---

## 확정된 결정 (재검토하지 않는다)

사용자가 2026-11-12 대화에서 확정한 사항이다. 구현은 이 결정을 그대로 따른다.

### 1. 호스팅 — 새 프로젝트 없이 기존 `NetSamLite.Server` 안에 Hub를 추가한다 (확정)

- `src/NetSamLite.Server/Realtime/AgentHub.cs`를 추가하고 `Server/Program.cs`에서 `AddSignalR()` + `MapHub<AgentHub>(...)`로 띄운다.
- 별도 "RealtimeGateway" 프로젝트를 만들지 않는다 → 「절대 규칙 7」의 "새 프로젝트 추가" 승인 절차와 솔루션 복잡도를 피한다.
- 의존 방향(`Agent → Shared ← Server ← Web`)은 그대로 유지된다.

### 2. 인증 — 장치 토큰 방식, 서버는 해시만 저장 (확정)

- Device 등록(enrollment) 시 서버가 토큰을 **1회 발급**하고, **평문은 그 응답에만 존재**한다. DB에는 해시만 남는다.
- 에이전트는 Hub 연결 시 **Bearer 토큰**으로 인증한다.
- **mTLS는 이번 Phase에서 하지 않는다**(아래 「안 할 것」).

### 3. `collect-now` 접근 통제 — localhost(루프백) 전용 허용 (확정)

- `POST /api/v1/devices/{deviceId}/collect-now` 핸들러에서 `HttpContext.Connection.RemoteIpAddress`를 확인해 **루프백이 아니면 403 Forbidden** 으로 거부한다.
  - 판정은 `IPAddress.IsLoopback(remoteIp)` 하나로 한다 — IPv4 `127.0.0.0/8`과 IPv6 `::1`을 모두 처리해 준다. 문자열 비교로 `"127.0.0.1"`만 보면 Kestrel이 IPv6로 수신할 때(`::1`) 정상 요청이 403이 된다.
  - `RemoteIpAddress`가 `null`인 경우(유닉스 소켓 등 비 TCP 전송)도 **거부**한다 — "모르면 막는다".
- **이것은 장치 토큰 인증과 별개의 체크다.** 장치 토큰은 *에이전트가 Hub에 붙을 때* 쓰는 것이고, `collect-now`는 *운영자(대시보드)가 서버에 명령을 넣을 때* 쓰는 엔드포인트다. 두 경로의 주체가 다르므로 통제 수단도 다르다.
- **왜 localhost 제한으로 충분한가**: CLAUDE.md 「절대 규칙 2」의 Phase 9 진입 게이트가 이 판단을 명시한다 — *"Phase 7의 `collect-now`는 조회성 명령이라 localhost 전용 제한으로 충분하지만, Phase 9부터는 ... 운영자 인증이 먼저 갖춰져야 한다."* `collect-now`가 트리거하는 것은 레지스트리/WMI 읽기 + HTTP POST뿐이고, 최악의 악용 결과는 "수집이 예정보다 일찍 한 번 돈다"다.
- Web(Blazor)은 Server와 같은 PC에서 돌리는 전제이므로 Web → Server 호출은 루프백으로 나간다. **Web을 다른 PC에서 띄우면 "지금 수집"이 403이 된다** — 의도된 동작이며, 그 상황에서는 Phase 9 게이트가 요구하는 운영자 인증을 먼저 만들어야 한다.

### 4. 재연결 정책 — 무한 재시도 + 지수 백오프(최대 간격 60초) + jitter (확정)

- **영구적으로 포기하지 않는다.** 재시도 횟수 상한이 없다.
- 백오프는 `min(base × 2^n, 60초)`에 jitter를 섞는다(여러 PC가 서버 재시작 직후 동시에 몰리는 thundering herd 방지 — `AgentOptions.StartupJitterMaxSeconds`와 같은 취지).

### 5. 온라인/오프라인 판정 — 이벤트 기반 즉시 반영 (확정)

- **온라인/오프라인 불값은 `OnConnectedAsync`/`OnDisconnectedAsync`에서 즉시 갱신한다.**
- **하트비트는 `LastHeartbeatUtc`(마지막 확인 시각) 컬럼만 갱신한다.** 온라인/오프라인 판정에는 **전혀 쓰이지 않는다.** 두 값은 분리된 별개 필드다.
- 하트비트 타임아웃 임계값으로 오프라인을 추론하는 방식은 **채택하지 않는다.**
- 이 결정이 받아들이는 트레이드오프(서버 비정상 종료 시 stale "온라인" 행)는 아래 「알려진 한계」에 별도로 기록했다. **사용자가 알고 선택한 것이므로 구현에서 임의로 되돌리지 않는다.**

### 6. 등록(enrollment) 비밀값 — 공통 비밀값 1개, user-secrets/환경변수로 관리 (확정)

- 서버는 `dotnet user-secrets`(또는 환경변수)에 둔 **공통 비밀값 1개**로 등록 요청을 검증한다. 장치별 등록 코드를 발급하지 않는다.
- 에이전트는 환경변수 `NETSAMLITE_ENROLLMENT_SECRET`에서 읽는다. **`appsettings.json`에 넣지 않는다**(「절대 규칙 4」).
- "장치별 분리 · 회전 · 승인 대기 큐(관리자가 대시보드에서 등록 승인)"는 범위 밖이며 **backlog 기록 대상**이다(아래 「안 할 것」).

### 7. Hub 계약 설계 — **설계안 A: 메시지별 전용 DTO + 버전은 각 메시지 바디 안(`SchemaVersion`)** (확정)

```csharp
// NetSamLite.Shared/Hub/AgentHubContract.cs
public static class AgentHubContract
{
    public const string Path = "/hubs/agent";
    public const int CurrentSchemaVersion = 1;
}

// 서버 → 에이전트 (강타입 Hub의 클라이언트 인터페이스)
public interface IAgentClient
{
    Task CollectNowAsync(CollectNowCommand command);
}

public sealed record CollectNowCommand(int SchemaVersion, string CommandId, DateTime IssuedAtUtc);
public sealed record HeartbeatMessage(int SchemaVersion, string DeviceId, string AgentVersion, DateTime SentAtUtc);
public sealed record HeartbeatAck(int SchemaVersion, DateTime ServerTimeUtc);
public sealed record CollectNowResult(int SchemaVersion, string CommandId, CollectNowOutcome Outcome, string? Detail);
public enum CollectNowOutcome { Succeeded, AlreadyRunning, Failed }

// 에이전트 → 서버 (AgentHub의 public 메서드 시그니처)
//   Task<HeartbeatAck> SendHeartbeatAsync(HeartbeatMessage message)
//   Task ReportCollectNowResultAsync(CollectNowResult result)
```

확정 사항 세부:

- **버전 필드는 각 메시지 record의 첫 필드 `int SchemaVersion`** 이다. 봉투(envelope)도, 별도 메서드 파라미터도 쓰지 않는다.
- 수신 측은 **핸들러 진입 직후 `SchemaVersion`을 확인**하고, `AgentHubContract.CurrentSchemaVersion`과 다르면 처리하지 않고 경고 로그를 남긴다(구조적 로깅: `{DeviceId}`, `{Received}`, `{Expected}`).
- `HeartbeatMessage.DeviceId`는 **로그·디버깅용 참고값일 뿐 신뢰하지 않는다.** 서버는 항상 인증 클레임의 `DeviceId`로 동작한다(아래 「범위 → 할 것 → 3」).
- 메시지 타입 추가 시(Phase 8 정책 push 등) `IAgentClient`에 메서드를 추가하고 record를 추가한다. 기존 메시지의 `SchemaVersion`은 건드리지 않는다 — **메시지별 독립 버전**이 이 설계안을 고른 핵심 이유다.

<details>
<summary><strong>검토 기록 — 함께 비교한 설계안 B / C (채택되지 않음)</strong></summary>

기록 가치가 있어 남긴다. 결론은 **A 확정**이며 B/C는 구현하지 않는다.

- **B — 단일 봉투 메서드**: `HubEnvelope(int Version, string Kind, string PayloadJson)` 하나로 양방향 메서드를 각 1개(`ReceiveAsync`/`SendAsync`)로 줄이는 안.
- **C — 계약 전체 버전을 별도 파라미터로**: `SendHeartbeatAsync(int contractVersion, HeartbeatMessage message)`처럼 버전을 파라미터로 분리하고 바디는 깨끗하게 두는 안.

| 항목 | **A (채택)** | B | C |
|---|---|---|---|
| 서버→에이전트 메서드 | `CollectNowAsync(CollectNowCommand)` | `ReceiveAsync(HubEnvelope)` 1개로 전부 | `CollectNowAsync(int, CollectNowCommand)` |
| 에이전트→서버 메서드 | `SendHeartbeatAsync(HeartbeatMessage)` → `HeartbeatAck`, `ReportCollectNowResultAsync(CollectNowResult)` | `SendAsync(HubEnvelope)` → `HubEnvelope` | 위와 같으나 앞에 `int contractVersion` |
| 버전 필드 위치 | **각 메시지 바디** | 봉투 | 별도 파라미터 |
| 메시지별 독립 진화 | **가능** | `Kind`별 규칙을 직접 관리 | 불가(전체 버전이 올라감) |
| 강타입 이점 | **온전히 유지** | 상실(`PayloadJson` 직접 역직렬화, `Kind` 오타는 런타임에만) | 유지 |
| 구버전 에이전트 호환 | 바디 필드 추가는 안전 | 가장 느슨 | 파라미터 추가 시 시그니처가 깨짐 |
| 감사 로그에 버전 보존 | **메시지만 저장해도 남음** | 봉투째 저장해야 남음 | 유실(따로 기록 필요) |
| 테스트 난이도 | 낮음(record 단위 단정) | 높음(JSON 조립/파싱 테스트) | 낮음 |
| Phase 8(서명 정책 replay 방지) 궁합 | 좋음 — 정책도 자체 버전을 갖고 다녀야 하므로 결이 맞음 | 버전이 2중화 | 버전이 2중화 |

**A를 고른 이유 요약**: ① SignalR 강타입 Hub가 해주는 일(메서드명·인자 타입 컴파일 검증)을 그대로 활용한다 ② Phase 8의 replay 방지는 "메시지가 자기 버전을 갖고 다니는" 구조를 요구하므로 미래 설계와 결이 같다 ③ 감사 로그에 메시지를 그대로 저장해도 버전 정보가 유실되지 않는다. 대가는 모든 record 첫 필드에 `SchemaVersion`이 반복되는 보일러플레이트인데, 메시지 수가 3~5개 수준이라 감수 가능한 비용이다.

</details>

---

## 범위

### 할 것

#### 1. 계약 (`NetSamLite.Shared`)

- 확정 결정 7(설계안 A)의 코드 블록 그대로 `src/NetSamLite.Shared/Hub/AgentHubContract.cs`에 추가한다: `AgentHubContract`(경로·현재 스키마 버전 상수), `IAgentClient`, `CollectNowCommand`, `HeartbeatMessage`, `HeartbeatAck`, `CollectNowResult`, `CollectNowOutcome`.
- 모든 메시지 record의 **첫 필드는 `int SchemaVersion`** 이다.
- Shared의 **패키지 참조는 0개를 유지**한다(`IAgentClient`는 `Task` 반환 메서드만 가진 평범한 인터페이스이므로 SignalR 패키지가 필요 없다).

#### 2. 장치 토큰 발급·검증 (`NetSamLite.Server`)

- **등록 엔드포인트** `POST /api/v1/devices/{deviceId}/enroll`
  - 요청에 **등록 공통 비밀값(enrollment secret)** 을 담아 보낸다(확정 결정 6). 서버는 설정(user-secrets/환경변수)에 있는 값과 **고정시간 비교**(`CryptographicOperations.FixedTimeEquals`)로 확인한다.
  - 통과하면 **32바이트 암호학적 난수**(`RandomNumberGenerator.GetBytes(32)`)를 Base64Url로 인코딩한 토큰을 생성해 **응답 바디에 1회만** 돌려주고, DB에는 **SHA-256 해시만** 저장한다.
  - **왜 SHA-256이고 PBKDF2/Argon2가 아닌가** (「절대 규칙 9」 — 설명하며 구현): 비밀번호 해싱에 느린 KDF를 쓰는 이유는 사람이 만든 저엔트로피 입력을 사전 공격으로부터 지키기 위함이다. 여기 토큰은 **256비트 난수**라서 해시를 탈취해도 원본을 역산할 탐색 공간이 없다. 느린 KDF는 매 연결마다 CPU만 쓰게 된다. 단, **솔트 없는 단순 해시라 "같은 토큰이면 같은 해시"** 라는 성질이 남으므로, 토큰은 장치마다 새로 생성하고 재사용하지 않는다.
  - 이미 토큰이 있는 장치가 다시 등록을 요청하면 **새 토큰을 발급하고 기존 해시를 교체**한다(재설치·토큰 분실 복구 경로).
- **인증 핸들러** `DeviceTokenAuthenticationHandler` (`AuthenticationHandler<AuthenticationSchemeOptions>`, 스킴명 `"DeviceToken"`)
  - `Authorization: Bearer <token>` 헤더, **그리고** `?access_token=<token>` 쿼리 스트링 둘 다에서 토큰을 읽는다.
  - **왜 쿼리 스트링도 읽어야 하는가**: 브라우저/SignalR의 **WebSocket 핸드셰이크에는 커스텀 헤더를 실을 수 없다.** 그래서 SignalR 클라이언트의 `AccessTokenProvider`는 WebSocket/SSE 전송에서 토큰을 `access_token` 쿼리 파라미터로 보낸다. 헤더만 읽으면 롱폴링에서는 되고 WebSocket에서는 401이 나는, 원인 찾기 어려운 증상이 생긴다.
  - 토큰을 해시해 DB와 대조하고, 성공하면 `ClaimTypes.NameIdentifier = DeviceId` 클레임을 담은 principal을 만든다. 실패는 `AuthenticateResult.Fail`.
  - 쿼리 스트링 토큰 허용은 **Hub 경로에 한정**한다(다른 경로에서는 헤더만).
- `Server/Program.cs`에 `AddAuthentication`/`AddAuthorization`/`UseAuthentication`/`UseAuthorization`을 추가하고, **Hub에만** `RequireAuthorization`을 건다. **기존 엔드포인트는 전부 익명 유지**(동작 변경 0).

#### 3. Hub + 연결 추적 (`NetSamLite.Server`)

- `AgentHub : Hub<IAgentClient>` — `OnConnectedAsync`/`OnDisconnectedAsync`에서 **온라인 불값을 즉시 갱신**하고(확정 결정 5), 하트비트 메서드에서 `LastHeartbeatUtc`만 갱신한다.
  - 연결의 `DeviceId`는 **클라이언트가 보낸 값이 아니라 인증된 클레임**에서 읽는다(인증 미비 시 임의 명령 주입 위험에 대한 직접적 대응 — 다른 장치를 사칭할 수 없다). 클레임에서 읽는 경로를 **단일 지점(`GetAuthenticatedDeviceId()`)으로 모아** 모든 Hub 메서드가 그것만 쓰게 한다. `HeartbeatMessage.DeviceId`는 참고값일 뿐이며, 클레임과 다르면 경고 로그를 남기고 **클레임 쪽을 쓴다**.
  - `Groups.AddToGroupAsync(deviceId)`로 장치별 그룹에 넣는다 → 같은 장치가 재연결로 `ConnectionId`가 바뀌어도 "그 장치로 보내기"가 유지된다.
  - 모든 수신 메서드는 진입 직후 `SchemaVersion`을 검사한다(확정 결정 7).
- `DeviceConnectionTracker` — 연결/해제/하트비트를 DB에 반영하고 장치별 상태를 조회한다.
  - `RecordConnectedAsync(deviceId, connectionId)`: `IsOnline = true`, `LastConnectedAtUtc = now`, `LastHeartbeatUtc = now`, `ConnectionId` 갱신.
    - 연결 시점에 `LastHeartbeatUtc`도 같이 찍는 이유: 첫 하트비트가 오기까지(최대 60초) 화면의 "마지막 확인" 칸이 비어 보이는 것을 막는다.
  - `RecordDisconnectedAsync(deviceId)`: `IsOnline = false`, `LastDisconnectedAtUtc = now`.
  - `RecordHeartbeatAsync(deviceId)`: **`LastHeartbeatUtc`만** 갱신한다. `IsOnline`을 건드리지 않는다.
  - `ListStatusesAsync()`: 장치별 `IsOnline` / `LastHeartbeatUtc` / `LastConnectedAtUtc`를 그대로 돌려준다. **파생 계산(타임아웃 판정)을 하지 않는다.**
  - 한 번도 Hub에 붙지 않은 장치는 `DeviceConnection` 행이 없다 → **오프라인으로 취급**하고 예외를 던지지 않는다(마이그레이션 직후 모든 기존 장치가 이 상태다).
- DB: `DeviceTokenEntity`(DeviceId PK, TokenHash, IssuedAtUtc) + `DeviceConnectionEntity`(DeviceId PK, ConnectionId, **IsOnline**, LastConnectedAtUtc, LastHeartbeatUtc, LastDisconnectedAtUtc) 두 엔티티를 **파일 1개**에 둔다(기존 `DeviceEntity` + `DeviceDiskEntity`가 한 파일인 패턴과 동일). 마이그레이션 1개 추가.
  - `DeviceEntity`에 컬럼을 덧붙이지 않는 이유: `DeviceEntity`는 인벤토리 수집 결과의 투영이고 `InventoryIngestionService.UpsertDeviceAsync`가 매 수집마다 전체를 덮어쓴다. 연결 상태/토큰을 같은 행에 두면 **수집 1회가 토큰을 날릴 위험**이 생긴다.

#### 4. "지금 수집" 명령 경로 (`NetSamLite.Server` + `NetSamLite.Web`)

- 서버: `POST /api/v1/devices/{deviceId}/collect-now`
  - **① 루프백 체크(확정 결정 3)**: `HttpContext.Connection.RemoteIpAddress`가 `null`이거나 `IPAddress.IsLoopback(...)`이 아니면 **403**. 다른 검사보다 먼저 수행한다(장치 존재 여부를 비루프백 호출자에게 알려 주지 않기 위해).
  - **② 장치 존재 확인**: DB에 없는 `DeviceId`면 404.
  - **③ 온라인 확인**: `DeviceConnectionTracker`의 `IsOnline`이 false면 **409 Conflict**. 명령을 큐에 쌓아 두지 않는다(「안 할 것」 참조).
  - **④ 전송**: `IHubContext<AgentHub, IAgentClient>`로 해당 장치 그룹에 `CollectNowCommand(SchemaVersion, CommandId=GUID, IssuedAtUtc)`를 보내고 **202 Accepted**.
  - 이 엔드포인트는 **대시보드(운영자)용**이지 에이전트용이 아니다. 장치 토큰 인가를 걸지 않는 대신 localhost로 제한한다.
- Web: **`Devices.razor`에 컬럼 2개와 버튼 1개 추가**
  - 컬럼: `연결 상태`(온라인/오프라인), `마지막 확인`(= `LastHeartbeatUtc`, 기존 `LastSeenUtc` 포맷과 동일하게 `ToLocalTime()`)
  - 버튼: `지금 수집` → Server의 위 엔드포인트를 **HTTP로** 호출하고 결과 메시지를 화면에 표시(202/409/403/404를 각각 사람이 읽을 메시지로 변환).
  - **왜 HTTP인가**: Web은 Server와 **별 프로세스**다. Web 프로세스에서 `IHubContext<AgentHub>`를 주입해도 에이전트는 그 프로세스에 연결돼 있지 않아 명령이 아무 데도 가지 않는다. 그래서 Web에 **Server를 가리키는 `HttpClient`를 처음으로 도입**한다(`ServerBaseUrl` 설정값).
  - 상태 **표시**는 기존 패턴대로 DB를 직접 읽는다(Web이 Server 프로젝트의 `DeviceConnectionTracker`를 DI로 주입해 사용). 즉 **쓰기(명령)만 HTTP, 읽기(상태)는 기존대로 DB**.
  - 상태는 `OnInitializedAsync` 시점 스냅샷 + 수동 `새로 고침` 버튼으로 본다(브라우저 자동 푸시는 「안 할 것」).

#### 5. 에이전트 쪽 (`NetSamLite.Agent`)

- `Microsoft.AspNetCore.SignalR.Client` 패키지 추가.
- `AgentHubWorker : BackgroundService` — 토큰 확보 → 연결 → 하트비트 루프 → 명령 수신.
  - **최초 연결 실패도 직접 재시도해야 한다**: `WithAutomaticReconnect`는 **이미 성립한 연결이 끊긴 경우만** 다시 붙는다. `StartAsync`가 처음부터 실패하면(서버가 아직 안 떠 있음 — 서비스 자동 시작에서 매우 흔함) 자동 재연결은 전혀 동작하지 않는다. 그래서 `StartAsync`를 **같은 백오프 정책으로 감싼 루프** 안에서 호출한다.
  - `WithAutomaticReconnect(new HubBackoffRetryPolicy(...))` — `NextRetryDelay`가 **절대 `null`을 반환하지 않는다**(null은 "포기"를 뜻한다. 확정 결정 4에 따라 영구 포기 금지).
  - 하트비트는 `PeriodicTimer`로 보낸다. 연결이 끊긴 동안에는 보내지 않고, 재연결되면 다시 시작한다.
- `HubBackoffRetryPolicy` — `min(base × 2^n, Max)` + jitter. **`IRetryPolicy`만 구현하는 순수 계산 클래스**로 분리해서 테스트 가능하게 한다(SignalR 연결 없이 단위 테스트).
- `DeviceTokenStore` — `QueueDirectory`와 같은 이유로 `ProgramData\NetSamLite\` 하위의 파일에 토큰을 저장/로드하고, 없으면 enroll 엔드포인트를 호출해 받아 저장한다. `ProgramData` 하위를 쓰는 이유는 기존 `AgentOptions.QueueDirectory`/`LogDirectory`와 동일(SYSTEM 계정에서도 안정적으로 쓰기 가능).
- `InventoryCollectionRunner` — **"수집 → 전송"을 한 번에 하나만 돌게 보장하는 얇은 래퍼.**
  - `SemaphoreSlim(1,1)`로 감싸고, 이미 돌고 있으면 **기다리지 않고 즉시 `AlreadyRunning`을 반환**한다.
  - **왜 필요한가**: `ReportDeliveryService`의 설계 의도는 "큐를 오래된 순서로 비우고, 그게 끝났을 때만 새 리포트를 보낸다"다. 주기 수집과 "지금 수집"이 동시에 들어오면 두 흐름이 같은 큐 디렉터리와 같은 전송 경로를 경쟁적으로 쓰게 되어 **서버에 도착하는 순서가 뒤집히고**(= 설치/삭제 이력이 거꾸로 기록된다) 같은 큐 파일을 양쪽이 지우려 할 수 있다. 「절대 규칙 6」 관점에서도 이건 새 기능이 아니라 **Phase 7에서 새로 생기는 동시성에 대한 필수 방어**다.
  - `AgentWorker`는 수집·전송 호출부를 이 래퍼 경유로 바꾼다(루프·jitter·예외 처리 구조는 그대로).
- `AgentOptions`에 추가: `HubPath`(기본 `/hubs/agent`), `HeartbeatIntervalSeconds`(기본 60), `ReconnectBaseDelaySeconds`(기본 2), `ReconnectMaxDelaySeconds`(기본 60), `TokenFilePath`(기본 `ProgramData\NetSamLite\device-token.dat`).
  - **등록 비밀값은 `AgentOptions`에 넣지 않고 `appsettings.json`에도 쓰지 않는다**(「절대 규칙 4」). 환경변수 `NETSAMLITE_ENROLLMENT_SECRET`로만 읽는다.

### 안 할 것

Phase 8 이후로 미루거나 별도 작업으로 분리하는 것들이다.

- **mTLS(상호 TLS) 인증** — 확정 결정 2에 따라 이번 Phase에서는 하지 않는다. **`docs/backlog.md`에 "나중에 고려" 항목으로 별도 기록이 필요하다**(토큰 방식의 한계: 토큰 파일이 유출되면 장치 사칭 가능, 서버 신원 검증은 TLS 인증서에만 의존). backlog.md 자체 수정은 developer 몫이다 — 아래 「수정할 파일」 참조.
- **에이전트 로컬 토큰 파일의 ACL/암호화 보호** — 토큰은 평문 파일로 저장된다. 이는 CLAUDE.md에서 Phase 8 범위로 명시된 "로컬 정책 캐시 보호(ACL/암호화)"와 같은 성질의 문제이므로 같이 처리한다. **backlog에 기록 필요.**
- **등록(enrollment) 비밀값의 장치별 분리·회전·승인 대기 큐** — 확정 결정 6에 따라 이번에는 공통 비밀값 1개다. 장치별 등록 코드, 만료, 1회용 토큰, 관리자 승인 대기 큐 전부 범위 밖. **backlog에 기록 필요.**
- **대시보드/운영자 인증** — 이번 Phase 범위 밖이다. `collect-now`는 확정 결정 3의 **localhost 제한**으로 막고, Web 자체는 익명으로 둔다. **단, 이 항목은 backlog 항목이 아니다** — `CLAUDE.md` 「절대 규칙 2」의 **「Phase 9 진입 게이트」** 로 이미 못박혀 있다("대시보드/운영자 인증이 구현되어 있지 않으면 Phase 9를 시작하지 않는다"). 즉 **Phase 9를 시작하기 전에 반드시 구현해야 하는 선결 과제**이며, 보류 메모가 아니라 로드맵상의 게이트다.
- **HTTPS 강제** — 현재 Server 기본 프로파일이 `http://localhost:5136`이고 Agent 기본 `ServerBaseUrl`도 http다. 베어러 토큰을 평문으로 보내는 상태이므로 **로컬호스트/신뢰 네트워크 전용**임을 코드 주석과 `docs/service-install.md`에 명시만 하고, HSTS/인증서 배포는 범위 밖. **backlog에 기록 필요.**
- **정책 서명 키 관리(발급·보관·교체)** — Phase 8.
- **서명된 `PolicyDocument` 전송·검증·버전 비교·ACK·로컬 정책 캐시** — Phase 8.
- **감사 로그 큐(네트워크 단절 시 재전송)** — Phase 8. 이번 Phase의 로그는 기존 Serilog 파일/콘솔 + 서버 `ILogger`에만 남긴다.
- **프로세스 종료, 방화벽 규칙 변경, 서비스 중지, 레지스트리 쓰기 등 모든 능동 조치** — **Phase 7에서 절대 금지**(「절대 규칙 2」). Hub가 받는 명령은 **"지금 수집" 단 하나**이고, 그것은 기존 조회성 수집 파이프라인의 1회 트리거일 뿐이다. `IAgentClient`에 조회성이 아닌 메서드를 추가하지 않는다.
- **온라인 판정의 하트비트 타임아웃 보정 / 서버 기동 시 stale 행 리셋** — 확정 결정 5에 따라 이번에는 순수 이벤트 기반이다. 아래 「알려진 한계」의 완화책은 **Phase 8 검토 후보로 메모만** 하고 구현하지 않는다.
- **오프라인 장치에 대한 명령 큐잉** — 지금 오프라인인 장치에 "다음에 붙으면 수집해라"를 저장해 두지 않는다. 409로 거절한다. (명령 큐는 Phase 8의 정책 배포 상태 관리와 같은 메커니즘이 필요하므로 그쪽에서 함께 설계한다.)
- **브라우저로의 실시간 푸시(대시보드 자동 갱신)** — Blazor Server 화면에 상태 변화를 밀어 주지 않는다. 수동 새로 고침이다. (Web은 Server와 별 프로세스이므로 에이전트 Hub 이벤트를 Web에 전달하려면 또 하나의 채널이 필요하다 — 범위 과다.)
- **에이전트 → 서버 역방향 조회(원격 진단, 로그 수집, 파일 전송)** — 범위 밖.
- **`--once` 경로 변경** — `Program.cs`의 `RunOnce`는 Hub/토큰과 무관한 오프라인 수집 도구로 그대로 둔다.
- **기존 엔드포인트(`/api/v1/inventory` 등)에 토큰 인증 적용** — 지금 적용하면 기존 Server 테스트 167개가 대량으로 깨지고, 에이전트의 기존 전송 경로까지 동시에 바꿔야 한다. Phase 7은 **Hub에만** 장치 인증을 건다. `/api/v1/inventory`에 장치 인증을 확장하는 것은 **backlog에 기록 필요.**
- **SignalR 백플레인(Redis 등) / 서버 다중 인스턴스** — 단일 서버 프로세스 전제. (확정 결정 5의 이벤트 기반 판정은 단일 인스턴스 전제와 맞물려 있다.)
- **`docs/backlog.md`의 기존 🟡 항목들**(`AgentOptions` 중복 구성, Serilog 보관 전제, SQLite 동시 쓰기 등) — 이번 작업과 섞지 않는다. 특히 `Agent/Program.cs`를 이번에 건드리게 되지만(DI 등록 추가), **`AgentOptions` 중복 구성 리팩터링은 하지 않는다**(별 사이클).

---

## 알려진 한계 (확정 결정 5의 트레이드오프)

확정 결정 5(이벤트 기반 즉시 반영)를 선택한 결과로 **의도적으로 받아들이는** 한계다. 사용자가 알고 고른 것이므로 구현에서 임의로 보정하지 않는다.

### 서버 프로세스가 비정상 종료되면 "온라인"이 DB에 영구히 남는다

- `OnDisconnectedAsync`는 **서버가 살아 있을 때만** 호출된다. 서버 프로세스가 **강제 종료(작업 관리자/`taskkill`), 전원 차단, 미처리 예외로 크래시**되면 그 순간 붙어 있던 장치들의 `IsOnline = true` 행이 **그대로 DB에 남는다.** 서버를 다시 띄워도 아무도 그 행을 `false`로 되돌려 주지 않으므로, 실제로는 끊긴 장치가 대시보드에 **영구히 "온라인"으로 표시된다**(stale row). 사람이 DB를 손으로 고치거나 그 장치가 다시 붙어 끊기기 전까지 복구되지 않는다 — **라는 한계가 있다(Phase 7).**
- **원래 설계가 하트비트 임계값 방식이었던 이유가 정확히 이 문제였다.** "마지막 하트비트가 N초 안인가"로 판정하면 서버가 어떻게 죽어도 시간이 지나면 자동으로 오프라인이 되므로 stale 행이 생기지 않는다. 그 대가는 ① 끊긴 직후 최대 N초간 "온라인"으로 남는 지연 ② "지금 연결돼 있다"는 사실과 "최근에 하트비트가 왔다"는 사실이 섞여 코드가 덜 직관적이라는 점이었다. 사용자는 **즉시성과 단순함**을 택했다.
- **이 한계가 미치지 않는 범위(= 의외로 넓다)**: 클라이언트 쪽 단절(에이전트 서비스 중지, PC 종료, 네트워크 케이블 분리, 방화벽 차단)은 **정상적으로 오프라인으로 반영된다.** SignalR 서버는 자체 keep-alive로 응답 없는 연결을 감지해(`HubOptions.ClientTimeoutInterval`, 기본 30초) `OnDisconnectedAsync`를 호출하기 때문이다. 따라서 stale 행은 **"서버 프로세스 자신이 죽는" 경우에만** 발생한다.
- **완화책 메모(구현하지 않음 — Phase 8 backlog 후보)**: 서버 기동 시 `DeviceConnection` 테이블에서 `IsOnline = true`로 남아 있는 모든 행을 `false`로 리셋하는 가벼운 보정을 Phase 8에서 검토할 수 있다. 서버가 막 기동한 시점에는 정의상 아무 연결도 성립해 있지 않으므로 안전하며, 마이그레이션이나 새 설정 없이 `Program.cs`의 기존 마이그레이션 블록 옆에 몇 줄로 끝난다. 이번 Phase에서는 **하지 않는다.**

### 서버 쪽 실시간 설정(`RealtimeOptions`)을 만들지 않는다

- 확정 결정 5로 **타임아웃 임계값이 온라인 판정에서 사라졌기 때문에**, 애초에 그 값을 담으려고 계획했던 `src/NetSamLite.Server/Realtime/RealtimeOptions.cs`는 **담을 설정이 없어졌다.** 하트비트 주기는 에이전트가 정하는 값이라 `AgentOptions`에 있고, 등록 비밀값은 「절대 규칙 4」에 따라 설정 클래스가 아니라 user-secrets에서 직접 읽는다.
- 따라서 **`RealtimeOptions.cs`를 만들지 않는다**(빈 설정 클래스를 미리 만들지 않는다). 서버 쪽에 조정값이 필요해지는 시점(예: 위 완화책을 넣을 때, 또는 `ClientTimeoutInterval`을 기본값에서 바꿀 때)에 그때 추가한다.

---

## 설계 상세 — 기존 코드 위에 얹히는 방식

### 1. 프로세스 구성도

```
[에이전트 프로세스 (Windows 서비스, SYSTEM)]
  AgentWorker ────────────┐
                          ├─→ InventoryCollectionRunner (SemaphoreSlim 1개)
  AgentHubWorker ─────────┘        └─→ 수집기 + ReportDeliveryService (기존 그대로)
       │  ▲
 Bearer│  │ CollectNowCommand
 토큰  │  │
       ▼  │
[서버 프로세스 :5136]  AgentHub : Hub<IAgentClient>  ←─ IHubContext ─┐
       │                                                             │
       └─→ SQLite (DeviceToken / DeviceConnection)                   │
                        ▲                            POST /collect-now
                        │ 직접 읽기(기존 패턴)         (루프백만 허용)
[Web 프로세스]  Devices.razor ───────── HttpClient ───────────────────┘
```

### 2. "지금 수집"의 전체 흐름

1. 운영자가 `Devices.razor`에서 버튼 클릭
2. Web → `POST {ServerBaseUrl}/api/v1/devices/{deviceId}/collect-now`
3. Server: **루프백 확인** → 아니면 403 (확정 결정 3)
4. Server: 장치 존재 확인(404) → `DeviceConnectionTracker.IsOnline` 확인 → 오프라인이면 409
5. Server: `IHubContext<AgentHub, IAgentClient>.Clients.Group(deviceId).CollectNowAsync(command)` → 202
6. Agent: `AgentHubWorker`의 핸들러 → `SchemaVersion` 확인 → `InventoryCollectionRunner.TryRunAsync()`
   - 이미 돌고 있으면 `AlreadyRunning`
   - 아니면 수집 → `ReportDeliveryService.DeliverAsync` (= 기존 파이프라인, 서버 DB에는 `POST /api/v1/inventory`를 통해 반영된다)
7. Agent → Hub로 `CollectNowResult` 전송, Server는 결과를 구조적 로그로 남김
8. Web: 응답(202/409/403/404)을 화면 메시지로 표시. 실제 수집 결과는 **다음 새로 고침 때 `LastSeenUtc`/SW 개수 변화로 확인**한다(7번 결과를 Web까지 되돌리려면 또 다른 채널이 필요 — 범위 밖).

### 3. 동시성 — `InventoryCollectionRunner`가 필요한 이유 (재강조)

주기 수집(`AgentWorker`)과 "지금 수집"이 동시에 들어오면:
- 둘 다 `PendingReportQueue.ListPendingAsync()`로 **같은 큐 파일 목록**을 받아 각자 전송하고 각자 `RemoveAsync`를 호출한다 → 한쪽의 `RemoveAsync`는 이미 사라진 파일을 지우려 한다(현재 `File.Exists` 가드 덕에 예외는 안 나지만, **같은 리포트가 서버에 두 번 POST**된다).
- 더 심각한 것은 **순서 역전**이다. `ReportDeliveryService` 클래스 주석이 명시한 대로, 새 리포트가 오래된 리포트보다 먼저 도착하면 서버가 설치/삭제 이력을 거꾸로 기록한다.

따라서 직렬화는 선택이 아니라 필수다. 기다리지 않고 `AlreadyRunning`을 반환하는 이유: 버튼 연타로 수집이 몇 번이고 줄 서는 것을 막고, 운영자에게 "지금 이미 돌고 있다"는 정확한 정보를 준다.

### 4. 온라인/오프라인 판정 — 이벤트 기반 (확정 결정 5)

| 이벤트 | `IsOnline` | `LastConnectedAtUtc` | `LastHeartbeatUtc` | `LastDisconnectedAtUtc` |
|---|---|---|---|---|
| `OnConnectedAsync` | **true** | now | now | — |
| 하트비트 수신 | **건드리지 않음** | — | now | — |
| `OnDisconnectedAsync` | **false** | — | — | now |

- 대시보드는 `IsOnline`을 **그대로** 표시한다. 시각 비교·파생 계산을 하지 않는다 → 화면 로직이 단순해지고, "왜 온라인으로 보이는가"가 DB 한 컬럼으로 설명된다.
- `LastHeartbeatUtc`는 **"마지막으로 살아 있음을 확인한 시각"** 이라는 독립적인 정보로 화면에 보여 준다. 온라인인데 이 값이 오래됐다면 그 자체가 유용한 진단 신호다(위 「알려진 한계」의 stale 행이 바로 그렇게 보인다 — 온라인인데 마지막 확인이 몇 시간 전).
- 클라이언트 단절은 SignalR 자체 keep-alive(`ClientTimeoutInterval`, 기본 30초)로 감지되어 `OnDisconnectedAsync`가 호출된다. 즉 **에이전트/네트워크 쪽 문제는 약 30초 안에 오프라인으로 반영된다.**

---

## 수정할 파일

구현은 **3단계로 나눠 진행하고, 각 단계마다 `dotnet build` + `dotnet test`가 통과한 상태로 커밋**한다. 단계별로 쪼개는 이유: 한 번에 20개 넘는 파일을 바꾸면 실패 원인(인증? 재연결? 동시성?)을 분리할 수 없고, 「절대 규칙 8」의 "통과 전 완료 보고 금지"를 단계별로 지키기 쉽다.

### 단계 7-A — 계약 + 토큰 인증 + Hub 연결 (연결까지만)

| 파일 | 할 일 |
|---|---|
| `src/NetSamLite.Shared/Hub/AgentHubContract.cs` **(신규)** | 확정 결정 7(설계안 A)의 코드 블록 그대로: `AgentHubContract`(`Path`, `CurrentSchemaVersion`), `IAgentClient`, `CollectNowCommand`, `HeartbeatMessage`, `HeartbeatAck`, `CollectNowResult`, `CollectNowOutcome`. 모든 메시지 record의 첫 필드는 `int SchemaVersion`. **패키지 참조 추가 금지** |
| `src/NetSamLite.Server/Data/Entities/DeviceRealtimeEntities.cs` **(신규)** | `DeviceTokenEntity`(DeviceId PK, TokenHash, IssuedAtUtc) + `DeviceConnectionEntity`(DeviceId PK, ConnectionId, **IsOnline**, LastConnectedAtUtc, LastHeartbeatUtc, LastDisconnectedAtUtc). 한 파일에 둘 다(`DeviceEntity`+`DeviceDiskEntity` 패턴) |
| `src/NetSamLite.Server/Data/NetSamLiteDbContext.cs` | `DbSet` 2개 추가 + `OnModelCreating`에 `HasKey(DeviceId)` 2건. **기존 엔티티 설정은 한 줄도 건드리지 않는다** |
| `src/NetSamLite.Server/Migrations/*_AddDeviceRealtime.cs` **(신규, 생성물)** | `dotnet ef migrations add AddDeviceRealtime --project src/NetSamLite.Server` 로 생성. 손으로 쓰지 않는다. 생성된 `Up`이 **기존 테이블을 건드리지 않는지 반드시 눈으로 확인** |
| `src/NetSamLite.Server/Realtime/DeviceTokenService.cs` **(신규)** | 토큰 생성(32바이트 난수 → Base64Url) · SHA-256 해시 저장 · 해시 대조 검증 · 재등록 시 해시 교체. 등록 비밀값 고정시간 비교(`CryptographicOperations.FixedTimeEquals`) |
| `src/NetSamLite.Server/Realtime/DeviceTokenAuthenticationHandler.cs` **(신규)** | `"DeviceToken"` 스킴. `Authorization: Bearer` + `?access_token=` 둘 다 읽는다(WebSocket 핸드셰이크에 커스텀 헤더를 실을 수 없기 때문 — 주석에 이유 명시). 쿼리 허용은 Hub 경로에 한정 |
| `src/NetSamLite.Server/Realtime/AgentHub.cs` **(신규)** | `Hub<IAgentClient>`. `[Authorize(AuthenticationSchemes = "DeviceToken")]`. `OnConnectedAsync`/`OnDisconnectedAsync`에서 `DeviceConnectionTracker` 호출(**온라인 불값 즉시 갱신**) + `Groups.AddToGroupAsync(deviceId)`. **DeviceId는 클라이언트 입력이 아니라 인증 클레임에서만 읽는다** — 읽는 경로를 `GetAuthenticatedDeviceId()` 한 곳으로 모은다 |
| `src/NetSamLite.Server/Realtime/DeviceConnectionTracker.cs` **(신규)** | `RecordConnectedAsync`(`IsOnline=true`, 연결 시각 + `LastHeartbeatUtc` 동시 갱신) / `RecordDisconnectedAsync`(`IsOnline=false`) / `ListStatusesAsync`(DB 값을 **그대로** 반환, 타임아웃 파생 계산 없음) / 행이 없는 장치는 오프라인 취급. **하트비트 임계값 판정 로직을 넣지 않는다**(확정 결정 5) |
| `src/NetSamLite.Server/Program.cs` | `AddSignalR()`, `AddAuthentication().AddScheme<...DeviceTokenAuthenticationHandler>("DeviceToken")`, `AddAuthorization()`, `UseAuthentication()`/`UseAuthorization()`, `DeviceTokenService`/`DeviceConnectionTracker` DI 등록, `MapHub<AgentHub>(AgentHubContract.Path).RequireAuthorization(...)`, `POST /api/v1/devices/{deviceId}/enroll`. **기존 엔드포인트 11개는 익명 유지 — 한 줄도 건드리지 않는다** |
| `src/NetSamLite.Agent/NetSamLite.Agent.csproj` | `Microsoft.AspNetCore.SignalR.Client` PackageReference 1줄 추가(버전은 다른 참조와 맞춰 `10.0.x`) |
| `src/NetSamLite.Agent/Realtime/HubBackoffRetryPolicy.cs` **(신규)** | `IRetryPolicy` 구현. `min(base × 2^n, Max)` + jitter, **`null` 반환 금지**(= 영구 포기 금지). 순수 계산만 담아 단위 테스트 가능하게 |
| `src/NetSamLite.Agent/Realtime/DeviceTokenStore.cs` **(신규)** | `ProgramData\NetSamLite\device-token.dat` 읽기/쓰기, 없으면 enroll 호출(환경변수 `NETSAMLITE_ENROLLMENT_SECRET` 사용) |
| `src/NetSamLite.Agent/Realtime/AgentHubWorker.cs` **(신규)** | `BackgroundService`. 토큰 확보 → `HubConnectionBuilder.WithUrl(..., o => o.AccessTokenProvider = ...)` + `WithAutomaticReconnect(정책)` → **`StartAsync`를 같은 백오프로 감싼 루프**(최초 연결 실패는 자동 재연결이 처리하지 않음 — 주석에 이유 명시) → 연결 상태 로그. 7-A에서는 하트비트/명령 핸들러 없이 **연결 유지까지만** |
| `src/NetSamLite.Agent/AgentOptions.cs` | `HubPath`, `HeartbeatIntervalSeconds`, `ReconnectBaseDelaySeconds`, `ReconnectMaxDelaySeconds`, `TokenFilePath` 추가. **기존 속성과 기본값은 건드리지 않는다.** 등록 비밀값은 추가하지 않는다(「절대 규칙 4」) |
| `src/NetSamLite.Agent/appsettings.json` | 위 **비밀값 아닌** 신규 항목만 추가. 토큰/비밀값은 넣지 않는다 |
| `src/NetSamLite.Agent/Program.cs` | `RunAsService`에 `AgentHubWorker`/`DeviceTokenStore`/`HubBackoffRetryPolicy` DI 등록. **`RunOnce`는 건드리지 않는다. `AgentOptions` 중복 구성 리팩터링도 하지 않는다**(별 backlog 항목) |
| `tests/NetSamLite.Server.Tests/NetSamLite.Server.Tests.csproj` | **(갱신 2에서 추가)** `Microsoft.AspNetCore.SignalR.Client` PackageReference **1줄만** 추가(버전 `10.0.x`, Mvc.Testing과 맞춤). **테스트 전용**이며 제품 코드(`NetSamLite.Server`)에는 추가하지 않는다 — 서버 쪽 SignalR은 공유 프레임워크에 이미 포함돼 있다. 이 패키지는 시나리오 B의 실제 Hub 연결 테스트에만 쓴다. 다른 패키지 버전·항목은 건드리지 않는다 |
| `tests/NetSamLite.Server.Tests/Realtime/DeviceTokenServiceTests.cs` **(신규)** | 시나리오 A |
| `tests/NetSamLite.Server.Tests/Realtime/DeviceTokenAuthTests.cs` **(신규)** | 시나리오 B |
| `tests/NetSamLite.Agent.Tests/Realtime/HubBackoffRetryPolicyTests.cs` **(신규)** | 시나리오 C |
| `tests/NetSamLite.Agent.Tests/Realtime/DeviceTokenStoreTests.cs` **(신규)** | 시나리오 D |

> `RealtimeOptions.cs`는 **만들지 않는다** — 확정 결정 5로 담을 설정이 없어졌다(위 「알려진 한계」 참조).

### 단계 7-B — 하트비트 + 대시보드 상태 표시

| 파일 | 할 일 |
|---|---|
| `src/NetSamLite.Server/Realtime/AgentHub.cs` | `Task<HeartbeatAck> SendHeartbeatAsync(HeartbeatMessage message)` 추가. 진입 즉시 `SchemaVersion` 검사(불일치 시 처리 중단 + 경고 로그). 클레임의 `DeviceId`로 `RecordHeartbeatAsync` 호출 |
| `src/NetSamLite.Server/Realtime/DeviceConnectionTracker.cs` | `RecordHeartbeatAsync` 추가 — **`LastHeartbeatUtc`만 갱신하고 `IsOnline`은 건드리지 않는다**(확정 결정 5). `ListStatusesAsync`가 `IsOnline`/`LastHeartbeatUtc`를 함께 반환 |
| `src/NetSamLite.Agent/Realtime/AgentHubWorker.cs` | `PeriodicTimer` 하트비트 루프(`HeartbeatIntervalSeconds`). 끊긴 동안 미전송, 재연결 후 재개. 응답 `HeartbeatAck`의 `SchemaVersion` 확인 |
| `src/NetSamLite.Web/Program.cs` | `DeviceConnectionTracker` DI 등록(상태 읽기용). `ServerBaseUrl` 설정 읽어 `HttpClient` 등록(7-C에서 쓰지만 여기서 같이) |
| `src/NetSamLite.Web/appsettings.json` | `ServerBaseUrl` 추가(비밀값 아님) |
| `src/NetSamLite.Web/Components/Pages/Devices.razor` | `연결 상태`(= `IsOnline` 그대로) / `마지막 확인`(= `LastHeartbeatUtc`) 컬럼 2개 + `새로 고침` 버튼. 기존 컬럼·`FormatGigabytes`는 그대로 |
| `tests/NetSamLite.Server.Tests/Realtime/DeviceConnectionTrackerTests.cs` **(신규)** | 시나리오 F |

### 단계 7-C — "지금 수집"

| 파일 | 할 일 |
|---|---|
| `src/NetSamLite.Agent/Realtime/InventoryCollectionRunner.cs` **(신규)** | `SemaphoreSlim(1,1)`로 "수집→전송"을 한 번에 하나만. 이미 실행 중이면 대기 없이 `AlreadyRunning` 반환 |
| `src/NetSamLite.Agent/AgentWorker.cs` | 수집·전송 호출부를 `InventoryCollectionRunner` 경유로 변경. **jitter·간격 클램프(`Math.Max(1, ...)`)·예외 처리·로그 메시지는 그대로**(기존 `AgentWorkerTests`가 이 동작을 검증 중) |
| `src/NetSamLite.Agent/Realtime/AgentHubWorker.cs` | `CollectNowCommand` 핸들러 → `SchemaVersion` 검사 → `InventoryCollectionRunner` 호출 → `CollectNowResult` 회신(`CommandId` 그대로 되돌려 줌) |
| `src/NetSamLite.Server/Realtime/AgentHub.cs` | `Task ReportCollectNowResultAsync(CollectNowResult result)` 추가 + 결과 구조적 로깅(`{DeviceId}`, `{CommandId}`, `{Outcome}`) |
| `src/NetSamLite.Server/Program.cs` | `POST /api/v1/devices/{deviceId}/collect-now` — **① 루프백 아니면 403**(`IPAddress.IsLoopback`, `null`도 거부) ② 미등록 장치 404 ③ 오프라인 409 ④ 전송 성공 202. 루프백 체크를 **가장 먼저** 수행(장치 존재 여부를 외부에 노출하지 않기 위해) |
| `src/NetSamLite.Web/Components/Pages/Devices.razor` | `지금 수집` 버튼 + 결과 메시지(202/409/403/404 각각 사람이 읽을 문구로). 버튼은 오프라인 장치에서 비활성화 |
| `tests/NetSamLite.Agent.Tests/Realtime/InventoryCollectionRunnerTests.cs` **(신규)** | 시나리오 E |
| `tests/NetSamLite.Server.Tests/Realtime/CollectNowEndpointTests.cs` **(신규)** | 시나리오 G |

### 모든 단계 공통 — 문서

| 파일 | 할 일 |
|---|---|
| `docs/backlog.md` | **구현·빌드·테스트·리뷰가 모두 끝난 뒤**, 위 「안 할 것」에서 **"backlog에 기록 필요"로 표시한 5개 항목**을 「나중에 고려」 성격의 새 섹션(예: 「보안 (Phase 7 이후)」)에 기존 항목과 같은 형식(**위치 / 현상 / 지금 안 하는 이유 / 나중에 고려할 방향 / 기록일 `2026-11-12`**)으로 추가한다: ① mTLS 상호 인증 ② 에이전트 로컬 토큰 파일 ACL/암호화 ③ 등록 비밀값 장치별 분리·회전·승인 대기 큐 ④ HTTPS 강제(베어러 토큰 평문 전송) ⑤ `/api/v1/inventory`에 장치 토큰 인증 확장. **"대시보드/운영자 인증"은 backlog에 넣지 않는다** — CLAUDE.md 「절대 규칙 2」의 Phase 9 진입 게이트로 이미 로드맵에 못박혀 있다. 선택적으로 「알려진 한계」의 완화책(서버 기동 시 stale `IsOnline` 리셋)을 Phase 8 후보로 1개 더 추가해도 좋다. **이번 작업은 backlog에서 고른 항목이 아니므로 「해결됨」 섹션으로 옮길 항목이 없다.** |
| `docs/service-install.md` | 서비스 설치 절차에 **등록 비밀값 환경변수 설정**(`NETSAMLITE_ENROLLMENT_SECRET`), **서버 쪽 user-secrets 설정**, **로컬호스트/신뢰 네트워크 전용 전제**, **Web은 Server와 같은 PC에서 띄워야 "지금 수집"이 동작한다**(루프백 제한)는 점을 추가 |
| `docs/learning-log.md` | 「학습 정리」 트리거 형식대로 이번에 새로 배운 개념을 날짜와 함께 추가(강타입 Hub와 메시지별 `SchemaVersion`, `IRetryPolicy`와 "초기 연결은 자동 재연결 대상이 아니다", WebSocket 핸드셰이크의 헤더 제약과 `access_token` 쿼리, 고엔트로피 토큰에 SHA-256이 충분한 이유, `OnDisconnectedAsync`가 **클라이언트 단절에는 동작하지만 서버 크래시에는 동작하지 않는** 이유와 그 트레이드오프, `IPAddress.IsLoopback`으로 운영자 엔드포인트를 제한하는 패턴, **TestServer에 `HubConnection`을 LongPolling으로 붙이는 방법**) |
| `CLAUDE.md` | Phase 7 완료 후 「진행 단계」 표의 "현재" 표시를 Phase 8로 옮긴다. **Phase 7이 끝나기 전에는 건드리지 않는다** |

**합계: 신규 16개 + 수정 12개 = 28개 파일**(마이그레이션 생성물 포함). 「절대 규칙 7」에 따라 **이 기획서가 사전 승인 문서**이며, 단계 7-A/7-B/7-C마다 빌드·테스트 통과를 확인하고 넘어간다.

---

## 절대 규칙 점검

| 규칙 | 해당 여부와 근거 |
|---|---|
| **1 (회사 자산 금지)** | 해당 없음. SignalR·ASP.NET Core 인증은 전부 공개 문서(Microsoft Docs)와 직접 구현만 사용. 회사 SAM 제품의 에이전트 통신 프로토콜·메시지 포맷·DB 스키마를 참조하거나 추측 재현하지 않는다 — Hub 계약은 이 프로젝트의 기존 `InventoryReport` 계약 위에서 새로 설계한 것이다 |
| **2 (능동 조치는 서버 서명 정책에 한정 — 개정됨)** | **이번 Phase에서 능동 조치를 하나도 구현하지 않는다.** ① 레지스트리 쓰기 없음(영구 금지 준수) ② 프로세스 종료·방화벽 변경 없음 ③ `IAgentClient`가 받는 명령은 **"지금 수집" 단 하나**이고, 이는 기존 조회성 수집 파이프라인(레지스트리/WMI 읽기 → HTTP POST)을 1회 당겨 돌리는 트리거다 ④ 서명 정책·Audit/Enforce 모드·보호 프로세스 목록·Kill switch·감사 로그 이중 기록은 그 능동 조치를 **하게 될 때** 필요한 장치이므로 Phase 8~10에서 만든다 ⑤ **「Phase 9 진입 게이트」 준수**: `collect-now`를 **localhost 전용**으로 제한해(확정 결정 3) 게이트 조문이 Phase 7에 허용한 수준을 정확히 따른다. 운영자 인증 없이 Phase 9로 넘어가지 않는다는 점을 「안 할 것」에 명시했다 ⑥ 이번 Phase가 "SYSTEM 권한 프로세스에 외부 제어 채널을 여는" 첫 작업이므로(CLAUDE.md Phase 7 위험 요소) 토큰 인증·클레임 기반 DeviceId·명령 종류 제한·루프백 제한을 필수 요건으로 둔다 |
| **3 (개인정보 수집 금지)** | 수집 항목 표에 변경 없음. 새로 오가는 데이터는 `DeviceId`, 에이전트 버전, 타임스탬프, `CommandId`, 수집 결과 상태, `SchemaVersion`뿐이다. 사용자 파일·브라우저 기록·비밀번호·화면은 수집하지 않으며, 하트비트에 로그인 사용자명/SID를 넣지 않는다 |
| **4 (비밀값 커밋 금지)** | **등록 비밀값은 `appsettings.json`에 넣지 않는다**(확정 결정 6) — 서버는 `dotnet user-secrets`, 에이전트는 환경변수 `NETSAMLITE_ENROLLMENT_SECRET`. 발급된 장치 토큰은 서버 DB에 **해시만**, 에이전트는 `ProgramData` 하위 파일(레포 밖)에 저장한다. 토큰 평문을 로그에 남기지 않는다(로그에는 `{DeviceId}`만). **테스트에서 쓰는 등록 비밀값은 테스트 코드 안의 더미 문자열**이며 실제 비밀값을 커밋하지 않는다 |
| **5 (`Win32_Product` 금지)** | 해당 없음. 수집 로직은 기존 `RegistrySoftwareCollector`/`WmiHardwareCollector`를 **그대로 재사용**하고 수정하지 않는다 |
| **6 (현재 Phase 밖 기능 금지)** | **Phase 7 범위 안이다.** CLAUDE.md Phase 7 완료 기준의 4요소(Hub 연결 / 인증 / 지수 백오프 재연결 / 대시보드 연결 상태 표시)를 그대로 다룬다. "지금 수집"은 완료 기준에 직접 적힌 항목은 아니지만 **"Hub가 실제로 양방향으로 동작함"을 확인하는 최소 검증 수단**으로 사용자가 범위에 포함시킨 것이다. Phase 8 항목(정책 서명·배포·ACK·로컬 캐시)과 Phase 9~10 항목(프로세스 감지·차단, 운영자 인증 게이트)은 전부 「안 할 것」에 명시했다. `InventoryCollectionRunner`는 새 기능이 아니라 Phase 7이 만들어내는 동시성에 대한 방어다 |
| **7 (계획 먼저)** | **이 문서가 승인용 계획이다.** 파일 28개 + 새 NuGet 패키지 2개(에이전트 제품용 1개, Server.Tests 테스트용 1개) + 새 마이그레이션. 새 **프로젝트**는 추가하지 않는다(확정 결정 1). 열린 질문은 모두 해소되어 구현에 들어갈 수 있는 상태다 |
| **8 (빌드·테스트 통과 전 완료 보고 금지)** | 단계 7-A/7-B/7-C **각각** `dotnet build`(신규 경고 0) + `dotnet test` 통과를 확인한 뒤 다음 단계로 넘어간다. 기존 테스트(Agent 86 + Server 167)는 **전부 그대로 통과**해야 한다 |
| **9 (설명하며 구현)** | 새로 쓰는 API/패턴마다 선택 이유를 2~3줄 주석으로 남긴다: 강타입 `Hub<IAgentClient>`와 메시지별 `SchemaVersion`, `IRetryPolicy`와 "`WithAutomaticReconnect`는 최초 연결 실패를 재시도하지 않는다", WebSocket 핸드셰이크의 헤더 제약과 `access_token` 쿼리, 고엔트로피 토큰에 SHA-256이 충분한 이유, **온라인 판정을 이벤트 기반으로 둔 이유와 그 대가(서버 크래시 시 stale 행)**, `IPAddress.IsLoopback`으로 운영자 엔드포인트를 제한하는 이유, `SemaphoreSlim`으로 수집을 직렬화하는 이유, **TestServer에 `HubConnection`을 붙일 때 LongPolling + `HttpMessageHandlerFactory`가 필요한 이유** |

---

## 완료 기준

`dotnet build`(신규 경고 0개)와 `dotnet test`(기존 Agent 86 + Server 167 전부 통과 + 신규 테스트) 외에, 다음을 **직접 확인**한다.

### 코드·계약 수준

1. `NetSamLite.Shared`의 `.csproj`에 **PackageReference가 여전히 0개**다. `IAgentClient`와 메시지 타입이 SignalR 타입을 참조하지 않는다.
2. **모든 Hub 메시지 record의 첫 필드가 `int SchemaVersion`** 이고(설계안 A), 양쪽의 모든 수신 핸들러가 **진입 직후 버전을 검사**한다. 버전 불일치 시 처리하지 않고 경고 로그를 남긴다.
3. `NetSamLite.Agent`가 `NetSamLite.Server`를, `NetSamLite.Server`가 `NetSamLite.Agent`를 **참조하지 않는다**(`grep`으로 `.csproj` 확인). 의존 방향 `Agent → Shared ← Server ← Web` 유지. **`NetSamLite.Server`(제품) 쪽에 SignalR 패키지가 추가되지 않았다**(공유 프레임워크로 충분) — 새 패키지는 `NetSamLite.Agent`와 `NetSamLite.Server.Tests` **두 곳에만** 들어간다.
4. `AgentHub`의 어떤 메서드도 **클라이언트가 보낸 `DeviceId`를 신뢰하지 않는다** — 모든 경로가 `GetAuthenticatedDeviceId()`(= `Context.User`의 클레임)만 쓴다. `grep`으로 Hub 안에 `message.DeviceId`를 **로그 외의 용도로** 쓰는 곳이 없음을 확인한다. **이 불변식은 시나리오 B의 실제 연결 테스트로 고정된다**(code-reviewer도 함께 확인).
5. 서버 DB에 **토큰 평문이 존재하지 않는다**: 등록 후 `DeviceTokens` 테이블을 조회해 `TokenHash`가 발급된 토큰 문자열과 다름을 확인한다.
6. **로그에 토큰이 남지 않는다**: 에이전트 로그 파일과 서버 콘솔 출력을 발급된 토큰 문자열로 검색해 0건.
7. `IAgentClient`에 선언된 메서드가 **조회성 명령뿐**이다 — 프로세스 종료/방화벽/레지스트리 쓰기/서비스 제어에 해당하는 메서드가 하나도 없다(「절대 규칙 2」).
8. `HubBackoffRetryPolicy.NextRetryDelay`가 **어떤 입력에도 `null`을 반환하지 않는다**(= 영구 포기 없음, 확정 결정 4).
9. **`DeviceConnectionTracker`에 시각 비교로 온라인을 추론하는 코드가 없다**(확정 결정 5). `IsOnline`을 바꾸는 곳은 `RecordConnectedAsync`와 `RecordDisconnectedAsync` **두 곳뿐**이고, `RecordHeartbeatAsync`는 `IsOnline`을 건드리지 않는다. (code-reviewer가 명시적으로 확인할 항목)
10. `Server/Program.cs`의 **기존 엔드포인트 11개 중 어느 것도 인증이 필요해지지 않았다** — 기존 Server 테스트 167개가 인증 관련 수정 없이 통과하는 것으로 확인된다.
11. `collect-now` 핸들러에서 **루프백 체크가 다른 모든 검사보다 먼저** 실행된다(비루프백 호출자가 404/409로 장치 존재 여부를 알아낼 수 없다).

### 동작 수준 (수동 확인)

CLAUDE.md 「Phase 7~10 위험 요소·테스트 방법」표의 Phase 7 행에 적힌 방법을 그대로 수행한다.

12. **Windows Sandbox 내 에이전트 → 호스트의 서버로 연결 확인**
    - Sandbox에서 에이전트를 실행하고 호스트의 서버(`:5136`)로 Hub 연결이 성립하는지 확인한다.
    - 대시보드 `장치 목록`에서 그 장치가 **즉시 온라인**으로 보이고(이벤트 기반이므로 하트비트를 기다리지 않는다), `마지막 확인` 시각이 하트비트 주기마다 갱신된다.
    - Sandbox를 쓰는 이유: 「절대 규칙 2」가 능동 조치를 금지하는 Phase지만, **SYSTEM 권한 서비스가 외부 채널을 여는 것 자체가 위험**이므로 처음 붙이는 실험은 스냅샷 가능한 환경에서 한다.
13. **서버 포트를 방화벽으로 막아 재연결/백오프 동작 확인**
    - 연결이 성립한 뒤 호스트 방화벽으로 `:5136`을 **차단** → 에이전트 로그에 재연결 시도가 **간격이 점점 늘어나며**(2초 → 4초 → 8초 → … → **60초에서 상한**) 기록되고, **포기 로그가 나오지 않는다**.
    - 로그에 찍힌 연속 대기 시간이 **매번 똑같지 않다**(jitter가 실제로 들어갔다는 증거).
    - **서버 쪽에서는 약 30초 안에(SignalR `ClientTimeoutInterval`) `OnDisconnectedAsync`가 호출되어 대시보드가 오프라인으로 바뀐다.** (= 클라이언트/네트워크 단절은 이벤트 기반 판정으로도 정상 반영된다는 확인)
    - 10분 이상 차단 상태를 유지한 뒤 **해제** → 사람이 손대지 않고 **60초 안에 자동 재연결**되고 대시보드가 다시 온라인으로 바뀐다.
    - 차단 구간 동안 **에이전트 서비스가 죽지 않는다**(`sc query NetSamLiteAgent`가 RUNNING, 주기 수집 루프도 계속 돈다).
14. **서버 먼저 끄고 에이전트 시작**(= 최초 연결 실패 경로): 서버를 내린 상태로 에이전트를 시작해도 서비스가 죽지 않고 백오프로 계속 시도하다가, 서버를 띄우면 자동으로 붙는다. (`WithAutomaticReconnect`만으로는 통과하지 못하는 시나리오 — 이 항목이 「설계 상세」의 `StartAsync` 래핑이 실제로 들어갔는지 검증한다.)
15. **에이전트 정상 종료 시 즉시 오프라인**: `sc stop NetSamLiteAgent`로 서비스를 멈추면 대시보드가 **곧바로**(타임아웃 대기 없이) 오프라인으로 바뀐다 — 이벤트 기반 판정의 핵심 이점 확인.
16. **잘못된 토큰 거부**: 토큰 파일을 임의 값으로 바꾸고 에이전트를 재시작 → Hub 연결이 **401로 거부**되고, 서버 로그에 거부 사유가 남으며, 그 장치는 대시보드에서 **온라인으로 표시되지 않는다**.
17. **"지금 수집" 왕복**: 대시보드 버튼 클릭 → 해당 PC의 에이전트 로그에 명령 수신과 수집 실행이 기록되고, 서버 DB의 해당 장치 `LastSeenUtc`가 **클릭 시각 기준으로 갱신**된다(= 기존 `POST /api/v1/inventory` 파이프라인을 실제로 탔다). 서버 로그에 `CollectNowResult`가 `Succeeded`로 남는다.
18. **`collect-now`의 루프백 제한이 실제로 동작한다**: 같은 PC에서(루프백) 호출하면 202/409가 정상적으로 나오고, **다른 PC나 LAN IP(예: `http://192.168.x.x:5136/...`)로 같은 요청을 보내면 403**이 돌아온다. 403일 때 응답 본문에 장치 존재 여부·온라인 여부가 **노출되지 않는다**.
19. **버튼 연타**: 수집이 도는 동안 버튼을 여러 번 눌러도 **수집이 중첩 실행되지 않고** `AlreadyRunning`이 로그에 남는다. 큐 디렉터리에 같은 리포트가 중복으로 쌓이지 않는다.
20. **오프라인 장치에 "지금 수집"**: 에이전트를 끈 장치의 버튼은 **비활성**이고, 그래도 엔드포인트를 직접 호출하면 **409**가 돌아온다(명령이 어디에도 쌓이지 않는다).
21. **서버 강제 종료 시의 stale 행이 「알려진 한계」대로 재현된다**(버그가 아니라 **문서화된 동작**임을 확인): 에이전트가 붙어 있는 상태에서 서버 프로세스를 **강제 종료**하고 서버를 다시 띄운 뒤, 대시보드에서 그 장치가 **여전히 "온라인"으로 표시되고 `마지막 확인` 시각만 과거에 멈춰 있는** 것을 확인한다. 이 상태가 「알려진 한계」 섹션의 서술과 일치하며, 에이전트가 다시 붙으면 정상으로 돌아온다.
22. **마이그레이션이 기존 데이터를 깨지 않는다**: 기존 `netsamlite.db` 사본에 마이그레이션을 적용한 뒤 기존 화면(장치 목록/SW 현황/라이선스/금지 SW)과 CSV 리포트가 전부 정상 동작한다. **Hub에 한 번도 붙지 않은 기존 장치들이 전부 "오프라인"으로 표시되고 예외가 나지 않는다.**

---

## 테스트 시나리오

단위/통합 테스트. 테스트 이름은 `메서드_상황_기대결과` 규칙을 따르고, Server 쪽은 기존 `SqliteTestDatabase`(실제 SQLite `:memory:`) 패턴을, Agent 쪽은 기존 가짜 구현(`FakeInventorySender` 등) 패턴을 재사용한다. **레지스트리/WMI에 직접 의존하는 테스트는 만들지 않는다.**

### A. 장치 토큰 — `tests/NetSamLite.Server.Tests/Realtime/DeviceTokenServiceTests.cs`

| 구분 | 테스트 | 내용 |
|---|---|---|
| **정상** | `IssueAsync_NewDevice_ReturnsPlaintextTokenAndStoresOnlyHash` | 발급된 평문 토큰이 비어 있지 않고, DB의 `TokenHash`가 **평문과 다르며**, 같은 평문을 다시 해시하면 저장값과 **일치**한다. 테이블 어느 컬럼에도 평문이 없다 |
| **정상** | `ValidateAsync_CorrectToken_ReturnsDeviceId` | 발급받은 토큰으로 검증하면 해당 `DeviceId`가 나온다 |
| **경계** | `IssueAsync_SameDeviceTwice_ReplacesHashAndOldTokenStopsWorking` | 재등록(재설치 복구 경로) 후 **새 토큰은 통과**, **이전 토큰은 거부**된다. `DeviceTokens` 행이 2개로 늘지 않는다(DeviceId가 PK) |
| **경계** | `IssueAsync_TwoDevices_ProduceDifferentTokensAndHashes` | 서로 다른 장치의 토큰·해시가 다르다(솔트 없는 해시를 쓰므로 "토큰 재사용 금지"가 실제로 지켜지는지 확인) |
| **예외** | `ValidateAsync_UnknownOrEmptyOrTamperedToken_ReturnsNull` | 미등록 토큰 / 빈 문자열 / 발급 토큰의 한 글자만 바꾼 값 → 전부 거부. 예외가 아니라 "실패"로 처리된다 |
| **예외** | `IssueAsync_WrongEnrollmentSecret_Rejected` | 등록 공통 비밀값이 틀리면 토큰을 발급하지 않는다(확정 결정 6) |

### B. 인증 핸들러 + Hub 사칭 방지 — `tests/NetSamLite.Server.Tests/Realtime/DeviceTokenAuthTests.cs`

기존 `WebApplicationFactory<TestEntryPoint>` 패턴(`InventoryEndpointTests` 등)을 재사용한다.

**갱신 2에서 확정된 범위**: 아래 표의 앞 3개는 **negotiate 레벨 HTTP 호출**로 검증하고(순수 `HttpClient`), 마지막 `Hub_WithTokenOfAnotherDevice_CannotActAsThatDevice`는 **실제 `HubConnection`으로 연결을 맺어** `OnConnectedAsync`까지 돌려 검증한다. 이를 위해 `NetSamLite.Server.Tests.csproj`에 **테스트 전용 `Microsoft.AspNetCore.SignalR.Client`** 를 추가한다(「수정할 파일」 7-A 참조).

- **왜 negotiate 레벨로만 끝내지 않는가**: `collect-now`를 포함한 Phase 7의 보안 모델 전체가 **"서버는 클레임의 `DeviceId`만 믿는다"** 는 단 하나의 불변식 위에 서 있다(CLAUDE.md Phase 7 위험 요소의 "인증 미비 시 임의 명령 주입"에 직접 대응). negotiate 응답은 200/401만 알려 주므로 **그 불변식을 전혀 건드리지 못한다.** "토큰 → 클레임 → Hub가 그 클레임으로 기록" 체인이 실제로 이어지는지는 연결이 성립해 봐야 알 수 있다.
- **`DeviceConnectionTracker` 단위 테스트로 대체할 수 없는 이유**: 그 테스트는 "`RecordConnectedAsync`에 넘긴 `deviceId`가 그대로 저장되는가"를 보는 것이고, **"`AgentHub`가 무엇을 넘기는가"** 는 보지 않는다. 즉 불변식이 깨지는 바로 그 지점(Hub가 메시지 바디의 `DeviceId`를 쓰는 실수)을 잡아내지 못한다.
- **TestServer에 `HubConnection`을 붙이는 방법**(developer 시간 절약용 메모):
  - 전송은 **LongPolling으로 고정**한다 — `TestServer`는 실제 TCP 소켓을 열지 않아 WebSocket 핸드셰이크가 되지 않는다.
  - `o.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();` 로 TestServer의 핸들러를 끼운다.
  - `o.Transports = HttpTransportType.LongPolling;`, `o.AccessTokenProvider = () => Task.FromResult<string?>(token);`
  - URL은 `factory.Server.BaseAddress`에 `AgentHubContract.Path`를 붙인다.
  - 이 테스트는 성격상 **통합 테스트**라 단위 테스트보다 느리다. 1~2개만 두고, 나머지는 negotiate 레벨/단위 테스트로 커버한다.
- **대안 경로(만약 TestServer + `HubConnection` 조합이 환경 문제로 끝까지 동작하지 않으면)**: 패키지를 추가하지 말고, `AgentHub`를 **직접 인스턴스화해 단위 테스트**한다 — 가짜 `HubCallerContext`(클레임이 담긴 `ClaimsPrincipal`과 임의 `ConnectionId`)와 가짜 `IGroupManager`를 주입하고, `SendHeartbeatAsync(new HeartbeatMessage(..., DeviceId: "OTHER-DEVICE", ...))`를 호출해 **tracker에 기록된 값이 클레임 쪽 `DeviceId`인지** 단정한다. 이 경로도 불변식을 직접 고정하므로 허용된다. **단, "tracker 단위 테스트 + 코드 리뷰"로만 대체하는 것은 허용하지 않는다**(위 이유). 어느 경로를 택했는지 테스트 클래스 주석에 한 줄 남긴다.

| 구분 | 테스트 | 내용 |
|---|---|---|
| **정상** | `Hub_WithValidTokenInAuthorizationHeader_Authenticates` | `Authorization: Bearer <토큰>`으로 negotiate 요청이 통과한다(200) |
| **정상** | `Hub_WithValidTokenInAccessTokenQueryString_Authenticates` | `?access_token=<토큰>`으로도 통과한다(WebSocket 전송 경로 등가성) |
| **경계** | `ExistingAnonymousEndpoints_WithoutToken_StillReturnSuccess` | `/api/v1/devices`, `/api/v1/licenses` 등 **기존 엔드포인트는 토큰 없이도 그대로 동작**한다(인증 도입이 기존 계약을 깨지 않았다는 회귀 테스트) |
| **예외** | `Hub_WithoutToken_Returns401` | 토큰 없이 Hub에 붙으면 401 |
| **예외** | `Hub_WithTokenOfAnotherDevice_CannotActAsThatDevice` | **실제 연결 테스트.** 장치 A·B를 등록하고 **B의 토큰으로** 연결한 뒤, 메시지 바디에는 `DeviceId = "A"`를 담아 보낸다. 검증: `DeviceConnection` 테이블에 **B만** 온라인으로 기록되고 **A는 기록되지 않는다**(행이 없거나 오프라인). = 클레임 기반 `DeviceId`의 핵심 보증 |

### C. 재연결 정책 — `tests/NetSamLite.Agent.Tests/Realtime/HubBackoffRetryPolicyTests.cs`

| 구분 | 테스트 | 내용 |
|---|---|---|
| **정상** | `NextRetryDelay_IncreasingRetryCount_GrowsExponentiallyWithinJitterBounds` | 재시도 횟수 0,1,2,3…에 대해 반환값이 `[base×2^n, base×2^n × (1+jitter비율)]` 범위 안에서 증가한다 |
| **경계** | `NextRetryDelay_LargeRetryCount_NeverExceedsSixtySeconds` | 재시도 횟수를 100, 1000처럼 크게 줘도 **60초 상한**을 넘지 않는다(지수 계산 오버플로로 음수/`TimeSpan.MaxValue`가 되지 않는다) |
| **경계** | `NextRetryDelay_FirstRetry_IsNotZero` | 첫 재시도 간격이 0이 아니다(즉시 재시도 폭주 방지) |
| **경계** | `NextRetryDelay_RepeatedCallsAtSameRetryCount_ProduceDifferentValues` | 같은 횟수로 여러 번 불러도 값이 전부 동일하지 않다(jitter가 실제로 적용됨) |
| **예외** | `NextRetryDelay_AnyRetryCount_NeverReturnsNull` | **어떤 입력에도 `null`을 반환하지 않는다** — 확정 결정 4(영구 포기 금지)를 테스트로 못박는다 |
| **예외** | `NextRetryDelay_ZeroOrNegativeConfiguredDelays_ClampsToMinimum` | `ReconnectBaseDelaySeconds`가 0이나 음수로 설정돼도(기존 `AgentWorker`의 `Math.Max(1, ...)` 방어와 같은 성질) 예외 없이 최솟값으로 클램프된다 |

### D. 토큰 저장소 — `tests/NetSamLite.Agent.Tests/Realtime/DeviceTokenStoreTests.cs`

기존 `PendingReportQueueTests`의 임시 디렉터리 패턴을 재사용한다.

| 구분 | 테스트 | 내용 |
|---|---|---|
| **정상** | `GetOrEnrollAsync_TokenFileExists_ReturnsStoredTokenWithoutEnrolling` | 파일이 있으면 그 값을 쓰고 **enroll 호출을 하지 않는다**(가짜 enroll 클라이언트의 호출 횟수 0) |
| **정상** | `GetOrEnrollAsync_NoTokenFile_EnrollsAndPersistsToken` | 파일이 없으면 enroll 1회 호출 후 파일로 저장하고, 다음 호출에서는 enroll 없이 같은 값을 돌려준다 |
| **경계** | `GetOrEnrollAsync_EmptyOrWhitespaceTokenFile_TreatsAsMissingAndEnrolls` | 빈 파일/공백만 있는 파일을 "없음"으로 취급해 재등록한다(`PendingReportQueue`의 "읽을 수 없는 파일은 건너뛴다" 가드와 같은 성질) |
| **예외** | `GetOrEnrollAsync_EnrollmentFails_ThrowsOrReturnsNullWithoutWritingFile` | enroll이 실패하면 **쓰레기 토큰 파일을 만들지 않는다**(다음 시도에서 깨진 토큰으로 영구히 401 나는 상황 방지) |

### E. 수집 직렬화 — `tests/NetSamLite.Agent.Tests/Realtime/InventoryCollectionRunnerTests.cs`

| 구분 | 테스트 | 내용 |
|---|---|---|
| **정상** | `TryRunAsync_NoOtherRunInProgress_CollectsAndDeliversOnce` | 수집기 1회 호출, `ReportDeliveryService` 경유 전송 1회, 결과 `Succeeded` |
| **경계** | `TryRunAsync_WhileAnotherRunInProgress_ReturnsAlreadyRunningWithoutCollecting` | 첫 실행을 붙잡아 둔 상태(가짜 sender가 완료를 지연)에서 두 번째 호출이 **대기하지 않고 즉시** `AlreadyRunning`을 반환하고, **수집기를 호출하지 않는다** |
| **경계** | `TryRunAsync_AfterPreviousRunCompletes_CanRunAgain` | 첫 실행이 끝난 뒤 다시 호출하면 정상 실행된다(세마포가 해제됐다) |
| **예외** | `TryRunAsync_CollectorThrows_ReleasesSemaphoreAndReturnsFailed` | 수집기가 예외를 던져도 **세마포가 영구 점유되지 않는다**(이후 호출이 `AlreadyRunning`에 영원히 걸리지 않는다). 결과는 `Failed` |
| **예외** | `TryRunAsync_Cancelled_DoesNotLeaveSemaphoreHeld` | `CancellationToken` 취소 시에도 세마포가 해제된다 |

### F. 연결 상태 추적 — `tests/NetSamLite.Server.Tests/Realtime/DeviceConnectionTrackerTests.cs`

**확정 결정 5(이벤트 기반)로 전면 교체된 시나리오다.** 하트비트 타임아웃 경계를 검증하던 초안의 두 테스트(`ListStatuses_HeartbeatExactlyAtTimeoutBoundary_IsOffline`, `ListStatuses_StaleConnectionRowWithoutDisconnect_IsOffline`)는 새 설계에서 **성립하지 않으므로 제거**하고, 대신 **"하트비트는 온라인 불값을 바꾸지 않는다"** 와 **"stale 행은 알려진 한계다"** 를 고정하는 테스트로 바꾼다.

| 구분 | 테스트 | 내용 |
|---|---|---|
| **정상** | `RecordConnectedAsync_MarksDeviceOnlineAndStampsLastHeartbeat` | 연결 직후 `IsOnline == true`, `LastConnectedAtUtc`와 `LastHeartbeatUtc`가 **둘 다** 찍힌다(첫 하트비트를 기다리는 동안 화면이 비지 않도록) |
| **정상** | `RecordDisconnectedAsync_MarksDeviceOfflineImmediately` | 해제 이벤트로 **즉시** `IsOnline == false`, `LastDisconnectedAtUtc`가 기록된다. 시간 경과를 기다리지 않는다 |
| **정상** | `RecordHeartbeatAsync_DoesNotChangeOnlineFlag_OnlyUpdatesLastHeartbeatUtc` | **확정 결정 5의 핵심 계약.** 온라인 상태에서 하트비트를 받으면 `LastHeartbeatUtc`만 바뀌고 `IsOnline`은 그대로 `true`. 오프라인으로 기록된 장치에 하트비트가 와도 `IsOnline`이 `true`로 **되살아나지 않는다**(해제 후 지연 도착한 하트비트가 상태를 뒤집지 않는다) |
| **경계** | `RecordConnectedAsync_SameDeviceReconnectsWithNewConnectionId_UpdatesConnectionIdAndStaysOnline` | 재연결로 `ConnectionId`가 바뀌어도 행이 중복되지 않고(DeviceId가 PK) 온라인이 유지된다 |
| **경계** | `ListStatusesAsync_VeryOldLastHeartbeatButStillFlaggedOnline_StillReportsOnline` | **「알려진 한계」를 고정하는 테스트.** `LastHeartbeatUtc`가 몇 시간 전이어도 `IsOnline == true`면 **온라인으로 보고한다**(= 타임아웃 판정 로직이 들어오지 않았음을 증명). 누가 나중에 임계값 판정을 몰래 추가하면 이 테스트가 깨진다 |
| **경계** | `NoDisconnectEvent_DeviceStaysOnlineForever_KnownLimitation` | 연결 기록만 있고 해제 이벤트가 없는(서버 크래시 재현) 행은 시간이 얼마나 지나도 온라인으로 남는다. **이것이 버그가 아니라 문서화된 트레이드오프**임을 테스트와 주석으로 명시한다(「알려진 한계」 링크) |
| **예외** | `ListStatusesAsync_DeviceWithInventoryButNeverConnected_IsOfflineNotMissing` | 수집은 됐지만 Hub에 한 번도 붙지 않은 장치(`DeviceConnection` 행 없음)가 **예외 없이** 오프라인으로 표시된다(기존 장치 목록이 깨지지 않는다 — 마이그레이션 직후 모든 장치가 이 상태다) |

### G. "지금 수집" 엔드포인트 — `tests/NetSamLite.Server.Tests/Realtime/CollectNowEndpointTests.cs`

`IHubContext`는 테스트 대역으로 교체하고, 원격 IP는 `WebApplicationFactory`의 서버 기능(또는 요청 파이프라인에 `RemoteIpAddress`를 설정하는 테스트용 미들웨어)으로 조작한다.

| 구분 | 테스트 | 내용 |
|---|---|---|
| **정상** | `CollectNow_FromLoopbackAndDeviceOnline_SendsCommandAndReturns202` | 루프백 호출 + 온라인 장치 → `IHubContext`로 명령이 **1회** 전송되고 202. 전송된 `CollectNowCommand`의 `SchemaVersion`이 `AgentHubContract.CurrentSchemaVersion`과 같고 `CommandId`가 비어 있지 않다 |
| **경계** | `CollectNow_FromIpv6Loopback_IsAllowed` | `::1`에서 온 요청도 허용된다(`IPAddress.IsLoopback` 사용 확인 — 문자열로 `"127.0.0.1"`만 비교하면 실패하는 케이스) |
| **경계** | `CollectNow_DeviceOffline_Returns409AndSendsNothing` | `IsOnline == false`인 장치는 409이고 **명령이 전송되지 않는다** |
| **예외** | `CollectNow_FromNonLoopbackAddress_Returns403` | LAN IP(예: `192.168.1.10`)에서 온 요청은 **403**이고 명령이 전송되지 않는다(확정 결정 3) |
| **예외** | `CollectNow_FromNonLoopbackAddress_DoesNotRevealDeviceExistence` | 비루프백 호출은 **장치가 있든 없든, 온라인이든 아니든 항상 403**이다(404/409로 갈리지 않는다 — 루프백 체크가 가장 먼저 실행된다는 증거) |
| **예외** | `CollectNow_RemoteIpAddressIsNull_Returns403` | `RemoteIpAddress`가 `null`인 요청도 거부된다("모르면 막는다") |
| **예외** | `CollectNow_UnknownDeviceId_Returns404` | 루프백 호출이지만 DB에 없는 `DeviceId`면 404 |

---

## 열린 질문

**없다.** 초안의 열린 질문 4개는 2026-11-12에 모두 확정되었다.

| 초안의 열린 질문 | 결론 |
|---|---|
| 1. Hub 계약 설계안 A/B/C | **A 확정** (확정 결정 7). B/C는 「검토 기록」으로만 보존 |
| 2. `collect-now` 접근 통제 | **(c) localhost 전용 허용 확정** (확정 결정 3). 비루프백은 403 |
| 3. 하트비트 주기 60초 / 타임아웃 180초 | 주기는 **60초 유지**. **타임아웃 값은 폐기** — 확정 결정 5로 온라인 판정에 쓰이지 않게 되어 `RealtimeOptions` 자체를 만들지 않는다 |
| 4. 등록 비밀값 방식 | **공통 비밀값 1개 + user-secrets/환경변수 확정** (확정 결정 6). 장치별 분리·회전·승인 큐는 backlog |

구현 착수(단계 7-A) 전에 더 받아야 할 결정은 없다.

### 구현 중 발생한 질의 처리 이력

| 날짜 | 질의 | 결론 |
|---|---|---|
| 2026-11-12 | 7-A 시나리오 B의 "장치 사칭 불가"를 실제 Hub 연결로 검증하려면 `NetSamLite.Server.Tests.csproj`에 `Microsoft.AspNetCore.SignalR.Client`가 필요한데, 기획서 파일 목록에 없음 (developer) | **패키지 추가 승인**(테스트 전용 1줄). 「수정할 파일」 7-A에 `NetSamLite.Server.Tests.csproj` 행을 추가하고 파일 수를 27 → 28로 갱신. negotiate 레벨 검증만으로 축소하는 안은 **채택하지 않음** — "서버는 클레임의 `DeviceId`만 믿는다"는 불변식을 negotiate 응답으로는 검증할 수 없고, tracker 단위 테스트는 "Hub가 무엇을 넘기는가"를 보지 못한다. TestServer 환경 문제로 실제 연결이 끝까지 안 되면 **가짜 `HubCallerContext`로 `AgentHub`를 직접 단위 테스트**하는 대안 경로를 허용(시나리오 B 참조) |
