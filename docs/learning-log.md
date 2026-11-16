# 학습 정리

## 2026-11-10 — 왜 `ProductNameKey` 컬럼 방식이 SQLite `NOCASE` 콜레이션보다 나은가

1. **`NOCASE`는 ASCII 전용이라 한글 제품명(예: "카카오톡")에는 전혀 효과가 없다.** 이 프로젝트는 설치 SW 이름의 상당수가 한글이라, 영어 대소문자만 다루는 콜레이션으로는 애초에 커버 범위가 안 맞는다.
2. **콜레이션은 "대소문자"만 해결하고, 앞뒤 공백·연속 공백 문제는 못 해결한다.** `ProductNameKey`는 `Trim` + 공백 축약 + `ToUpperInvariant`를 한 함수(`ComputeKey`)에서 같이 처리해서, "`Zoom`" / "`zoom`" / "` Zoom `" / "`Zoom  Meeting`"을 전부 같은 키로 모은다 — 콜레이션 하나로는 이 중 공백 관련 케이스를 못 잡는다.
3. **DB 콜레이션은 "DB가 비교하는 방식"일 뿐이라, 앱 코드가 그 규칙을 모르면 쉽게 따로 노는 두 기준이 생긴다.** `ProductNameKey`처럼 별도 컬럼에 정규화 결과를 저장하면, 앱의 중복 판정 로직과 DB 유니크 인덱스가 **똑같은 C# 함수의 결과값**을 보게 되어 기준이 어긋날 일이 없다(실제로 이번에 "등록은 `ProductNameKey` 기준인데 알림 매칭은 원본 `ProductName` 기준"이라 공백이 불규칙한 이름은 알림이 안 뜨는 버그가 났었고, 이걸 고치면서 이 교훈을 직접 겪었다).

## 2026-11-11 — Phase 6(서비스화)에서 나온 개념 3가지

### Head-of-line blocking (맨 앞 항목이 뒤를 다 막는 현상)
한 줄로 선 줄에서 맨 앞 사람이 일을 못 끝내면 뒤에 선 사람들이 전부 못 나아가는 상황. `ReportDeliveryService`가 처음엔 "큐의 오래된 리포트 하나가 계속 전송 실패하면, 그보다 새로운 리포트들은 영원히 전송 시도조차 못 받는" 구조였다 — 순서를 지키려고 "앞이 성공해야 다음으로 넘어간다"는 규칙을 걸었기 때문이다. 그 자체는 "서버 쪽 이력이 거꾸로 기록되는 걸 막는다"는 올바른 설계였지만, 그 앞 항목이 *영원히* 실패하는 경우(예: 서버가 그 데이터 형식을 거부하는 버그)까지 대비하지 않으면 큐 전체가 멈춘다. 해결책은 "포기할 기준(재시도 상한)"을 정해서, 그 기준을 넘긴 항목만 큐에서 빼내 뒤가 막히지 않게 하는 것 — 이게 바로 아래 dead-letter 큐다.

### 원자적 파일 쓰기 (Atomic file write)
"쓰는 중간 상태가 절대 보이지 않는" 쓰기 방식. `File.WriteAllTextAsync(path, ...)`를 최종 경로에 직접 하면, 쓰는 도중 프로세스가 죽을 때 반쪼가리 JSON 파일이 그 자리에 남는다 — 나중에 그 파일을 읽으면 깨진 JSON이라 파싱 실패한다. 해결 패턴: ①임시 파일(`path.tmp`)에 전부 쓰고 ②`File.Move(tempPath, path, overwrite: true)`로 교체. 같은 볼륨 안에서 `File.Move`는 파일시스템 수준에서 원자적이라, 그 순간 프로세스가 죽어도 최종 경로에는 "쓰기 전 상태" 아니면 "쓰기 후 상태" 둘 중 하나만 있고 중간 상태는 절대 없다.

### Dead-letter 큐 (죽은 편지 보관함)
우편 시스템에서 배달 불가능한 편지를 모아두는 곳에서 따온 이름. 메시지 큐 시스템에서 "정상 처리 경로로는 더 이상 처리할 수 없는 항목"을 따로 격리해두는 별도 저장소를 뜻한다. `PendingReportQueue.RecordFailureAsync`가 같은 리포트가 `MaxQueueSendFailures`번 연속 실패하면 `failed/` 폴더로 옮기는 게 바로 이것 — "포기"가 "무한 재시도로 전체를 막는 것"보다 나은 선택일 때가 있다는 인정이다. 포기한 데이터를 아예 삭제하지 않고 별도 폴더에 보존해두는 이유는, 나중에 사람이 왜 실패했는지 들여다보고 필요하면 수동으로 재처리할 수 있게 하기 위해서다.

## 2026-11-16 — Phase 7(SignalR 실시간 채널)에서 배운 개념들

### 강타입 Hub(`Hub<IAgentClient>`)와 메시지별 `SchemaVersion`
SignalR의 Hub는 기본적으로 메서드명을 문자열로 호출하는 느슨한 타입 시스템이지만, `Hub<T>` 제네릭을 쓰면 서버→클라이언트 호출(`IHubContext<THub, T>.Clients.Group(...).XxxAsync(...)`)이 컴파일 시점에 메서드명·인자 타입을 검증받는다. 여기에 각 메시지 record의 첫 필드로 `int SchemaVersion`을 두는 설계(설계안 A)를 더하면, "버전"이 메시지 전체를 감싸는 봉투가 아니라 메시지 자신의 일부가 되어 메시지별로 독립적으로 버전을 올릴 수 있고, 감사 로그에 메시지를 그대로 저장해도 버전 정보가 유실되지 않는다.

