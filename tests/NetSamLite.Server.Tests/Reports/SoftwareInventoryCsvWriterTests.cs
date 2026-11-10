using FluentAssertions;
using NetSamLite.Server.Licensing;
using NetSamLite.Server.Reports;

namespace NetSamLite.Server.Tests.Reports;

/// <summary>
/// SoftwareInventoryCsvWriter.Write 단위 테스트. LicenseMatchReport의 네 섹션이 각각
/// 설계된 컬럼 값(분류상태/라이선스대상여부/카테고리 등)으로 변환되는지, 그리고 쉼표/쌍따옴표가
/// 포함된 제품명이 CSV 규칙대로 이스케이프되는지를 다룬다.
/// </summary>
public sealed class SoftwareInventoryCsvWriterTests
{
    private const string Header = "제품명,분류상태,카테고리,라이선스대상여부,설치디바이스수,보유라이선스수량,상태,부족분";

    private static LicenseMatchReport SampleReport(string matchedProductName = "Microsoft 365 Apps") => new(
        Matched: [new LicenseMatchItem(matchedProductName, InstalledCount: 3, LicensedQuantity: 5, Status: "미사용 라이선스", Shortage: -2)],
        UnlicensedProducts: [new UnlicensedProduct("Unlicensed App", InstalledCount: 2)],
        ExcludedFromLicensing: [new ExcludedProduct("Runtime App", Category: "Runtime(무료)", InstalledCount: 7)],
        UnclassifiedSoftware: [new UnclassifiedProduct("Mystery App", InstalledCount: 1)],
        FailedCount: 0);

    [Fact]
    public void Write_ReportWithOneItemPerSection_ProducesHeaderPlusFourRows()
    {
        var csv = SoftwareInventoryCsvWriter.Write(SampleReport());

        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(5);
        lines[0].Should().Be(Header);
    }

    [Fact]
    public void Write_MatchedSection_FillsLicensedQuantityStatusAndShortage()
    {
        var csv = SoftwareInventoryCsvWriter.Write(SampleReport());
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        lines[1].Should().Be("Microsoft 365 Apps,매칭,,Y,3,5,미사용 라이선스,-2");
    }

    [Fact]
    public void Write_UnlicensedSection_MarksLicensableYWithEmptyQuantityStatusShortage()
    {
        var csv = SoftwareInventoryCsvWriter.Write(SampleReport());
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        lines[2].Should().Be("Unlicensed App,라이선스미등록,,Y,2,,,");
    }

    [Fact]
    public void Write_ExcludedSection_FillsCategoryAndMarksLicensableN()
    {
        var csv = SoftwareInventoryCsvWriter.Write(SampleReport());
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        lines[3].Should().Be("Runtime App,매칭제외,Runtime(무료),N,7,,,");
    }

    [Fact]
    public void Write_UnclassifiedSection_LeavesCategoryAndLicensableEmpty()
    {
        var csv = SoftwareInventoryCsvWriter.Write(SampleReport());
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        lines[4].Should().Be("Mystery App,미분류,,,1,,,");
    }

    [Fact]
    public void Write_ProductNameContainsComma_IsQuotedInOutput()
    {
        var csv = SoftwareInventoryCsvWriter.Write(SampleReport(matchedProductName: "Acme Suite, Pro Edition"));
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        lines[1].Should().Be("\"Acme Suite, Pro Edition\",매칭,,Y,3,5,미사용 라이선스,-2");
    }

    [Fact]
    public void Write_ProductNameContainsDoubleQuote_IsEscapedWithDoubledQuotesAndWrapped()
    {
        var csv = SoftwareInventoryCsvWriter.Write(SampleReport(matchedProductName: "Acme \"Pro\" Suite"));
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        lines[1].Should().Be("\"Acme \"\"Pro\"\" Suite\",매칭,,Y,3,5,미사용 라이선스,-2");
    }

    [Theory]
    [InlineData("=1+1", "'=1+1")]
    [InlineData("+1", "'+1")]
    [InlineData("-1", "'-1")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("\t1", "'\t1")]
    [InlineData("\r1", "'\r1")]
    [InlineData("Normal App", "Normal App")]
    public void Write_ProductNameStartsWithFormulaInjectionChar_PrefixesWithSingleQuoteWhenRisky(
        string productName, string expectedFirstField)
    {
        var csv = SoftwareInventoryCsvWriter.Write(SampleReport(matchedProductName: productName));
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var firstField = lines[1].Split(',')[0];

        firstField.Should().Be(expectedFirstField);
    }

    [Fact]
    public void Write_ProductNameStartsWithFormulaCharAndContainsComma_IsEscapedThenWrappedInQuotes()
    {
        var csv = SoftwareInventoryCsvWriter.Write(SampleReport(matchedProductName: "=A,B"));
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        lines[1].Should().Be("\"'=A,B\",매칭,,Y,3,5,미사용 라이선스,-2");
    }

    [Fact]
    public void Write_UnlicensedProductNameStartsWithEquals_PrefixesWithSingleQuote()
    {
        var report = SampleReport() with
        {
            UnlicensedProducts = [new UnlicensedProduct("=cmd|'/c calc'!A1", InstalledCount: 2)],
        };

        var csv = SoftwareInventoryCsvWriter.Write(report);
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        lines[2].Should().StartWith("'=cmd");
    }

    [Fact]
    public void Write_EmptyReport_ReturnsHeaderOnly()
    {
        var emptyReport = new LicenseMatchReport([], [], [], [], FailedCount: 0);

        var csv = SoftwareInventoryCsvWriter.Write(emptyReport);

        csv.Should().Be(Header + "\n");
    }
}
