using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NetSamLite.Agent.Collectors;
using NetSamLite.Agent.Sending;
using NetSamLite.Agent.Tests.Sending;
using NetSamLite.Shared;

namespace NetSamLite.Agent.Tests;

/// <summary>
/// AgentWorker는 BackgroundService라 ExecuteAsync를 직접 호출하기 까다롭다. 다행히
/// BackgroundService.ExecuteTask가 public이라, StartAsync로 실제로 돌려보고 그 Task의
/// 상태(완료/예외 여부)로 "CollectionIntervalMinutes가 0 이하일 때 Math.Max(1, ...)로
/// 클램프돼서 Task.Delay가 즉시 ArgumentOutOfRangeException으로 죽지 않는지"를 검증한다.
/// 시간 자체를 추상화하지 않았으므로 실제 1분 대기가 끝나길 기다리지는 않고, 클램프가
/// 없었다면 즉시(동기적으로) 발생했을 예외가 안 나는지만 짧은 유예 시간 안에 확인한다.
/// </summary>
public sealed class AgentWorkerTests : IDisposable
{
    private readonly string _queueDirectory;

    public AgentWorkerTests()
    {
        _queueDirectory = Path.Combine(Path.GetTempPath(), "NetSamLiteTests", Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_queueDirectory))
        {
            Directory.Delete(_queueDirectory, recursive: true);
        }
    }

    private static InventoryReport CreateReport() =>
        new(
            new DeviceInfo("DEV-1", "PC-01", null, null, null, null, 0, null, null, null, 0, Disks: []),
            InstalledSoftware: []);

    private sealed class FakeSoftwareCollector : ISoftwareCollector
    {
        public int CallCount;
        public IReadOnlyList<InstalledSoftware> Collect()
        {
            CallCount++;
            return [];
        }
    }

    private sealed class FakeHardwareCollector : IHardwareCollector
    {
        public DeviceInfo Collect() => CreateReport().Device;
    }

    [Fact]
    public async Task ExecuteAsync_CollectionIntervalMinutesIsZeroOrNegative_ClampsToOneMinuteAndDoesNotFaultImmediately()
    {
        // CollectionIntervalMinutes가 0 이하이면 클램프(Math.Max(1, ...))가 없을 경우
        // Task.Delay(TimeSpan.FromMinutes(음수 또는 0))가 아니라 실제로는 FromMinutes(0)은
        // 문제없지만 음수는 ArgumentOutOfRangeException을 동기적으로 던진다. 여기서는 음수
        // 값으로 그 동기적 실패 여부를 확인한다 — 클램프가 있으면 ExecuteTask가 1분 대기
        // 상태로 "아직 안 끝난 채" 남아 있어야 하고, 클램프가 없으면 거의 즉시 Faulted가 된다.
        var agentOptions = new AgentOptions
        {
            QueueDirectory = _queueDirectory,
            CollectionIntervalMinutes = -100,
            StartupJitterMaxSeconds = 0,
        };
        var optionsAccessor = Options.Create(agentOptions);
        var queue = new PendingReportQueue(optionsAccessor, NullLogger<PendingReportQueue>.Instance);
        var sender = new FakeInventorySender(); // 결과를 enqueue하지 않으면 항상 성공으로 처리됨
        var delivery = new ReportDeliveryService(
            sender, queue, optionsAccessor, NullLogger<ReportDeliveryService>.Instance);
        var software = new FakeSoftwareCollector();
        var hardware = new FakeHardwareCollector();
        var sut = new NetSamLite.Agent.AgentWorker(
            software, hardware, delivery, optionsAccessor, NullLogger<NetSamLite.Agent.AgentWorker>.Instance);

        await sut.StartAsync(CancellationToken.None);
        try
        {
            // StartupJitterMaxSeconds=0이어도 jitter는 Random.Shared.Next(0, 2)라 0~1초
            // 사이가 될 수 있으므로 넉넉히 기다린 뒤, 수집 사이클이 돌고 클램프된 1분
            // Task.Delay에 들어갈 시간을 준다. 클램프가 없었다면 이 시점 이전에 이미
            // Faulted가 돼 있을 것이다.
            await Task.Delay(2000);

            sut.ExecuteTask.Should().NotBeNull();
            sut.ExecuteTask!.IsFaulted.Should().BeFalse(
                "CollectionIntervalMinutes가 음수여도 Math.Max(1, ...)로 클램프돼 Task.Delay가 즉시 실패하면 안 된다");
            sut.ExecuteTask!.IsCompleted.Should().BeFalse(
                "클램프된 1분 지연 중이라 이 짧은 유예 시간 안에 루프가 끝나면 안 된다");
            software.CallCount.Should().BeGreaterThan(0, "최소 1회 수집 사이클은 돌았어야 한다");
        }
        finally
        {
            await sut.StopAsync(CancellationToken.None);
            sut.Dispose();
        }
    }
}
