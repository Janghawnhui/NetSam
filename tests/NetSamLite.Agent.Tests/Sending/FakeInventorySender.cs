using NetSamLite.Agent.Sending;
using NetSamLite.Shared;

namespace NetSamLite.Agent.Tests.Sending;

/// <summary>
/// 실제 서버 전송 없이 ReportDeliveryService의 재시도/큐 플러시 흐름만 검증하기 위한 가짜 IInventorySender.
/// EnqueueResult로 호출 순서대로 성공/실패를 미리 정해둔다. 더 정해둔 결과가 없으면 성공으로 처리한다.
/// </summary>
internal sealed class FakeInventorySender : IInventorySender
{
    private readonly Queue<bool> _results = new();

    public List<InventoryReport> Calls { get; } = [];

    public void EnqueueResult(bool success) => _results.Enqueue(success);

    public Task<bool> TrySendAsync(InventoryReport report, CancellationToken cancellationToken)
    {
        Calls.Add(report);
        var success = _results.Count > 0 ? _results.Dequeue() : true;
        return Task.FromResult(success);
    }
}
