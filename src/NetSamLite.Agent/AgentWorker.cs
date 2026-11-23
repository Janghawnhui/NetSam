using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetSamLite.Agent.Realtime;
using NetSamLite.Shared.Hub;

namespace NetSamLite.Agent;

/// <summary>
/// 주기 수집 본체. 시작할 때 한 번 jitter로 지연시킨 뒤(여러 PC가 한꺼번에 몰리는 걸 방지),
/// 그 이후는 매번 같은 간격으로 수집→전송을 반복한다. 매 사이클마다 jitter를 다시 넣지 않는
/// 이유: 시작 시 한 번만 흩어놓으면 이후 사이클들도 각자 시작 시각 기준으로 계속 흩어진
/// 상태를 유지하기 때문이다.
///
/// 수집·전송은 직접 하지 않고 InventoryCollectionRunner를 경유한다 — "지금 수집"
/// (AgentHubWorker의 CollectNowCommand 핸들러)과 같은 세마포를 공유해야, 두 흐름이
/// 동시에 들어와도 ReportDeliveryService의 큐 전송 순서가 뒤집히지 않는다.
/// </summary>
public sealed class AgentWorker(
    InventoryCollectionRunner collectionRunner,
    IOptions<AgentOptions> options,
    ILogger<AgentWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var jitterSeconds = Random.Shared.Next(0, Math.Max(1, options.Value.StartupJitterMaxSeconds) + 1);
        logger.LogInformation("시작 지연(jitter) {JitterSeconds}초", jitterSeconds);
        await Task.Delay(TimeSpan.FromSeconds(jitterSeconds), stoppingToken);

        // 0 이하로 잘못 설정되면 Task.Delay가 바로 예외를 던져 서비스가 죽으므로 최솟값(1분)을 보장한다.
        var intervalMinutes = Math.Max(1, options.Value.CollectionIntervalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var outcome = await collectionRunner.TryRunAsync(stoppingToken);
                if (outcome == CollectNowOutcome.AlreadyRunning)
                {
                    // "지금 수집" 명령과 이번 주기가 겹쳤다 — 이번 사이클은 건너뛰고 다음 사이클에 다시 시도한다.
                    logger.LogInformation("지금 수집이 이미 진행 중이라 이번 주기 수집은 건너뜀");
                }
                else if (outcome == CollectNowOutcome.Failed)
                {
                    logger.LogError("이번 수집 사이클 실패, 다음 사이클에서 다시 시도");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // 이번 사이클이 실패해도 서비스 자체는 죽지 않고 다음 사이클을 계속 돈다.
                logger.LogError(ex, "이번 수집 사이클 실패, 다음 사이클에서 다시 시도");
            }

            await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), stoppingToken);
        }
    }
}