### `IRetryPolicy`와 "최초 연결은 자동 재연결 대상이 아니다"
SignalR의 `WithAutomaticReconnect(IRetryPolicy)`는 **이미 한 번 성립했던 연결이 끊겼을 때만** 재시도 정책을 적용한다. `HubConnection.StartAsync()` 자체가 처음부터 실패하면(서버가 아직 안 떠 있는 상태 — 서비스 자동 시작 시 매우 흔하다) 자동 재연결 메커니즘은 전혀 개입하지 않고 예외가 그대로 올라온다. 그래서 "무한 재시도, 포기 없음"을 보장하려면 최초 연결 자체를 같은 백오프 정책으로 감싼 별도 루프(`AgentHubWorker.ConnectWithBackoffAsync`) 안에서 시도해야 한다 — 재연결 정책 하나만 걸어두면 "서버가 늦게 뜨는" 가장 흔한 운영 상황을 놓친다.

### WebSocket 핸드셰이크의 헤더 제약과 `access_token` 쿼리 파라미터
브라우저의 WebSocket API는 핸드셰이크 요청에 임의의 커스텀 헤더(`Authorization` 등)를 실을 수 없다. SignalR 클라이언트는 이 제약을 알고 있어서, WebSocket/SSE 전송을 쓸 때는 `AccessTokenProvider`로 받은 토큰을 `Authorization` 헤더가 아니라 URL의 `access_token` 쿼리 파라미터로 보낸다. 서버 쪽 인증 핸들러가 헤더만 읽으면 롱폴링(헤더 가능)에서는 통과하고 WebSocket(헤더 불가, 쿼리만 가능)에서는 이유 없이 401이 나는, 전송 방식에 따라 증상이 갈리는 디버깅하기 까다로운 문제가 생긴다. 쿼리 스트링 허용을 Hub 경로에만 한정하는 이유는, 다른 일반 API 경로에서는 URL에 토큰이 남아 로그 등에 노출될 위험을 넓히지 않기 위해서다.

### 고엔트로피 토큰에는 SHA-256 같은 빠른 해시로 충분하다
비밀번호처럼 사람이 만드는 저엔트로피 입력은 사전 공격(rainbow table 등)에 취약해 PBKDF2/Argon2 같은 "일부러 느린" KDF가 필요하다. 반면 `RandomNumberGenerator.GetBytes(32)`로 만든 256비트 난수는 해시가 털려도 역산할 탐색 공간이 사실상 무한하므로, 느린 KDF는 매 연결마다 CPU만 태울 뿐 보안 이득이 없다. 다만 이 토큰 해시 방식은 솔트가 없어 "같은 토큰이면 같은 해시"라는 성질이 남으므로, 토큰을 장치마다 새로 발급하고 재사용하지 않는 전제가 반드시 지켜져야 한다.

### `OnDisconnectedAsync`가 클라이언트 단절은 잡지만 서버 크래시는 못 잡는 이유
`OnDisconnectedAsync`는 **서버 프로세스가 살아서 그 콜백을 실행할 수 있을 때만** 호출된다. 클라이언트 쪽 단절(서비스 중지, PC 종료, 네트워크 차단)은 SignalR 서버의 자체 keep-alive(`HubOptions.ClientTimeoutInterval`, 기본 30초)가 감지해 정상적으로 `OnDisconnectedAsync`를 호출해 준다. 하지만 **서버 프로세스 자신이** 강제 종료되거나 크래시하면, 그 순간 "온라인"으로 기록돼 있던 행을 아무도 `false`로 되돌려 주지 않는다 — 되돌리는 코드 자체가 그 죽은 프로세스 안에 있었기 때문이다. 이벤트 기반(연결/해제 즉시 반영) 설계를 고르면 "즉시성"을 얻는 대신 이 stale 행 가능성을 감수해야 하고, 타임아웃 기반 판정을 고르면 그 반대가 된다 — 어느 쪽이 맞는지는 "즉시성이 중요한가, 서버 크래시 복원력이 중요한가"의 트레이드오프 문제다.

### `IPAddress.IsLoopback`으로 운영자 전용 엔드포인트를 제한하는 패턴
운영자(대시보드)만 호출해야 하는 엔드포인트를 "이 PC에서 온 요청만 받는다"로 제한할 때, 문자열로 `RemoteIpAddress.ToString() == "127.0.0.1"`을 비교하면 Kestrel이 IPv6(`::1`)로 수신하는 흔한 상황에서 정상 요청까지 걸러버린다. `IPAddress.IsLoopback(remoteIp)`는 IPv4 `127.0.0.0/8`과 IPv6 `::1`을 모두 올바르게 처리해 준다. `RemoteIpAddress`가 `null`인 경우(유닉스 소켓 등 비TCP 전송)도 "모르면 막는다" 원칙으로 거부해야, 판정 불가능한 상황이 자동으로 "허용"으로 새지 않는다.

### `TestServer`에 `HubConnection`을 붙이는 방법 — LongPolling + `HttpMessageHandlerFactory`
`WebApplicationFactory`/`TestServer`는 실제 TCP 소켓을 열지 않는 인메모리 테스트 서버라, WebSocket 핸드셰이크 자체가 성립하지 않는다. 실제 Hub 연결로 통합 테스트를 하려면 전송을 `HttpTransportType.LongPolling`으로 고정하고, `HttpConnectionOptions.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();`로 TestServer의 인메모리 핸들러를 끼워 넣어야 한다. 이렇게 하면 실제 네트워크 없이도 `OnConnectedAsync`/Hub 메서드 호출까지 포함한 전체 인증·라우팅 경로를 진짜로 태울 수 있다 — negotiate 응답(200/401)만 보는 것보다 훨씬 강한 검증이 된다.
