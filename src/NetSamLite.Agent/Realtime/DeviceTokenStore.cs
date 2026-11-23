using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NetSamLite.Agent.Realtime;

/// <summary>등록(enroll) 엔드포인트 호출을 추상화한다 — 테스트는 실제 HTTP 없이 가짜 구현으로 대체한다.</summary>
public interface IDeviceEnrollmentClient
{
    Task<string?> EnrollAsync(string deviceId, string enrollmentSecret, CancellationToken cancellationToken);
}

internal sealed record EnrollmentRequest(string EnrollmentSecret);

internal sealed record EnrollmentResponse(string DeviceId, string Token);

/// <summary>
/// POST /api/v1/devices/{deviceId}/enroll 를 호출해 평문 토큰을 받아온다.
/// HttpInventorySender와 같은 패턴(실패는 예외 대신 null로 알린다).
/// </summary>
internal sealed class HttpDeviceEnrollmentClient(HttpClient httpClient, ILogger<HttpDeviceEnrollmentClient> logger)
    : IDeviceEnrollmentClient
{
    public async Task<string?> EnrollAsync(string deviceId, string enrollmentSecret, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                $"/api/v1/devices/{Uri.EscapeDataString(deviceId)}/enroll",
                new EnrollmentRequest(enrollmentSecret),
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("장치 등록(enroll) 실패: 서버가 {StatusCode}를 응답함", (int)response.StatusCode);
                return null;
            }

            var body = await response.Content.ReadFromJsonAsync<EnrollmentResponse>(cancellationToken);
            return body?.Token;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "장치 등록(enroll) 중 예외 발생");
            return null;
        }
    }
}

/// <summary>
/// Hub 인증 토큰을 ProgramData\NetSamLite\ 하위 파일에 저장/로드한다. QueueDirectory/LogDirectory와
/// 같은 이유로 ProgramData 하위를 쓴다(SYSTEM 계정에서도 안정적으로 쓰기 가능). 파일이 없거나
/// 비어 있으면 enroll을 호출해 새로 받는다. 등록 비밀값은 AgentOptions나 appsettings.json에
/// 두지 않고(절대 규칙 4) 환경변수 NETSAMLITE_ENROLLMENT_SECRET에서만 읽는다.
/// </summary>
public sealed class DeviceTokenStore(
    IDeviceEnrollmentClient enrollmentClient, IOptions<AgentOptions> options, ILogger<DeviceTokenStore> logger)
{
    public async Task<string?> GetOrEnrollAsync(string deviceId, CancellationToken cancellationToken)
    {
        var existing = await ReadTokenFileAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(existing))
        {
            return existing;
        }

        var enrollmentSecret = Environment.GetEnvironmentVariable("NETSAMLITE_ENROLLMENT_SECRET");
        if (string.IsNullOrEmpty(enrollmentSecret))
        {
            logger.LogError("환경변수 NETSAMLITE_ENROLLMENT_SECRET이 설정돼 있지 않아 장치를 등록할 수 없습니다.");
            return null;
        }

        var token = await enrollmentClient.EnrollAsync(deviceId, enrollmentSecret, cancellationToken);
        if (string.IsNullOrEmpty(token))
        {
            // 등록 실패 시 쓰레기 토큰 파일을 만들지 않는다 — 다음 시도에서 깨진 토큰으로
            // 영구히 401이 나는 상황을 막기 위함이다.
            return null;
        }

        await WriteTokenFileAsync(token, cancellationToken);
        return token;
    }

    private async Task<string?> ReadTokenFileAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(options.Value.TokenFilePath))
        {
            return null;
        }

        var content = await File.ReadAllTextAsync(options.Value.TokenFilePath, cancellationToken);
        // 빈 파일/공백만 있는 파일은 "없음"으로 취급한다(PendingReportQueue의
        // "읽을 수 없는 파일은 건너뛴다" 가드와 같은 성질).
        return string.IsNullOrWhiteSpace(content) ? null : content.Trim();
    }

    private async Task WriteTokenFileAsync(string token, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(options.Value.TokenFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // 임시 파일에 먼저 쓰고 File.Move로 교체한다(PendingReportQueue의 원자적 쓰기와 같은 이유) —
        // 쓰는 도중 프로세스가 죽어도 반쪽짜리 토큰 파일이 최종 경로에 남지 않는다.
        var tempPath = $"{options.Value.TokenFilePath}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(tempPath, token, cancellationToken);
        File.Move(tempPath, options.Value.TokenFilePath, overwrite: true);
    }
}
