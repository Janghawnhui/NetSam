using System.Text.RegularExpressions;

namespace NetSamLite.Server.Data.Entities;

/// <summary>
/// 정책상 설치되면 안 되는 제품 1건. ProductName은 SoftwareNormalizer가 출력하는
/// ProductName과 대소문자 무시 완전일치로 매칭한다.
/// </summary>
public sealed class ProhibitedSoftwareEntity
{
    public int Id { get; set; }
    public required string ProductName { get; set; }

    /// <summary>
    /// ProductName을 Trim + 공백 1개로 축약 + 대문자화한 값. 유니크 인덱스를 이 컬럼에
    /// 걸어서 "DB 제약"과 "앱의 중복 판정 규칙"이 항상 같은 기준을 쓰게 한다 — SQLite의
    /// NOCASE 콜레이션은 ASCII만 대소문자 무시라 한글 등 비ASCII 제품명에는 안 먹힌다.
    /// </summary>
    public required string ProductNameKey { get; set; }

    public required string Reason { get; set; }
    public DateTime AddedAtUtc { get; set; }

    public static string ComputeKey(string productName) =>
        Regex.Replace(productName.Trim(), @"\s+", " ").ToUpperInvariant();
}
