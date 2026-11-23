namespace NetSamLite.Server.Data.Entities;

/// <summary>
/// 장치 Hub 인증 토큰의 해시만 저장한다(절대 규칙 4 — 평문 비밀값은 어디에도 두지 않는다).
/// DeviceId가 PK라 재등록 시 기존 행을 그대로 덮어써 토큰을 교체한다(장치별 1개).
/// </summary>
public sealed class DeviceTokenEntity
{
    public required string DeviceId { get; set; }

    /// <summary>발급된 토큰(256비트 난수)의 SHA-256 해시. 평문은 발급 응답에만 존재한다.</summary>
    public required string TokenHash { get; set; }

    public DateTime IssuedAtUtc { get; set; }
}

/// <summary>
/// 장치별 Hub 연결 상태 1행. DeviceEntity와 분리한 이유는
/// DeviceEntity가 InventoryIngestionService.UpsertDeviceAsync가 매 수집마다 전체를
/// 덮어쓰는 "최신 스냅샷"이라, 같은 행에 두면 수집 1회가 토큰/연결 상태를 날릴 위험이 있기 때문이다.
///
/// IsOnline은 OnConnectedAsync/OnDisconnectedAsync에서만 즉시 갱신되고(확정 결정 5),
/// LastHeartbeatUtc는 하트비트에서만 갱신된다 — 두 값은 분리된 별개 필드다.
/// </summary>
public sealed class DeviceConnectionEntity
{
    public required string DeviceId { get; set; }
    public string? ConnectionId { get; set; }
    public bool IsOnline { get; set; }
    public DateTime? LastConnectedAtUtc { get; set; }
    public DateTime? LastHeartbeatUtc { get; set; }
    public DateTime? LastDisconnectedAtUtc { get; set; }
}
