namespace NetSamLite.Server.Normalization;

/// <summary>
/// RawSoftware(= NetSamLite.Shared.InstalledSoftware) 1건을 정규화한 결과.
/// </summary>
public sealed record NormalizedSoftware(
    string RawDisplayName,
    string ProductName,
    string? Category,
    bool Licensable,
    IReadOnlyDictionary<string, string> Attributes,
    bool Unclassified,
    IReadOnlyList<string> AppliedRuleIds);
