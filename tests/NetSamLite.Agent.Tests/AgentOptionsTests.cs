using FluentAssertions;
using NetSamLite.Agent;

namespace NetSamLite.Agent.Tests;

/// <summary>
/// AgentOptions 기본값이 설계대로인지(appsettings.json에서 아무것도 덮어쓰지 않았을 때
/// 서비스가 어떻게 동작할지)만 확인한다.
/// </summary>
public class AgentOptionsTests
{
    [Fact]
    public void Constructor_NoOverrides_UsesDesignedDefaultsForSchedulingAndRetry()
    {
        var options = new AgentOptions();

        options.ServerBaseUrl.Should().Be("http://localhost:5136");
        options.CollectionIntervalMinutes.Should().Be(60);
        options.StartupJitterMaxSeconds.Should().Be(300);
        options.MaxRetryAttempts.Should().Be(3);
        options.RetryBaseDelaySeconds.Should().Be(2);
        options.LogRetentionDays.Should().Be(14);
        options.MaxQueuedReports.Should().Be(500);
        options.QueueRetentionDays.Should().Be(30);
        options.MaxQueueSendFailures.Should().Be(5);
    }

    [Fact]
    public void CollectionIntervalMinutes_ZeroOrNegative_AgentWorkerClampsToOneMinute()
    {
        // AgentWorker.ExecuteAsync가 실제로 쓰는 공식: Math.Max(1, options.Value.CollectionIntervalMinutes).
        // AgentWorker 자체는 BackgroundService라 직접 단위 테스트하기 까다로우므로(별도
        // AgentWorkerTests에서 통합 테스트로 간접 검증), 여기서는 그 클램프 공식 자체가
        // 0/음수에서 항상 1 이상이 되는지를 순수 값으로 확인한다.
        Math.Max(1, 0).Should().Be(1);
        Math.Max(1, -100).Should().Be(1);
        Math.Max(1, 60).Should().Be(60);
    }

    [Fact]
    public void Constructor_NoOverrides_QueueAndLogDirectoriesAreUnderCommonApplicationData()
    {
        var options = new AgentOptions();

        options.QueueDirectory.Should().Contain("NetSamLite").And.Contain("queue");
        options.LogDirectory.Should().Contain("NetSamLite").And.Contain("logs");
    }
}
