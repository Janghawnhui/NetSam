using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NetSamLite.Server.Data.Entities;
using NetSamLite.Server.Inventory;
using NetSamLite.Shared;

namespace NetSamLite.Server.Tests.Inventory;

/// <summary>
/// InventoryIngestionService는 EF Core의 유니크 인덱스/FK/Cascade delete 제약에 동작이
/// 크게 의존하므로(InMemory 프로바이더는 이 제약들을 검증하지 않는다), 실제 SQLite 엔진
/// (in-memory 모드)에 대해 테스트한다. 요청마다 새 DbContext를 쓰는 실제 서버 동작을
/// 흉내 내기 위해 "수집 1회"마다 SqliteTestDatabase.CreateContext()로 새 DbContext를 만든다.
/// </summary>
public sealed class InventoryIngestionServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    // ----- 정상 케이스: 최초 수집 -----

    [Fact]
    public async Task IngestAsync_NewDeviceFirstReport_AllSoftwareCountedAsInstalled()
    {
        var report = TestData.Report(software:
        [
            TestData.Software("App One"),
            TestData.Software("App Two"),
        ]);

        var result = await IngestAsync(report);

        result.InstalledCount.Should().Be(2);
        result.UnchangedCount.Should().Be(0);
        result.UninstalledCount.Should().Be(0);
    }

    [Fact]
    public async Task IngestAsync_NewDeviceFirstReport_PersistsDeviceAndSnapshotsAndInstalledEvents()
    {
        var device = TestData.Device(deviceId: "DEV-NEW", computerName: "PC-NEW");
        var report = TestData.Report(device, TestData.Software("App One", displayVersion: "1.0.0"));

        await IngestAsync(report);

        using var verify = _database.CreateContext();
        var savedDevice = await verify.Devices.Include(d => d.Disks)
            .FirstAsync(d => d.DeviceId == "DEV-NEW");
        savedDevice.ComputerName.Should().Be("PC-NEW");
        savedDevice.Manufacturer.Should().Be(device.Manufacturer);
        savedDevice.Disks.Should().ContainSingle(d => d.Model == "Samsung SSD 970 EVO 500GB");

        var snapshot = await verify.InstalledSoftwareSnapshots
            .SingleAsync(s => s.DeviceId == "DEV-NEW" && s.DisplayName == "App One");
        snapshot.DisplayVersion.Should().Be("1.0.0");
        snapshot.FirstSeenUtc.Should().Be(snapshot.LastSeenUtc);

        var changeEvent = await verify.SoftwareChangeEvents
            .SingleAsync(c => c.DeviceId == "DEV-NEW" && c.DisplayName == "App One");
        changeEvent.ChangeType.Should().Be(SoftwareChangeType.Installed);
    }

    // ----- 경계 케이스: 설치된 SW가 없는 디바이스 -----

    [Fact]
    public async Task IngestAsync_EmptyInstalledSoftwareList_RecordsDeviceOnlyWithZeroCounts()
    {
        var report = TestData.Report(software: []);

        var result = await IngestAsync(report);

        result.Should().Be(new IngestionResult(0, 0, 0));

        using var verify = _database.CreateContext();
        (await verify.Devices.CountAsync()).Should().Be(1);
        (await verify.InstalledSoftwareSnapshots.CountAsync()).Should().Be(0);
    }

    // ----- 같은 리포트를 반복 전송 -----

    [Fact]
    public async Task IngestAsync_SameReportSentTwice_SecondCallReportsAllUnchanged()
    {
        var report = TestData.Report(software:
        [
            TestData.Software("App One"),
            TestData.Software("App Two"),
        ]);
        await IngestAsync(report);

        var secondResult = await IngestAsync(report);

        secondResult.Should().Be(new IngestionResult(0, 0, 2));
        using var verify = _database.CreateContext();
        (await verify.InstalledSoftwareSnapshots.CountAsync(s => s.DeviceId == report.Device.DeviceId))
            .Should().Be(2);
    }

    [Fact]
    public async Task IngestAsync_SameDeviceIngestedTwice_DoesNotViolateUniqueIndexOnDeviceIdAndDisplayName()
    {
        // (DeviceId, DisplayName) 유니크 인덱스가 걸려 있으므로, 이미 존재하는 스냅샷을
        // 다시 보낼 때 Add가 아니라 업데이트 경로를 타는지가 중요하다 — 안 그러면
        // SaveChangesAsync에서 유니크 제약 위반 예외가 난다.
        var report = TestData.Report(software: [TestData.Software("App One")]);
        await IngestAsync(report);

        var act = async () => await IngestAsync(report);

        await act.Should().NotThrowAsync();
    }

    // ----- SW 삭제 -----

    [Fact]
    public async Task IngestAsync_SoftwareRemovedInSecondReport_CountsAsUninstalledAndRemovesSnapshotAndRecordsEvent()
    {
        var firstReport = TestData.Report(software:
        [
            TestData.Software("App One"),
            TestData.Software("App Two", displayVersion: "2.5.0"),
        ]);
        await IngestAsync(firstReport);
        var secondReport = TestData.Report(software: [TestData.Software("App One")]);

        var result = await IngestAsync(secondReport);

        result.Should().Be(new IngestionResult(0, 1, 1));

        using var verify = _database.CreateContext();
        (await verify.InstalledSoftwareSnapshots
                .AnyAsync(s => s.DeviceId == firstReport.Device.DeviceId && s.DisplayName == "App Two"))
            .Should().BeFalse();

        var uninstallEvent = await verify.SoftwareChangeEvents
            .SingleAsync(c => c.DeviceId == firstReport.Device.DeviceId
                && c.DisplayName == "App Two"
                && c.ChangeType == SoftwareChangeType.Uninstalled);
        uninstallEvent.DisplayVersion.Should().Be("2.5.0");
    }

    // ----- 버전만 바뀐 경우 -----

    [Fact]
    public async Task IngestAsync_VersionOnlyChanged_CountsAsUnchangedAndUpdatesSnapshotVersionWithoutHistoryEvent()
    {
        var firstReport = TestData.Report(software: [TestData.Software("App One", displayVersion: "1.0.0")]);
        await IngestAsync(firstReport);
        var secondReport = TestData.Report(
            TestData.Device(),
            TestData.Software("App One", displayVersion: "2.0.0"));

        var result = await IngestAsync(secondReport);

        result.Should().Be(new IngestionResult(0, 0, 1));

        using var verify = _database.CreateContext();
        var snapshot = await verify.InstalledSoftwareSnapshots
            .SingleAsync(s => s.DeviceId == firstReport.Device.DeviceId && s.DisplayName == "App One");
        snapshot.DisplayVersion.Should().Be("2.0.0");

        // 버전 변경은 이력(SoftwareChangeEvent)을 남기지 않는다 — 최초 설치 이벤트 1건만 있어야 한다.
        (await verify.SoftwareChangeEvents.CountAsync(c => c.DeviceId == firstReport.Device.DeviceId))
            .Should().Be(1);
    }

    // ----- 이상한 버전 문자열은 그대로 통과 -----

    [Theory]
    [InlineData("")]
    [InlineData("1.0.0.0 (x64)")]
    [InlineData(null)]
    public async Task IngestAsync_UnusualDisplayVersionString_IsPassedThroughUnchanged(string? version)
    {
        var report = TestData.Report(software: [TestData.Software("Weird Version App", displayVersion: version)]);

        await IngestAsync(report);

        using var verify = _database.CreateContext();
        var snapshot = await verify.InstalledSoftwareSnapshots
            .SingleAsync(s => s.DeviceId == report.Device.DeviceId && s.DisplayName == "Weird Version App");
        snapshot.DisplayVersion.Should().Be(version);
    }

    // ----- 디바이스 하드웨어 정보 갱신 -----

    [Fact]
    public async Task IngestAsync_DeviceHardwareChangedOnSecondReport_OverwritesDeviceFields()
    {
        var firstDevice = TestData.Device(manufacturer: "Dell Inc.", model: "OptiPlex 7090");
        await IngestAsync(TestData.Report(firstDevice));
        var secondDevice = TestData.Device(manufacturer: "HP", model: "EliteDesk 800");

        await IngestAsync(TestData.Report(secondDevice));

        using var verify = _database.CreateContext();
        var savedDevice = await verify.Devices.SingleAsync(d => d.DeviceId == firstDevice.DeviceId);
        savedDevice.Manufacturer.Should().Be("HP");
        savedDevice.Model.Should().Be("EliteDesk 800");
        (await verify.Devices.CountAsync()).Should().Be(1);
    }

    // ----- 디스크 구성 변경 -----

    [Fact]
    public async Task IngestAsync_DiskConfigurationChangedOnSecondReport_ReplacesDeviceDisks()
    {
        var firstDevice = TestData.Device(disks: [new DiskInfo("Old SSD", 256_000_000_000)]);
        await IngestAsync(TestData.Report(firstDevice));
        var secondDevice = TestData.Device(disks:
        [
            new DiskInfo("New NVMe", 1_000_000_000_000),
            new DiskInfo("New HDD", 2_000_000_000_000),
        ]);

        await IngestAsync(TestData.Report(secondDevice));

        using var verify = _database.CreateContext();
        var disks = await verify.DeviceDisks
            .Where(d => d.DeviceId == firstDevice.DeviceId)
            .ToListAsync();
        disks.Select(d => d.Model).Should().BeEquivalentTo("New NVMe", "New HDD");
    }

    // ----- BIOS 시리얼이 OEM 더미값인 경우 -----

    [Fact]
    public async Task IngestAsync_SerialNumberIsDummyOemPlaceholder_IsStoredAsIsWithoutModification()
    {
        // DeviceId 계산(더미 시리얼 -> MachineGuid로 대체)은 Agent의 책임이다(NetSamLite.Agent.Tests에서 검증).
        // 서버는 Agent가 이미 계산해서 보낸 DeviceId/SerialNumber를 그대로 믿고 저장만 한다.
        var device = TestData.Device(serialNumber: "To be filled by O.E.M.");

        await IngestAsync(TestData.Report(device));

        using var verify = _database.CreateContext();
        var savedDevice = await verify.Devices.SingleAsync(d => d.DeviceId == device.DeviceId);
        savedDevice.SerialNumber.Should().Be("To be filled by O.E.M.");
    }

    // ----- 같은 수집 안에 DisplayName이 중복된 경우 -----

    [Fact]
    public async Task IngestAsync_DuplicateDisplayNameWithinSameReport_LastOccurrenceWinsWithoutThrowing()
    {
        // RegistrySoftwareCollector는 32/64비트 Uninstall 키 중복을 제거하지 않으므로
        // 같은 요청 안에 동일 DisplayName이 여러 번 올 수 있다. 이제는 예외 없이 처리되고
        // 마지막에 들어온 값이 최종 상태가 돼야 한다(유니크 인덱스 위반이 나면 안 됨).
        var report = TestData.Report(software:
        [
            TestData.Software("Same App", displayVersion: "1.0 (x64)"),
            TestData.Software("Same App", displayVersion: "1.0 (x86)"),
        ]);

        var result = await IngestAsync(report);

        result.InstalledCount.Should().Be(1);
        using var verify = _database.CreateContext();
        var snapshot = await verify.InstalledSoftwareSnapshots
            .SingleAsync(s => s.DeviceId == report.Device.DeviceId && s.DisplayName == "Same App");
        snapshot.DisplayVersion.Should().Be("1.0 (x86)");
    }

    private async Task<IngestionResult> IngestAsync(InventoryReport report)
    {
        using var context = _database.CreateContext();
        var sut = new InventoryIngestionService(context);
        return await sut.IngestAsync(report, CancellationToken.None);
    }
}
