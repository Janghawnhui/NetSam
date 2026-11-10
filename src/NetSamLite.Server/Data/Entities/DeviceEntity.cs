namespace NetSamLite.Server.Data.Entities;

/// <summary>
/// NetSamLite.Shared.DeviceInfo를 저장한 것. 하드웨어 정보는 이력 없이 최신 값으로 덮어쓴다.
/// </summary>
public sealed class DeviceEntity
{
    public required string DeviceId { get; set; }
    public required string ComputerName { get; set; }
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? SerialNumber { get; set; }
    public string? ProcessorName { get; set; }
    public int ProcessorCoreCount { get; set; }
    public string? OperatingSystemCaption { get; set; }
    public string? OperatingSystemVersion { get; set; }
    public string? OperatingSystemBuildNumber { get; set; }
    public long TotalPhysicalMemoryBytes { get; set; }
    public DateTime LastSeenUtc { get; set; }

    public List<DeviceDiskEntity> Disks { get; set; } = [];
}

public sealed class DeviceDiskEntity
{
    public int Id { get; set; }
    public required string DeviceId { get; set; }
    public string? Model { get; set; }
    public long SizeBytes { get; set; }
}
