using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NetSamLite.Agent;
using NetSamLite.Agent.Collectors;
using NetSamLite.Agent.Realtime;
using NetSamLite.Agent.Sending;
using NetSamLite.Agent.Tests.Sending;
using NetSamLite.Shared;
using NetSamLite.Shared.Hub;

namespace NetSamLite.Agent.Tests.Realtime;

/// <summary>
/// "수집 → 전송"을 세마포로 직렬화하는 InventoryCollectionRunner. 실제 PendingReportQueue
/// (임시 디렉터리) + 가짜 IInventorySender를 조합해 ReportDeliveryService와 같은 경로를 탄다
/// (레지스트리/WMI에 직접 의존하지 않는다 — 수집기는 전부 가짜로 대체한다).
/// </summary>
public sealed class InventoryCollectionRunnerTests : IDisposable
{
    private readonly string _queueDirectory;

    public InventoryCollectionRunnerTests()
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

    private sealed class CountingSoftwareCollector : ISoftwareCollector
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

    private sealed class ThrowingSoftwareCollector : ISoftwareCollector
    {
        public IReadOnlyList<InstalledSoftware> Collect() => throw new InvalidOperationException("수집 실패");
    }

    /// <summary>
    /// 전송이 끝나지 않고 멈춰 있는 상태를 재현한다 — "이미 진행 중" 테스트에서 첫 번째
    /// 실행이 세마포를 붙잡고 있는 동안 두 번째 호출을 걸어 보기 위함이다.
    /// </summary>
    private sealed class BlockingInventorySender : IInventorySender
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int CallCount;

        public Task WaitUntilStartedAsync() => _started.Task;
        public void Release() => _release.TrySetResult();

        public async Task<bool> TrySendAsync(InventoryReport report, CancellationToken cancellationToken)
        {
            CallCount++;
            _started.TrySetResult();
            await _release.Task;
            return true;
        }
    }

    /// <summary>
    /// 첫 호출만 취소될 때까지 끝나지 않고(세마포를 붙잡은 상태를 재현), 그 다음 호출부터는
    /// 바로 성공한다 — "취소 후 세마포가 풀렸는지"를 같은 sut로 다시 호출해 확인하기 위함이다.
    /// </summary>
    private sealed class CancelableOnFirstCallInventorySender : IInventorySender
    {
        private int _callCount;

        public Task<bool> TrySendAsync(InventoryReport report, CancellationToken cancellationToken) =>
            Interlocked.Increment(ref _callCount) == 1
                ? WaitForCancellationAsync(cancellationToken)
                : Task.FromResult(true);

        private static async Task<bool> WaitForCancellationAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return true;
        }
    }

    private InventoryCollectionRunner CreateSut(
        ISoftwareCollector softwareCollector, IHardwareCollector hardwareCollector, IInventorySender sender)
    {
        var agentOptions = new AgentOptions { QueueDirectory = _queueDirectory, MaxRetryAttempts = 1 };
        var optionsAccessor = Options.Create(agentOptions);
        var queue = new PendingReportQueue(optionsAccessor, NullLogger<PendingReportQueue>.Instance);
        var delivery = new ReportDeliveryService(sender, queue, optionsAccessor, NullLogger<ReportDeliveryService>.Instance);
        return new InventoryCollectionRunner(
            softwareCollector, hardwareCollector, delivery, NullLogger<InventoryCollectionRunner>.Instance);
    }

    [Fact]
    public async Task TryRunAsync_NoOtherRunInProgress_CollectsAndDeliversOnce()
    {
        var software = new CountingSoftwareCollector();
        var sender = new FakeInventorySender();
        var sut = CreateSut(software, new FakeHardwareCollector(), sender);

        var outcome = await sut.TryRunAsync(CancellationToken.None);

        outcome.Should().Be(CollectNowOutcome.Succeeded);
        software.CallCount.Should().Be(1);
        sender.Calls.Should().HaveCount(1);
    }

    [Fact]
    public async Task TryRunAsync_WhileAnotherRunInProgress_ReturnsAlreadyRunningWithoutCollecting()
    {
        var software = new CountingSoftwareCollector();
        var sender = new BlockingInventorySender();
        var sut = CreateSut(software, new FakeHardwareCollector(), sender);

        var firstRun = sut.TryRunAsync(CancellationToken.None);
        await sender.WaitUntilStartedAsync();

        var secondOutcome = await sut.TryRunAsync(CancellationToken.None);

        secondOutcome.Should().Be(CollectNowOutcome.AlreadyRunning);
        software.CallCount.Should().Be(1, "두 번째 호출은 대기하지 않고 즉시 돌아와야 하고 수집기를 호출하면 안 된다");

        sender.Release();
        var firstOutcome = await firstRun;
        firstOutcome.Should().Be(CollectNowOutcome.Succeeded);
    }

    [Fact]
    public async Task TryRunAsync_AfterPreviousRunCompletes_CanRunAgain()
    {
        var software = new CountingSoftwareCollector();
        var sender = new FakeInventorySender();
        var sut = CreateSut(software, new FakeHardwareCollector(), sender);

        var firstOutcome = await sut.TryRunAsync(CancellationToken.None);
        var secondOutcome = await sut.TryRunAsync(CancellationToken.None);

        firstOutcome.Should().Be(CollectNowOutcome.Succeeded);
        secondOutcome.Should().Be(CollectNowOutcome.Succeeded);
        software.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task TryRunAsync_CollectorThrows_ReleasesSemaphoreAndReturnsFailed()
    {
        var sender = new FakeInventorySender();
        var sut = CreateSut(new ThrowingSoftwareCollector(), new FakeHardwareCollector(), sender);

        var outcome = await sut.TryRunAsync(CancellationToken.None);

        outcome.Should().Be(CollectNowOutcome.Failed);

        // 같은 sut(같은 세마포)로 다시 호출해도 AlreadyRunning이 아니라 Failed가 나와야 한다 —
        // 세마포가 영구 점유되지 않고 풀렸다는 증거다(수집기는 항상 예외를 던지므로 결과 자체는
        // 다시 Failed이지만, 그게 "이미 실행 중"이 아니라는 점이 중요하다).
        var nextOutcome = await sut.TryRunAsync(CancellationToken.None);
        nextOutcome.Should().Be(CollectNowOutcome.Failed);
    }

    [Fact]
    public async Task TryRunAsync_Cancelled_DoesNotLeaveSemaphoreHeld()
    {
        var software = new CountingSoftwareCollector();
        var sender = new CancelableOnFirstCallInventorySender();
        var sut = CreateSut(software, new FakeHardwareCollector(), sender);
        using var cts = new CancellationTokenSource();

        var runTask = sut.TryRunAsync(cts.Token);
        // 세마포를 잡고 sender.TrySendAsync의 무한 대기 안에 들어갈 시간을 준다.
        await Task.Delay(50);
        cts.Cancel();

        var act = async () => await runTask;
        await act.Should().ThrowAsync<OperationCanceledException>();

        // 세마포가 해제돼 다음 호출이 AlreadyRunning에 걸리지 않고 정상 진행된다.
        var secondOutcome = await sut.TryRunAsync(CancellationToken.None);
        secondOutcome.Should().NotBe(CollectNowOutcome.AlreadyRunning);
    }
}
