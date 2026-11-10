namespace NetSamLite.Server.Policy;

/// <summary>
/// 이미 등록된 ProductName으로 금지 SW를 다시 등록하려 할 때 던진다.
/// </summary>
public sealed class DuplicateProhibitedSoftwareException(string productName)
    : Exception($"이미 등록된 금지 SW입니다: {productName}");
