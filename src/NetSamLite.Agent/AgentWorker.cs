using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetSamLite.Agent.Collectors;
using NetSamLite.Agent.Sending;
using NetSamLite.Shared;

namespace NetSamLite.Agent;

/// <summary>
/// 주기 수집 본체. 시작할 때 한 번 jitter로 지연시킨 뒤(여러 PC가 한꺼번에 몰리는 걸 방지),
/// 그 이후는 매번 같은 간격으로 수집→전송을 반복한다. 매 사이클마다 jitter를 다시 넣지 않는
/// 이유: 시작 시 한 번만 흩어놓으면 이후 사이클들도 각자 시작 시각 기준으로 계속 흩어진
/// 상태를 유지하기 때문이다.
/// </summary>
public sealed class AgentWorker(
    ISoftwareCollector softwareCollector,
    IHardwareCollector hardwareCollector,
    ReportDeliveryService deliveryService,
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
                var report = new InventoryReport(hardwareCollector.Collect(), softwareCollector.Collect());
                await deliveryService.DeliverAsync(report, stoppingToken);
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
