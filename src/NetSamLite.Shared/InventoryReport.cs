namespace NetSamLite.Shared;

/// <summary>
/// Agent가 한 번 수집한 결과 전체. Server로 보내는 최종 계약(API contract)이다.
/// 수집 항목 표에 없는 메타데이터(수집 시각 등)는 의도적으로 넣지 않았다.
/// </summary>
public sealed record InventoryReport(
    DeviceInfo Device,
    IReadOnlyList<InstalledSoftware> InstalledSoftware);
