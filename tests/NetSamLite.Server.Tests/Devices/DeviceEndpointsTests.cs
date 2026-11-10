using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using NetSamLite.Server.Devices;
using NetSamLite.Server.Tests.Inventory;

namespace NetSamLite.Server.Tests.Devices;

/// <summary>
/// GET /api/v1/devices, GET /api/v1/devices/{deviceId}/software 엔드포인트를
/// 실제 HTTP 호출(WebApplicationFactory)로 검증한다. 집계 로직 자체는
/// DeviceDirectoryServiceTests에서 다룬다.
/// </summary>
public sealed class DeviceEndpointsTests : IClassFixture<InventoryApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly InventoryApiFactory _factory;

    public DeviceEndpointsTests(InventoryApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetDevices_AfterInventoryIngested_ReturnsOkWithDeviceInList()
    {
        var client = _factory.CreateClient();
        var deviceId = $"DEV-{Guid.NewGuid()}";
        var report = TestData.Report(
            device: TestData.Device(deviceId: deviceId, computerName: "ENDPOINT-TEST-PC"),
            software: [TestData.Software("Endpoint Test App")]);
        await client.PostAsJsonAsync("/api/v1/inventory", report);

        var response = await client.GetAsync("/api/v1/devices");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var devices = await response.Content.ReadFromJsonAsync<List<DeviceSummary>>(JsonOptions);
        devices.Should().Contain(d => d.DeviceId == deviceId
            && d.ComputerName == "ENDPOINT-TEST-PC"
            && d.InstalledSoftwareCount == 1);
    }

    [Fact]
    public async Task GetDeviceSoftware_AfterInventoryIngested_ReturnsOnlyThatDevicesSoftware()
    {
        var client = _factory.CreateClient();
        var deviceId = $"DEV-{Guid.NewGuid()}";
        var report = TestData.Report(
            device: TestData.Device(deviceId: deviceId),
            software: [TestData.Software("Per-Device Software")]);
        await client.PostAsJsonAsync("/api/v1/inventory", report);

        var response = await client.GetAsync($"/api/v1/devices/{deviceId}/software");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        document.RootElement.EnumerateArray().Should().ContainSingle(
            s => s.GetProperty("displayName").GetString() == "Per-Device Software");
    }

    [Fact]
    public async Task GetDeviceSoftware_UnknownDeviceId_ReturnsOkWithEmptyArray()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/devices/DEV-DOES-NOT-EXIST/software");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        document.RootElement.GetArrayLength().Should().Be(0);
    }
}
