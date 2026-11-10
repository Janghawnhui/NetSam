using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using NetSamLite.Server.Inventory;
using NetSamLite.Server.Tests.Inventory;

namespace NetSamLite.Server.Tests;

/// <summary>
/// POST /api/v1/inventory 엔드포인트 자체(모델 바인딩, 상태 코드)를 실제 HTTP 호출로 검증한다.
/// InventoryIngestionServiceTests는 서비스 로직만 보므로, "빈 바디를 보내면 뭐가 나오는지" 같은
/// 엔드포인트 레벨 동작은 여기서 다룬다.
/// </summary>
public sealed class InventoryEndpointTests : IClassFixture<InventoryApiFactory>
{
    private readonly InventoryApiFactory _factory;

    public InventoryEndpointTests(InventoryApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PostInventory_EmptyJsonBody_ReturnsBadRequestNotServerError()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync(
            "/api/v1/inventory", new StringContent("{}", Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostInventory_ValidReport_ReturnsOkWithInstalledCount()
    {
        var client = _factory.CreateClient();
        var report = TestData.Report(
            device: TestData.Device(deviceId: $"DEV-{Guid.NewGuid()}"),
            software: [TestData.Software("Integration Test App")]);

        var response = await client.PostAsJsonAsync("/api/v1/inventory", report);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<IngestionResult>();
        result!.InstalledCount.Should().Be(1);
    }
}

/// <summary>
/// 테스트 동안만 쓰는 임시 SQLite 파일로 연결 문자열을 바꿔 끼운다 — 실제 운영 DB(netsamlite.db)
/// 를 건드리지 않고, Program.cs의 Database.Migrate() 등 실제 시작 경로를 그대로 탄다.
/// </summary>
public sealed class InventoryApiFactory : WebApplicationFactory<TestEntryPoint>
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"netsamlite-test-{Guid.NewGuid()}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:NetSamLite"] = $"Data Source={_dbPath}",
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
