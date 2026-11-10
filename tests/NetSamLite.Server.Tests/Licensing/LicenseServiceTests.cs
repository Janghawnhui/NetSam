using FluentAssertions;
using NetSamLite.Server.Licensing;
using NetSamLite.Server.Tests.Inventory;

namespace NetSamLite.Server.Tests.Licensing;

/// <summary>
/// LicenseService(CRUD + IsValid 검증) 단위/통합 테스트. HTTP 레벨 동작은 LicenseEndpointsTests에서,
/// 여기서는 서비스 로직 자체(특히 존재하지 않는 id, IsValid 경계)를 다룬다.
/// </summary>
public sealed class LicenseServiceTests
{
    private static LicenseRequest ValidRequest(
        string? productName = null, string licenseType = "Perpetual", int quantity = 5) =>
        new(productName ?? $"Product-{Guid.NewGuid()}", licenseType, quantity, null, "note");

    [Fact]
    public async Task CreateAsync_ValidRequest_PersistsAndReturnsEntityWithId()
    {
        using var db = new SqliteTestDatabase();
        using var context = db.CreateContext();
        var service = new LicenseService(context);
        var request = ValidRequest(quantity: 7);

        var created = await service.CreateAsync(request, CancellationToken.None);

        created.Id.Should().BePositive();
        created.ProductName.Should().Be(request.ProductName);
        created.Quantity.Should().Be(7);

        using var verifyContext = db.CreateContext();
        var stored = await new LicenseService(verifyContext).ListAsync(CancellationToken.None);
        stored.Should().ContainSingle(l => l.Id == created.Id);
    }

    [Fact]
    public async Task UpdateAsync_ExistingId_UpdatesFieldsAndReturnsEntity()
    {
        using var db = new SqliteTestDatabase();
        int id;
        using (var setupContext = db.CreateContext())
        {
            var created = await new LicenseService(setupContext).CreateAsync(
                ValidRequest(quantity: 3), CancellationToken.None);
            id = created.Id;
        }

        using var context = db.CreateContext();
        var service = new LicenseService(context);
        var updatedName = $"Updated-{Guid.NewGuid()}";

        var updated = await service.UpdateAsync(id, ValidRequest(updatedName, quantity: 42), CancellationToken.None);

        updated.Should().NotBeNull();
        updated!.ProductName.Should().Be(updatedName);
        updated.Quantity.Should().Be(42);
    }

    [Fact]
    public async Task UpdateAsync_NonExistingId_ReturnsNull()
    {
        using var db = new SqliteTestDatabase();
        using var context = db.CreateContext();
        var service = new LicenseService(context);

        var result = await service.UpdateAsync(999_999, ValidRequest(), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_ExistingId_RemovesAndReturnsTrue()
    {
        using var db = new SqliteTestDatabase();
        int id;
        using (var setupContext = db.CreateContext())
        {
            var created = await new LicenseService(setupContext).CreateAsync(ValidRequest(), CancellationToken.None);
            id = created.Id;
        }

        using var context = db.CreateContext();
        var result = await new LicenseService(context).DeleteAsync(id, CancellationToken.None);

        result.Should().BeTrue();
        using var verifyContext = db.CreateContext();
        var remaining = await new LicenseService(verifyContext).ListAsync(CancellationToken.None);
        remaining.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteAsync_NonExistingId_ReturnsFalse()
    {
        using var db = new SqliteTestDatabase();
        using var context = db.CreateContext();
        var service = new LicenseService(context);

        var result = await service.DeleteAsync(999_999, CancellationToken.None);

        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("", "Perpetual", 1, false)]
    [InlineData("   ", "Perpetual", 1, false)]
    [InlineData("Product", "", 1, false)]
    [InlineData("Product", "   ", 1, false)]
    [InlineData("Product", "Perpetual", -1, false)]
    [InlineData("Product", "Perpetual", 0, true)]
    [InlineData("Product", "Perpetual", 1, true)]
    public void IsValid_BoundaryAndNormalCases_ReturnsExpectedResult(
        string productName, string licenseType, int quantity, bool expected)
    {
        var request = new LicenseRequest(productName, licenseType, quantity, null, null);

        LicenseService.IsValid(request).Should().Be(expected);
    }

    [Fact]
    public void IsValid_NullLicenseType_ReturnsFalse()
    {
        var request = new LicenseRequest("Product", null!, 1, null, null);

        LicenseService.IsValid(request).Should().BeFalse();
    }
}
