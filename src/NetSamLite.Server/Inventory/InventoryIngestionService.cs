using Microsoft.EntityFrameworkCore;
using NetSamLite.Server.Data;
using NetSamLite.Server.Data.Entities;
using NetSamLite.Shared;

namespace NetSamLite.Server.Inventory;

public sealed record IngestionResult(int InstalledCount, int UninstalledCount, int UnchangedCount);

/// <summary>
/// POST로 들어온 InventoryReport를 디바이스의 이전 스냅샷과 비교해 DB에 반영하고
/// 설치/삭제 변화를 SoftwareChangeEventEntity로 남긴다.
/// 식별 키는 DisplayName만 쓴다 — 버전만 바뀐 경우는 "같은 SW의 갱신"으로 보고
/// 설치/삭제 이력을 남기지 않는다(버전 해석은 Phase 3 정규화 영역).
/// RegistrySoftwareCollector는 32/64비트 Uninstall 키 중복을 제거하지 않으므로 같은 요청
/// 안에 동일한 DisplayName이 여러 번 올 수 있다 — 그 경우 마지막 값이 최종 상태가 된다.
/// </summary>
public sealed class InventoryIngestionService(NetSamLiteDbContext dbContext)
{
    public async Task<IngestionResult> IngestAsync(InventoryReport report, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        await UpsertDeviceAsync(report.Device, now, cancellationToken);

        var existingSnapshots = await dbContext.InstalledSoftwareSnapshots
            .Where(s => s.DeviceId == report.Device.DeviceId)
            .ToListAsync(cancellationToken);
        var existingByName = existingSnapshots.ToDictionary(s => s.DisplayName);
        var incomingNames = report.InstalledSoftware.Select(s => s.DisplayName).ToHashSet();

        var installedCount = 0;
        var unchangedCount = 0;

        foreach (var incoming in report.InstalledSoftware)
        {
            if (existingByName.TryGetValue(incoming.DisplayName, out var existing))
            {
                existing.DisplayVersion = incoming.DisplayVersion;
                existing.Publisher = incoming.Publisher;
                existing.InstallDate = incoming.InstallDate;
                existing.InstallLocation = incoming.InstallLocation;
                existing.LastSeenUtc = now;
                unchangedCount++;
            }
            else
            {
                var newSnapshot = new InstalledSoftwareSnapshotEntity
                {
                    DeviceId = report.Device.DeviceId,
                    DisplayName = incoming.DisplayName,
                    DisplayVersion = incoming.DisplayVersion,
                    Publisher = incoming.Publisher,
                    InstallDate = incoming.InstallDate,
                    InstallLocation = incoming.InstallLocation,
                    FirstSeenUtc = now,
                    LastSeenUtc = now,
                };
                dbContext.InstalledSoftwareSnapshots.Add(newSnapshot);
                // 같은 요청 안에 같은 DisplayName이 또 나오면 insert가 아니라 update 경로를 타도록
                // 딕셔너리도 갱신한다 — 안 하면 유니크 인덱스 위반으로 전체 수집이 실패한다.
                existingByName[incoming.DisplayName] = newSnapshot;
                dbContext.SoftwareChangeEvents.Add(new SoftwareChangeEventEntity
                {
                    DeviceId = report.Device.DeviceId,
                    DisplayName = incoming.DisplayName,
                    DisplayVersion = incoming.DisplayVersion,
                    ChangeType = SoftwareChangeType.Installed,
                    OccurredAtUtc = now,
                });
                installedCount++;
            }
        }

        var uninstalledCount = 0;
        foreach (var existing in existingSnapshots.Where(s => !incomingNames.Contains(s.DisplayName)))
        {
            dbContext.InstalledSoftwareSnapshots.Remove(existing);
            dbContext.SoftwareChangeEvents.Add(new SoftwareChangeEventEntity
            {
                DeviceId = report.Device.DeviceId,
                DisplayName = existing.DisplayName,
                DisplayVersion = existing.DisplayVersion,
                ChangeType = SoftwareChangeType.Uninstalled,
                OccurredAtUtc = now,
            });
            uninstalledCount++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return new IngestionResult(installedCount, uninstalledCount, unchangedCount);
    }

    private async Task UpsertDeviceAsync(DeviceInfo deviceInfo, DateTime now, CancellationToken cancellationToken)
    {
        var device = await dbContext.Devices
            .Include(d => d.Disks)
            .FirstOrDefaultAsync(d => d.DeviceId == deviceInfo.DeviceId, cancellationToken);

        if (device is null)
        {
            device = new DeviceEntity { DeviceId = deviceInfo.DeviceId, ComputerName = deviceInfo.ComputerName };
            dbContext.Devices.Add(device);
        }

        device.ComputerName = deviceInfo.ComputerName;
        device.Manufacturer = deviceInfo.Manufacturer;
        device.Model = deviceInfo.Model;
        device.SerialNumber = deviceInfo.SerialNumber;
        device.ProcessorName = deviceInfo.ProcessorName;
        device.ProcessorCoreCount = deviceInfo.ProcessorCoreCount;
        device.OperatingSystemCaption = deviceInfo.OperatingSystemCaption;
        device.OperatingSystemVersion = deviceInfo.OperatingSystemVersion;
        device.OperatingSystemBuildNumber = deviceInfo.OperatingSystemBuildNumber;
        device.TotalPhysicalMemoryBytes = deviceInfo.TotalPhysicalMemoryBytes;
        device.LastSeenUtc = now;

        // 디스크는 매번 전체 교체한다 — 필수(FK not null) 관계라 컬렉션에서 빠진 항목은
        // EF Core가 고아로 판단해 자동으로 삭제한다(OnModelCreating의 Cascade 설정).
        device.Disks.Clear();
        foreach (var disk in deviceInfo.Disks)
        {
            device.Disks.Add(new DeviceDiskEntity
            {
                DeviceId = deviceInfo.DeviceId,
                Model = disk.Model,
                SizeBytes = disk.SizeBytes,
            });
        }
    }
}
