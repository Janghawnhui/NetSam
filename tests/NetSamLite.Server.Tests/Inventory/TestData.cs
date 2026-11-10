using NetSamLite.Shared;

namespace NetSamLite.Server.Tests.Inventory;

/// <summary>
/// 테스트용 InventoryReport/DeviceInfo/InstalledSoftware를 만들어주는 헬퍼.
/// 기본값은 "정상적인 윈도우 PC 한 대"를 흉내 내고, 필요한 필드만 override해서 쓴다.
/// </summary>
internal static class TestData
{
    public static DeviceInfo Device(
        string deviceId = "DEV-001",
        string computerName = "PC-001",
        string? manufacturer = "Dell Inc.",
        string? model = "OptiPlex 7090",
        string? serialNumber = "ABC123XYZ",
        string? processorName = "Intel(R) Core(TM) i7-11700",
        int processorCoreCount = 8,
        string? operatingSystemCaption = "Microsoft Windows 11 Pro",
        string? operatingSystemVersion = "10.0.22631",
        string? operatingSystemBuildNumber = "22631",
        long totalPhysicalMemoryBytes = 17_179_869_184,
        IReadOnlyList<DiskInfo>? disks = null)
        => new(
            deviceId,
            computerName,
            manufacturer,
            model,
            serialNumber,
            processorName,
            processorCoreCount,
            operatingSystemCaption,
            operatingSystemVersion,
            operatingSystemBuildNumber,
            totalPhysicalMemoryBytes,
            disks ?? [new DiskInfo("Samsung SSD 970 EVO 500GB", 512_110_190_592)]);

    public static InstalledSoftware Software(
        string displayName = "Contoso App",
        string? displayVersion = "1.0.0",
        string? publisher = "Contoso",
        string? installDate = "20250101",
        string? installLocation = @"C:\Program Files\Contoso App")
        => new(displayName, displayVersion, publisher, installDate, installLocation);

    public static InventoryReport Report(DeviceInfo? device = null, params InstalledSoftware[] software)
        => new(device ?? Device(), software);
}
