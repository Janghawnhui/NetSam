using FluentAssertions;
using NetSamLite.Server.Normalization;

namespace NetSamLite.Server.Tests.Normalization;

/// <summary>
/// SoftwareNormalizer의 단계별 동작(0: 텍스트 정규화 → 1: 제외 → 2: 별칭 → 3: 제품 패턴 →
/// 4: 공통 정리 → 5: 마무리/미분류)을 커스텀 NormalizationRuleDocument로 하나씩 분리해서 검증한다.
/// 실제 normalization-rules.json에 대한 회귀 테스트는 NormalizationRulesJsonTests에 있다.
/// </summary>
public sealed class SoftwareNormalizerTests
{
    // ----- 0단계: 텍스트 정규화(고정 동작) -----

    [Fact]
    public void Normalize_DisplayNameWithLeadingAndTrailingWhitespace_TrimsToCore()
    {
        var sut = new SoftwareNormalizer(NormalizationTestData.RuleDocument());

        var result = sut.Normalize(NormalizationTestData.Software("   App One   "));

        result!.ProductName.Should().Be("App One");
        result.Unclassified.Should().BeTrue();
    }

    [Fact]
    public void Normalize_DisplayNameWithFullWidthAndRepeatedSpaces_CollapsesToSingleAsciiSpace()
    {
        var sut = new SoftwareNormalizer(NormalizationTestData.RuleDocument());
        // U+3000(전각 공백)은 NFKC 호환 분해로 일반 공백이 되고, 연속 공백은 1개로 축약된다.
        var software = NormalizationTestData.Software("App　　One   Two");

        var result = sut.Normalize(software);

        result!.ProductName.Should().Be("App One Two");
    }

    [Fact]
    public void Normalize_DisplayNameWithZeroWidthSpaceAndTab_RemovesZeroWidthButCollapsesTabToSpace()
    {
        var sut = new SoftwareNormalizer(NormalizationTestData.RuleDocument());
        var software = NormalizationTestData.Software("App​One\tTwo");

        var result = sut.Normalize(software);

        // U+200B(제로폭 공백)는 완전히 사라져서 "App"과 "One"이 붙어버리지만,
        // 탭(\t)은 공백성 제어문자라 남고 공백 축약 단계에서 일반 공백 1개가 된다.
        result!.ProductName.Should().Be("AppOne Two");
    }

    // ----- 1단계: exclude -----

    [Fact]
    public void Normalize_DisplayNameMatchesExcludeRule_ReturnsNull()
    {
        var doc = NormalizationTestData.RuleDocument(
            NormalizationTestData.Group("exclude",
                NormalizationTestData.Rule("exclude.test", "DisplayName", "regex", "^Noise.*$")));
        var sut = new SoftwareNormalizer(doc);

        var result = sut.Normalize(NormalizationTestData.Software("Noise Generator Tool"));

        result.Should().BeNull();
    }

    [Fact]
    public void Normalize_DisplayNameDoesNotMatchExcludeRule_ReturnsNonNullResult()
    {
        var doc = NormalizationTestData.RuleDocument(
            NormalizationTestData.Group("exclude",
                NormalizationTestData.Rule("exclude.test", "DisplayName", "regex", "^Noise.*$")));
        var sut = new SoftwareNormalizer(doc);

        var result = sut.Normalize(NormalizationTestData.Software("Useful App"));

        result.Should().NotBeNull();
    }

    // ----- 2단계: alias -----

    [Fact]
    public void Normalize_DisplayNameMatchesAliasRule_ReplacesNameAndClearsUnclassified()
    {
        var doc = NormalizationTestData.RuleDocument(
            NormalizationTestData.Group("alias",
                NormalizationTestData.Rule("alias.test", "DisplayName", "exact", "Old Name", replaceWith: "New Name")));
        var sut = new SoftwareNormalizer(doc);

        var result = sut.Normalize(NormalizationTestData.Software("Old Name"));

        result!.ProductName.Should().Be("New Name");
        result.Unclassified.Should().BeFalse();
        result.AppliedRuleIds.Should().Contain("alias.test");
    }

