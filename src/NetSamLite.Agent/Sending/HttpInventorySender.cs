using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using NetSamLite.Shared;

namespace NetSamLite.Agent.Sending;

internal sealed class HttpInventorySender(HttpClient httpClient, ILogger<HttpInventorySender> logger) : IInventorySender
{
    public async Task<bool> TrySendAsync(InventoryReport report, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.PostAsJsonAsync("/api/v1/inventory", report, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            logger.LogWarning("서버가 {StatusCode}를 응답함", (int)response.StatusCode);
            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException
            || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // 취소(종료 요청)로 인한 TaskCanceledException은 그대로 전파해 서비스가 멈추게 둔다.
            // 타임아웃/연결 실패로 인한 것만 "전송 실패"로 취급해 재시도/큐잉 경로로 보낸다.
            logger.LogWarning(ex, "서버 전송 중 예외 발생");
            return false;
        }
    }
}
