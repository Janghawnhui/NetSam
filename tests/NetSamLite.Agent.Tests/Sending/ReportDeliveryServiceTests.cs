using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NetSamLite.Agent;
using NetSamLite.Agent.Sending;
using NetSamLite.Shared;

namespace NetSamLite.Agent.Tests.Sending;

/// <summary>
/// ReportDeliveryService는 실제 PendingReportQueue(임시 디렉터리)와 가짜 IInventorySender를
/// 조합해서, 재시도 횟수와 "오래된 큐 리포트가 다 빠지기 전에는 새 리포트를 보내지 않는다"는
/// 순서 보장 동작을 검증한다. RetryBaseDelaySeconds는 0으로 둬서 지수 백오프 대기로 테스트가
/// 느려지지 않게 한다.
/// </summary>
public sealed class ReportDeliveryServiceTests : IDisposable
{
    private readonly string _queueDirectory;
    private readonly AgentOptions _agentOptions;
    private readonly PendingReportQueue _queue;
    private readonly FakeInventorySender _sender;

    public ReportDeliveryServiceTests()
    {
        _queueDirectory = Path.Combine(Path.GetTempPath(), "NetSamLiteTests", Guid.NewGuid().ToString("N"));
        _agentOptions = new AgentOptions
        {
            QueueDirectory = _queueDirectory,
            MaxRetryAttempts = 3,
            RetryBaseDelaySeconds = 0,
        };
        _queue = new PendingReportQueue(Options.Create(_agentOptions), NullLogger<PendingReportQueue>.Instance);
        _sender = new FakeInventorySender();
    }

    public void Dispose()
    {
        if (Directory.Exists(_queueDirectory))
        {
            Directory.Delete(_queueDirectory, recursive: true);
        }
    }

    private ReportDeliveryService CreateSut() =>
        new(_sender, _queue, Options.Create(_agentOptions), NullLogger<ReportDeliveryService>.Instance);

    private static InventoryReport CreateReport(string deviceId) =>
        new(
            new DeviceInfo(deviceId, "PC-01", null, null, null, null, 0, null, null, null, 0, Disks: []),
            InstalledSoftware: []);