    [Fact]
    public void Normalize_DisplayNameMatchesMultipleAliasRules_OnlyFirstMatchingRuleIsApplied()
    {
        var doc = NormalizationTestData.RuleDocument(
            NormalizationTestData.Group("alias",
                NormalizationTestData.Rule("alias.first", "DisplayName", "contains", "App", replaceWith: "First"),
                NormalizationTestData.Rule("alias.second", "DisplayName", "contains", "App", replaceWith: "Second")));
        var sut = new SoftwareNormalizer(doc);

        var result = sut.Normalize(NormalizationTestData.Software("My App"));

        result!.ProductName.Should().Be("First");
        result.AppliedRuleIds.Should().Equal("alias.first");
    }

    [Fact]
    public void Normalize_DisplayNameDoesNotMatchAnyAliasRule_NameStaysUnchangedAndUnclassified()
    {
        var doc = NormalizationTestData.RuleDocument(
            NormalizationTestData.Group("alias",
                NormalizationTestData.Rule("alias.test", "DisplayName", "exact", "Old Name", replaceWith: "New Name")));
        var sut = new SoftwareNormalizer(doc);

        var result = sut.Normalize(NormalizationTestData.Software("Totally Different App"));

        result!.ProductName.Should().Be("Totally Different App");
        result.Unclassified.Should().BeTrue();
    }

    // ----- 3단계: productPattern -----

    [Fact]
    public void Normalize_ProductPatternSetsCategoryAndLicensable_AppliesBothActionsWithoutChangingName()
    {
        var doc = NormalizationTestData.RuleDocument(
            NormalizationTestData.Group("productPattern",
                NormalizationTestData.Rule(
                    "product.test", "Publisher", "exact", "Acme Corp",
                    actions:
                    [
                        NormalizationTestData.Action("setCategory", value: "Tool"),
                        NormalizationTestData.Action("setLicensable", value: "false"),
                    ])));
        var sut = new SoftwareNormalizer(doc);

        var result = sut.Normalize(NormalizationTestData.Software("Acme Widget", publisher: "Acme Corp"));

        result!.Category.Should().Be("Tool");
        result.Licensable.Should().BeFalse();
        result.ProductName.Should().Be("Acme Widget");
        result.Unclassified.Should().BeFalse();
    }

    [Fact]
    public void Normalize_ProductPatternExtractAttributeMatches_StoresCapturedValueInAttributes()
    {
        var doc = NormalizationTestData.RuleDocument(
            NormalizationTestData.Group("productPattern",
                NormalizationTestData.Rule(
                    "product.arch", "DisplayName", "contains", "Tool",
                    actions: [NormalizationTestData.Action("extractAttribute", attribute: "Architecture", pattern: "(x64|x86)")])));
        var sut = new SoftwareNormalizer(doc);

        var result = sut.Normalize(NormalizationTestData.Software("Acme Tool x64"));

        result!.Attributes.Should().ContainKey("Architecture").WhoseValue.Should().Be("x64");
    }

    [Fact]
    public void Normalize_ProductPatternExtractAttributeNoMatch_AttributeIsNotAdded()
    {
        var doc = NormalizationTestData.RuleDocument(
            NormalizationTestData.Group("productPattern",
                NormalizationTestData.Rule(
                    "product.arch", "DisplayName", "contains", "Tool",
                    actions: [NormalizationTestData.Action("extractAttribute", attribute: "Architecture", pattern: "(x64|x86)")])));
        var sut = new SoftwareNormalizer(doc);

        var result = sut.Normalize(NormalizationTestData.Software("Acme Tool"));

        result!.Attributes.Should().BeEmpty();
    }

