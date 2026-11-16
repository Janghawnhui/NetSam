using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NetSamLite.Server.Data;
using NetSamLite.Server.Realtime;
using NetSamLite.Server.Tests.Devices;
using NetSamLite.Shared.Hub;

namespace NetSamLite.Server.Tests.Realtime;

/// <summary>
/// POST /api/v1/devices/{deviceId}/collect-now 를 실제 HTTP 호출로 검증한다. 루프백 체크가
/// 다른 모든 검사보다 먼저 실행되는지(확정 결정 3)와, 전송된 CollectNowCommand의 내용을
/// 확인하기 위해 실제 IHubContext&lt;AgentHub, IAgentClient&gt;를 레코딩 전용 대역으로
/// 교체한다(TestServer는 실제 소켓이 없어 에이전트가 그룹에 연결돼 있지 않으므로, 전송
/// 여부/내용을 확인하려면 대역이 필요하다). 원격 IP는 TestServer가 기본적으로 null로 두므로,
/// 루프백/비루프백 값은 테스트 전용 미들웨어(IStartupFilter)가 요청 헤더를 읽어 주입한다.
/// </summary>
public sealed class CollectNowEndpointTests : IClassFixture<CollectNowApiFactory>
{
    private readonly CollectNowApiFactory _factory;

    public CollectNowEndpointTests(CollectNowApiFactory factory)
    {
        _factory = factory;
    }

