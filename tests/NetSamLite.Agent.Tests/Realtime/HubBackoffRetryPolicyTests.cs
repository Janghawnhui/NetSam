using FluentAssertions;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NetSamLite.Agent.Realtime;

namespace NetSamLite.Agent.Tests.Realtime;

/// <summary>
/// IRetryPolicy만 구현하는 순수 계산 클래스라 실제 Hub 연결 없이 RetryContext만 넣어 테스트한다.
/// </summary>
public sealed class HubBackoffRetryPolicyTests
{
    private static HubBackoffRetryPolicy CreateSut(int baseDelaySeconds = 2, int maxDelaySeconds = 60) =>
        new(
            Options.Create(new AgentOptions
            {
                ReconnectBaseDelaySeconds = baseDelaySeconds,
                ReconnectMaxDelaySeconds = maxDelaySeconds,
            }),
            NullLogger<HubBackoffRetryPolicy>.Instance);

    private static RetryContext ContextForRetry(int previousRetryCount) =>
        new() { PreviousRetryCount = previousRetryCount, ElapsedTime = TimeSpan.Zero, RetryReason = new Exception("test") };

    [Fact]
    public void NextRetryDelay_IncreasingRetryCount_GrowsExponentiallyWithinJitterBounds()
    {
        var sut = CreateSut(baseDelaySeconds: 2, maxDelaySeconds: 1000);

        var delay0 = sut.NextRetryDelay(ContextForRetry(0))!.Value.TotalSeconds;
        var delay1 = sut.NextRetryDelay(ContextForRetry(1))!.Value.TotalSeconds;
        var delay2 = sut.NextRetryDelay(ContextForRetry(2))!.Value.TotalSeconds;
        var delay3 = sut.NextRetryDelay(ContextForRetry(3))!.Value.TotalSeconds;

        // base=2 → 기대 범위: [2×2^n, 2×2^n×1.5](jitter 상한 50%).
        delay0.Should().BeInRange(2, 3);
        delay1.Should().BeInRange(4, 6);
        delay2.Should().BeInRange(8, 12);
        delay3.Should().BeInRange(16, 24);
    }

    [Fact]
    public void NextRetryDelay_LargeRetryCount_NeverExceedsSixtySeconds()
    {
        var sut = CreateSut(baseDelaySeconds: 2, maxDelaySeconds: 60);

        var delayAt100 = sut.NextRetryDelay(ContextForRetry(100));
        var delayAt1000 = sut.NextRetryDelay(ContextForRetry(1000));

        delayAt100.Should().NotBeNull();
        delayAt1000.Should().NotBeNull();
        delayAt100!.Value.TotalSeconds.Should().BeInRange(0, 60);
        delayAt1000!.Value.TotalSeconds.Should().BeInRange(0, 60);
    }

    [Fact]
    public void NextRetryDelay_FirstRetry_IsNotZero()
    {
        var sut = CreateSut();

        var delay = sut.NextRetryDelay(ContextForRetry(0));

        delay!.Value.TotalSeconds.Should().BeGreaterThan(0);
    }

    [Fact]
    public void NextRetryDelay_RepeatedCallsAtSameRetryCount_ProduceDifferentValues()
    {
        var sut = CreateSut(baseDelaySeconds: 10, maxDelaySeconds: 1000);

        var values = Enumerable.Range(0, 20)
            .Select(_ => sut.NextRetryDelay(ContextForRetry(5))!.Value.TotalSeconds)
            .Distinct()
            .ToList();

        values.Should().HaveCountGreaterThan(1, "jitter가 실제로 섞여야 매번 다른 값이 나온다");
    }

    [Fact]
    public void NextRetryDelay_AnyRetryCount_NeverReturnsNull()
    {
        var sut = CreateSut();

        for (var retryCount = 0; retryCount <= 50; retryCount++)
        {
            sut.NextRetryDelay(ContextForRetry(retryCount)).Should().NotBeNull();
        }
    }

    [Fact]
    public void NextRetryDelay_ZeroOrNegativeConfiguredDelays_ClampsToMinimum()
    {
        var sut = CreateSut(baseDelaySeconds: -5, maxDelaySeconds: 0);

        var delay = sut.NextRetryDelay(ContextForRetry(0));

        delay.Should().NotBeNull();
        delay!.Value.TotalSeconds.Should().BeGreaterThan(0);
    }
}
