using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetSamLite.Shared;

namespace NetSamLite.Agent.Sending;

/// <summary>
/// 전송 실패 시 들고 있는 정보: 실패 누적 횟수 + 원본 리포트. 파일 안에 그대로 직렬화해서 저장한다.
/// </summary>
internal sealed record QueuedReportEnvelope(int FailureCount, InventoryReport Report);

/// <summary>ListPendingAsync가 반환하는 큐 항목 1건. Id는 파일 전체 경로다.</summary>
public sealed record PendingReportEntry(string Id, InventoryReport Report, int FailureCount);

/// <summary>
/// 서버 전송에 실패한 InventoryReport를 로컬 파일로 쌓아둔다. 파일 이름 앞부분이 UTC 타임스탬프라
/// 이름순 정렬이 곧 수집 순서가 되고, ListPendingAsync가 그 순서로 반환한다 — 오래된 것부터
/// 먼저 보내야 서버 쪽 변경 이력(설치/삭제)이 실제 순서와 어긋나지 않는다.
///
/// 연속으로 MaxQueueSendFailures번 실패한 항목은 failed 하위 폴더로 옮긴다(RecordFailureAsync) —
/// 그래야 "영구히 실패하는 옛 리포트 하나" 때문에 그 뒤에 쌓인 모든 리포트가 영원히 막히지 않는다.
/// MaxQueuedReports로 큐 전체 크기를, QueueRetentionDays로 보관 기간을 제한해서 디스크가
/// 무한정 차는 것도 막는다.
/// </summary>
public sealed class PendingReportQueue(IOptions<AgentOptions> options, ILogger<PendingReportQueue> logger)
{
    private string FailedDirectory => Path.Combine(options.Value.QueueDirectory, "failed");

    public async Task EnqueueAsync(InventoryReport report, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(options.Value.QueueDirectory);
        EnforceMaxQueuedReports();

        var path = Path.Combine(options.Value.QueueDirectory, NewFileName());
        await WriteEnvelopeAsync(path, new QueuedReportEnvelope(0, report), cancellationToken);
    }

    public async Task<IReadOnlyList<PendingReportEntry>> ListPendingAsync(CancellationToken cancellationToken)
    {
        PruneExpired(options.Value.QueueDirectory);
        PruneExpired(FailedDirectory);

        if (!Directory.Exists(options.Value.QueueDirectory))
        {
            return [];
        }

        var results = new List<PendingReportEntry>();
        foreach (var path in Directory.GetFiles(options.Value.QueueDirectory, "*.json").OrderBy(p => p))
        {
            var envelope = await ReadEnvelopeAsync(path, cancellationToken);
            if (envelope is not null)
            {
                results.Add(new PendingReportEntry(path, envelope.Report, envelope.FailureCount));
            }
        }

        return results;
    }

    public Task RemoveAsync(string id, CancellationToken cancellationToken)
    {
        if (File.Exists(id))
        {
            File.Delete(id);
        }

        return Task.CompletedTask;
    }

    /// <returns>true면 이 항목은 재시도 상한을 넘어 failed 폴더로 옮겨졌다(더 이상 뒤를 막지 않아도 됨).</returns>
    public async Task<bool> RecordFailureAsync(string id, CancellationToken cancellationToken)
    {
        var envelope = await ReadEnvelopeAsync(id, cancellationToken);
        if (envelope is null)
        {
            // 읽을 수 없는 파일을 계속 들고 있어봐야 영원히 막히기만 하니 치운다.
            RemoveQuietly(id);
            return true;
        }

        var updated = envelope with { FailureCount = envelope.FailureCount + 1 };
        var maxFailures = Math.Max(1, options.Value.MaxQueueSendFailures);
        if (updated.FailureCount < maxFailures)
        {
            await WriteEnvelopeAsync(id, updated, cancellationToken);
            return false;
        }

        Directory.CreateDirectory(FailedDirectory);
        var failedPath = Path.Combine(FailedDirectory, Path.GetFileName(id));
        await WriteEnvelopeAsync(failedPath, updated, cancellationToken);
        RemoveQuietly(id);
        logger.LogError(
            "리포트가 {FailureCount}번 연속 전송 실패해서 포기하고 failed 폴더로 옮김: {Path}",
            updated.FailureCount, failedPath);
        return true;
    }

    private void EnforceMaxQueuedReports()
    {
        if (!Directory.Exists(options.Value.QueueDirectory))
        {
            return;
        }

        var maxQueued = Math.Max(1, options.Value.MaxQueuedReports);
        var files = Directory.GetFiles(options.Value.QueueDirectory, "*.json").OrderBy(p => p).ToList();
        while (files.Count >= maxQueued)
        {
            var oldest = files[0];
            RemoveQuietly(oldest);
            logger.LogWarning(
                "큐 최대 개수({MaxQueued})를 넘어서 가장 오래된 리포트를 버림: {Path}", maxQueued, oldest);
            files.RemoveAt(0);
        }
    }

    private void PruneExpired(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        var cutoffUtc = DateTime.UtcNow - TimeSpan.FromDays(Math.Max(1, options.Value.QueueRetentionDays));
        foreach (var path in Directory.GetFiles(directory, "*.json"))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(path) < cutoffUtc)
                {
                    File.Delete(path);
                    logger.LogInformation(
                        "보관 기간({RetentionDays}일)이 지난 큐 파일 삭제: {Path}",
                        options.Value.QueueRetentionDays, path);
                }
            }
            catch (IOException ex)
            {
                logger.LogWarning(ex, "큐 파일 삭제 실패: {Path}", path);
            }
        }
    }

    private async Task<QueuedReportEnvelope?> ReadEnvelopeAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken);
            return JsonSerializer.Deserialize<QueuedReportEnvelope>(json);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            // 큐 파일 하나가 깨져 있어도 나머지 큐는 계속 처리한다.
            logger.LogWarning(ex, "큐 파일을 읽을 수 없어 건너뜀: {Path}", path);
            return null;
        }
    }

    // 임시 파일에 먼저 쓰고 File.Move로 교체한다 — 쓰는 도중 프로세스가 죽어도
    // 반쪽짜리 JSON이 최종 경로에 남지 않는다(원자적 쓰기).
    private static async Task WriteEnvelopeAsync(
        string path, QueuedReportEnvelope envelope, CancellationToken cancellationToken)
    {
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(tempPath, JsonSerializer.Serialize(envelope), cancellationToken);
        File.Move(tempPath, path, overwrite: true);
    }

    private void RemoveQuietly(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "큐 파일 삭제 실패: {Path}", path);
        }
    }

    private static string NewFileName() => $"{DateTime.UtcNow:yyyyMMddTHHmmssfffZ}_{Guid.NewGuid():N}.json";
}
