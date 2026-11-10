using NetSamLite.Server.Data.Entities;

namespace NetSamLite.Server.Tests.Devices;

/// <summary>
/// DeviceDirectoryService 테스트에서 쓰는 DeviceEntity/InstalledSoftwareSnapshotEntity 빌더.
/// </summary>
internal static class DeviceTestData
{
    public static DeviceEntity Device(
        string deviceId = "DEV-001",
        string computerName = "PC-001",
        string? manufacturer = "Dell Inc.",
        string? model = "OptiPlex 7090",
        DateTime? lastSeenUtc = null)
        => new()
        {
            DeviceId = deviceId,
            ComputerName = computerName,
            Manufacturer = manufacturer,
            Model = model,
            LastSeenUtc = lastSeenUtc ?? DateTime.UtcNow,
        };

    public static InstalledSoftwareSnapshotEntity Snapshot(
        string deviceId,
        string displayName,
        string? displayVersion = "1.0.0")
        => new()
        {
            DeviceId = deviceId,
            DisplayName = displayName,
            DisplayVersion = displayVersion,
            FirstSeenUtc = DateTime.UtcNow,
            LastSeenUtc = DateTime.UtcNow,
        };
}
