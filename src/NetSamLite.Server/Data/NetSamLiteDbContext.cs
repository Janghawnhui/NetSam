using Microsoft.EntityFrameworkCore;
using NetSamLite.Server.Data.Entities;

namespace NetSamLite.Server.Data;

public sealed class NetSamLiteDbContext(DbContextOptions<NetSamLiteDbContext> options) : DbContext(options)
{
    public DbSet<DeviceEntity> Devices => Set<DeviceEntity>();
    public DbSet<DeviceDiskEntity> DeviceDisks => Set<DeviceDiskEntity>();
    public DbSet<InstalledSoftwareSnapshotEntity> InstalledSoftwareSnapshots => Set<InstalledSoftwareSnapshotEntity>();
    public DbSet<SoftwareChangeEventEntity> SoftwareChangeEvents => Set<SoftwareChangeEventEntity>();
    public DbSet<LicenseEntity> Licenses => Set<LicenseEntity>();
    public DbSet<ProhibitedSoftwareEntity> ProhibitedSoftware => Set<ProhibitedSoftwareEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DeviceEntity>(device =>
        {
            device.HasKey(d => d.DeviceId);
            device.HasMany(d => d.Disks)
                .WithOne()
                .HasForeignKey(d => d.DeviceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DeviceDiskEntity>(disk =>
        {
            disk.HasKey(d => d.Id);
        });

        modelBuilder.Entity<InstalledSoftwareSnapshotEntity>(snapshot =>
        {
            snapshot.HasKey(s => s.Id);
            snapshot.HasIndex(s => new { s.DeviceId, s.DisplayName }).IsUnique();
        });

        modelBuilder.Entity<SoftwareChangeEventEntity>(change =>
        {
            change.HasKey(c => c.Id);
            change.HasIndex(c => c.DeviceId);
        });

        modelBuilder.Entity<LicenseEntity>(license =>
        {
            license.HasKey(l => l.Id);
            // 유니크로 안 둔다 — 같은 제품을 여러 번(추가 구매 등)에 걸쳐 나눠 등록할 수 있다.
            license.HasIndex(l => l.ProductName);
        });

        modelBuilder.Entity<ProhibitedSoftwareEntity>(prohibited =>
        {
            prohibited.HasKey(p => p.Id);
            // License와 달리 같은 제품을 두 번 금지 등록할 이유가 없어 유니크로 둔다.
            // ProductName이 아니라 정규화된 ProductNameKey에 거는 이유는
            // ProhibitedSoftwareEntity.ComputeKey 주석 참고.
            prohibited.HasIndex(p => p.ProductNameKey).IsUnique();
        });
    }
}
