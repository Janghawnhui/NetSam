namespace NetSamLite.Agent;

/// <summary>
/// appsettings.json의 "Agent" 섹션. 주기 수집·재시도·큐·로그 동작을 설정으로 조정한다.
/// </summary>
public sealed class AgentOptions
{
    public const string SectionName = "Agent";

    public string ServerBaseUrl { get; set; } = "http://localhost:5136";

    /// <summary>수집 주기(분). 한 사이클이 끝난 뒤 다음 사이클까지의 대기 시간.</summary>
    public int CollectionIntervalMinutes { get; set; } = 60;

    /// <summary>시작 시 0~이 값(초) 사이에서 랜덤 지연 — 여러 PC가 한 번에 몰리는 걸 막는다.</summary>
    public int StartupJitterMaxSeconds { get; set; } = 300;

    /// <summary>전송 1회 시도 내에서의 재시도 횟수(지수 백오프). 다 실패하면 로컬 큐에 쌓는다.</summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>재시도 백오프 기준 지연(초). 1차 시도 실패 시 이 값, 2차는 2배, 3차는 4배...</summary>
    public int RetryBaseDelaySeconds { get; set; } = 2;

    /// <summary>서버 전송 실패 시 쌓아두는 로컬 큐 폴더. SYSTEM 계정도 안정적으로 쓰도록 ProgramData 하위를 기본값으로 둔다.</summary>
    public string QueueDirectory { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NetSamLite", "queue");

    /// <summary>큐에 쌓을 수 있는 최대 리포트 수. 넘으면 가장 오래된 것부터 버려서 디스크가 무한정 차는 걸 막는다.</summary>
    public int MaxQueuedReports { get; set; } = 500;

    /// <summary>큐/폐기(failed) 파일 보관 일수. 이보다 오래된 건 다음 조회 때 자동 삭제.</summary>
    public int QueueRetentionDays { get; set; } = 30;

    /// <summary>같은 리포트가 연속으로 이 횟수만큼 전송 실패하면 포기하고 failed 폴더로 옮긴다 — 그래야 그 뒤에 쌓인 리포트들이 계속 막히지 않는다.</summary>
    public int MaxQueueSendFailures { get; set; } = 5;

    /// <summary>로그 파일 폴더. 큐와 같은 이유로 ProgramData 하위가 기본값.</summary>
    public string LogDirectory { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NetSamLite", "logs");

    /// <summary>로그 파일 보관 일수. 이보다 오래된 로그 파일은 자동 삭제.</summary>
    public int LogRetentionDays { get; set; } = 14;
}
