namespace NetSamLite.Server.Data.Entities;

/// <summary>
/// 보유 라이선스 1건. ProductName은 SoftwareNormalizer가 출력하는 ProductName과
/// 대소문자 무시 완전일치로 매칭하는 키다.
/// </summary>
public sealed class LicenseEntity
{
    public int Id { get; set; }
    public required string ProductName { get; set; }
    public required string LicenseType { get; set; }
    public int Quantity { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public string? Notes { get; set; }
}
