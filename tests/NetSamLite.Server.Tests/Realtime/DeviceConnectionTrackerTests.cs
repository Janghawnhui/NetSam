using FluentAssertions;
using NetSamLite.Server.Data.Entities;
using NetSamLite.Server.Realtime;
using NetSamLite.Server.Tests.Inventory;

namespace NetSamLite.Server.Tests.Realtime;

/// <summary>
/// 확정 결정 5(이벤트 기반 온라인 판정)를 고정하는 테스트. 하트비트 타임아웃으로 오프라인을
/// 추론하던 초안의 시나리오는 더 이상 성립하지 않으므로 전부 제거됐고, 대신 "하트비트는
/// IsOnline을 바꾸지 않는다"와 "stale 행은 알려진 한계다"를 고정한다.
/// </summary>
public sealed class DeviceConnectionTrackerTests
{
    [Fact]
    public async Task RecordConnectedAsync_MarksDeviceOnlineAndStampsLastHeartbeat()
    {
        using var db = new SqliteTestDatabase();
        using var context = db.CreateContext();
        var sut = new DeviceConnectionTracker(context);

        await sut.RecordConnectedAsync("DEV-1", "conn-1", CancellationToken.None);

        var statuses = await sut.ListStatusesAsync(CancellationToken.None);
        var status = statuses.Should().ContainSingle(s => s.DeviceId == "DEV-1").Subject;
        status.IsOnline.Should().BeTrue();
        status.LastConnectedAtUtc.Should().NotBeNull();
        // 첫 하트비트가 오기까지(최대 60초) 화면의 "마지막 확인" 칸이 비어 보이는 것을 막는다.
        status.LastHeartbeatUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task RecordDisconnectedAsync_MarksDeviceOfflineImmediately()
    {
        using var db = new SqliteTestDatabase();
        using var context = db.CreateContext();
        var sut = new DeviceConnectionTracker(context);
        await sut.RecordConnectedAsync("DEV-1", "conn-1", CancellationToken.None);

        await sut.RecordDisconnectedAsync("DEV-1", CancellationToken.None);

        var statuses = await sut.ListStatusesAsync(CancellationToken.None);
        var status = statuses.Should().ContainSingle(s => s.DeviceId == "DEV-1").Subject;
        status.IsOnline.Should().BeFalse();
        status.LastDisconnectedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task RecordHeartbeatAsync_DoesNotChangeOnlineFlag_OnlyUpdatesLastHeartbeatUtc()
    {
        using var db = new SqliteTestDatabase();
        using var context = db.CreateContext();
        var sut = new DeviceConnectionTracker(context);
        await sut.RecordConnectedAsync("DEV-ONLINE", "conn-1", CancellationToken.None);
        await sut.RecordConnectedAsync("DEV-OFFLINE", "conn-2", CancellationToken.None);
        await sut.RecordDisconnectedAsync("DEV-OFFLINE", CancellationToken.None);

        await sut.RecordHeartbeatAsync("DEV-ONLINE", CancellationToken.None);
        await sut.RecordHeartbeatAsync("DEV-OFFLINE", CancellationToken.None);

        var statuses = await sut.ListStatusesAsync(CancellationToken.None);
        statuses.Should().ContainSingle(s => s.DeviceId == "DEV-ONLINE" && s.IsOnline);
        // 해제 후 지연 도착한 하트비트가 상태를 뒤집지 않는다.
        statuses.Should().ContainSingle(s => s.DeviceId == "DEV-OFFLINE" && !s.IsOnline);
    }

    [Fact]
    public async Task RecordConnectedAsync_SameDeviceReconnectsWithNewConnectionId_UpdatesConnectionIdAndStaysOnline()
    {
        using var db = new SqliteTestDatabase();
        using var context = db.CreateContext();
        var sut = new DeviceConnectionTracker(context);
        await sut.RecordConnectedAsync("DEV-1", "conn-1", CancellationToken.None);

        await sut.RecordConnectedAsync("DEV-1", "conn-2", CancellationToken.None);

        using var verifyContext = db.CreateContext();
        var rows = verifyContext.DeviceConnections.Where(c => c.DeviceId == "DEV-1").ToList();
        rows.Should().ContainSingle();
        rows[0].ConnectionId.Should().Be("conn-2");
        rows[0].IsOnline.Should().BeTrue();
    }

    [Fact]
    public async Task ListStatusesAsync_VeryOldLastHeartbeatButStillFlaggedOnline_StillReportsOnline()
    {
        using var db = new SqliteTestDatabase();
        using var context = db.CreateContext();
        context.DeviceConnections.Add(new DeviceConnectionEntity
        {
            DeviceId = "DEV-1",
            ConnectionId = "conn-1",
            IsOnline = true,
            LastConnectedAtUtc = DateTime.UtcNow.AddHours(-5),
            LastHeartbeatUtc = DateTime.UtcNow.AddHours(-5),
        });
        await context.SaveChangesAsync();
        var sut = new DeviceConnectionTracker(context);

        var statuses = await sut.ListStatusesAsync(CancellationToken.None);

        // 타임아웃 판정 로직이 들어오지 않았음을 증명한다 — 몇 시간 전 하트비트여도 온라인으로 본다.
        statuses.Should().ContainSingle(s => s.DeviceId == "DEV-1" && s.IsOnline);
    }

    [Fact]
    public async Task NoDisconnectEvent_DeviceStaysOnlineForever_KnownLimitation()
    {
        // 서버 프로세스 강제 종료 재현: OnDisconnectedAsync가 호출되지 않은 채 연결 행만 남은 상태.
        // 이것이 버그가 아니라 「알려진 한계」에 문서화된 트레이드오프임을 테스트로 고정한다.
        using var db = new SqliteTestDatabase();
        using var context = db.CreateContext();
        var sut = new DeviceConnectionTracker(context);
        await sut.RecordConnectedAsync("DEV-1", "conn-1", CancellationToken.None);

        var statuses = await sut.ListStatusesAsync(CancellationToken.None);

        statuses.Should().ContainSingle(s => s.DeviceId == "DEV-1" && s.IsOnline);
    }

    [Fact]
    public async Task ListStatusesAsync_DeviceWithInventoryButNeverConnected_IsOfflineNotMissing()
    {
        // 마이그레이션 직후 모든 기존 장치가 이 상태다 — DeviceConnection 행이 아예 없다.
        using var db = new SqliteTestDatabase();
        using var context = db.CreateContext();
        var sut = new DeviceConnectionTracker(context);

        var act = async () => await sut.ListStatusesAsync(CancellationToken.None);

        var statuses = await act.Should().NotThrowAsync();
        statuses.Subject.Should().NotContain(s => s.DeviceId == "DEV-NEVER-CONNECTED");
    }
}
