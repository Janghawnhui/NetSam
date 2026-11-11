using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetSamLite.Agent.Collectors;

namespace NetSamLite.Agent.Realtime;

/// <summary>
/// 서버 Hub(AgentHubContract.Path)에 상시 연결을 유지한다. 7-A 단계에서는 연결 유지까지만
/// 다루고, 하트비트/명령 수신 핸들러는 7-B/7-C에서 추가된다.
///
/// WithAutomaticReconnect는 "이미 성립한 연결이 끊긴 경우"만 다시 붙여준다 — StartAsync
/// 자체가 처음부터 실패하면(서버가 아직 안 떠 있음, 서비스 자동 시작에서 매우 흔한 상황)
/// 자동 재연결은 전혀 동작하지 않는다. 그래서 최초 연결은 이 워커가 같은 백오프 정책
/// (HubBackoffRetryPolicy)으로 직접 감싼 루프 안에서 시도한다.
/// </summary>
public sealed class AgentHubWorker(
    IHardwareCollector hardwareCollector,
    DeviceTokenStore tokenStore,
    HubBackoffRetryPolicy retryPolicy,
    IOptions<AgentOptions> options,
    ILogger<AgentHubWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var connection = await ConnectWithBackoffAsync(stoppingToken);
        if (connection is null)
        {
            // stoppingToken이 취소되어 루프를 빠져나온 경우뿐이다(영구 포기는 하지 않는다 — 확정 결정 4).
            return;
        }

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // 서비스 정상 종료 — 아래 finally에서 연결을 정리한다.
        }
        finally
        {
            await connection.DisposeAsync();
        }
    }

    private async Task<HubConnection?> ConnectWithBackoffAsync(CancellationToken stoppingToken)
    {
        var attempt = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            HubConnection? connection = null;
            try
            {
                // DeviceId 계산(WMI 조회)도 이 try 안에서 매 시도마다 다시 한다 — AgentWorker.cs가
                // 수집 호출을 매 사이클 try/catch로 감싸는 것과 같은 이유다. 이 줄이 루프 밖에서
                // 가드 없이 예외를 던지면 BackgroundService의 처리되지 않은 예외로 호스트 전체가
                // 죽어, 같은 프로세스의 AgentWorker(주기 수집)까지 함께 멈춘다.
                var deviceId = hardwareCollector.Collect().DeviceId;
                var token = await tokenStore.GetOrEnrollAsync(deviceId, stoppingToken);
                if (token is null)
                {
                    throw new InvalidOperationException(
                        "Hub 인증 토큰을 확보하지 못했습니다 — 등록 비밀값(NETSAMLITE_ENROLLMENT_SECRET) 또는 서버 상태를 확인하세요.");
                }

                connection = BuildConnection(token);
                RegisterLifecycleLogging(connection, deviceId);

                await connection.StartAsync(stoppingToken);
                logger.LogInformation("Hub 연결 성립: {DeviceId}", deviceId);
                return connection;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (connection is not null)
                {
                    await connection.DisposeAsync();
                }

                var delay = retryPolicy.NextRetryDelay(
                    new RetryContext { PreviousRetryCount = attempt, ElapsedTime = TimeSpan.Zero, RetryReason = ex })
                    ?? TimeSpan.FromSeconds(1);
                attempt++;
                logger.LogWarning(ex, "Hub 최초 연결 실패({Attempt}번째), {DelaySeconds:F1}초 후 재시도", attempt, delay.TotalSeconds);

                try
                {
                    await Task.Delay(delay, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return null;
                }
            }
        }

        return null;
    }

    private HubConnection BuildConnection(string token)
    {
        var hubUri = new Uri(new Uri(options.Value.ServerBaseUrl), options.Value.HubPath);
        return new HubConnectionBuilder()
            .WithUrl(hubUri, httpOptions =>
            {
                httpOptions.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .WithAutomaticReconnect(retryPolicy)
            .Build();
    }

    private void RegisterLifecycleLogging(HubConnection connection, string deviceId)
    {
        connection.Reconnecting += ex =>
        {
            logger.LogWarning(ex, "Hub 연결 끊김, 재연결 시도 시작: {DeviceId}", deviceId);
            return Task.CompletedTask;
        };
        connection.Reconnected += _ =>
        {
            logger.LogInformation("Hub 재연결 성공: {DeviceId}", deviceId);
            return Task.CompletedTask;
        };
        connection.Closed += ex =>
        {
            logger.LogWarning(ex, "Hub 연결이 완전히 닫힘(자동 재연결도 중단됨): {DeviceId}", deviceId);
            return Task.CompletedTask;
        };
    }
}
