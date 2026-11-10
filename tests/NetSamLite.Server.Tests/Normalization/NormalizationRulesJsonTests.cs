using FluentAssertions;
using NetSamLite.Server.Normalization;

namespace NetSamLite.Server.Tests.Normalization;

/// <summary>
/// 실제 Normalization/normalization-rules.json을 NormalizationRuleLoader로 로드해서 돌리는
/// 회귀 테스트. 규칙 파일(JSON)의 내용이 바뀌면 이 테스트들이 깨지는 게 의도다.
/// </summary>
public sealed class NormalizationRulesJsonTests
{
    private static readonly NormalizationRuleDocument Document =
        NormalizationRuleLoader.LoadFromFile(
            Path.Combine(AppContext.BaseDirectory, "Normalization", "normalization-rules.json"));

    private static readonly SoftwareNormalizer Sut = new(Document);

    // ----- 1. exclude.windows-update-kb -----

    [Fact]
    public void Normalize_WindowsUpdateKbSample_ReturnsNull()
    {
        var result = Sut.Normalize(
            NormalizationTestData.Software("Update for x64-based Windows Systems (KB5001716)"));

        result.Should().BeNull();
    }

    [Theory]
    [InlineData("Security Update for Microsoft Office (KB1234567)")]
    [InlineData("Hotfix for Windows (KB123456)")]
    public void Normalize_KbUpdateAliasVariants_ReturnsNull(string displayName)
    {
        var result = Sut.Normalize(NormalizationTestData.Software(displayName));

        result.Should().BeNull();
    }

    [Theory]
    [InlineData("Update for Windows (KB12345)")] // 5자리: \d{6,7} 범위보다 짧음
    [InlineData("Update for Windows (KB123456789)")] // 9자리: \d{6,7} 범위보다 긺
    public void Normalize_KbNumberOutsideSixToSevenDigitRange_IsNotExcluded(string displayName)
    {
        var result = Sut.Normalize(NormalizationTestData.Software(displayName));

        result.Should().NotBeNull();
    }

    // ----- 2. alias.ms365-apps-kr -----

    [Fact]
    public void Normalize_Ms365KoreanLocaleSuffix_ReplacesWithMicrosoft365Apps()
    {
        var result = Sut.Normalize(NormalizationTestData.Software("엔터프라이즈용 Microsoft 365 앱 - ko-kr"));

        result!.ProductName.Should().Be("Microsoft 365 Apps");
        result.Unclassified.Should().BeFalse();
        result.AppliedRuleIds.Should().Contain("alias.ms365-apps-kr");
    }

    [Fact]
    public void Normalize_Ms365WithoutLocaleSuffix_ReplacesWithMicrosoft365Apps()
    {
        var result = Sut.Normalize(NormalizationTestData.Software("엔터프라이즈용 Microsoft 365 앱"));

        result!.ProductName.Should().Be("Microsoft 365 Apps");
    }

    [Fact]
    public void Normalize_Ms365SourceSampleWithTrailingVersionText_DoesNotActuallyMatchAliasRule()
    {
        // 규칙 파일의 note/sourceSample에는 예시로
        // "엔터프라이즈용 Microsoft 365 앱 - ko-kr (버전 16.0.20527.20150)"가 적혀 있지만,
        // 실제 정규식(alias.ms365-apps-kr)은 로케일 접미사 바로 뒤에 문자열 끝($)을 요구한다.
        // 뒤에 "(버전 ...)" 같은 텍스트가 더 붙으면 매칭되지 않는다 — 코드 버그는 아니고
        // JSON 규칙의 note/sourceSample 설명과 실제 동작이 어긋나는 사례라 회귀로 남겨둔다.
        var displayName = "엔터프라이즈용 Microsoft 365 앱 - ko-kr (버전 16.0.20527.20150)";

        var result = Sut.Normalize(NormalizationTestData.Software(displayName));

        result!.Unclassified.Should().BeTrue();
        result.ProductName.Should().NotBe("Microsoft 365 Apps");
    }

    [Fact]
    public void Normalize_Ms365NameWithExtraTrailingWord_DoesNotMatchAliasRule()
    {
        var result = Sut.Normalize(NormalizationTestData.Software("엔터프라이즈용 Microsoft 365 앱 Pro"));

        result!.Unclassified.Should().BeTrue();
        result.ProductName.Should().Be("엔터프라이즈용 Microsoft 365 앱 Pro");
    }

