namespace NetSamLite.Server.Policy;

public sealed record ProhibitedSoftwareDeviceRef(string DeviceId, string ComputerName);

/// <summary>
/// 금지 SW 1건이 실제로 발견된 현황. 라이선스 매칭과 달리 Licensable/Unclassified 여부와
/// 무관하게 전체 정규화 결과를 대상으로 찾는다 — "설치돼 있다는 사실 자체"가 문제이기 때문이다.
/// </summary>
public sealed record ProhibitedSoftwareAlert(
    string ProductName, string Reason, IReadOnlyList<ProhibitedSoftwareDeviceRef> Devices);

public sealed record ProhibitedSoftwareRequest(string ProductName, string Reason);