    [Fact]
    public void Normalize_MultipleProductPatternRulesMatchSameName_AllMatchingRulesAreApplied()
    {
        var doc = NormalizationTestData.RuleDocument(
            NormalizationTestData.Group("productPattern",
                NormalizationTestData.Rule(
                    "product.category", "DisplayName", "contains", "Tool",
                    actions: [NormalizationTestData.Action("setCategory", value: "Utility")]),
                NormalizationTestData.Rule(
                    "product.licensable", "DisplayName", "contains", "Tool",
                    actions: [NormalizationTestData.Action("setLicensable", value: "false")])));
        var sut = new SoftwareNormalizer(doc);

        var result = sut.Normalize(NormalizationTestData.Software("Acme Tool"));

        result!.Category.Should().Be("Utility");
        result.Licensable.Should().BeFalse();
        result.AppliedRuleIds.Should().Equal("product.category", "product.licensable");
    }

    // ----- 4단계: commonCleanup + 마무리(고정 동작) -----

    [Fact]
    public void Normalize_CommonCleanupPatternMatchesPartOfName_RemovesMatchedSubstring()
    {
        var doc = NormalizationTestData.RuleDocument(
            NormalizationTestData.Group("commonCleanup",
                NormalizationTestData.Rule("cleanup.test", "DisplayName", "regex", @"\s*\(Trial\)$", replaceWith: "")));
        var sut = new SoftwareNormalizer(doc);

        var result = sut.Normalize(NormalizationTestData.Software("Acme App (Trial)"));

        result!.ProductName.Should().Be("Acme App");
        result.AppliedRuleIds.Should().Contain("cleanup.test");
    }

    [Fact]
    public void Normalize_CommonCleanupPatternDoesNotMatch_NameUnchangedAndRuleIdNotRecorded()
    {
        var doc = NormalizationTestData.RuleDocument(
            NormalizationTestData.Group("commonCleanup",
                NormalizationTestData.Rule("cleanup.test", "DisplayName", "regex", @"\s*\(Trial\)$", replaceWith: "")));
        var sut = new SoftwareNormalizer(doc);

        var result = sut.Normalize(NormalizationTestData.Software("Acme App"));

        result!.ProductName.Should().Be("Acme App");
        result.AppliedRuleIds.Should().NotContain("cleanup.test");
    }

    [Fact]
    public void Normalize_CommonCleanupLeavesTrailingSeparator_FinalizeStripsTrailingSeparatorsAndWhitespace()
    {
        var doc = NormalizationTestData.RuleDocument(
            NormalizationTestData.Group("commonCleanup",
                NormalizationTestData.Rule("cleanup.test", "DisplayName", "regex", "x64", replaceWith: "")));
        var sut = new SoftwareNormalizer(doc);

        var result = sut.Normalize(NormalizationTestData.Software("Acme App - x64"));

        // "x64"만 지우면 "Acme App - "가 남는데, 마무리 단계에서 끝의 공백/구분자까지 떨어져야 한다.
        result!.ProductName.Should().Be("Acme App");
    }

    [Fact]
    public void Normalize_NameMatchesOnlyCommonCleanupButNoAliasOrProductPattern_UnclassifiedIsTrue()
    {
        var doc = NormalizationTestData.RuleDocument(
            NormalizationTestData.Group("commonCleanup",
                NormalizationTestData.Rule("cleanup.test", "DisplayName", "regex", @"\s*\(x64\)$", replaceWith: "")));
        var sut = new SoftwareNormalizer(doc);

        var result = sut.Normalize(NormalizationTestData.Software("Random Tool (x64)"));

        result!.Unclassified.Should().BeTrue();
        result.ProductName.Should().Be("Random Tool");
    }

    // ----- Match.Type: exact vs contains -----

    [Fact]
    public void Normalize_MatchTypeExact_DoesNotMatchWhenValueHasExtraText()
    {
        var doc = NormalizationTestData.RuleDocument(
            NormalizationTestData.Group("exclude",
                NormalizationTestData.Rule("exclude.test", "Publisher", "exact", "Acme")));
        var sut = new SoftwareNormalizer(doc);

        var result = sut.Normalize(NormalizationTestData.Software("App", publisher: "Acme Corp"));

        // "Acme Corp" != "Acme" 이므로 exact는 매칭되지 않고 제외되지 않아야 한다.
        result.Should().NotBeNull();
    }

