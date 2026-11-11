using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetSamLite.Shared;

namespace NetSamLite.Agent.Sending;

/// <summary>
/// 한 사이클의 전송 흐름: 큐에 쌓인 과거 리포트를 오래된 것부터 먼저 다 보내고,
/// 그게 끝까지 성공했을 때만 이번에 새로 수집한 리포트를 보낸다. 순서를 안 지키면
/// (새 리포트가 오래된 것보다 먼저 서버에 도착하면) 서버가 설치/삭제 이력을 거꾸로 기록한다.
/// </summary>
public sealed class ReportDeliveryService(
    IInventorySender sender, PendingReportQueue queue, IOptions<AgentOptions> options, ILogger<ReportDeliveryService> logger)
{
    public async Task DeliverAsync(InventoryReport report, CancellationToken cancellationToken)
    {
        var queueFullyFlushed = await FlushQueueAsync(cancellationToken);
        if (!queueFullyFlushed)
        {
            logger.LogWarning("큐가 아직 안 비워져서 이번에 수집한 리포트는 큐에만 쌓음");
            await queue.EnqueueAsync(report, cancellationToken);
            return;
        }

        if (await SendWithRetryAsync(report, cancellationToken))
        {
            return;
        }

        await queue.EnqueueAsync(report, cancellationToken);
        logger.LogWarning("서버 전송 실패, 로컬 큐에 저장함");
    }

    private async Task<bool> FlushQueueAsync(CancellationToken cancellationToken)
    {
        foreach (var entry in await queue.ListPendingAsync(cancellationToken))
        {
            if (await SendWithRetryAsync(entry.Report, cancellationToken))
            {
                await queue.RemoveAsync(entry.FilePath, cancellationToken);
                continue;
            }

            var gaveUpOnThisItem = await queue.RecordFailureAsync(entry.FilePath, cancellationToken);
            if (gaveUpOnThisItem)
            {
                // 재시도 상한을 넘겨 failed로 옮겨졌다 — 이 항목은 포기하지만 뒤에 쌓인
                // 항목들은 이 항목 때문에 막히지 않고 계속 플러시한다.
                continue;
            }

            // 아직 재시도 여지가 있는 항목 — 순서 보장을 위해 여기서 멈추고 다음 사이클에 다시 시도한다.
            return false;
        }

        return true;
    }

    private async Task<bool> SendWithRetryAsync(InventoryReport report, CancellationToken cancellationToken)
    {
        var maxAttempts = Math.Max(1, options.Value.MaxRetryAttempts);
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            if (await sender.TrySendAsync(report, cancellationToken))
            {
                return true;
            }

            if (attempt < maxAttempts)
            {
                var delaySeconds = options.Value.RetryBaseDelaySeconds * Math.Pow(2, attempt - 1);
                logger.LogWarning(
                    "전송 실패, {DelaySeconds}초 후 재시도 ({Attempt}/{MaxAttempts})",
                    delaySeconds, attempt, maxAttempts);
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
            }
        }

        return false;
    }
}
