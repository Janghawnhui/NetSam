using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using NetSamLite.Shared.Hub;

namespace NetSamLite.Server.Realtime;

/// <summary>
/// "DeviceToken" 인증 스킴. Authorization: Bearer 헤더와 ?access_token= 쿼리 스트링 둘 다에서
/// 토큰을 읽는다. 쿼리 스트링 허용은 Hub 경로에 한정한다(다른 경로는 헤더만) — WebSocket
/// 핸드셰이크는 커스텀 헤더를 실을 수 없어서 SignalR 클라이언트의 AccessTokenProvider가
/// WebSocket/SSE 전송에서는 토큰을 access_token 쿼리 파라미터로 보내기 때문이다. 헤더만
/// 읽으면 롱폴링은 통과하고 WebSocket만 401이 나는, 원인 찾기 어려운 증상이 생긴다.
/// </summary>
public sealed class DeviceTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    DeviceTokenService tokenService)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    public const string SchemeName = "DeviceToken";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = ExtractToken();
        if (string.IsNullOrEmpty(token))
        {
            return AuthenticateResult.Fail("토큰이 없습니다.");
        }

        var deviceId = await tokenService.ValidateAsync(token, Context.RequestAborted);
        if (deviceId is null)
        {
            return AuthenticateResult.Fail("토큰이 유효하지 않습니다.");
        }

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, deviceId)], SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return AuthenticateResult.Success(ticket);
    }

    private string? ExtractToken()
    {
        var header = Request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return header["Bearer ".Length..].Trim();
        }

        if (Request.Path.StartsWithSegments(AgentHubContract.Path)
            && Request.Query.TryGetValue("access_token", out var queryToken))
        {
            return queryToken.ToString();
        }

        return null;
    }
}