    private async Task SeedDeviceAsync(string deviceId, bool online)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NetSamLiteDbContext>();
        dbContext.Devices.Add(DeviceTestData.Device(deviceId: deviceId));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        if (online)
        {
            var tracker = scope.ServiceProvider.GetRequiredService<DeviceConnectionTracker>();
            await tracker.RecordConnectedAsync(deviceId, $"conn-{deviceId}", CancellationToken.None);
        }
    }

    private HttpClient CreateClientWithRemoteIp(string? ip)
    {
        var client = _factory.CreateClient();
        if (ip is not null)
        {
            client.DefaultRequestHeaders.Add(RemoteIpAddressStartupFilter.HeaderName, ip);
        }

        return client;
    }

    [Fact]
    public async Task CollectNow_FromLoopbackAndDeviceOnline_SendsCommandAndReturns202()
    {
        var deviceId = $"DEV-{Guid.NewGuid()}";
        await SeedDeviceAsync(deviceId, online: true);
        var client = CreateClientWithRemoteIp("127.0.0.1");

        var response = await client.PostAsync($"/api/v1/devices/{deviceId}/collect-now", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var sent = _factory.HubContextRecorder.SentCommands.Should().ContainSingle(c => c.GroupName == deviceId).Subject;
        sent.Command.SchemaVersion.Should().Be(AgentHubContract.CurrentSchemaVersion);
        sent.Command.CommandId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task CollectNow_FromIpv6Loopback_IsAllowed()
    {
        var deviceId = $"DEV-{Guid.NewGuid()}";
        await SeedDeviceAsync(deviceId, online: true);
        var client = CreateClientWithRemoteIp("::1");

        var response = await client.PostAsync($"/api/v1/devices/{deviceId}/collect-now", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        _factory.HubContextRecorder.SentCommands.Should().Contain(c => c.GroupName == deviceId);
    }

    [Fact]
    public async Task CollectNow_DeviceOffline_Returns409AndSendsNothing()
    {
        var deviceId = $"DEV-{Guid.NewGuid()}";
        await SeedDeviceAsync(deviceId, online: false);
        var client = CreateClientWithRemoteIp("127.0.0.1");

        var response = await client.PostAsync($"/api/v1/devices/{deviceId}/collect-now", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        _factory.HubContextRecorder.SentCommands.Should().NotContain(c => c.GroupName == deviceId);
    }

    [Fact]
    public async Task CollectNow_FromNonLoopbackAddress_Returns403()
    {
        var deviceId = $"DEV-{Guid.NewGuid()}";
        await SeedDeviceAsync(deviceId, online: true);
        var client = CreateClientWithRemoteIp("192.168.1.10");

        var response = await client.PostAsync($"/api/v1/devices/{deviceId}/collect-now", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _factory.HubContextRecorder.SentCommands.Should().NotContain(c => c.GroupName == deviceId);
    }

    [Fact]
    public async Task CollectNow_FromNonLoopbackAddress_DoesNotRevealDeviceExistence()
    {
        var onlineDeviceId = $"DEV-{Guid.NewGuid()}";
        var offlineDeviceId = $"DEV-{Guid.NewGuid()}";
        var unknownDeviceId = $"DEV-{Guid.NewGuid()}";
        await SeedDeviceAsync(onlineDeviceId, online: true);
        await SeedDeviceAsync(offlineDeviceId, online: false);
        var client = CreateClientWithRemoteIp("192.168.1.10");

        var onlineResponse = await client.PostAsync($"/api/v1/devices/{onlineDeviceId}/collect-now", content: null);
        var offlineResponse = await client.PostAsync($"/api/v1/devices/{offlineDeviceId}/collect-now", content: null);
        var unknownResponse = await client.PostAsync($"/api/v1/devices/{unknownDeviceId}/collect-now", content: null);

        // 404/409로 갈리지 않는다 — 루프백 체크가 가장 먼저 실행된다는 증거다.
        onlineResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        offlineResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        unknownResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CollectNow_RemoteIpAddressIsNull_Returns403()
    {
        var deviceId = $"DEV-{Guid.NewGuid()}";
        await SeedDeviceAsync(deviceId, online: true);
        // 헤더를 아예 안 보내면 테스트 미들웨어가 RemoteIpAddress를 건드리지 않고,
        // TestServer는 실제 소켓이 없어 기본값이 null이다 — "모르면 막는다" 경로를 그대로 탄다.
        var client = CreateClientWithRemoteIp(ip: null);

        var response = await client.PostAsync($"/api/v1/devices/{deviceId}/collect-now", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CollectNow_UnknownDeviceId_Returns404()
    {
        var deviceId = $"DEV-{Guid.NewGuid()}";
        var client = CreateClientWithRemoteIp("127.0.0.1");

        var response = await client.PostAsync($"/api/v1/devices/{deviceId}/collect-now", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}

/// <summary>
/// 테스트 요청 헤더(<see cref="HeaderName"/>)에 담긴 IP 문자열을
/// HttpContext.Connection.RemoteIpAddress에 주입한다. Program.cs를 건드리지 않고
/// IStartupFilter로 파이프라인 맨 앞에 끼워 넣는다 — TestServer는 실제 TCP 연결이 없어
/// RemoteIpAddress가 기본적으로 null이므로, 루프백/비루프백 시나리오를 재현하려면 이 방법이 필요하다.
/// </summary>
public sealed class RemoteIpAddressStartupFilter : IStartupFilter
{
    public const string HeaderName = "X-Test-RemoteIp";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => builder =>
    {
        builder.Use((context, nextMiddleware) =>
        {
            if (context.Request.Headers.TryGetValue(HeaderName, out var ipHeader)
                && IPAddress.TryParse(ipHeader.ToString(), out var ip))
            {
                context.Connection.RemoteIpAddress = ip;
            }

            return nextMiddleware();
        });
        next(builder);
    };
}

/// <summary>
/// 실제 SignalR 연결 없이 "어떤 그룹에 어떤 CollectNowCommand가 전송됐는지"를 기록한다.
/// TestServer에는 연결된 에이전트가 없어 진짜 IHubContext로는 전송 여부/내용을 검증할 수 없다.
/// </summary>
public sealed class RecordingAgentHubContext : IHubContext<AgentHub, IAgentClient>
{
    public List<(string GroupName, CollectNowCommand Command)> SentCommands { get; } = [];

    public IHubClients<IAgentClient> Clients => new RecordingHubClients(this);

    public IGroupManager Groups => throw new NotSupportedException("이 테스트 대역은 Groups를 쓰지 않는다.");

    private sealed class RecordingHubClients(RecordingAgentHubContext owner) : IHubClients<IAgentClient>
    {
        public IAgentClient All => throw new NotSupportedException();
        public IAgentClient AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        public IAgentClient Client(string connectionId) => throw new NotSupportedException();
        public IAgentClient Clients(IReadOnlyList<string> connectionIds) => throw new NotSupportedException();
        public IAgentClient Group(string groupName) => new RecordingAgentClient(owner, groupName);
        public IAgentClient GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) =>
            throw new NotSupportedException();
        public IAgentClient Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();
        public IAgentClient OthersInGroup(string groupName) => throw new NotSupportedException();
        public IAgentClient User(string userId) => throw new NotSupportedException();
        public IAgentClient Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();
    }

    private sealed class RecordingAgentClient(RecordingAgentHubContext owner, string groupName) : IAgentClient
    {
        public Task CollectNowAsync(CollectNowCommand command)
        {
            owner.SentCommands.Add((groupName, command));
            return Task.CompletedTask;
        }
    }
}

/// <summary>
/// InventoryApiFactory와 같은 이유로 임시 SQLite 파일을 쓰고, 루프백 체크를 재현하는
/// 테스트 미들웨어와 IHubContext 레코딩 대역을 DI에 끼워 넣는다.
/// </summary>
public sealed class CollectNowApiFactory : WebApplicationFactory<TestEntryPoint>
{
    public RecordingAgentHubContext HubContextRecorder { get; } = new();

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"netsamlite-collectnow-test-{Guid.NewGuid()}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:NetSamLite"] = $"Data Source={_dbPath}",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.AddTransient<IStartupFilter, RemoteIpAddressStartupFilter>();
            services.AddSingleton<IHubContext<AgentHub, IAgentClient>>(HubContextRecorder);
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
