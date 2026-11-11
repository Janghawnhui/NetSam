using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NetSamLite.Agent;
using NetSamLite.Agent.Sending;
using NetSamLite.Shared;

namespace NetSamLite.Agent.Tests.Sending;

/// <summary>
/// PendingReportQueue는 실제 파일 시스템(임시 디렉터리)을 사용한다. 레지스트리/네트워크에는
/// 의존하지 않으므로 실제 디스크 I/O만 허용한 기존 컨벤션에 맞는다. 테스트별로 고유한 임시
/// 폴더를 쓰고 끝나면 삭제한다.
/// </summary>
public sealed class PendingReportQueueTests : IDisposable
{
    private readonly string _queueDirectory;
    private readonly PendingReportQueue _sut;

    public PendingReportQueueTests()
    {
        _queueDirectory = Path.Combine(Path.GetTempPath(), "NetSamLiteTests", Guid.NewGuid().ToString("N"));
        var options = Options.Create(new AgentOptions { QueueDirectory = _queueDirectory });
        _sut = new PendingReportQueue(options, NullLogger<PendingReportQueue>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_queueDirectory))
        {
            Directory.Delete(_queueDirectory, recursive: true);
        }
    }

    private static InventoryReport CreateReport(string deviceId) =>
        new(
            new DeviceInfo(deviceId, "PC-01", null, null, null, null, 0, null, null, null, 0, Disks: []),
            InstalledSoftware: []);

    [Fact]
    public async Task ListPendingAsync_TwoReportsEnqueuedInOrder_ReturnsThemOldestFirst()
    {
        await _sut.EnqueueAsync(CreateReport("DEV-OLD"), CancellationToken.None);
        // 파일명 앞부분이 밀리초 단위 UTC 타임스탬프라 두 호출의 타임스탬프가 달라야
        // 이름순 정렬이 수집 순서와 일치한다는 걸 확인할 수 있다.
        await Task.Delay(50);
        await _sut.EnqueueAsync(CreateReport("DEV-NEW"), CancellationToken.None);

        var pending = await _sut.ListPendingAsync(CancellationToken.None);

        pending.Select(p => p.Report.Device.DeviceId).Should().Equal("DEV-OLD", "DEV-NEW");
    }

    [Fact]
    public async Task RemoveAsync_AfterEnqueue_ReportNoLongerAppearsInListPending()
    {
        await _sut.EnqueueAsync(CreateReport("DEV-1"), CancellationToken.None);
        var pendingBefore = await _sut.ListPendingAsync(CancellationToken.None);
        pendingBefore.Should().ContainSingle();

        await _sut.RemoveAsync(pendingBefore[0].FilePath, CancellationToken.None);
        var pendingAfter = await _sut.ListPendingAsync(CancellationToken.None);

        pendingAfter.Should().BeEmpty();
    }

    [Fact]
    public async Task ListPendingAsync_QueueDirectoryDoesNotExist_ReturnsEmptyListWithoutThrowing()
    {
        // 생성자/EnqueueAsync를 한 번도 호출하지 않았으므로 큐 폴더 자체가 아직 없다.
        Directory.Exists(_queueDirectory).Should().BeFalse();

        var pending = await _sut.ListPendingAsync(CancellationToken.None);

        pending.Should().BeEmpty();
    }

    [Fact]
    public async Task ListPendingAsync_OneQueueFileIsCorruptJson_SkipsItButReturnsOtherValidReports()
    {
        await _sut.EnqueueAsync(CreateReport("DEV-GOOD"), CancellationToken.None);
        var corruptPath = Path.Combine(_queueDirectory, "20200101T000000000Z_corrupt.json");
        await File.WriteAllTextAsync(corruptPath, "{ not valid json");

        var pending = await _sut.ListPendingAsync(CancellationToken.None);

        pending.Should().ContainSingle();
        pending[0].Report.Device.DeviceId.Should().Be("DEV-GOOD");
    }

    // ----- RecordFailureAsync / dead-letter (수정 3) -----

    [Fact]
    public async Task RecordFailureAsync_FewerThanMaxQueueSendFailures_ReturnsFalseAndStaysInQueueWithIncrementedFailureCount()
    {
        var options = Options.Create(new AgentOptions { QueueDirectory = _queueDirectory, MaxQueueSendFailures = 5 });
        var sut = new PendingReportQueue(options, NullLogger<PendingReportQueue>.Instance);
        await sut.EnqueueAsync(CreateReport("DEV-1"), CancellationToken.None);
        var entry = (await sut.ListPendingAsync(CancellationToken.None)).Single();
        entry.FailureCount.Should().Be(0);

        var gaveUp1 = await sut.RecordFailureAsync(entry.FilePath, CancellationToken.None);
        var gaveUp2 = await sut.RecordFailureAsync(entry.FilePath, CancellationToken.None);

        gaveUp1.Should().BeFalse();
        gaveUp2.Should().BeFalse();
        var pending = await sut.ListPendingAsync(CancellationToken.None);
        pending.Should().ContainSingle();
        pending[0].FailureCount.Should().Be(2);
        pending[0].Report.Device.DeviceId.Should().Be("DEV-1");
    }

    [Fact]
    public async Task RecordFailureAsync_ReachesMaxQueueSendFailures_ReturnsTrueMovesFileToFailedFolderAndRemovesFromQueue()
    {
        const int maxFailures = 3;
        var options = Options.Create(
            new AgentOptions { QueueDirectory = _queueDirectory, MaxQueueSendFailures = maxFailures });
        var sut = new PendingReportQueue(options, NullLogger<PendingReportQueue>.Instance);
        await sut.EnqueueAsync(CreateReport("DEV-1"), CancellationToken.None);
        var entry = (await sut.ListPendingAsync(CancellationToken.None)).Single();

        bool gaveUp = false;
        for (var i = 0; i < maxFailures; i++)
        {
            gaveUp = await sut.RecordFailureAsync(entry.FilePath, CancellationToken.None);
        }

        gaveUp.Should().BeTrue();
        (await sut.ListPendingAsync(CancellationToken.None)).Should().BeEmpty();
        var failedDirectory = Path.Combine(_queueDirectory, "failed");
        Directory.Exists(failedDirectory).Should().BeTrue();
        Directory.GetFiles(failedDirectory, "*.json").Should().ContainSingle();
    }

    [Fact]
    public async Task RecordFailureAsync_FileIsUnreadable_ReturnsTrueAndRemovesFile()
    {
        var corruptPath = Path.Combine(_queueDirectory, "20200101T000000000Z_corrupt.json");
        Directory.CreateDirectory(_queueDirectory);
        await File.WriteAllTextAsync(corruptPath, "{ not valid json");

        var gaveUp = await _sut.RecordFailureAsync(corruptPath, CancellationToken.None);

        gaveUp.Should().BeTrue();
        File.Exists(corruptPath).Should().BeFalse();
    }

    // ----- failed/ 이동 원자화 (File.Move 한 번) -----

    [Fact]
    public async Task RecordFailureAsync_ReachesMaxFailures_MovedFileKeepsReportAndFinalFailureCount()
    {
        const int maxFailures = 2;
        var options = Options.Create(
            new AgentOptions { QueueDirectory = _queueDirectory, MaxQueueSendFailures = maxFailures });
        var sut = new PendingReportQueue(options, NullLogger<PendingReportQueue>.Instance);
        await sut.EnqueueAsync(CreateReport("DEV-DEAD"), CancellationToken.None);
        var entry = (await sut.ListPendingAsync(CancellationToken.None)).Single();

        await sut.RecordFailureAsync(entry.FilePath, CancellationToken.None);
        var gaveUp = await sut.RecordFailureAsync(entry.FilePath, CancellationToken.None);

        gaveUp.Should().BeTrue();
        (await sut.ListPendingAsync(CancellationToken.None)).Should().BeEmpty();
        Directory.GetFiles(_queueDirectory, "*.json").Should().BeEmpty();
        var failedDirectory = Path.Combine(_queueDirectory, "failed");
        var failedFile = Directory.GetFiles(failedDirectory, "*.json").Single();
        var json = await File.ReadAllTextAsync(failedFile);
        var envelope = JsonSerializer.Deserialize<QueuedReportEnvelope>(json);
        envelope!.Report!.Device.DeviceId.Should().Be("DEV-DEAD");
        envelope.FailureCount.Should().Be(maxFailures);
        Directory.GetFiles(failedDirectory, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public async Task RecordFailureAsync_FailedFolderAlreadyHasFileWithSameName_OverwritesItWithoutThrowing()
    {
        var options = Options.Create(
            new AgentOptions { QueueDirectory = _queueDirectory, MaxQueueSendFailures = 1 });
        var sut = new PendingReportQueue(options, NullLogger<PendingReportQueue>.Instance);
        await sut.EnqueueAsync(CreateReport("DEV-NEW"), CancellationToken.None);
        var entry = (await sut.ListPendingAsync(CancellationToken.None)).Single();
        var failedDirectory = Path.Combine(_queueDirectory, "failed");
        Directory.CreateDirectory(failedDirectory);
        var preExistingPath = Path.Combine(failedDirectory, Path.GetFileName(entry.FilePath));
        await File.WriteAllTextAsync(preExistingPath, "{}");

        var gaveUp = await sut.RecordFailureAsync(entry.FilePath, CancellationToken.None);

        gaveUp.Should().BeTrue();
        Directory.GetFiles(_queueDirectory, "*.json").Should().BeEmpty();
        var failedFile = Directory.GetFiles(failedDirectory, "*.json").Should().ContainSingle().Subject;
        var json = await File.ReadAllTextAsync(failedFile);
        var envelope = JsonSerializer.Deserialize<QueuedReportEnvelope>(json);
        envelope!.Report!.Device.DeviceId.Should().Be("DEV-NEW");
        envelope.FailureCount.Should().Be(1);
    }

    [Fact]
    public async Task RecordFailureAsync_QueueFileDoesNotExist_ReturnsTrueAndCreatesNothingInFailedFolder()
    {
        Directory.CreateDirectory(_queueDirectory);
        var missingPath = Path.Combine(_queueDirectory, "20200101T000000000Z_missing.json");

        var gaveUp = await _sut.RecordFailureAsync(missingPath, CancellationToken.None);

        gaveUp.Should().BeTrue();
        var failedDirectory = Path.Combine(_queueDirectory, "failed");
        if (Directory.Exists(failedDirectory))
        {
            Directory.GetFiles(failedDirectory, "*.json").Should().BeEmpty();
        }
    }

    // ----- MaxQueuedReports (수정 3) -----

    [Fact]
    public async Task EnqueueAsync_ExceedsMaxQueuedReports_DropsOldestAndKeepsOnlyNewestWithinLimit()
    {
        var options = Options.Create(new AgentOptions { QueueDirectory = _queueDirectory, MaxQueuedReports = 2 });
        var sut = new PendingReportQueue(options, NullLogger<PendingReportQueue>.Instance);

        await sut.EnqueueAsync(CreateReport("DEV-1"), CancellationToken.None);
        await Task.Delay(20);
        await sut.EnqueueAsync(CreateReport("DEV-2"), CancellationToken.None);
        await Task.Delay(20);
        await sut.EnqueueAsync(CreateReport("DEV-3"), CancellationToken.None);

        var pending = await sut.ListPendingAsync(CancellationToken.None);
        pending.Select(p => p.Report.Device.DeviceId).Should().Equal("DEV-2", "DEV-3");
    }

    // ----- QueueRetentionDays (수정 3) -----

    [Fact]
    public async Task ListPendingAsync_FileOlderThanRetentionPeriod_IsDeletedDuringListing()
    {
        var options = Options.Create(
            new AgentOptions { QueueDirectory = _queueDirectory, QueueRetentionDays = -5 });
        var sut = new PendingReportQueue(options, NullLogger<PendingReportQueue>.Instance);
        await sut.EnqueueAsync(CreateReport("DEV-OLD"), CancellationToken.None);
        var entry = (await sut.ListPendingAsync(CancellationToken.None)).Single();
        // QueueRetentionDays가 0 이하면 Math.Max(1, ...)로 1일로 처리되므로, 파일의 마지막 쓰기
        // 시각을 명시적으로 이틀 전으로 돌려서 "보관 기간이 지난 파일"을 재현한다.
        File.SetLastWriteTimeUtc(entry.FilePath, DateTime.UtcNow.AddDays(-2));

        var pending = await sut.ListPendingAsync(CancellationToken.None);

        pending.Should().BeEmpty();
        File.Exists(entry.FilePath).Should().BeFalse();
    }

    [Fact]
    public async Task ListPendingAsync_FailedFolderHasExpiredFile_IsAlsoDeletedDuringListing()
    {
        const int maxFailures = 1;
        var options = Options.Create(new AgentOptions
        {
            QueueDirectory = _queueDirectory,
            MaxQueueSendFailures = maxFailures,
            QueueRetentionDays = -5,
        });
        var sut = new PendingReportQueue(options, NullLogger<PendingReportQueue>.Instance);
        await sut.EnqueueAsync(CreateReport("DEV-1"), CancellationToken.None);
        var entry = (await sut.ListPendingAsync(CancellationToken.None)).Single();
        await sut.RecordFailureAsync(entry.FilePath, CancellationToken.None); // -> failed 폴더로 이동

        var failedDirectory = Path.Combine(_queueDirectory, "failed");
        var failedFile = Directory.GetFiles(failedDirectory, "*.json").Single();
        File.SetLastWriteTimeUtc(failedFile, DateTime.UtcNow.AddDays(-2));

        await sut.ListPendingAsync(CancellationToken.None);

        File.Exists(failedFile).Should().BeFalse();
    }

    // ----- 원자적 쓰기 (수정 5) -----

    [Fact]
    public async Task EnqueueAsync_AfterCompletion_NoTempFilesLeftInQueueDirectory()
    {
        await _sut.EnqueueAsync(CreateReport("DEV-1"), CancellationToken.None);

        Directory.GetFiles(_queueDirectory, "*.tmp").Should().BeEmpty();
    }

    // ----- Report=null 역직렬화 가드 -----

    [Fact]
    public async Task ListPendingAsync_CurrentEnvelopeFormat_ReturnsReportWithFailureCount()
    {
        await _sut.EnqueueAsync(CreateReport("DEV-1"), CancellationToken.None);
        var entry = (await _sut.ListPendingAsync(CancellationToken.None)).Single();
        await _sut.RecordFailureAsync(entry.FilePath, CancellationToken.None);

        var pending = await _sut.ListPendingAsync(CancellationToken.None);

        pending.Should().ContainSingle();
        pending[0].Report.Device.DeviceId.Should().Be("DEV-1");
        pending[0].FailureCount.Should().Be(1);
    }

    [Theory]
    [InlineData("legacy")]
    [InlineData("{}")]
    [InlineData("{\"FailureCount\":2}")]
    public async Task ListPendingAsync_LegacyFormatFileWithoutEnvelope_SkipsItButReturnsOtherValidReports(
        string badFileContentKind)
    {
        await _sut.EnqueueAsync(CreateReport("DEV-GOOD"), CancellationToken.None);
        var badContent = badFileContentKind == "legacy"
            ? JsonSerializer.Serialize(CreateReport("DEV-LEGACY"))
            : badFileContentKind;
        var badPath = Path.Combine(_queueDirectory, "20200101T000000000Z_legacy.json");
        await File.WriteAllTextAsync(badPath, badContent);

        var pending = await _sut.ListPendingAsync(CancellationToken.None);

        pending.Should().ContainSingle();
        pending[0].Report.Device.DeviceId.Should().Be("DEV-GOOD");
    }

    [Fact]
    public async Task RecordFailureAsync_FileHasNoReportInEnvelope_ReturnsTrueAndRemovesFile()
    {
        Directory.CreateDirectory(_queueDirectory);
        var path = Path.Combine(_queueDirectory, "20200101T000000000Z_empty.json");
        await File.WriteAllTextAsync(path, "{}");

        var gaveUp = await _sut.RecordFailureAsync(path, CancellationToken.None);

        gaveUp.Should().BeTrue();
        File.Exists(path).Should().BeFalse();
    }

    // ----- FilePath 계약 -----

    [Fact]
    public async Task ListPendingAsync_ReturnedEntry_FilePathPointsToExistingFileInQueueDirectory()
    {
        await _sut.EnqueueAsync(CreateReport("DEV-1"), CancellationToken.None);

        var entry = (await _sut.ListPendingAsync(CancellationToken.None)).Single();

        File.Exists(entry.FilePath).Should().BeTrue();
        Path.GetDirectoryName(entry.FilePath).Should().Be(_queueDirectory);
        Path.GetExtension(entry.FilePath).Should().Be(".json");
    }

    [Fact]
    public async Task ListPendingAsync_AfterEntryMovedToFailedFolder_NoEntryFilePathPointsIntoFailedFolder()
    {
        var options = Options.Create(
            new AgentOptions { QueueDirectory = _queueDirectory, MaxQueueSendFailures = 1 });
        var sut = new PendingReportQueue(options, NullLogger<PendingReportQueue>.Instance);
        await sut.EnqueueAsync(CreateReport("DEV-DEAD"), CancellationToken.None);
        var entry = (await sut.ListPendingAsync(CancellationToken.None)).Single();
        var originalFilePath = entry.FilePath;

        await sut.RecordFailureAsync(entry.FilePath, CancellationToken.None);

        File.Exists(originalFilePath).Should().BeFalse();
        (await sut.ListPendingAsync(CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task RemoveAsync_FilePathDoesNotExist_DoesNotThrow()
    {
        Directory.CreateDirectory(_queueDirectory);
        var missingPath = Path.Combine(_queueDirectory, "20200101T000000000Z_gone.json");

        var act = async () => await _sut.RemoveAsync(missingPath, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
