using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NetSamLite.Server.Data;
using NetSamLite.Server.Data.Entities;

namespace NetSamLite.Server.Realtime;

public sealed record EnrollmentRequest(string EnrollmentSecret);

/// <summary>Token은 이 응답에만 평문으로 존재한다 — DB에는 해시만 저장된다.</summary>
public sealed record EnrollmentResponse(string DeviceId, string Token);

/// <summary>
/// 장치 Hub 인증 토큰 발급·검증. 등록(enroll) 공통 비밀값은 설정(user-secrets/환경변수)에서
/// 읽고(절대 규칙 4 — appsettings.json에 넣지 않음), 토큰 자체는 256비트 암호학적 난수라
/// 해시 탈취로 원본을 역산할 탐색 공간이 없다. 그래서 사람이 만든 저엔트로피 비밀번호를
/// 보호하기 위한 느린 KDF(PBKDF2/Argon2) 대신 단순 SHA-256을 쓴다 — 매 연결마다 느린 KDF를
/// 돌리는 비용을 치를 이유가 없다. 다만 솔트 없는 단순 해시라 "같은 토큰이면 같은 해시"라는
/// 성질이 남으므로, 토큰은 장치마다 매번 새로 생성하고 재사용하지 않는다.
/// </summary>
public sealed class DeviceTokenService(
    NetSamLiteDbContext dbContext, IConfiguration configuration, ILogger<DeviceTokenService> logger)
{
    /// <returns>등록 비밀값이 틀리면 null(토큰 미발급). 맞으면 평문 토큰(응답에 1회만 노출).</returns>
    public async Task<string?> IssueAsync(string deviceId, string providedEnrollmentSecret, CancellationToken cancellationToken)
    {
        var expectedSecret = configuration["Realtime:EnrollmentSecret"];
        if (string.IsNullOrEmpty(expectedSecret) || !FixedTimeEquals(providedEnrollmentSecret, expectedSecret))
        {
            logger.LogWarning("등록 비밀값이 일치하지 않아 토큰 발급을 거부함: {DeviceId}", deviceId);
            return null;
        }

        var token = GenerateToken();
        var tokenHash = ComputeHash(token);

        var existing = await dbContext.DeviceTokens.FindAsync([deviceId], cancellationToken);
        if (existing is null)
        {
            dbContext.DeviceTokens.Add(new DeviceTokenEntity
            {
                DeviceId = deviceId,
                TokenHash = tokenHash,
                IssuedAtUtc = DateTime.UtcNow,
            });
        }
        else
        {
            // 재등록(재설치·토큰 분실 복구) — 기존 해시를 교체한다. DeviceId가 PK라 행이 늘지 않는다.
            existing.TokenHash = tokenHash;
            existing.IssuedAtUtc = DateTime.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return token;
    }

    /// <returns>유효한 토큰이면 해당 DeviceId, 아니면 null(미등록/빈 값/변조 전부 "실패"로 처리).</returns>
    public async Task<string?> ValidateAsync(string? token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        var tokenHash = ComputeHash(token);
        var entity = await dbContext.DeviceTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);
        return entity?.DeviceId;
    }

    private static string GenerateToken() => Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    private static string ComputeHash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    // 등록 비밀값 비교에 == 대신 고정시간 비교를 쓰는 이유: 문자열 ==은 첫 불일치 바이트에서
    // 바로 리턴해 비교 시간으로 "앞이 몇 글자 맞았는지"가 새어 나갈 수 있다(타이밍 공격).
    private static bool FixedTimeEquals(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));
}
