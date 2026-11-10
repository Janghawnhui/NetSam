using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using NetSamLite.Server.Data.Entities;
using NetSamLite.Server.Licensing;

namespace NetSamLite.Server.Tests.Licensing;

/// <summary>
/// /api/v1/licenses CRUD 엔드포인트와 /api/v1/licensing/report 조회 엔드포인트를
/// 실제 HTTP 호출(WebApplicationFactory)로 검증한다. 모델 바인딩/상태 코드/본문 형태가
/// 관심사이고, 매칭 로직 자체는 LicenseMatchingServiceTests에서 다룬다.
///
/// InventoryEndpointTests.InventoryApiFactory를 그대로 재사용한다 — 테스트 전용 임시 SQLite
/// 파일로 연결 문자열을 바꿔 끼우고 Program.cs의 실제 시작 경로(마이그레이션 포함)를 그대로 탄다.
/// </summary>
public sealed class LicenseEndpointsTests : IClassFixture<InventoryApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly InventoryApiFactory _factory;

    public LicenseEndpointsTests(InventoryApiFactory factory)
    {
        _factory = factory;
    }

    private static LicenseRequest ValidRequest(string? productName = null, int quantity = 5, string? licenseType = "Perpetual") =>
        new(productName ?? $"Product-{Guid.NewGuid()}", licenseType!, quantity, null, "note");

    [Fact]
    public async Task PostLicenses_ValidRequest_ReturnsCreatedWithLocationAndBody()
    {
        var client = _factory.CreateClient();
        var request = ValidRequest();

        var response = await client.PostAsJsonAsync("/api/v1/licenses", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location!.ToString().Should().Contain("/api/v1/licenses/");
        var created = await response.Content.ReadFromJsonAsync<LicenseEntity>(JsonOptions);
        created!.ProductName.Should().Be(request.ProductName);
        created.Quantity.Should().Be(request.Quantity);
        created.Id.Should().BePositive();
    }

    [Fact]
    public async Task GetLicenses_AfterCreate_ReturnsListContainingCreatedLicense()
    {
        var client = _factory.CreateClient();
        var request = ValidRequest();
        await client.PostAsJsonAsync("/api/v1/licenses", request);

        var response = await client.GetAsync("/api/v1/licenses");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var licenses = await response.Content.ReadFromJsonAsync<List<LicenseEntity>>(JsonOptions);
        licenses!.Should().Contain(l => l.ProductName == request.ProductName);
    }

    [Fact]
    public async Task PutLicenses_ExistingId_UpdatesAndReturnsOk()
    {
        var client = _factory.CreateClient();
        var createResponse = await client.PostAsJsonAsync("/api/v1/licenses", ValidRequest(quantity: 3));
        var created = await createResponse.Content.ReadFromJsonAsync<LicenseEntity>(JsonOptions);
        var updatedProductName = $"Updated-{Guid.NewGuid()}";

        var response = await client.PutAsJsonAsync(
            $"/api/v1/licenses/{created!.Id}", ValidRequest(updatedProductName, quantity: 9));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<LicenseEntity>(JsonOptions);
        updated!.ProductName.Should().Be(updatedProductName);
        updated.Quantity.Should().Be(9);
    }

    [Fact]
    public async Task PutLicenses_NonExistingId_ReturnsNotFound()
    {
        var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync("/api/v1/licenses/999999999", ValidRequest());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteLicenses_ExistingId_ReturnsNoContentThenSecondDeleteReturnsNotFound()
    {
        var client = _factory.CreateClient();
        var createResponse = await client.PostAsJsonAsync("/api/v1/licenses", ValidRequest());
        var created = await createResponse.Content.ReadFromJsonAsync<LicenseEntity>(JsonOptions);

        var firstDelete = await client.DeleteAsync($"/api/v1/licenses/{created!.Id}");
        var secondDelete = await client.DeleteAsync($"/api/v1/licenses/{created.Id}");

        firstDelete.StatusCode.Should().Be(HttpStatusCode.NoContent);
        secondDelete.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteLicenses_NonExistingId_ReturnsNotFound()
    {
        var client = _factory.CreateClient();

        var response = await client.DeleteAsync("/api/v1/licenses/999999998");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PostLicenses_EmptyProductName_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/licenses", ValidRequest(productName: ""));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostLicenses_NegativeQuantity_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/licenses", ValidRequest(quantity: -1));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task PostLicenses_LicenseTypeEmptyOrWhitespaceOrNull_ReturnsBadRequest(string? licenseType)
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/licenses", ValidRequest(licenseType: licenseType));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task PutLicenses_LicenseTypeEmptyOrWhitespaceOrNull_ReturnsBadRequest(string? licenseType)
    {
        var client = _factory.CreateClient();
        var createResponse = await client.PostAsJsonAsync("/api/v1/licenses", ValidRequest());
        var created = await createResponse.Content.ReadFromJsonAsync<LicenseEntity>(JsonOptions);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/licenses/{created!.Id}", ValidRequest(licenseType: licenseType));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetLicensingReport_ReturnsOkWithExpectedJsonShape()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/licensing/report");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        root.TryGetProperty("matched", out _).Should().BeTrue();
        root.TryGetProperty("unlicensedProducts", out _).Should().BeTrue();
        root.TryGetProperty("excludedFromLicensing", out _).Should().BeTrue();
        root.TryGetProperty("unclassifiedSoftware", out _).Should().BeTrue();
    }
}
