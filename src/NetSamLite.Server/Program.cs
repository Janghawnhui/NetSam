using System.Text;
using Microsoft.EntityFrameworkCore;
using NetSamLite.Server.Data;
using NetSamLite.Server.Devices;
using NetSamLite.Server.Inventory;
using NetSamLite.Server.Licensing;
using NetSamLite.Server.Normalization;
using NetSamLite.Server.Policy;
using NetSamLite.Server.Reports;
using NetSamLite.Shared;

var builder = WebApplication.CreateBuilder(args);

// 로컬 개발 연결 문자열은 appsettings.json에 두지 않는다(절대 규칙 4) — dotnet user-secrets로 설정:
//   dotnet user-secrets set "ConnectionStrings:NetSamLite" "Data Source=netsamlite.db" --project src/NetSamLite.Server
var connectionString = builder.Configuration.GetConnectionString("NetSamLite")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:NetSamLite이 설정돼 있지 않습니다. " +
        "dotnet user-secrets set \"ConnectionStrings:NetSamLite\" \"Data Source=netsamlite.db\" --project src/NetSamLite.Server 를 실행하세요.");

builder.Services.AddDbContext<NetSamLiteDbContext>(options => options.UseSqlite(connectionString));
builder.Services.AddScoped<InventoryIngestionService>();

var normalizationRulesPath = Path.Combine(AppContext.BaseDirectory, "Normalization", "normalization-rules.json");
builder.Services.AddSingleton(NormalizationRuleLoader.LoadFromFile(normalizationRulesPath));
builder.Services.AddSingleton<SoftwareNormalizer>();
builder.Services.AddScoped<LicenseMatchingService>();
builder.Services.AddScoped<ProhibitedSoftwareAlertService>();
builder.Services.AddScoped<DeviceDirectoryService>();
builder.Services.AddScoped<LicenseService>();
builder.Services.AddScoped<ProhibitedSoftwareService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<NetSamLiteDbContext>().Database.Migrate();
}

app.MapPost("/api/v1/inventory", async (
    InventoryReport? report, InventoryIngestionService ingestionService, CancellationToken cancellationToken) =>
{
    if (report?.Device is null)
    {
        return Results.BadRequest("Device 정보가 필요합니다.");
    }

    var result = await ingestionService.IngestAsync(report, cancellationToken);
    return Results.Ok(result);
});

app.MapGet("/api/v1/licenses", async (LicenseService licenseService, CancellationToken ct) =>
    Results.Ok(await licenseService.ListAsync(ct)));

app.MapPost("/api/v1/licenses", async (LicenseRequest request, LicenseService licenseService, CancellationToken ct) =>
{
    if (!LicenseService.IsValid(request))
    {
        return Results.BadRequest("ProductName/LicenseType은 필수이고 Quantity는 0 이상이어야 합니다.");
    }

    var license = await licenseService.CreateAsync(request, ct);
    return Results.Created($"/api/v1/licenses/{license.Id}", license);
});

app.MapPut("/api/v1/licenses/{id:int}", async (
    int id, LicenseRequest request, LicenseService licenseService, CancellationToken ct) =>
{
    if (!LicenseService.IsValid(request))
    {
        return Results.BadRequest("ProductName/LicenseType은 필수이고 Quantity는 0 이상이어야 합니다.");
    }

    var license = await licenseService.UpdateAsync(id, request, ct);
    return license is null ? Results.NotFound() : Results.Ok(license);
});

app.MapDelete("/api/v1/licenses/{id:int}", async (int id, LicenseService licenseService, CancellationToken ct) =>
    await licenseService.DeleteAsync(id, ct) ? Results.NoContent() : Results.NotFound());

app.MapGet("/api/v1/licensing/report", async (LicenseMatchingService matchingService, CancellationToken ct) =>
    Results.Ok(await matchingService.BuildReportAsync(ct)));

app.MapGet("/api/v1/devices", async (DeviceDirectoryService directoryService, CancellationToken ct) =>
    Results.Ok(await directoryService.ListAsync(ct)));

app.MapGet("/api/v1/devices/{deviceId}/software", async (
    string deviceId, DeviceDirectoryService directoryService, CancellationToken ct) =>
    Results.Ok(await directoryService.GetInstalledSoftwareAsync(deviceId, ct)));

app.MapGet("/api/v1/prohibited-software", async (ProhibitedSoftwareService prohibitedService, CancellationToken ct) =>
    Results.Ok(await prohibitedService.ListAsync(ct)));

app.MapPost("/api/v1/prohibited-software", async (
    ProhibitedSoftwareRequest request, ProhibitedSoftwareService prohibitedService, CancellationToken ct) =>
{
    if (!ProhibitedSoftwareService.IsValid(request))
    {
        return Results.BadRequest("ProductName/Reason은 필수입니다.");
    }

    try
    {
        var entity = await prohibitedService.CreateAsync(request, ct);
        return Results.Created($"/api/v1/prohibited-software/{entity.Id}", entity);
    }
    catch (DuplicateProhibitedSoftwareException ex)
    {
        return Results.Conflict(ex.Message);
    }
});

app.MapDelete("/api/v1/prohibited-software/{id:int}", async (
    int id, ProhibitedSoftwareService prohibitedService, CancellationToken ct) =>
    await prohibitedService.DeleteAsync(id, ct) ? Results.NoContent() : Results.NotFound());

app.MapGet("/api/v1/prohibited-software/alerts", async (
    ProhibitedSoftwareAlertService alertService, CancellationToken ct) =>
    Results.Ok(await alertService.BuildAlertsAsync(ct)));

app.MapGet("/api/v1/reports/software-inventory.csv", async (LicenseMatchingService matchingService, CancellationToken ct) =>
{
    var report = await matchingService.BuildReportAsync(ct);
    var csv = SoftwareInventoryCsvWriter.Write(report);
    // UTF-8 BOM을 붙인다 — Excel(한국어 Windows)에서 그냥 열었을 때 한글이 깨지는 것을 방지.
    var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray();
    return Results.Bytes(bytes, "text/csv; charset=utf-8", "software-inventory.csv");
});

app.Run();

// Microsoft.NET.Sdk.Web은 최상위 문(top-level statements)의 암묵적 Program 클래스를
// 기본적으로 public으로 만든다. NetSamLite.Web이 이 어셈블리를 참조하면 Web 자신의
// Program 타입과 이름이 충돌(CS0436)하므로 internal로 내린다.
internal partial class Program;

// WebApplicationFactory<T>의 T는 타입 자체가 아니라 "이 어셈블리를 찾는 용도"로만 쓰인다.
// Program이 internal이라 테스트 프로젝트에서 바로 못 쓰므로, 이름이 안 겹치는 별도의
// public 마커 타입을 둔다(InternalsVisibleTo보다 간단 — 베이스 타입 인자 접근성 규칙은
// IVT로도 못 우회된다).
public sealed class TestEntryPoint;
