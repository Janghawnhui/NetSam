namespace NetSamLite.Server.Data.Entities;

/// <summary>
/// 디바이스 1대에 "현재" 설치돼 있다고 알려진 SW 1건. DisplayName이 디바이스 안에서의 식별 키다.
/// 이 테이블은 최신 상태만 들고 있고, 설치/삭제 변화는 SoftwareChangeEventEntity에 따로 기록한다.
/// </summary>
public sealed class InstalledSoftwareSnapshotEntity
{
    public int Id { get; set; }
    public required string DeviceId { get; set; }
    public required string DisplayName { get; set; }
    public string? DisplayVersion { get; set; }
    public string? Publisher { get; set; }
    public string? InstallDate { get; set; }
    public string? InstallLocation { get; set; }
    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }
}
