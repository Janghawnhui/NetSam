using NetSamLite.Server.Data.Entities;
using NetSamLite.Server.Normalization;

namespace NetSamLite.Server.Tests.Licensing;

/// <summary>
/// LicenseMatchingService 테스트에서 쓰는 InstalledSoftwareSnapshotEntity/LicenseEntity 빌더와,
/// 실제 normalization-rules.json을 로드한 SoftwareNormalizer 생성 헬퍼.
/// Normalization 단위 테스트(NormalizationTestData)와 달리 여기서는 커스텀 규칙이 아니라
/// 실제 배포되는 규칙 파일로 통합 테스트하는 것이 목적이라 항상 실제 파일을 읽는다.
/// </summary>
internal static class LicensingTestData
{
    public static SoftwareNormalizer CreateRealNormalizer()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Normalization", "normalization-rules.json");
        var document = NormalizationRuleLoader.LoadFromFile(path);
        return new SoftwareNormalizer(document);
    }

    public static InstalledSoftwareSnapshotEntity Snapshot(
        string deviceId,
        string displayName,
        string? displayVersion = "1.0.0",
        string? publisher = null)
        => new()
        {
            DeviceId = deviceId,
            DisplayName = displayName,
            DisplayVersion = displayVersion,
            Publisher = publisher,
            FirstSeenUtc = DateTime.UtcNow,
            LastSeenUtc = DateTime.UtcNow,
        };

    public static LicenseEntity License(
        string productName,
        int quantity,
        string licenseType = "Perpetual",
        DateTime? expiresAtUtc = null,
        string? notes = null)
        => new()
        {
            ProductName = productName,
            LicenseType = licenseType,
            Quantity = quantity,
            ExpiresAtUtc = expiresAtUtc,
            Notes = notes,
        };
}
