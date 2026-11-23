namespace NetSamLite.Shared.Hub;

/// <summary>
/// 에이전트↔서버 Hub 연결 경로와 현재 계약 버전. Shared는 패키지 참조가 0개를
/// 유지해야 하므로(의존 방향 Agent → Shared ← Server), 여기 선언된 타입은
/// SignalR 패키지를 참조하지 않는 평범한 record/interface다.
/// </summary>
public static class AgentHubContract
{
    public const string Path = "/hubs/agent";
    public const int CurrentSchemaVersion = 1;
}

/// <summary>
/// 서버 → 에이전트 방향의 강타입 Hub 클라이언트 인터페이스(Hub&lt;IAgentClient&gt;).
/// 조회성 명령만 선언한다 — 프로세스 종료/방화벽/레지스트리 쓰기 등 능동 조치는
/// Phase 7에서 금지된다(CLAUDE.md 절대 규칙 2).
/// </summary>
public interface IAgentClient
{
    Task CollectNowAsync(CollectNowCommand command);
}

/// <summary>
/// 서버가 "지금 수집"을 트리거할 때 보내는 명령. CommandId로 결과(CollectNowResult)와 짝지어진다.
/// </summary>
public sealed record CollectNowCommand(int SchemaVersion, string CommandId, DateTime IssuedAtUtc);

/// <summary>
/// 에이전트가 주기적으로 보내는 생존 신호. DeviceId는 로그·디버깅용 참고값일 뿐이며,
/// 서버는 이 값을 신뢰하지 않고 항상 인증 클레임의 DeviceId로 동작한다.
/// </summary>
public sealed record HeartbeatMessage(int SchemaVersion, string DeviceId, string AgentVersion, DateTime SentAtUtc);

public sealed record HeartbeatAck(int SchemaVersion, DateTime ServerTimeUtc);

public sealed record CollectNowResult(int SchemaVersion, string CommandId, CollectNowOutcome Outcome, string? Detail);

public enum CollectNowOutcome
{
    Succeeded,
    AlreadyRunning,
    Failed,
}
