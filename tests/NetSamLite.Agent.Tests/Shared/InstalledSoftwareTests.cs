using FluentAssertions;
using NetSamLite.Shared;

namespace NetSamLite.Agent.Tests.Shared;

/// <summary>
/// InstalledSoftware 자체는 유효성 검사를 하지 않는 순수 DTO record다.
/// DisplayName 필수 여부 등 규칙은 RegistrySoftwareCollector가 책임지므로,
/// 여기서는 record의 값 동등성과 null 허용 필드만 확인한다.
/// </summary>
public class InstalledSoftwareTests
{
    [Fact]
    public void Equals_TwoInstancesWithSameValues_AreEqual()
    {
        var a = new InstalledSoftware("App", "1.0", "Pub", "20250101", @"C:\App");
        var b = new InstalledSoftware("App", "1.0", "Pub", "20250101", @"C:\App");

        a.Should().Be(b);
    }

    [Fact]
    public void Constructor_OptionalFieldsNull_DoesNotThrow()
    {
        // DisplayVersion/Publisher/InstallDate/InstallLocation은 모두 nullable이다.
        var act = () => new InstalledSoftware("App", null, null, null, null);

        act.Should().NotThrow();
    }
}