    [Fact]
    public async Task DeliverAsync_EmptyQueueAndFirstAttemptSucceeds_SendsOnceAndQueueStaysEmpty()
    {
        _sender.EnqueueResult(true);
        var sut = CreateSut();

        await sut.DeliverAsync(CreateReport("DEV-1"), CancellationToken.None);

        _sender.Calls.Should().HaveCount(1);
        (await _queue.ListPendingAsync(CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task DeliverAsync_FirstAttemptFailsSecondSucceeds_SucceedsWithinRetryLimitAndQueueStaysEmpty()
    {
        _sender.EnqueueResult(false);
        _sender.EnqueueResult(true);
        var sut = CreateSut();

        await sut.DeliverAsync(CreateReport("DEV-1"), CancellationToken.None);

        _sender.Calls.Should().HaveCount(2); // MaxRetryAttempts(3)을 넘지 않음
        (await _queue.ListPendingAsync(CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task DeliverAsync_AllRetryAttemptsFail_ReportIsEnqueuedToLocalQueue()
    {
        _sender.EnqueueResult(false);
        _sender.EnqueueResult(false);
        _sender.EnqueueResult(false);
        var sut = CreateSut();

        await sut.DeliverAsync(CreateReport("DEV-1"), CancellationToken.None);

        _sender.Calls.Should().HaveCount(_agentOptions.MaxRetryAttempts);
        var pending = await _queue.ListPendingAsync(CancellationToken.None);
        pending.Should().ContainSingle(p => p.Report.Device.DeviceId == "DEV-1");
    }

    [Fact]
    public async Task DeliverAsync_OldQueuedReportStillFailingAfterRetries_NewReportIsNeverAttemptedAndIsQueued()
    {
        await _queue.EnqueueAsync(CreateReport("DEV-OLD"), CancellationToken.None);
        _sender.EnqueueResult(false);
        _sender.EnqueueResult(false);
        _sender.EnqueueResult(false); // 오래된 리포트에 대한 MaxRetryAttempts(3)번의 시도, 모두 실패
        var sut = CreateSut();

        await sut.DeliverAsync(CreateReport("DEV-NEW"), CancellationToken.None);

        // 새 리포트는 전송 시도 자체가 없어야 한다 — 호출 전부가 DEV-OLD여야 함.
        _sender.Calls.Should().HaveCount(_agentOptions.MaxRetryAttempts);
        _sender.Calls.Should().OnlyContain(r => r.Device.DeviceId == "DEV-OLD");

        var pending = await _queue.ListPendingAsync(CancellationToken.None);
        pending.Select(p => p.Report.Device.DeviceId).Should().BeEquivalentTo("DEV-OLD", "DEV-NEW");
    }

    [Fact]
    public async Task DeliverAsync_OldQueuedReportFailsFewerThanMaxQueueSendFailuresTimes_StaysInQueueAndNewReportIsNotAttempted()
    {
        // MaxQueueSendFailures(기본5)보다 적게 실패한 큐 항목은 여전히 큐에 남아 뒤의 리포트를
        // 계속 막아야 한다(순서 보장 유지) — RecordFailureAsync가 아직 false를 반환하는 동안은
        // 기존 head-of-line 동작이 그대로 적용된다.
        _agentOptions.MaxQueueSendFailures = 5;
        await _queue.EnqueueAsync(CreateReport("DEV-OLD"), CancellationToken.None);
        _sender.EnqueueResult(false);
        _sender.EnqueueResult(false);
        _sender.EnqueueResult(false); // MaxRetryAttempts(3)번의 시도, 모두 실패 -> RecordFailureAsync 1회 호출(1 < 5)
        var sut = CreateSut();

        await sut.DeliverAsync(CreateReport("DEV-NEW"), CancellationToken.None);

        _sender.Calls.Should().OnlyContain(r => r.Device.DeviceId == "DEV-OLD");
        var pending = await _queue.ListPendingAsync(CancellationToken.None);
        pending.Select(p => p.Report.Device.DeviceId).Should().BeEquivalentTo("DEV-OLD", "DEV-NEW");
        pending.Single(p => p.Report.Device.DeviceId == "DEV-OLD").FailureCount.Should().Be(1);
    }

    [Fact]
    public async Task DeliverAsync_OldQueuedReportGivesUpAfterMaxQueueSendFailures_LaterQueueItemAndNewReportAreStillAttempted()
    {
        // 핵심 동작: 영구히 실패하는 옛 리포트 하나가 MaxQueueSendFailures번째 실패로 포기(dead-letter)
        // 되면, 그 뒤에 쌓인 다른 큐 항목이나 새로 수집한 리포트는 더 이상 막히지 않고 전송이
        // 시도돼야 한다(head-of-line blocking 해소).
        _agentOptions.MaxQueueSendFailures = 2;
        await _queue.EnqueueAsync(CreateReport("DEV-OLD-BAD"), CancellationToken.None);
        await Task.Delay(20);
        await _queue.EnqueueAsync(CreateReport("DEV-OLD-GOOD"), CancellationToken.None);

        // DEV-OLD-BAD: 이번 사이클에서 MaxRetryAttempts(3)번 모두 실패 -> RecordFailureAsync가
        // FailureCount를 1->2로 올려 MaxQueueSendFailures(2)에 도달 -> 포기하고 failed로 이동.
        _sender.EnqueueResult(false);
        _sender.EnqueueResult(false);
        _sender.EnqueueResult(false);
        // DEV-OLD-GOOD: 큐의 다음 항목, 1차 시도에 성공해서 큐에서 제거됨.
        _sender.EnqueueResult(true);
        // DEV-NEW: 큐가 완전히 비워졌으므로 바로 전송 시도, 1차 성공.
        _sender.EnqueueResult(true);

        // DEV-OLD-BAD를 이미 한 번 실패시켜서 FailureCount를 1로 만들어 둔다(이번 사이클에서
        // MaxQueueSendFailures(2)에 도달하도록).
        var preFlush = await _queue.ListPendingAsync(CancellationToken.None);
        var badEntry = preFlush.Single(p => p.Report.Device.DeviceId == "DEV-OLD-BAD");
        await _queue.RecordFailureAsync(badEntry.Id, CancellationToken.None);

        var sut = CreateSut();

        await sut.DeliverAsync(CreateReport("DEV-NEW"), CancellationToken.None);

        _sender.Calls.Select(r => r.Device.DeviceId).Should().Equal(
            "DEV-OLD-BAD", "DEV-OLD-BAD", "DEV-OLD-BAD", "DEV-OLD-GOOD", "DEV-NEW");

        var pending = await _queue.ListPendingAsync(CancellationToken.None);
        pending.Should().BeEmpty();

        var failedDirectory = Path.Combine(_queueDirectory, "failed");
        Directory.Exists(failedDirectory).Should().BeTrue();
        Directory.GetFiles(failedDirectory, "*.json").Should().ContainSingle();
    }

    [Fact]
    public async Task DeliverAsync_OldQueuedReportFlushedAfterRetry_NewReportIsSentOnlyAfterQueueBecomesEmpty()
    {
        await _queue.EnqueueAsync(CreateReport("DEV-OLD"), CancellationToken.None);
        _sender.EnqueueResult(false); // old: 1차 시도 실패
        _sender.EnqueueResult(true);  // old: 2차 시도 성공 -> 큐가 완전히 비워짐
        _sender.EnqueueResult(true);  // new: 큐가 비었으므로 바로 시도, 1차 성공
        var sut = CreateSut();

        await sut.DeliverAsync(CreateReport("DEV-NEW"), CancellationToken.None);

        _sender.Calls.Select(r => r.Device.DeviceId).Should().Equal("DEV-OLD", "DEV-OLD", "DEV-NEW");
        (await _queue.ListPendingAsync(CancellationToken.None)).Should().BeEmpty();
    }
}
