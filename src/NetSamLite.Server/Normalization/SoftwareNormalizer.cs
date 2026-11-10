using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using NetSamLite.Shared;

namespace NetSamLite.Server.Normalization;

/// <summary>
/// RawSoftware(InstalledSoftware)를 SoftwareProduct로 묶는 규칙 엔진.
/// 단계: 0) 텍스트 정규화(고정 동작) → 1) 제외 → 2) 별칭 테이블 → 3) 제품별 패턴 → 4) 공통 정리 → 5) 미분류.
/// 0단계와 최종 공백/구분자 정리는 의미와 무관한 기계적 청소라 규칙(JSON)이 아니라 엔진에 고정했다.
/// </summary>
public sealed class SoftwareNormalizer
{
    // 토큰을 떼어낸 뒤 남는 " - ", 끝 공백 등 자투리 정리. 의미 없는 마무리라 0단계와 같은 이유로 고정.
    private static readonly Regex TrailingSeparatorPattern = new(@"[\s\-–_,/]+$", RegexOptions.Compiled);
    private static readonly Regex WhitespacePattern = new(@"\s+", RegexOptions.Compiled);

    private readonly IReadOnlyList<NormalizationRule> _excludeRules;
    private readonly IReadOnlyList<NormalizationRule> _aliasRules;
    private readonly IReadOnlyList<NormalizationRule> _productPatternRules;
    private readonly IReadOnlyList<NormalizationRule> _commonCleanupRules;

    public SoftwareNormalizer(NormalizationRuleDocument document)
    {
        _excludeRules = GetGroup(document, "exclude");
        _aliasRules = GetGroup(document, "alias");
        _productPatternRules = GetGroup(document, "productPattern");
        _commonCleanupRules = GetGroup(document, "commonCleanup");
    }

    private static IReadOnlyList<NormalizationRule> GetGroup(NormalizationRuleDocument document, string type) =>
        document.RuleGroups.FirstOrDefault(g => g.Type == type)?.Rules ?? [];

    /// <summary>
    /// null을 반환하면 "제외" 규칙에 걸려 SoftwareProduct를 만들지 않는다는 뜻이다.
    /// </summary>
    public NormalizedSoftware? Normalize(InstalledSoftware software)
    {
        var name = NormalizeText(software.DisplayName);

        foreach (var rule in _excludeRules)
        {
            if (IsMatch(rule.Match, software, name))
            {
                return null;
            }
        }

        var appliedRuleIds = new List<string>();
        string? category = null;
        var licensable = true;
        var attributes = new Dictionary<string, string>();
        var classified = false;

        foreach (var rule in _aliasRules)
        {
            if (!IsMatch(rule.Match, software, name))
            {
                continue;
            }

            name = rule.ReplaceWith ?? name;
            appliedRuleIds.Add(rule.Id);
            classified = true;
            break; // 별칭은 가장 먼저 매칭된 것 하나만 적용한다.
        }

        foreach (var rule in _productPatternRules)
        {
            if (!IsMatch(rule.Match, software, name))
            {
                continue;
            }

            appliedRuleIds.Add(rule.Id);
            classified = true;
            foreach (var action in rule.Actions ?? [])
            {
                ApplyAction(action, ref name, ref category, ref licensable, attributes);
            }
        }

        foreach (var rule in _commonCleanupRules)
        {
            var cleaned = Regex.Replace(name, rule.Match.Pattern, rule.ReplaceWith ?? string.Empty);
            if (cleaned != name)
            {
                appliedRuleIds.Add(rule.Id);
            }

            name = cleaned;
        }

        name = FinalizeName(name);

        return new NormalizedSoftware(
            software.DisplayName, name, category, licensable, attributes, !classified, appliedRuleIds);
    }

    private static bool IsMatch(MatchSpec spec, InstalledSoftware software, string currentDisplayName)
    {
        var value = spec.Field switch
        {
            "DisplayName" => currentDisplayName,
            "Publisher" => software.Publisher,
            "DisplayVersion" => software.DisplayVersion,
            _ => throw new InvalidOperationException($"알 수 없는 match.field: {spec.Field}"),
        };

        if (value is null)
        {
            return false;
        }

        return spec.Type switch
        {
            "exact" => string.Equals(value, spec.Pattern, StringComparison.Ordinal),
            "contains" => value.Contains(spec.Pattern, StringComparison.Ordinal),
            "regex" => Regex.IsMatch(value, spec.Pattern),
            _ => throw new InvalidOperationException($"알 수 없는 match.type: {spec.Type}"),
        };
    }

    private static void ApplyAction(
        RuleAction action,
        ref string name,
        ref string? category,
        ref bool licensable,
        Dictionary<string, string> attributes)
    {
        switch (action.Op)
        {
            case "setCategory":
                category = action.Value;
                break;
            case "setLicensable":
                licensable = bool.Parse(action.Value!);
                break;
            case "extractAttribute":
                var match = Regex.Match(name, action.Pattern!, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    attributes[action.Attribute!] = match.Value;
                }

                break;
            default:
                throw new InvalidOperationException($"알 수 없는 action.op: {action.Op}");
        }
    }

    // 0단계: 유니코드 정규화(NFKC) → 제로폭/제어문자 제거 → 공백 1개로 축약 → trim.
    // NFKC를 가장 먼저 해야 전각 공백 등 공백류 변형이 일반 공백으로 모이고,
    // 공백은 지우지 않고 "1개로 축약"해야 "word1\tword2"가 "word1word2"로 뭉개지지 않는다.
    private static string NormalizeText(string input)
    {
        var text = input.Normalize(NormalizationForm.FormKC);
        text = RemoveInvisibleCharacters(text);
        text = WhitespacePattern.Replace(text, " ");
        return text.Trim();
    }

    private static string RemoveInvisibleCharacters(string input)
    {
        var builder = new StringBuilder(input.Length);
        foreach (var ch in input)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.Format)
            {
                continue; // 제로폭 문자, BOM 등
            }

            if (category == UnicodeCategory.Control && !char.IsWhiteSpace(ch))
            {
                continue; // 공백성 제어문자(\t, \n 등)는 남겨서 공백 축약 단계가 처리하게 둔다.
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }

    private static string FinalizeName(string name) =>
        TrailingSeparatorPattern.Replace(WhitespacePattern.Replace(name, " "), string.Empty).Trim();
}
