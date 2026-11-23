using Microsoft.EntityFrameworkCore;
using NetSamLite.Server.Data;
using NetSamLite.Server.Data.Entities;

namespace NetSamLite.Server.Realtime;

public sealed record DeviceConnectionStatus(
    string DeviceId,
    bool IsOnline,
    DateTime? LastConnectedAtUtc,
    DateTime? LastHeartbeatUtc,
    DateTime? LastDisconnectedAtUtc);

/// <summary>
/// Hub 연결/해제/하트비트를 DB에 반영하고 장치별 상태를 조회한다. 온라인 판정은 순수
/// 이벤트 기반이다(확정 결정 5) — 하트비트 타임아웃으로 오프라인을 추론하는 로직을
/// 여기에 넣지 않는다. IsOnline을 바꾸는 곳은 RecordConnectedAsync/RecordDisconnectedAsync
/// 두 곳뿐이다. 한 번도 Hub에 붙지 않은 장치는 행이 없으므로 "오프라인"으로 취급하고
/// 예외를 던지지 않는다(마이그레이션 직후 모든 기존 장치가 이 상태다).
/// </summary>
public sealed class DeviceConnectionTracker(NetSamLiteDbContext dbContext)
{
    public async Task RecordConnectedAsync(string deviceId, string connectionId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var entity = await dbContext.DeviceConnections.FindAsync([deviceId], cancellationToken);
        if (entity is null)
        {
            entity = new DeviceConnectionEntity { DeviceId = deviceId };
            dbContext.DeviceConnections.Add(entity);
        }

        entity.ConnectionId = connectionId;
        entity.IsOnline = true;
        entity.LastConnectedAtUtc = now;
        // 첫 하트비트가 오기까지(최대 60초) 화면의 "마지막 확인" 칸이 비어 보이는 것을
        // 막기 위해 연결 시점에도 LastHeartbeatUtc를 같이 찍는다.
        entity.LastHeartbeatUtc = now;

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordDisconnectedAsync(string deviceId, CancellationToken cancellationToken)
    {
        var entity = await dbContext.DeviceConnections.FindAsync([deviceId], cancellationToken);
        if (entity is null)
        {
            // 연결 기록이 없는 장치의 해제 이벤트는 정상적으로는 발생하지 않는다 — 조용히 무시.
            return;
        }

        entity.IsOnline = false;
        entity.LastDisconnectedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// LastHeartbeatUtc만 갱신한다. IsOnline은 건드리지 않는다(확정 결정 5) — 해제 후
    /// 지연 도착한 하트비트가 이미 false로 기록된 상태를 다시 true로 뒤집지 않는다.
    /// </summary>
    public async Task RecordHeartbeatAsync(string deviceId, CancellationToken cancellationToken)
    {
        var entity = await dbContext.DeviceConnections.FindAsync([deviceId], cancellationToken);
        if (entity is null)
        {
            // 연결 기록이 없는 장치의 하트비트는 정상적으로는 발생하지 않는다 — 조용히 무시.
            return;
        }

        entity.LastHeartbeatUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DeviceConnectionStatus>> ListStatusesAsync(CancellationToken cancellationToken) =>
        await dbContext.DeviceConnections
            .AsNoTracking()
            .Select(c => new DeviceConnectionStatus(
                c.DeviceId, c.IsOnline, c.LastConnectedAtUtc, c.LastHeartbeatUtc, c.LastDisconnectedAtUtc))
            .ToListAsync(cancellationToken);
}
