using NetSamLite.Server.Normalization;
using NetSamLite.Shared;

namespace NetSamLite.Server.Tests.Normalization;

/// <summary>
/// SoftwareNormalizer 테스트에서 쓰는 InstalledSoftware / NormalizationRuleDocument 빌더.
/// InstallDate/InstallLocation은 정규화 엔진이 쓰지 않으므로 기본값을 null로 둔다.
/// </summary>
internal static class NormalizationTestData
{
    public static InstalledSoftware Software(
        string displayName,
        string? displayVersion = null,
        string? publisher = null)
        => new(displayName, displayVersion, publisher, InstallDate: null, InstallLocation: null);

    public static NormalizationRuleDocument RuleDocument(params RuleGroup[] groups)
        => new(
            Version: 1,
            RuleGroups: groups,
            UnclassifiedPolicy: new UnclassifiedPolicy("passThroughAsIs", "unclassified"));

    public static RuleGroup Group(string type, params NormalizationRule[] rules)
        => new(type, rules);

    public static NormalizationRule Rule(
        string id,
        string field,
        string type,
        string pattern,
        string? replaceWith = null,
        IReadOnlyList<RuleAction>? actions = null)
        => new(id, new MatchSpec(field, type, pattern), replaceWith, actions, SourceSample: null, Note: null);

    public static RuleAction Action(string op, string? value = null, string? attribute = null, string? pattern = null)
        => new(op, value, attribute, pattern);
}
