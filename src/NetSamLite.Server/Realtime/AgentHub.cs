using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using NetSamLite.Shared.Hub;

namespace NetSamLite.Server.Realtime;

/// <summary>
/// 에이전트↔서버 상시 연결 Hub. 강타입 Hub&lt;IAgentClient&gt;를 쓰는 이유: 서버→에이전트
/// 메서드명·인자 타입이 컴파일 시점에 검증되고, Hub 계약(설계안 A)의 메시지별 SchemaVersion과
/// 결이 같다. DeviceId는 클라이언트가 보낸 어떤 값도 신뢰하지 않고 항상 인증된 클레임에서만
/// 읽는다 — 인증 미비 시 다른 장치를 사칭해 명령 주입을 할 수 있는 위험에 대한 직접적 대응이다.
/// </summary>
[Authorize(AuthenticationSchemes = DeviceTokenAuthenticationHandler.SchemeName)]
public sealed class AgentHub(DeviceConnectionTracker tracker, ILogger<AgentHub> logger) : Hub<IAgentClient>
{
    public override async Task OnConnectedAsync()
    {
        var deviceId = GetAuthenticatedDeviceId();
        // 장치별 그룹에 넣는 이유: 같은 장치가 재연결로 ConnectionId가 바뀌어도
        // "그 장치로 보내기"(IHubContext.Clients.Group(deviceId))가 유지된다.
        await Groups.AddToGroupAsync(Context.ConnectionId, deviceId);
        await tracker.RecordConnectedAsync(deviceId, Context.ConnectionId, Context.ConnectionAborted);
        logger.LogInformation("장치 연결됨: {DeviceId}", deviceId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var deviceId = GetAuthenticatedDeviceId();
        // 이미 연결이 끊긴 뒤라 Context.ConnectionAborted를 쓰면 즉시 취소된 토큰이 된다.
        await tracker.RecordDisconnectedAsync(deviceId, CancellationToken.None);
        logger.LogInformation("장치 연결 해제됨: {DeviceId}", deviceId);
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Hub에 클라이언트 입력이 처음 생기는 지점이다. HeartbeatMessage.DeviceId는 로그에만
    /// 쓰는 참고값일 뿐이고, 기록은 항상 GetAuthenticatedDeviceId()(인증 클레임)로 한다 —
    /// 메시지 바디로 다른 장치를 사칭해 그 장치의 LastHeartbeatUtc를 갱신할 수 없다.
    /// </summary>
    public async Task<HeartbeatAck> SendHeartbeatAsync(HeartbeatMessage message)
    {
        if (message.SchemaVersion != AgentHubContract.CurrentSchemaVersion)
        {
            logger.LogWarning(
                "하트비트 SchemaVersion 불일치, 처리하지 않음: {DeviceId}, 수신={Received}, 기대={Expected}",
                message.DeviceId, message.SchemaVersion, AgentHubContract.CurrentSchemaVersion);
            return new HeartbeatAck(AgentHubContract.CurrentSchemaVersion, DateTime.UtcNow);
        }

        var claimDeviceId = GetAuthenticatedDeviceId();
        if (!string.Equals(claimDeviceId, message.DeviceId, StringComparison.Ordinal))
        {
            logger.LogWarning(
                "하트비트 바디의 DeviceId가 인증 클레임과 다름(무시하고 클레임 값 사용): {ClaimDeviceId} != {BodyDeviceId}",
                claimDeviceId, message.DeviceId);
        }

        await tracker.RecordHeartbeatAsync(claimDeviceId, Context.ConnectionAborted);
        return new HeartbeatAck(AgentHubContract.CurrentSchemaVersion, DateTime.UtcNow);
    }

    /// <summary>
    /// 에이전트가 "지금 수집" 실행 결과를 보고한다. DeviceId도 인증 클레임에서만 읽어
    /// 구조적 로그에 남긴다 — 감사 로그 이중 기록(서버+로컬 파일)은 Phase 8에서 다룬다.
    /// </summary>
    public Task ReportCollectNowResultAsync(CollectNowResult result)
    {
        if (result.SchemaVersion != AgentHubContract.CurrentSchemaVersion)
        {
            logger.LogWarning(
                "CollectNowResult SchemaVersion 불일치, 처리하지 않음: 수신={Received}, 기대={Expected}",
                result.SchemaVersion, AgentHubContract.CurrentSchemaVersion);
            return Task.CompletedTask;
        }

        var deviceId = GetAuthenticatedDeviceId();
        logger.LogInformation(
            "지금 수집 결과 수신: {DeviceId}, {CommandId}, {Outcome}", deviceId, result.CommandId, result.Outcome);
        return Task.CompletedTask;
    }

    private string GetAuthenticatedDeviceId() =>
        Context.User?.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("인증된 DeviceId 클레임이 없는 연결입니다.");
}
