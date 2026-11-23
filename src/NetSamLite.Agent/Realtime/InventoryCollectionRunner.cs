using Microsoft.Extensions.Logging;
using NetSamLite.Agent.Collectors;
using NetSamLite.Agent.Sending;
using NetSamLite.Shared;
using NetSamLite.Shared.Hub;

namespace NetSamLite.Agent.Realtime;

/// <summary>
/// "수집 → 전송"을 한 번에 하나만 돌게 보장하는 얇은 래퍼. 주기 수집(AgentWorker)과
/// "지금 수집"(AgentHubWorker의 CollectNowCommand 핸들러)이 동시에 들어오면 둘 다
/// ReportDeliveryService의 같은 큐 디렉터리·전송 경로를 경쟁적으로 쓰게 되어 서버에
/// 도착하는 순서가 뒤집힌다(= 설치/삭제 이력이 거꾸로 기록된다). SemaphoreSlim(1,1)로
/// 감싸고, 이미 돌고 있으면 기다리지 않고 즉시 AlreadyRunning을 반환한다 — 버튼 연타로
/// 수집이 줄 서는 것을 막고 "지금 이미 돌고 있다"는 정확한 정보를 돌려준다.
/// </summary>
public sealed class InventoryCollectionRunner(
    ISoftwareCollector softwareCollector,
    IHardwareCollector hardwareCollector,
    ReportDeliveryService deliveryService,
    ILogger<InventoryCollectionRunner> logger)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<CollectNowOutcome> TryRunAsync(CancellationToken cancellationToken)
    {
        // timeout=0이라 대기하지 않고 즉시 성공/실패가 결정된다.
        if (!await _gate.WaitAsync(0, cancellationToken))
        {
            logger.LogInformation("이미 수집이 진행 중이라 이번 요청은 건너뜀");
            return CollectNowOutcome.AlreadyRunning;
        }

        try
        {
            var report = new InventoryReport(hardwareCollector.Collect(), softwareCollector.Collect());
            await deliveryService.DeliverAsync(report, cancellationToken);
            return CollectNowOutcome.Succeeded;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "수집 실행 실패");
            return CollectNowOutcome.Failed;
        }
        finally
        {
            // 취소/예외 경로 전부 포함해 세마포가 영구 점유되지 않도록 보장한다.
            _gate.Release();
        }
    }
}
