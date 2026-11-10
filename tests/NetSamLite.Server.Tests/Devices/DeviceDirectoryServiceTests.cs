using FluentAssertions;
using NetSamLite.Server.Devices;
using NetSamLite.Server.Tests.Inventory;

namespace NetSamLite.Server.Tests.Devices;

/// <summary>
/// DeviceDirectoryService.ListAsync/GetInstalledSoftwareAsync 통합 테스트.
/// EF InMemory 대신 실제 SQLite(SqliteTestDatabase)로, 디바이스별 설치 SW 개수 집계와
/// 디바이스별 조회 격리를 검증한다.
/// </summary>
public sealed class DeviceDirectoryServiceTests
{
    [Fact]
    public async Task ListAsync_OneDeviceWithThreeSoftwareAndOneWithNone_CountsInstalledSoftwareCorrectly()
    {
        using var db = new SqliteTestDatabase();
        using (var setup = db.CreateContext())
        {
            setup.Devices.AddRange(
                DeviceTestData.Device(deviceId: "DEV-A", computerName: "PC-A"),
                DeviceTestData.Device(deviceId: "DEV-B", computerName: "PC-B"));
            setup.InstalledSoftwareSnapshots.AddRange(
                DeviceTestData.Snapshot("DEV-A", "App One"),
                DeviceTestData.Snapshot("DEV-A", "App Two"),
                DeviceTestData.Snapshot("DEV-A", "App Three"));
            await setup.SaveChangesAsync();
        }

        using var context = db.CreateContext();
        var service = new DeviceDirectoryService(context);

        var devices = await service.ListAsync(CancellationToken.None);

        devices.Should().ContainSingle(d => d.DeviceId == "DEV-A" && d.InstalledSoftwareCount == 3);
        devices.Should().ContainSingle(d => d.DeviceId == "DEV-B" && d.InstalledSoftwareCount == 0);
    }

    [Fact]
    public async Task GetInstalledSoftwareAsync_TwoDevicesWithSoftware_ReturnsOnlyRequestedDevicesSoftware()
    {
        using var db = new SqliteTestDatabase();
        using (var setup = db.CreateContext())
        {
            setup.Devices.AddRange(
                DeviceTestData.Device(deviceId: "DEV-A", computerName: "PC-A"),
                DeviceTestData.Device(deviceId: "DEV-B", computerName: "PC-B"));
            setup.InstalledSoftwareSnapshots.AddRange(
                DeviceTestData.Snapshot("DEV-A", "App One"),
                DeviceTestData.Snapshot("DEV-A", "App Two"),
                DeviceTestData.Snapshot("DEV-B", "Other Device App"));
            await setup.SaveChangesAsync();
        }

        using var context = db.CreateContext();
        var service = new DeviceDirectoryService(context);

        var software = await service.GetInstalledSoftwareAsync("DEV-A", CancellationToken.None);

        software.Should().HaveCount(2);
        software.Should().OnlyContain(s => s.DeviceId == "DEV-A");
        software.Select(s => s.DisplayName).Should().BeEquivalentTo(["App One", "App Two"]);
    }

    [Fact]
    public async Task GetInstalledSoftwareAsync_UnknownDeviceId_ReturnsEmpty()
    {
        using var db = new SqliteTestDatabase();
        using (var setup = db.CreateContext())
        {
            setup.Devices.Add(DeviceTestData.Device(deviceId: "DEV-A", computerName: "PC-A"));
            setup.InstalledSoftwareSnapshots.Add(DeviceTestData.Snapshot("DEV-A", "App One"));
            await setup.SaveChangesAsync();
        }

        using var context = db.CreateContext();
        var service = new DeviceDirectoryService(context);

        var software = await service.GetInstalledSoftwareAsync("DEV-NONEXISTENT", CancellationToken.None);

        software.Should().BeEmpty();
    }
}
