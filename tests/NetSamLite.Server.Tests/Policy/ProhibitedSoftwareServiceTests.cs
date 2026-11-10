using FluentAssertions;
using NetSamLite.Server.Data.Entities;
using NetSamLite.Server.Policy;
using NetSamLite.Server.Tests.Inventory;

namespace NetSamLite.Server.Tests.Policy;

/// <summary>
/// ProhibitedSoftwareService(CRUD + IsValid 검증) 단위/통합 테스트.
/// 알림 매칭 로직은 ProhibitedSoftwareAlertServiceTests에서 다룬다.
/// </summary>
public sealed class ProhibitedSoftwareServiceTests
{
    private static ProhibitedSoftwareRequest ValidRequest(
        string? productName = null, string reason = "보안 정책 위반") =>
        new(productName ?? $"Product-{Guid.NewGuid()}", reason);

    [Fact]
    public async Task CreateAsync_ValidRequest_PersistsAndSetsAddedAtUtc()
    {
        using var db = new SqliteTestDatabase();
        using var context = db.CreateContext();
        var service = new ProhibitedSoftwareService(context);
        var before = DateTime.UtcNow.AddSeconds(-1);
        var request = ValidRequest();

        var created = await service.CreateAsync(request, CancellationToken.None);

        created.Id.Should().BePositive();
        created.ProductName.Should().Be(request.ProductName);
        created.Reason.Should().Be(request.Reason);
        created.AddedAtUtc.Should().BeOnOrAfter(before);

        using var verifyContext = db.CreateContext();
        var stored = await new ProhibitedSoftwareService(verifyContext).ListAsync(CancellationToken.None);
        stored.Should().ContainSingle(p => p.Id == created.Id);
    }

    [Fact]
    public async Task DeleteAsync_ExistingId_RemovesAndReturnsTrue()
    {
        using var db = new SqliteTestDatabase();
        int id;
        using (var setupContext = db.CreateContext())
        {
            var created = await new ProhibitedSoftwareService(setupContext)
                .CreateAsync(ValidRequest(), CancellationToken.None);
            id = created.Id;
        }

        using var context = db.CreateContext();
        var result = await new ProhibitedSoftwareService(context).DeleteAsync(id, CancellationToken.None);

        result.Should().BeTrue();
        using var verifyContext = db.CreateContext();
        var remaining = await new ProhibitedSoftwareService(verifyContext).ListAsync(CancellationToken.None);
        remaining.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteAsync_NonExistingId_ReturnsFalse()
    {
        using var db = new SqliteTestDatabase();
        using var context = db.CreateContext();
        var service = new ProhibitedSoftwareService(context);

        var result = await service.DeleteAsync(999_999, CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task CreateAsync_DuplicateProductName_ThrowsDuplicateProhibitedSoftwareException()
    {
        using var db = new SqliteTestDatabase();
        using var context = db.CreateContext();
        var service = new ProhibitedSoftwareService(context);
        var request = ValidRequest(productName: "Foo");
        await service.CreateAsync(request, CancellationToken.None);

        Func<Task> act = () => service.CreateAsync(request, CancellationToken.None);

        await act.Should().ThrowAsync<DuplicateProhibitedSoftwareException>()
            .WithMessage("*Foo*");
    }

    [Fact]
    public async Task CreateAsync_DuplicateProductNameWithDifferentCase_ThrowsDuplicateProhibitedSoftwareException()
    {
        using var db = new SqliteTestDatabase();
        using var context = db.CreateContext();
        var service = new ProhibitedSoftwareService(context);
        await service.CreateAsync(ValidRequest(productName: "Foo"), CancellationToken.None);

        Func<Task> act = () => service.CreateAsync(ValidRequest(productName: "foo"), CancellationToken.None);

        await act.Should().ThrowAsync<DuplicateProhibitedSoftwareException>();
    }

    [Fact]
    public async Task CreateAsync_DuplicateProductNameWithLeadingAndTrailingWhitespace_ThrowsDuplicateProhibitedSoftwareException()
    {
        using var db = new SqliteTestDatabase();
        using var context = db.CreateContext();
        var service = new ProhibitedSoftwareService(context);
        await service.CreateAsync(ValidRequest(productName: "Zoom"), CancellationToken.None);

        Func<Task> act = () => service.CreateAsync(ValidRequest(productName: " Zoom "), CancellationToken.None);

        await act.Should().ThrowAsync<DuplicateProhibitedSoftwareException>();
    }

    [Fact]
    public async Task CreateAsync_DuplicateProductNameWithCollapsedInternalWhitespace_ThrowsDuplicateProhibitedSoftwareException()
    {
        using var db = new SqliteTestDatabase();
        using var context = db.CreateContext();
        var service = new ProhibitedSoftwareService(context);
        await service.CreateAsync(ValidRequest(productName: "Zoom  Meeting"), CancellationToken.None);

        Func<Task> act = () => service.CreateAsync(ValidRequest(productName: "Zoom Meeting"), CancellationToken.None);

        await act.Should().ThrowAsync<DuplicateProhibitedSoftwareException>();
    }

    [Fact]
    public async Task CreateAsync_ValidRequest_PersistsProductNameKeyMatchingComputeKey()
    {
        using var db = new SqliteTestDatabase();
        using var context = db.CreateContext();
        var service = new ProhibitedSoftwareService(context);
        var request = ValidRequest(productName: " Zoom  Meeting ");

        var created = await service.CreateAsync(request, CancellationToken.None);

        created.ProductNameKey.Should().Be(ProhibitedSoftwareEntity.ComputeKey(request.ProductName));
        created.ProductNameKey.Should().Be("ZOOM MEETING");

        using var verifyContext = db.CreateContext();
        var stored = await new ProhibitedSoftwareService(verifyContext).ListAsync(CancellationToken.None);
        stored.Should().ContainSingle(p => p.Id == created.Id)
            .Which.ProductNameKey.Should().Be("ZOOM MEETING");
    }

    [Fact]
    public async Task CreateAsync_DuplicateProductName_DoesNotPersistSecondEntity()
    {
        using var db = new SqliteTestDatabase();
        using var context = db.CreateContext();
        var service = new ProhibitedSoftwareService(context);
        await service.CreateAsync(ValidRequest(productName: "Foo"), CancellationToken.None);

        Func<Task> act = () => service.CreateAsync(ValidRequest(productName: "FOO"), CancellationToken.None);
        await act.Should().ThrowAsync<DuplicateProhibitedSoftwareException>();

        using var verifyContext = db.CreateContext();
        var stored = await new ProhibitedSoftwareService(verifyContext).ListAsync(CancellationToken.None);
        stored.Should().ContainSingle(p => p.ProductName == "Foo");
    }

    [Theory]
    [InlineData("", "Reason", false)]
    [InlineData("   ", "Reason", false)]
    [InlineData("Product", "", false)]
    [InlineData("Product", "   ", false)]
    [InlineData("Product", "Reason", true)]
    public void IsValid_BoundaryAndNormalCases_ReturnsExpectedResult(
        string productName, string reason, bool expected)
    {
        var request = new ProhibitedSoftwareRequest(productName, reason);

        ProhibitedSoftwareService.IsValid(request).Should().Be(expected);
    }
}
