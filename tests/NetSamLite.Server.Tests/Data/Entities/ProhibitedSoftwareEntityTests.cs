using FluentAssertions;
using NetSamLite.Server.Data.Entities;

namespace NetSamLite.Server.Tests.Data.Entities;

/// <summary>
/// ProhibitedSoftwareEntity.ComputeKey 정규화 규칙(Trim + 공백 축약 + 대문자화) 단위 테스트.
/// DB 유니크 인덱스와 ProhibitedSoftwareService의 중복 체크가 모두 이 메서드에 의존한다.
/// </summary>
public sealed class ProhibitedSoftwareEntityTests
{
    [Theory]
    [InlineData("Zoom", "ZOOM")]
    [InlineData(" Zoom ", "ZOOM")]
    [InlineData("Zoom  Meeting", "ZOOM MEETING")]
    [InlineData("  Zoom   Meeting  ", "ZOOM MEETING")]
    [InlineData("zoom", "ZOOM")]
    public void ComputeKey_VariousWhitespaceAndCase_ReturnsTrimmedCollapsedUppercaseKey(
        string productName, string expected)
    {
        var key = ProhibitedSoftwareEntity.ComputeKey(productName);

        key.Should().Be(expected);
    }

    [Fact]
    public void ComputeKey_NonAsciiKoreanProductName_ReturnsSameStringUnchanged()
    {
        // 한글은 대소문자 구분이 없으므로 ToUpperInvariant를 적용해도 원본과 같아야 한다.
        // (SQLite NOCASE 콜레이션은 ASCII만 처리하므로 ComputeKey는 이 경우에도
        // 깨지지 않고 정상적으로 같은 키를 돌려줘야 한다.)
        var key = ProhibitedSoftwareEntity.ComputeKey("카카오톡");

        key.Should().Be("카카오톡");
    }

    [Fact]
    public void ComputeKey_NonAsciiKoreanProductNameWithWhitespace_TrimsAndCollapsesButKeepsCase()
    {
        var key = ProhibitedSoftwareEntity.ComputeKey("  카카오톡   메신저  ");

        key.Should().Be("카카오톡 메신저");
    }
}
