namespace NetSamLite.Server.Devices;

public sealed record DiskSummary(string? Model, long SizeBytes);

/// <summary>
/// 장치 목록 화면용 요약. DeviceEntity + 설치 SW 개수(별도 집계)를 합친 것이다.
/// </summary>
public sealed record DeviceSummary(
    string DeviceId,
    string ComputerName,
    string? Manufacturer,
    string? Model,
    string? OperatingSystemCaption,
    string? OperatingSystemVersion,
    string? ProcessorName,
    int ProcessorCoreCount,
    long TotalPhysicalMemoryBytes,
    IReadOnlyList<DiskSummary> Disks,
    DateTime LastSeenUtc,
    int InstalledSoftwareCount);