    // ----- 3. product.chrome-webapp -----

    [Fact]
    public void Normalize_ChromePublisherExactMatch_SetsWebAppCategoryAndNotLicensable()
    {
        var software = NormalizationTestData.Software("Chrome 원격 데스크톱", publisher: @"Google\Chrome");

        var result = Sut.Normalize(software);

        result!.Category.Should().Be("WebApp");
        result.Licensable.Should().BeFalse();
        result.ProductName.Should().Be("Chrome 원격 데스크톱");
        result.Unclassified.Should().BeFalse();
    }

    [Fact]
    public void Normalize_ChromePublisherWithExtraText_DoesNotMatchExactRule()
    {
        var software = NormalizationTestData.Software("Some App", publisher: @"Google\Chrome Inc");

        var result = Sut.Normalize(software);

        result!.Category.Should().BeNull();
        result.Licensable.Should().BeTrue();
        result.Unclassified.Should().BeTrue();
    }

    // ----- 4. product.msvc-redistributable-runtime (sourceSample 3건 전부) -----

    [Fact]
    public void Normalize_MsvcSampleWithDoubleSpaceBeforeArchitecture_CategorizesAndStripsArchitectureToken()
    {
        var software = NormalizationTestData.Software("Microsoft Visual C++ 2010  x64 Redistributable - 10.0.40219");

        var result = Sut.Normalize(software);

        result!.Category.Should().Be("Runtime(무료)");
        result.Licensable.Should().BeFalse();
        result.Attributes.Should().ContainKey("Architecture").WhoseValue.Should().Be("x64");
        result.ProductName.Should().Be("Microsoft Visual C++ 2010 Redistributable - 10.0.40219");
        result.ProductName.Should().NotContain("x64");
    }

    [Fact]
    public void Normalize_MsvcSampleWithArchitectureAfterVersionNumber_CategorizesAndStripsArchitectureToken()
    {
        var software = NormalizationTestData.Software("Microsoft Visual C++ 2008 Redistributable - x64 9.0.30729.4148");

        var result = Sut.Normalize(software);

        result!.Category.Should().Be("Runtime(무료)");
        result.Licensable.Should().BeFalse();
        result.Attributes["Architecture"].Should().Be("x64");
        result.ProductName.Should().Be("Microsoft Visual C++ 2008 Redistributable - 9.0.30729.4148");
    }

    [Fact]
    public void Normalize_MsvcSampleWithArchitectureInParenthesesAndVPrefixVersion_CategorizesAndStripsArchitectureToken()
    {
        var software = NormalizationTestData.Software("Microsoft Visual C++ v14 Redistributable (x64) - 14.51.36247");

        var result = Sut.Normalize(software);

        result!.Category.Should().Be("Runtime(무료)");
        result.Licensable.Should().BeFalse();
        result.Attributes["Architecture"].Should().Be("x64");
        result.ProductName.Should().Be("Microsoft Visual C++ v14 Redistributable - 14.51.36247");
    }

    // ----- 5. commonCleanup: 로케일 접미사 / 아키텍처 토큰 (sourceSample 3건 전부) -----

    [Theory]
    [InlineData("Everything 1.4.1.1032 (x64)", "Everything 1.4.1.1032")]
    [InlineData("Notepad++ (64-bit x64)", "Notepad++")]
    [InlineData("TouchEn nxKey with E2E for 32bit", "TouchEn nxKey with E2E for")]
    public void Normalize_CommonCleanupArchitectureTokenSamples_StripsArchitectureTokenButStaysUnclassified(
        string displayName, string expectedProductName)
    {
        var result = Sut.Normalize(NormalizationTestData.Software(displayName));

        result!.ProductName.Should().Be(expectedProductName);
        result.Unclassified.Should().BeTrue();
    }

    // ----- 미분류(Unclassified) -----

    [Fact]
    public void Normalize_ArbitraryUnmatchedSoftwareName_IsUnclassified()
    {
        var result = Sut.Normalize(NormalizationTestData.Software("Totally Custom In-House Tool v3"));

        result!.Unclassified.Should().BeTrue();
        result.Category.Should().BeNull();
        result.Licensable.Should().BeTrue();
    }
}
