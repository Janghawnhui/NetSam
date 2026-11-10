using FluentAssertions;
using NetSamLite.Shared;

namespace NetSamLite.Agent.Tests.Shared;

/// <summary>
/// InventoryReport는 Device + InstalledSoftware 목록을 담는 최종 계약(DTO)이다.
/// 여기서는 빈 SW 목록도 유효한 보고서로 구성될 수 있는지(설치 SW가 0개인 PC)만 확인한다.
/// </summary>
public class InventoryReportTests
{
    [Fact]
    public void Constructor_EmptyInstalledSoftwareList_IsAllowed()
    {
        var device = new DeviceInfo(
            "DEV-1", "PC-01", null, null, null, null, 0, null, null, null, 0, Disks: []);

        var act = () => new InventoryReport(device, InstalledSoftware: []);

        act.Should().NotThrow();
    }

    [Fact]
    public void Constructor_WithInstalledSoftware_ExposesSameListReference()
    {
        var device = new DeviceInfo(
            "DEV-1", "PC-01", null, null, null, null, 0, null, null, null, 0, Disks: []);
        IReadOnlyList<InstalledSoftware> software = [new InstalledSoftware("App", "1.0", null, null, null)];

        var report = new InventoryReport(device, software);

        report.InstalledSoftware.Should().BeSameAs(software);
        report.Device.Should().Be(device);
    }
}
