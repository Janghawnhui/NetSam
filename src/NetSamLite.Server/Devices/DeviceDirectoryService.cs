using Microsoft.EntityFrameworkCore;
using NetSamLite.Server.Data;
using NetSamLite.Server.Data.Entities;

namespace NetSamLite.Server.Devices;

/// <summary>
/// 장치 목록/상세 조회. API 엔드포인트와 Blazor 대시보드가 같은 로직을 쓰도록 분리했다.
/// </summary>
public sealed class DeviceDirectoryService(NetSamLiteDbContext dbContext)
{
    public async Task<IReadOnlyList<DeviceSummary>> ListAsync(CancellationToken cancellationToken)
    {
        var devices = await dbContext.Devices.AsNoTracking().Include(d => d.Disks).ToListAsync(cancellationToken);
        var softwareCountByDevice = await dbContext.InstalledSoftwareSnapshots.AsNoTracking()
            .GroupBy(s => s.DeviceId)
            .Select(g => new { DeviceId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.DeviceId, x => x.Count, cancellationToken);

        return devices
            .Select(d => new DeviceSummary(
                d.DeviceId,
                d.ComputerName,
                d.Manufacturer,
                d.Model,
                d.OperatingSystemCaption,
                d.OperatingSystemVersion,
                d.ProcessorName,
                d.ProcessorCoreCount,
                d.TotalPhysicalMemoryBytes,
                d.Disks.Select(disk => new DiskSummary(disk.Model, disk.SizeBytes)).ToList(),
                d.LastSeenUtc,
                softwareCountByDevice.GetValueOrDefault(d.DeviceId, 0)))
            .ToList();
    }

    public async Task<IReadOnlyList<InstalledSoftwareSnapshotEntity>> GetInstalledSoftwareAsync(
        string deviceId, CancellationToken cancellationToken) =>
        await dbContext.InstalledSoftwareSnapshots
            .AsNoTracking()
            .Where(s => s.DeviceId == deviceId)
            .ToListAsync(cancellationToken);
}
