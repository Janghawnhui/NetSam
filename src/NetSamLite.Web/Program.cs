using Microsoft.EntityFrameworkCore;
using NetSamLite.Server.Data;
using NetSamLite.Server.Devices;
using NetSamLite.Server.Licensing;
using NetSamLite.Server.Normalization;
using NetSamLite.Server.Policy;
using NetSamLite.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// NetSamLite.Web은 NetSamLite.Server를 직접 참조한다(CLAUDE.md 의존 방향: Server ← Web).
// 별도 프로세스라 DbContext/정규화 엔진 DI를 Server.Program.cs와 동일하게 다시 구성해야 한다.
// 연결 문자열은 Server와 같은 이유로 appsettings.json에 두지 않는다(절대 규칙 4):
//   dotnet user-secrets set "ConnectionStrings:NetSamLite" "Data Source=netsamlite.db" --project src/NetSamLite.Web
var connectionString = builder.Configuration.GetConnectionString("NetSamLite")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:NetSamLite이 설정돼 있지 않습니다. " +
        "dotnet user-secrets set \"ConnectionStrings:NetSamLite\" \"Data Source=netsamlite.db\" --project src/NetSamLite.Web 를 실행하세요.");

builder.Services.AddDbContext<NetSamLiteDbContext>(options => options.UseSqlite(connectionString));

var normalizationRulesPath = Path.Combine(
    AppContext.BaseDirectory, "Normalization", "normalization-rules.json");
builder.Services.AddSingleton(NormalizationRuleLoader.LoadFromFile(normalizationRulesPath));
builder.Services.AddSingleton<SoftwareNormalizer>();
builder.Services.AddScoped<LicenseMatchingService>();
builder.Services.AddScoped<LicenseService>();
builder.Services.AddScoped<ProhibitedSoftwareAlertService>();
builder.Services.AddScoped<ProhibitedSoftwareService>();
builder.Services.AddScoped<DeviceDirectoryService>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// Web도 Server처럼 시작 시 마이그레이션을 적용한다 — Server를 한 번도 안 띄운 깨끗한 DB
// 상태에서 Web을 먼저 열면 테이블이 없어 거의 모든 페이지가 바로 터진다.
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<NetSamLiteDbContext>().Database.Migrate();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapGet("/export/software-inventory.csv", async (LicenseMatchingService matchingService, CancellationToken ct) =>
{
    var report = await matchingService.BuildReportAsync(ct);
    var csv = NetSamLite.Server.Reports.SoftwareInventoryCsvWriter.Write(report);
    var bytes = System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(csv)).ToArray();
    return Results.Bytes(bytes, "text/csv; charset=utf-8", "software-inventory.csv");
});

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
