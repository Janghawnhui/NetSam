using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NetSamLite.Server.Realtime;
using NetSamLite.Shared.Hub;

namespace NetSamLite.Server.Tests.Realtime;

/// <summary>
/// "DeviceToken" 인증 스킴 + AgentHub 전체를 실제 HTTP/Hub 협상(WebApplicationFactory +
/// TestServer)으로 검증한다. TestServer는 실제 소켓을 열지 않으므로 전송은 LongPolling으로
/// 고정한다 — WebSocket 전용 경로(access_token 쿼리)는 URL에 직접 쿼리 스트링을 붙여
/// DeviceTokenAuthenticationHandler가 쿼리도 읽는지를 등가적으로 검증한다.
///
/// 선택한 경로: 가짜 HubCallerContext 대신 실제 HubConnection(테스트 전용
/// Microsoft.AspNetCore.SignalR.Client 패키지)으로 TestServer에 붙인다 — 협상 레벨
/// 응답만 보는 방식으로는 "AgentHub가 클라이언트 입력이 아니라 인증 클레임에서만
/// DeviceId를 읽는다"는 핵심 불변식을 전혀 검증하지 못하기 때문이다. 7-A의 AgentHub는
/// OnConnectedAsync()/OnDisconnectedAsync() 뿐이라 클라이언트가 보낄 수 있는 "메시지
/// 바디"가 없다 — 그래서 Hub_WithTokenOfAnotherDevice_CannotActAsThatDevice는 "연결
/// 자체가 토큰의 클레임으로만 기록되는지"를 확인한다. "메시지 바디의 DeviceId를
/// 위조해도 무시된다"는 하위 케이스는 SendHeartbeatAsync가 추가되는 7-B에서 추가한다.
/// </summary>
public sealed class DeviceTokenAuthTests : IClassFixture<RealtimeApiFactory>
{
    private readonly RealtimeApiFactory _factory;

    public DeviceTokenAuthTests(RealtimeApiFactory factory)
    {
        _factory = factory;
    }

