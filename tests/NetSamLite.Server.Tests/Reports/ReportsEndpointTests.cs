using System.Net;
using FluentAssertions;
using NetSamLite.Server.Tests.Inventory;

namespace NetSamLite.Server.Tests.Reports;

/// <summary>
/// GET /api/v1/reports/software-inventory.csv 엔드포인트를 실제 HTTP 호출로 검증한다.
/// CSV 포맷 자체는 SoftwareInventoryCsvWriterTests에서 다루고, 여기서는 상태 코드/Content-Type/
/// UTF-8 BOM처럼 엔드포인트 레벨에서만 확인할 수 있는 것들을 다룬다.
/// </summary>
public sealed class ReportsEndpointTests : IClassFixture<InventoryApiFactory>
{
    private readonly InventoryApiFactory _factory;

    public ReportsEndpointTests(InventoryApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetSoftwareInventoryCsv_ReturnsOkWithCsvContentTypeAndUtf8Bom()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/reports/software-inventory.csv");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Should().HaveCountGreaterThanOrEqualTo(3);
        bytes[..3].Should().Equal([0xEF, 0xBB, 0xBF]);
    }
}
