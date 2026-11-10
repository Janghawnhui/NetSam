namespace NetSamLite.Server.Data.Entities;

public enum SoftwareChangeType
{
    Installed,
    Uninstalled,
}

/// <summary>
/// 디바이스의 설치 스냅샷이 이전 수집과 달라졌을 때 남기는 이력 1건.
/// </summary>
public sealed class SoftwareChangeEventEntity
{
    public int Id { get; set; }
    public required string DeviceId { get; set; }
    public required string DisplayName { get; set; }
    public string? DisplayVersion { get; set; }
    public SoftwareChangeType ChangeType { get; set; }
    public DateTime OccurredAtUtc { get; set; }
}