    private async Task<string> EnrollAsync(string deviceId)
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            $"/api/v1/devices/{deviceId}/enroll", new EnrollmentRequest(RealtimeApiFactory.EnrollmentSecret));
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<EnrollmentResponse>();
        return body!.Token;
    }

    private HubConnection BuildConnection(string? headerToken = null, string? queryToken = null)
    {
        var hubUri = new Uri(_factory.Server.BaseAddress, AgentHubContract.Path);
        if (queryToken is not null)
        {
            hubUri = new Uri($"{hubUri}?access_token={Uri.EscapeDataString(queryToken)}");
        }

        return new HubConnectionBuilder()
            .WithUrl(hubUri, options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                // TestServer는 실제 소켓을 열지 않으므로 LongPolling만 동작한다.
                options.Transports = HttpTransportType.LongPolling;
                if (headerToken is not null)
                {
                    options.Headers["Authorization"] = $"Bearer {headerToken}";
                }
            })
            .Build();
    }

    [Fact]
    public async Task Hub_WithValidTokenInAuthorizationHeader_Authenticates()
    {
        var deviceId = $"DEV-{Guid.NewGuid()}";
        var token = await EnrollAsync(deviceId);
        var connection = BuildConnection(headerToken: token);

        try
        {
            var act = async () => await connection.StartAsync();
            await act.Should().NotThrowAsync();
        }
        finally
        {
            await connection.DisposeAsync();
        }
    }

    [Fact]
    public async Task Hub_WithValidTokenInAccessTokenQueryString_Authenticates()
    {
        var deviceId = $"DEV-{Guid.NewGuid()}";
        var token = await EnrollAsync(deviceId);
        var connection = BuildConnection(queryToken: token);

        try
        {
            var act = async () => await connection.StartAsync();
            await act.Should().NotThrowAsync();
        }
        finally
        {
            await connection.DisposeAsync();
        }
    }

    [Fact]
    public async Task ExistingAnonymousEndpoints_WithoutToken_StillReturnSuccess()
    {
        var client = _factory.CreateClient();

        var devicesResponse = await client.GetAsync("/api/v1/devices");
        var licensesResponse = await client.GetAsync("/api/v1/licenses");

        devicesResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        licensesResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Hub_WithoutToken_Returns401()
    {
        var connection = BuildConnection();

        try
        {
            var act = async () => await connection.StartAsync();
            await act.Should().ThrowAsync<Exception>();
        }
        finally
        {
            await connection.DisposeAsync();
        }
    }

    private static async Task<IReadOnlyList<DeviceConnectionStatus>> PollUntilAsync(
        Func<Task<IReadOnlyList<DeviceConnectionStatus>>> query, Func<IReadOnlyList<DeviceConnectionStatus>, bool> isReady)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        IReadOnlyList<DeviceConnectionStatus> result;
        do
        {
            result = await query();
            if (isReady(result))
            {
                return result;
            }

            await Task.Delay(50);
        } while (DateTime.UtcNow < deadline);

        return result;
    }

    [Fact]
    public async Task Hub_WithTokenOfAnotherDevice_CannotActAsThatDevice()
    {
        var deviceA = $"DEV-A-{Guid.NewGuid()}";
        var deviceB = $"DEV-B-{Guid.NewGuid()}";
        await EnrollAsync(deviceA);
        var tokenB = await EnrollAsync(deviceB);
        var connection = BuildConnection(headerToken: tokenB);

        try
        {
            await connection.StartAsync();

            using var scope = _factory.Services.CreateScope();
            var tracker = scope.ServiceProvider.GetRequiredService<DeviceConnectionTracker>();
            // StartAsync()는 handshake 응답이 오면 바로 반환되는데, 서버의 AgentHub.OnConnectedAsync()
            // (→ RecordConnectedAsync)는 그 handshake 응답과 독립적으로 비동기 실행돼 아직 끝나지
            // 않았을 수 있다(실측 경쟁 상태 — StartAsync 직후 조회하면 간헐적으로 비어 있다). DB에
            // 반영될 때까지 짧게 폴링한다.
            var statuses = await PollUntilAsync(
                () => tracker.ListStatusesAsync(CancellationToken.None),
                s => s.Any(x => x.DeviceId == deviceB && x.IsOnline));

            statuses.Should().ContainSingle(s => s.DeviceId == deviceB && s.IsOnline);
            statuses.Should().NotContain(s => s.DeviceId == deviceA);
        }
        finally
        {
            await connection.DisposeAsync();
        }
    }

    /// <summary>
    /// (갱신 3) 메시지 바디의 DeviceId 위조는 SendHeartbeatAsync가 생기는 7-B에서 처음
    /// 가능해진다 — 7-A의 연결 테스트 인프라(위 Hub_WithTokenOfAnotherDevice_CannotActAsThatDevice)를
    /// 재사용해 "바디에 다른 장치 ID를 담아 보내도 무시되고 클레임의 DeviceId만 쓴다"를 검증한다.
    /// </summary>
    [Fact]
    public async Task Hub_HeartbeatBodyClaimsAnotherDeviceId_RecordsClaimDeviceIdOnly()
    {
        var deviceA = $"DEV-A-{Guid.NewGuid()}";
        var deviceB = $"DEV-B-{Guid.NewGuid()}";
        await EnrollAsync(deviceA);
        var tokenB = await EnrollAsync(deviceB);
        var connection = BuildConnection(headerToken: tokenB);

        try
        {
            await connection.StartAsync();

            using var scope = _factory.Services.CreateScope();
            var tracker = scope.ServiceProvider.GetRequiredService<DeviceConnectionTracker>();
            var connectedStatuses = await PollUntilAsync(
                () => tracker.ListStatusesAsync(CancellationToken.None),
                s => s.Any(x => x.DeviceId == deviceB && x.IsOnline));
            var beforeHeartbeat = connectedStatuses.Single(s => s.DeviceId == deviceB).LastHeartbeatUtc;

            await Task.Delay(50);
            var message = new HeartbeatMessage(
                AgentHubContract.CurrentSchemaVersion, DeviceId: deviceA, AgentVersion: "test-agent", SentAtUtc: DateTime.UtcNow);
            var ack = await connection.InvokeAsync<HeartbeatAck>("SendHeartbeatAsync", message);

            ack.SchemaVersion.Should().Be(AgentHubContract.CurrentSchemaVersion);
            var statuses = await tracker.ListStatusesAsync(CancellationToken.None);
            var deviceBStatus = statuses.Should().ContainSingle(s => s.DeviceId == deviceB).Subject;
            deviceBStatus.LastHeartbeatUtc.Should().BeAfter(beforeHeartbeat!.Value);
            statuses.Should().NotContain(s => s.DeviceId == deviceA);
        }
        finally
        {
            await connection.DisposeAsync();
        }
    }

    /// <summary>
    /// 완료 기준 2번("모든 수신 핸들러가 진입 직후 SchemaVersion을 검사하고, 불일치 시
    /// 처리하지 않는다")을 하트비트 경로에서 고정한다. AgentHub.SendHeartbeatAsync는 버전이
    /// 다르면 tracker.RecordHeartbeatAsync를 호출하지 않고 경고만 남긴 채 ack만 돌려준다 —
    /// ack 자체는 항상 온다(클라이언트가 응답을 못 받아 블로킹되지 않도록).
    /// </summary>
    [Fact]
    public async Task Hub_HeartbeatWithWrongSchemaVersion_DoesNotUpdateLastHeartbeatButStillReturnsAck()
    {
        var deviceId = $"DEV-{Guid.NewGuid()}";
        var token = await EnrollAsync(deviceId);
        var connection = BuildConnection(headerToken: token);

        try
        {
            await connection.StartAsync();

            using var scope = _factory.Services.CreateScope();
            var tracker = scope.ServiceProvider.GetRequiredService<DeviceConnectionTracker>();
            var connectedStatuses = await PollUntilAsync(
                () => tracker.ListStatusesAsync(CancellationToken.None),
                s => s.Any(x => x.DeviceId == deviceId && x.IsOnline));
            var beforeHeartbeat = connectedStatuses.Single(s => s.DeviceId == deviceId).LastHeartbeatUtc;

            await Task.Delay(50);
            var message = new HeartbeatMessage(
                SchemaVersion: AgentHubContract.CurrentSchemaVersion + 999,
                DeviceId: deviceId, AgentVersion: "test-agent", SentAtUtc: DateTime.UtcNow);
            var ack = await connection.InvokeAsync<HeartbeatAck>("SendHeartbeatAsync", message);

            ack.SchemaVersion.Should().Be(AgentHubContract.CurrentSchemaVersion);
            var statuses = await tracker.ListStatusesAsync(CancellationToken.None);
            var status = statuses.Should().ContainSingle(s => s.DeviceId == deviceId).Subject;
            status.LastHeartbeatUtc.Should().Be(beforeHeartbeat);
        }
        finally
        {
            await connection.DisposeAsync();
        }
    }
}

/// <summary>
/// InventoryApiFactory와 같은 이유로 임시 SQLite 파일을 쓰지만, Hub 인증 테스트를 위해
/// 등록(enroll) 비밀값(Realtime:EnrollmentSecret)도 함께 in-memory 설정으로 주입한다.
/// </summary>
public sealed class RealtimeApiFactory : WebApplicationFactory<TestEntryPoint>
{
    public const string EnrollmentSecret = "test-enrollment-secret-for-hub-auth";

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"netsamlite-realtime-test-{Guid.NewGuid()}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:NetSamLite"] = $"Data Source={_dbPath}",
                ["Realtime:EnrollmentSecret"] = EnrollmentSecret,
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }
}
