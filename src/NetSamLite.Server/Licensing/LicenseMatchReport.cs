namespace NetSamLite.Server.Licensing;

public sealed record LicenseMatchItem(
    string ProductName, int InstalledCount, int LicensedQuantity, string Status, int Shortage);

public sealed record UnlicensedProduct(string ProductName, int InstalledCount);

public sealed record ExcludedProduct(string ProductName, string? Category, int InstalledCount);

public sealed record UnclassifiedProduct(string ProductName, int InstalledCount);

/// <summary>
/// 라이선스 매칭 결과. 네 섹션으로 나눈 이유:
/// - Matched: 분류됐고 라이선스 레코드도 있음 → 수량 비교 가능
/// - UnlicensedProducts: 분류는 됐는데 라이선스 레코드가 아예 없음 → "등록을 안 했다"
/// - ExcludedFromLicensing: Phase 3에서 Licensable=false로 태그된 제품(Runtime/WebApp 등) → 매칭 대상 아님
/// - UnclassifiedSoftware: 정규화 규칙에 안 걸림 → 라이선스 위반 여부를 판단할 근거가 없어 집계에서 뺌
/// FailedCount: 정규화 중 예외가 나서 건너뛴 원본 항목 수 — 0이 아니면 로그에서 원인을 확인해야 한다.
/// </summary>
public sealed record LicenseMatchReport(
    IReadOnlyList<LicenseMatchItem> Matched,
    IReadOnlyList<UnlicensedProduct> UnlicensedProducts,
    IReadOnlyList<ExcludedProduct> ExcludedFromLicensing,
    IReadOnlyList<UnclassifiedProduct> UnclassifiedSoftware,
    int FailedCount);

public sealed record LicenseRequest(
    string ProductName, string LicenseType, int Quantity, DateTime? ExpiresAtUtc, string? Notes);
