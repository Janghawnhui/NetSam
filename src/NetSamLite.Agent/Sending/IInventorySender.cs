using NetSamLite.Shared;

namespace NetSamLite.Agent.Sending;

/// <summary>
/// 서버로 InventoryReport 1건을 보내는 단일 시도. 재시도/백오프는 호출자(ReportDeliveryService)가 한다.
/// </summary>
public interface IInventorySender
{
    Task<bool> TrySendAsync(InventoryReport report, CancellationToken cancellationToken);
}
