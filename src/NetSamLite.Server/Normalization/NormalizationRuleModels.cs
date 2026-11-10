namespace NetSamLite.Server.Normalization;

/// <summary>
/// normalization-rules.json의 역직렬화 모델. 필드는 전부 camelCase JSON과 매칭된다.
/// </summary>
public sealed record MatchSpec(string Field, string Type, string Pattern);

public sealed record RuleAction(string Op, string? Value, string? Attribute, string? Pattern);

public sealed record NormalizationRule(
    string Id,
    MatchSpec Match,
    string? ReplaceWith,
    IReadOnlyList<RuleAction>? Actions,
    IReadOnlyList<string>? SourceSample,
    string? Note);

public sealed record RuleGroup(string Type, IReadOnlyList<NormalizationRule> Rules);

public sealed record UnclassifiedPolicy(string Action, string Flag);

public sealed record NormalizationRuleDocument(
    int Version,
    IReadOnlyList<RuleGroup> RuleGroups,
    UnclassifiedPolicy UnclassifiedPolicy);
