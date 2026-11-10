namespace NetSamLite.Shared;

/// <summary>
/// 디스크 1개(Win32_DiskDrive)의 모델/용량.
/// </summary>
public sealed record DiskInfo(
    string? Model,
    long SizeBytes);

/// <summary>
/// 수집 대상 PC 1대의 하드웨어 정보 (수집 항목 표의 WMI 섹션에 대응).
/// </summary>
public sealed record DeviceInfo(
    // BIOS 시리얼 + 컴퓨터 이름 조합, 시리얼이 더미값이면 MachineGuid로 대체한 최종 식별자.
    // 서버 쪽에서 Device를 매칭하는 키로 쓰인다(Phase 2).
    string DeviceId,
    string ComputerName,
    string? Manufacturer,
    string? Model,
    // DeviceId 계산에 쓰인 원본 시리얼. 더미 판별/디버깅을 위해 원본도 보존한다.
    string? SerialNumber,
    string? ProcessorName,
    int ProcessorCoreCount,
    string? OperatingSystemCaption,
    string? OperatingSystemVersion,
    string? OperatingSystemBuildNumber,
    long TotalPhysicalMemoryBytes,
    IReadOnlyList<DiskInfo> Disks);