    [Fact]
    public void Normalize_MatchTypeContains_MatchesSubstringAnywhereInValue()
    {
        var doc = NormalizationTestData.RuleDocument(
            NormalizationTestData.Group("exclude",
                NormalizationTestData.Rule("exclude.test", "Publisher", "contains", "Acme")));
        var sut = new SoftwareNormalizer(doc);

        var result = sut.Normalize(NormalizationTestData.Software("App", publisher: "Acme Corp"));

        result.Should().BeNull();
    }

    // ----- 경계: Publisher/DisplayVersion이 null인 경우 -----

    [Fact]
    public void Normalize_PublisherIsNullAndRuleMatchesOnPublisher_RuleDoesNotMatchAndDoesNotThrow()
    {
        var doc = NormalizationTestData.RuleDocument(
            NormalizationTestData.Group("productPattern",
                NormalizationTestData.Rule(
                    "product.test", "Publisher", "exact", "Acme Corp",
                    actions: [NormalizationTestData.Action("setCategory", value: "Tool")])));
        var sut = new SoftwareNormalizer(doc);

        var act = () => sut.Normalize(NormalizationTestData.Software("Acme Widget", publisher: null));

        act.Should().NotThrow();
        var result = act();
        result!.Category.Should().BeNull();
        result.Unclassified.Should().BeTrue();
    }

    [Fact]
    public void Normalize_DisplayVersionIsNullAndRuleMatchesOnDisplayVersion_RuleDoesNotMatchAndDoesNotThrow()
    {
        var doc = NormalizationTestData.RuleDocument(
            NormalizationTestData.Group("exclude",
                NormalizationTestData.Rule("exclude.test", "DisplayVersion", "exact", "0.0.0")));
        var sut = new SoftwareNormalizer(doc);

        var act = () => sut.Normalize(NormalizationTestData.Software("Some App", displayVersion: null));

        act.Should().NotThrow();
        act().Should().NotBeNull();
    }

    // ----- 예외 케이스: 알 수 없는 match.field / match.type / action.op -----

    [Fact]
    public void Normalize_UnknownMatchField_ThrowsInvalidOperationException()
    {
        var doc = NormalizationTestData.RuleDocument(
            NormalizationTestData.Group("exclude",
                NormalizationTestData.Rule("exclude.test", "UnknownField", "exact", "x")));
        var sut = new SoftwareNormalizer(doc);

        var act = () => sut.Normalize(NormalizationTestData.Software("App"));

        act.Should().Throw<InvalidOperationException>().WithMessage("*UnknownField*");
    }

    [Fact]
    public void Normalize_UnknownMatchType_ThrowsInvalidOperationException()
    {
        var doc = NormalizationTestData.RuleDocument(
            NormalizationTestData.Group("exclude",
                NormalizationTestData.Rule("exclude.test", "DisplayName", "fuzzy", "x")));
        var sut = new SoftwareNormalizer(doc);

        var act = () => sut.Normalize(NormalizationTestData.Software("App"));

        act.Should().Throw<InvalidOperationException>().WithMessage("*fuzzy*");
    }

    [Fact]
    public void Normalize_UnknownActionOp_ThrowsInvalidOperationException()
    {
        var doc = NormalizationTestData.RuleDocument(
            NormalizationTestData.Group("productPattern",
                NormalizationTestData.Rule(
                    "product.test", "DisplayName", "contains", "App",
                    actions: [NormalizationTestData.Action("unknownOp")])));
        var sut = new SoftwareNormalizer(doc);

        var act = () => sut.Normalize(NormalizationTestData.Software("App"));

        act.Should().Throw<InvalidOperationException>().WithMessage("*unknownOp*");
    }
}
