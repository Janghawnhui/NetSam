using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NetSamLite.Server.Data;
using NetSamLite.Server.Normalization;
using NetSamLite.Shared;

namespace NetSamLite.Server.Licensing;

/// <summary>
/// 모든 디바이스의 현재 설치 스냅샷을 SoftwareNormalizer(Phase 3)로 정규화한 뒤
/// License 테이블의 보유 수량과 비교한다. 정규화 결과를 저장하는 테이블이 없으므로
/// 매번 다시 계산한다 — 정규화 규칙이 바뀌어도 항상 최신 규칙으로 판정하기 위해서다.
/// </summary>
public sealed class LicenseMatchingService(NetSamLiteDbContext dbContext, SoftwareNormalizer normalizer, ILogger<LicenseMatchingService> logger)
{
    public async Task<LicenseMatchReport> BuildReportAsync(CancellationToken cancellationToken)
    {
        var snapshots = await dbContext.InstalledSoftwareSnapshots
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        // (DeviceId, ProductName) 단위로 먼저 유니크화한다 — 한 디바이스에 같은 제품이
        // 중복 등록된 경우(예: 32/64비트 중복, User/Machine 중복) 설치 수가 부풀려지는 걸 막는다.
        var classified = new HashSet<(string DeviceId, string ProductName)>();
        var excluded = new Dictionary<(string DeviceId, string ProductName), string?>();
        var unclassified = new HashSet<(string DeviceId, string ProductName)>();
        var failedCount = 0;

        foreach (var snapshot in snapshots)
        {
            var software = new InstalledSoftware(
                snapshot.DisplayName, snapshot.DisplayVersion, snapshot.Publisher,
                snapshot.InstallDate, snapshot.InstallLocation);

            NormalizedSoftware? normalized;
            try
            {
                normalized = normalizer.Normalize(software);
            }
            catch (Exception ex)
            {
                // 규칙 하나(정규식 오류 등)가 잘못돼도 그 항목만 건너뛰고 리포트는 계속 만든다.
                failedCount++;
                logger.LogWarning(ex,
                    "{DeviceId}의 {DisplayName} 정규화 실패, 건너뜀", snapshot.DeviceId, snapshot.DisplayName);
                continue;
            }

            if (normalized is null)
            {
                continue; // 정규화 1단계(exclude)에 걸린 노이즈 항목
            }

            var key = (snapshot.DeviceId, normalized.ProductName);

            if (normalized.Unclassified)
            {
                unclassified.Add(key);
            }
            else if (!normalized.Licensable)
            {
                excluded[key] = normalized.Category;
            }
            else
            {
                classified.Add(key);
            }
        }

        var installedCountByProduct = classified
            .GroupBy(k => k.ProductName)
            .ToDictionary(g => g.Key, g => g.Count());

        var now = DateTime.UtcNow;
        var licenses = await dbContext.Licenses.AsNoTracking().ToListAsync(cancellationToken);
        var licensedQuantityByProduct = licenses
            .Where(l => l.ExpiresAtUtc is null || l.ExpiresAtUtc > now)
            .GroupBy(l => l.ProductName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity), StringComparer.OrdinalIgnoreCase);

        var matched = new List<LicenseMatchItem>();
        var unlicensedProducts = new List<UnlicensedProduct>();

        foreach (var (productName, installedCount) in installedCountByProduct)
        {
            if (!licensedQuantityByProduct.TryGetValue(productName, out var licensedQuantity))
            {
                unlicensedProducts.Add(new UnlicensedProduct(productName, installedCount));
                continue;
            }

            var shortage = installedCount - licensedQuantity;
            var status = shortage switch
            {
                > 0 => "초과 설치",
                < 0 => "미사용 라이선스",
                _ => "정상",
            };
            matched.Add(new LicenseMatchItem(productName, installedCount, licensedQuantity, status, shortage));
        }

        var excludedFromLicensing = excluded.Keys
            .GroupBy(k => k.ProductName)
            .Select(g => new ExcludedProduct(g.Key, excluded[g.First()], g.Count()))
            .ToList();

        var unclassifiedSoftware = unclassified
            .GroupBy(k => k.ProductName)
            .Select(g => new UnclassifiedProduct(g.Key, g.Count()))
            .ToList();

        return new LicenseMatchReport(matched, unlicensedProducts, excludedFromLicensing, unclassifiedSoftware, failedCount);
    }
}
