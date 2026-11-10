using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using NetSamLite.Server.Data.Entities;
using NetSamLite.Server.Policy;
using NetSamLite.Server.Tests.Inventory;

namespace NetSamLite.Server.Tests.Policy;

/// <summary>
/// /api/v1/prohibited-software CRUD + /api/v1/prohibited-software/alerts 엔드포인트를
/// 실제 HTTP 호출(WebApplicationFactory)로 검증한다. 알림 매칭 로직 자체는
/// ProhibitedSoftwareAlertServiceTests에서 다룬다.
/// </summary>
public sealed class ProhibitedSoftwareEndpointsTests : IClassFixture<InventoryApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly InventoryApiFactory _factory;

    public ProhibitedSoftwareEndpointsTests(InventoryApiFactory factory)
    {
        _factory = factory;
    }

    private static ProhibitedSoftwareRequest ValidRequest(string? productName = null, string reason = "정책 위반") =>
        new(productName ?? $"Product-{Guid.NewGuid()}", reason);

    [Fact]
    public async Task PostProhibitedSoftware_ValidRequest_ReturnsCreatedWithLocationAndBody()
    {
        var client = _factory.CreateClient();
        var request = ValidRequest();

        var response = await client.PostAsJsonAsync("/api/v1/prohibited-software", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location!.ToString().Should().Contain("/api/v1/prohibited-software/");
        var created = await response.Content.ReadFromJsonAsync<ProhibitedSoftwareEntity>(JsonOptions);
        created!.ProductName.Should().Be(request.ProductName);
        created.Reason.Should().Be(request.Reason);
        created.Id.Should().BePositive();
    }

    [Fact]
    public async Task GetProhibitedSoftware_AfterCreateThenDelete_ListReflectsBothChanges()
    {
        var client = _factory.CreateClient();
        var request = ValidRequest();
        var createResponse = await client.PostAsJsonAsync("/api/v1/prohibited-software", request);
        var created = await createResponse.Content.ReadFromJsonAsync<ProhibitedSoftwareEntity>(JsonOptions);

        var afterCreate = await client.GetAsync("/api/v1/prohibited-software");
        var listAfterCreate = await afterCreate.Content.ReadFromJsonAsync<List<ProhibitedSoftwareEntity>>(JsonOptions);
        listAfterCreate!.Should().Contain(p => p.ProductName == request.ProductName);

        await client.DeleteAsync($"/api/v1/prohibited-software/{created!.Id}");

        var afterDelete = await client.GetAsync("/api/v1/prohibited-software");
        var listAfterDelete = await afterDelete.Content.ReadFromJsonAsync<List<ProhibitedSoftwareEntity>>(JsonOptions);
        listAfterDelete!.Should().NotContain(p => p.Id == created.Id);
    }

    [Fact]
    public async Task DeleteProhibitedSoftware_NonExistingId_ReturnsNotFound()
    {
        var client = _factory.CreateClient();

        var response = await client.DeleteAsync("/api/v1/prohibited-software/999999997");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("", "Reason")]
    [InlineData("   ", "Reason")]
    [InlineData("Product", "")]
    [InlineData("Product", "   ")]
    public async Task PostProhibitedSoftware_EmptyProductNameOrReason_ReturnsBadRequest(
        string productName, string reason)
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/prohibited-software", new ProhibitedSoftwareRequest(productName, reason));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostProhibitedSoftware_DuplicateProductName_ReturnsConflict()
    {
        var client = _factory.CreateClient();
        var request = ValidRequest();
        var firstResponse = await client.PostAsJsonAsync("/api/v1/prohibited-software", request);
        firstResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var secondResponse = await client.PostAsJsonAsync("/api/v1/prohibited-software", request);

        secondResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await secondResponse.Content.ReadAsStringAsync();
        body.Should().Contain(request.ProductName);
    }

    [Fact]
    public async Task PostProhibitedSoftware_DuplicateProductNameWithDifferentCase_ReturnsConflict()
    {
        var client = _factory.CreateClient();
        var productName = $"Dup-{Guid.NewGuid()}";
        var firstResponse = await client.PostAsJsonAsync(
            "/api/v1/prohibited-software", new ProhibitedSoftwareRequest(productName.ToUpperInvariant(), "정책 위반"));
        firstResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var secondResponse = await client.PostAsJsonAsync(
            "/api/v1/prohibited-software", new ProhibitedSoftwareRequest(productName.ToLowerInvariant(), "정책 위반"));

        secondResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task GetProhibitedSoftwareAlerts_ReturnsOk()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/prohibited-software/alerts");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        Action parse = () => JsonDocument.Parse(json).Dispose();
        parse.Should().NotThrow();
    }
}
