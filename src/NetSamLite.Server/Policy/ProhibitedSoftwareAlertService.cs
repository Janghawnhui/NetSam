using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NetSamLite.Server.Data;
using NetSamLite.Server.Data.Entities;
using NetSamLite.Server.Normalization;
using NetSamLite.Shared;

namespace NetSamLite.Server.Policy;

/// <summary>
/// 모든 디바이스의 설치 스냅샷을 정규화해서 ProhibitedSoftware 목록과 ProductNameKey
/// (ProhibitedSoftwareEntity.ComputeKey — Trim+공백축약+대소문자 무시) 완전일치로 비교한다.
/// 등록/DB 유니크 인덱스와 똑같은 키 함수를 써야 한다 — 다른 기준을 쓰면 "등록은 됐는데
/// 알림이 안 뜨는" 불일치가 생긴다(공백이 불규칙한 제품명을 등록한 경우 등).
/// LicenseMatchingService와 달리 Licensable/Unclassified는 안 가린다 — 금지 SW는
/// 분류 여부와 상관없이 설치돼 있으면 바로 문제다.
/// </summary>
public sealed class ProhibitedSoftwareAlertService(
    NetSamLiteDbContext dbContext, SoftwareNormalizer normalizer, ILogger<ProhibitedSoftwareAlertService> logger)
{
    public async Task<IReadOnlyList<ProhibitedSoftwareAlert>> BuildAlertsAsync(CancellationToken cancellationToken)
    {
        var prohibited = await dbContext.ProhibitedSoftware.AsNoTracking().ToListAsync(cancellationToken);
        if (prohibited.Count == 0)
        {
            return [];
        }

        var reasonByKey = prohibited.ToDictionary(p => p.ProductNameKey, p => p.Reason);

        var snapshots = await dbContext.InstalledSoftwareSnapshots.AsNoTracking().ToListAsync(cancellationToken);
        var computerNameByDeviceId = await dbContext.Devices.AsNoTracking()
            .ToDictionaryAsync(d => d.DeviceId, d => d.ComputerName, cancellationToken);

        var devicesByKey = new Dictionary<string, HashSet<(string DeviceId, string ComputerName)>>();

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
                logger.LogWarning(ex,
                    "{DeviceId}의 {DisplayName} 정규화 실패, 건너뜀", snapshot.DeviceId, snapshot.DisplayName);
                continue;
            }

            if (normalized is null)
            {
                continue;
            }

            var key = ProhibitedSoftwareEntity.ComputeKey(normalized.ProductName);
            if (!reasonByKey.ContainsKey(key))
            {
                continue;
            }

            var computerName = computerNameByDeviceId.GetValueOrDefault(snapshot.DeviceId, snapshot.DeviceId);
            if (!devicesByKey.TryGetValue(key, out var devices))
            {
                devices = [];
                devicesByKey[key] = devices;
            }

            devices.Add((snapshot.DeviceId, computerName));
        }

        return prohibited
            .Where(p => devicesByKey.ContainsKey(p.ProductNameKey))
            .Select(p => new ProhibitedSoftwareAlert(
                p.ProductName,
                p.Reason,
                devicesByKey[p.ProductNameKey]
                    .Select(d => new ProhibitedSoftwareDeviceRef(d.DeviceId, d.ComputerName))
                    .ToList()))
            .ToList();
    }
}
