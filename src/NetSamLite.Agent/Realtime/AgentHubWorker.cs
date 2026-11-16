using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetSamLite.Agent.Collectors;
using NetSamLite.Shared.Hub;

namespace NetSamLite.Agent.Realtime;

/// <summary>
/// 서버 Hub(AgentHubContract.Path)에 상시 연결을 유지하고 PeriodicTimer로 주기 하트비트를
/// 보내며(7-B), 서버가 보내는 CollectNowCommand를 받아 InventoryCollectionRunner로
/// 실행한 뒤 결과를 회신한다(7-C).
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
    InventoryCollectionRunner collectionRunner,
    IOptions<AgentOptions> options,
    ILogger<AgentHubWorker> logger) : BackgroundService
{
    private static readonly string AgentVersion =
        typeof(AgentHubWorker).Assembly.GetName().Version?.ToString() ?? "unknown";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var connected = await ConnectWithBackoffAsync(stoppingToken);
        if (connected is null)
        {
            // stoppingToken이 취소되어 루프를 빠져나온 경우뿐이다(영구 포기는 하지 않는다 — 확정 결정 4).
            return;
        }

        var (connection, deviceId) = connected.Value;
        try
        {
            await RunHeartbeatLoopAsync(connection, deviceId, stoppingToken);
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

    /// <summary>
    /// PeriodicTimer로 하트비트를 보낸다. 연결이 끊긴 동안(재연결 시도 중)에는 건너뛰고,
    /// WithAutomaticReconnect가 다시 Connected 상태로 돌려놓으면 다음 틱부터 재개한다 —
    /// 별도의 "재연결됨" 이벤트 처리 없이 상태만 확인하면 되는 단순한 방식이다.
    /// </summary>
    private async Task RunHeartbeatLoopAsync(HubConnection connection, string deviceId, CancellationToken stoppingToken)
    {
        var intervalSeconds = Math.Max(1, options.Value.HeartbeatIntervalSeconds);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(intervalSeconds));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (connection.State != HubConnectionState.Connected)
            {
                continue;
            }

            try
            {
                var message = new HeartbeatMessage(
                    AgentHubContract.CurrentSchemaVersion, deviceId, AgentVersion, DateTime.UtcNow);
                var ack = await connection.InvokeAsync<HeartbeatAck>(
                    "SendHeartbeatAsync", message, stoppingToken);
                if (ack.SchemaVersion != AgentHubContract.CurrentSchemaVersion)
                {
                    logger.LogWarning(
                        "하트비트 응답 SchemaVersion 불일치: {DeviceId}, 수신={Received}, 기대={Expected}",
                        deviceId, ack.SchemaVersion, AgentHubContract.CurrentSchemaVersion);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // 하트비트 1회 전송 실패는 치명적이지 않다 — 연결 유지 자체는
                // WithAutomaticReconnect가 별도로 관리하므로 다음 틱에서 다시 시도한다.
                logger.LogWarning(ex, "하트비트 전송 실패: {DeviceId}", deviceId);
            }
        }
    }

    private async Task<(HubConnection Connection, string DeviceId)?> ConnectWithBackoffAsync(
        CancellationToken stoppingToken)
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
                RegisterCommandHandlers(connection, deviceId, stoppingToken);

                await connection.StartAsync(stoppingToken);
                logger.LogInformation("Hub 연결 성립: {DeviceId}", deviceId);
                return (connection, deviceId);
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

    /// <summary>
    /// 서버가 보내는 CollectNowAsync(IAgentClient의 유일한 메서드)를 처리한다. InventoryCollectionRunner가
    /// 주기 수집(AgentWorker)과 같은 세마포를 공유하므로, 이미 수집이 돌고 있으면 기다리지 않고
    /// AlreadyRunning을 그대로 돌려준다. 결과 보고에 InvokeAsync를 쓰는 이유: SendAsync(fire-and-forget)
    /// 대신 서버가 실제로 수신했는지 확인하고, 실패하면 로그를 남기기 위함이다.
    /// </summary>
    private void RegisterCommandHandlers(HubConnection connection, string deviceId, CancellationToken stoppingToken)
    {
        connection.On<CollectNowCommand>("CollectNowAsync", async command =>
        {
            if (command.SchemaVersion != AgentHubContract.CurrentSchemaVersion)
            {
                logger.LogWarning(
                    "CollectNowCommand SchemaVersion 불일치, 처리하지 않음: {DeviceId}, 수신={Received}, 기대={Expected}",
                    deviceId, command.SchemaVersion, AgentHubContract.CurrentSchemaVersion);
                return;
            }

            logger.LogInformation("지금 수집 명령 수신: {DeviceId}, {CommandId}", deviceId, command.CommandId);
            var outcome = await collectionRunner.TryRunAsync(stoppingToken);
            logger.LogInformation(
                "지금 수집 완료: {DeviceId}, {CommandId}, {Outcome}", deviceId, command.CommandId, outcome);

            var result = new CollectNowResult(AgentHubContract.CurrentSchemaVersion, command.CommandId, outcome, Detail: null);
            try
            {
                await connection.InvokeAsync("ReportCollectNowResultAsync", result, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "지금 수집 결과 보고 실패: {DeviceId}, {CommandId}", deviceId, command.CommandId);
            }
        });
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
