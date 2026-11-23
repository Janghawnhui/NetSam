using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NetSamLite.Agent.Realtime;

/// <summary>
/// WithAutomaticReconnect에 꽂는 재연결 간격 계산기. min(base × 2^n, Max)에 jitter를 섞는다
/// (여러 PC가 서버 재시작 직후 동시에 몰리는 thundering herd 방지 — AgentOptions의
/// StartupJitterMaxSeconds와 같은 취지). NextRetryDelay는 어떤 입력에도 null을 반환하지
/// 않는다 — SignalR에서 null은 "재연결 포기"를 뜻하고, 확정 결정 4(영구 재시도, 포기 없음)에
/// 따라 이 정책에는 재시도 횟수 상한이 없다. IRetryPolicy만 구현하는 순수 계산 클래스로
/// 분리해서 실제 Hub 연결 없이 단위 테스트가 가능하다.
/// </summary>
public sealed class HubBackoffRetryPolicy(IOptions<AgentOptions> options, ILogger<HubBackoffRetryPolicy> logger)
    : IRetryPolicy
{
    private const double MaxJitterRatio = 0.5;

    public TimeSpan? NextRetryDelay(RetryContext retryContext)
    {
        // 0 이하로 잘못 설정돼도 즉시 재시도 폭주나 예외가 나지 않도록 최솟값 1초로 클램프한다
        // (기존 AgentOptions.CollectionIntervalMinutes의 Math.Max(1, ...) 방어와 같은 성질).
        var baseDelaySeconds = Math.Max(1, options.Value.ReconnectBaseDelaySeconds);
        var maxDelaySeconds = Math.Max(baseDelaySeconds, options.Value.ReconnectMaxDelaySeconds);

        var retryCount = Math.Max(0, retryContext.PreviousRetryCount);
        // Math.Pow가 큰 retryCount에서 double 오버플로로 Infinity를 반환할 수 있지만,
        // 음수나 NaN은 되지 않으므로 아래 Math.Min으로 안전하게 상한에 맞춰진다.
        var exponentialSeconds = baseDelaySeconds * Math.Pow(2, retryCount);

        var jitterRatio = Random.Shared.NextDouble() * MaxJitterRatio;
        var withJitterSeconds = exponentialSeconds * (1 + jitterRatio);

        var delaySeconds = Math.Min(withJitterSeconds, maxDelaySeconds);
        var delay = TimeSpan.FromSeconds(delaySeconds);
        logger.LogWarning(
            "Hub 재연결 {RetryCount}번째 시도, {DelaySeconds:F1}초 후 재시도", retryCount, delay.TotalSeconds);
        return delay;
    }
}
