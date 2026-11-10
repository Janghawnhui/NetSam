using FluentAssertions;
using NetSamLite.Shared;

namespace NetSamLite.Agent.Tests.Shared;

/// <summary>
/// DeviceInfo/DiskInfo record의 값 동등성과 더미 BIOS 시리얼("To be filled by O.E.M.") 같은
/// 값도 그대로 보존(가공은 HardwareCollector 책임)되는지를 확인한다.
/// 실제 BIOS/WMI 더미값 대체 로직은 이번 세션에 추가된 HardwareCollector가 없어
/// 여기서는 record가 해당 문자열을 그대로 보관할 수 있음만 검증한다.
/// </summary>
public class DeviceInfoTests
{
    [Fact]
    public void Constructor_DummyBiosSerial_IsStoredAsIs()
    {
        var device = new DeviceInfo(
            DeviceId: "DEV-1",
            ComputerName: "PC-01",
            Manufacturer: "Contoso",
            Model: "Box 1",
            SerialNumber: "To be filled by O.E.M.",
            ProcessorName: "CPU",
            ProcessorCoreCount: 4,
            OperatingSystemCaption: "Windows 11",
            OperatingSystemVersion: "10.0.22631",
            OperatingSystemBuildNumber: "22631",
            TotalPhysicalMemoryBytes: 16L * 1024 * 1024 * 1024,
            Disks: [new DiskInfo("Disk 1", 512L * 1024 * 1024 * 1024)]);

        device.SerialNumber.Should().Be("To be filled by O.E.M.");
    }

    [Fact]
    public void Constructor_EmptyDiskList_IsAllowed()
    {
        var act = () => new DeviceInfo(
            "DEV-1", "PC-01", null, null, null, null, 0, null, null, null, 0, Disks: []);

        act.Should().NotThrow();
    }

    [Fact]
    public void Equals_TwoInstancesWithSameDiskValues_AreEqual()
    {
        var disks = new DiskInfo[] { new("Disk 1", 1000) };
        var a = new DeviceInfo("DEV-1", "PC-01", "M", "Mo", "SN", "CPU", 4, "Win", "1.0", "100", 1000, disks);
        var b = new DeviceInfo("DEV-1", "PC-01", "M", "Mo", "SN", "CPU", 4, "Win", "1.0", "100", 1000, disks);

        a.Should().Be(b);
    }
}
