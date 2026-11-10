using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetSamLite.Agent;
using NetSamLite.Agent.Collectors;
using NetSamLite.Agent.Sending;
using NetSamLite.Shared;
using Serilog;

if (args.Contains("--once"))
{
    return RunOnce(args);
}

return RunAsService(args);

static int RunOnce(string[] args)
{
    var outIndex = Array.IndexOf(args, "--out");
    if (outIndex < 0 || outIndex + 1 >= args.Length)
    {
        Console.Error.WriteLine("--out <경로>가 필요합니다.");
        return 1;
    }

    var outputPath = Path.GetFullPath(args[outIndex + 1]);

    using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());

    var softwareCollector = new RegistrySoftwareCollector(
        new Win32RegistryReader(loggerFactory.CreateLogger<Win32RegistryReader>()),
        loggerFactory.CreateLogger<RegistrySoftwareCollector>());

    var hardwareCollector = new WmiHardwareCollector(
        new Win32WmiReader(loggerFactory.CreateLogger<Win32WmiReader>()),
        new Win32MachineGuidReader(loggerFactory.CreateLogger<Win32MachineGuidReader>()),
        loggerFactory.CreateLogger<WmiHardwareCollector>());

    var report = new InventoryReport(hardwareCollector.Collect(), softwareCollector.Collect());

    var outputDirectory = Path.GetDirectoryName(outputPath);
    if (!string.IsNullOrEmpty(outputDirectory))
    {
        Directory.CreateDirectory(outputDirectory);
    }

    File.WriteAllText(outputPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

    var logger = loggerFactory.CreateLogger("NetSamLite.Agent");
    logger.LogInformation(
        "수집 완료: 설치 SW {SoftwareCount}건 → {OutputPath}", report.InstalledSoftware.Count, outputPath);

    return 0;
}

static int RunAsService(string[] args)
{
    var builder = Host.CreateApplicationBuilder(args);

    // Windows SCM 밑에서 실행될 때만 서비스로 동작하고, 콘솔에서 직접 실행하면 평소처럼
    // 콘솔 앱으로 동작한다 — 코드 분기 없이 자동 감지됨.
    builder.Services.AddWindowsService(options => options.ServiceName = "NetSamLiteAgent");

    var agentOptions = builder.Configuration.GetSection(AgentOptions.SectionName).Get<AgentOptions>()
        ?? new AgentOptions();

    // 로그 로테이션/보관기간 관리는 Serilog.Sinks.File에 맡긴다(직접 구현 대상이 아님).
    Log.Logger = new LoggerConfiguration()
        .MinimumLevel.Information()
        .WriteTo.Console()
        .WriteTo.File(
            Path.Combine(agentOptions.LogDirectory, "agent-.log"),
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: agentOptions.LogRetentionDays)
        .CreateLogger();

    builder.Logging.ClearProviders();
    builder.Services.AddSerilog();

    builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection(AgentOptions.SectionName));

    builder.Services.AddSingleton<ISoftwareCollector>(sp => new RegistrySoftwareCollector(
        new Win32RegistryReader(sp.GetRequiredService<ILogger<Win32RegistryReader>>()),
        sp.GetRequiredService<ILogger<RegistrySoftwareCollector>>()));
    builder.Services.AddSingleton<IHardwareCollector>(sp => new WmiHardwareCollector(
        new Win32WmiReader(sp.GetRequiredService<ILogger<Win32WmiReader>>()),
        new Win32MachineGuidReader(sp.GetRequiredService<ILogger<Win32MachineGuidReader>>()),
        sp.GetRequiredService<ILogger<WmiHardwareCollector>>()));

    builder.Services.AddSingleton<PendingReportQueue>();
    builder.Services.AddSingleton<ReportDeliveryService>();
    builder.Services.AddHttpClient<IInventorySender, HttpInventorySender>((sp, client) =>
    {
        client.BaseAddress = new Uri(agentOptions.ServerBaseUrl);
    });

    builder.Services.AddHostedService<AgentWorker>();

    try
    {
        builder.Build().Run();
        return 0;
    }
    finally
    {
        Log.CloseAndFlush();
    }
}
