using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NetSamLite.Server.Data.Entities;
using NetSamLite.Server.Policy;
using NetSamLite.Server.Tests.Devices;
using NetSamLite.Server.Tests.Inventory;
using NetSamLite.Server.Tests.Licensing;
using NetSamLite.Shared;

namespace NetSamLite.Server.Tests.Policy;

/// <summary>
/// ProhibitedSoftwareAlertService.BuildAlertsAsync 통합 테스트.
/// LicenseMatchingServiceTests와 같은 패턴: 실제 SQLite 엔진 + 실제 normalization-rules.json.
///
/// 실제 규칙 파일에서 Licensable=false로 태그되는 제품(MSVC 재배포 패키지)과,
/// 아무 규칙에도 안 걸려 Unclassified=true가 되는 제품 모두 "분류 여부와 무관하게" 금지 알림에
/// 뜨는지가 이 서비스의 핵심 차별점이다(LicenseMatchingService는 이 둘을 매칭에서 제외한다).
/// </summary>
public sealed class ProhibitedSoftwareAlertServiceTests
{
    private const string Ms365DisplayName = "엔터프라이즈용 Microsoft 365 앱";
    private const string Ms365DisplayNameWithLocale = "엔터프라이즈용 Microsoft 365 앱 - ko-kr";
    private const string Ms365ProductName = "Microsoft 365 Apps";
    private const string MsvcDisplayName = "Microsoft Visual C++ 2015 Redistributable (x64) - 14.0.24215.1";
    private const string UnclassifiedDisplayName = "Totally Random Internal Tool 9000";

    private static async Task<IReadOnlyList<ProhibitedSoftwareAlert>> BuildAlertsAsync(SqliteTestDatabase db)
    {
        using var context = db.CreateContext();
        var service = new ProhibitedSoftwareAlertService(
            context, LicensingTestData.CreateRealNormalizer(), NullLogger<ProhibitedSoftwareAlertService>.Instance);
        return await service.BuildAlertsAsync(CancellationToken.None);
    }

    [Fact]
    public async Task BuildAlertsAsync_NoProhibitedSoftwareRegistered_ReturnsEmptyListWithoutReadingSnapshots()
    {
        using var db = new SqliteTestDatabase();
        using (var setup = db.CreateContext())
        {
            setup.Devices.Add(DeviceTestData.Device(deviceId: "DEV-A", computerName: "PC-A"));
            setup.InstalledSoftwareSnapshots.Add(DeviceTestData.Snapshot("DEV-A", Ms365DisplayName));
            await setup.SaveChangesAsync();
        }

        var alerts = await BuildAlertsAsync(db);

        alerts.Should().BeEmpty();
    }

    [Fact]
    public async Task BuildAlertsAsync_InstalledProductMatchesProhibitedName_ReturnsAlertWithCorrectComputerName()
    {
        using var db = new SqliteTestDatabase();
        using (var setup = db.CreateContext())
        {
            setup.Devices.Add(DeviceTestData.Device(deviceId: "DEV-A", computerName: "ACCOUNTING-PC-01"));
            setup.InstalledSoftwareSnapshots.Add(DeviceTestData.Snapshot("DEV-A", Ms365DisplayName));
            setup.ProhibitedSoftware.Add(new ProhibitedSoftwareEntity
            {
                ProductName = Ms365ProductName,
                ProductNameKey = ProhibitedSoftwareEntity.ComputeKey(Ms365ProductName),
                Reason = "라이선스 정책상 금지",
                AddedAtUtc = DateTime.UtcNow,
            });
            await setup.SaveChangesAsync();
        }

        var alerts = await BuildAlertsAsync(db);

        alerts.Should().ContainSingle(a => a.ProductName == Ms365ProductName && a.Reason == "라이선스 정책상 금지");
        var alert = alerts.Single(a => a.ProductName == Ms365ProductName);
        alert.Devices.Should().ContainSingle(d => d.DeviceId == "DEV-A" && d.ComputerName == "ACCOUNTING-PC-01");
    }

    [Fact]
    public async Task BuildAlertsAsync_SameProductOnMultipleDevices_ListsAllDevicesWithoutDuplicates()
    {
        using var db = new SqliteTestDatabase();
        using (var setup = db.CreateContext())
        {
            setup.Devices.AddRange(
                DeviceTestData.Device(deviceId: "DEV-A", computerName: "PC-A"),
                DeviceTestData.Device(deviceId: "DEV-B", computerName: "PC-B"));
            setup.InstalledSoftwareSnapshots.AddRange(
                // DEV-A에는 로케일 접미사가 다른 raw 항목 2건이 같은 제품으로 정규화된다 —
                // 디바이스는 1번만 집계돼야 한다(중복 제거 검증).
                DeviceTestData.Snapshot("DEV-A", Ms365DisplayName),
                DeviceTestData.Snapshot("DEV-A", Ms365DisplayNameWithLocale),
                DeviceTestData.Snapshot("DEV-B", Ms365DisplayName));
            setup.ProhibitedSoftware.Add(new ProhibitedSoftwareEntity
            {
                ProductName = Ms365ProductName,
                ProductNameKey = ProhibitedSoftwareEntity.ComputeKey(Ms365ProductName),
                Reason = "금지",
                AddedAtUtc = DateTime.UtcNow,
            });
            await setup.SaveChangesAsync();
        }

        var alerts = await BuildAlertsAsync(db);

        var alert = alerts.Should().ContainSingle(a => a.ProductName == Ms365ProductName).Subject;
        alert.Devices.Should().HaveCount(2);
        alert.Devices.Select(d => d.DeviceId).Should().BeEquivalentTo(["DEV-A", "DEV-B"]);
    }

    [Fact]
    public async Task BuildAlertsAsync_LicensableFalseProductRegisteredAsProhibited_StillAppearsInAlerts()
    {
        var normalizer = LicensingTestData.CreateRealNormalizer();
        var normalized = normalizer.Normalize(new InstalledSoftware(MsvcDisplayName, "14.0.24215.1", null, null, null));
        normalized!.Licensable.Should().BeFalse("사전 조건: MSVC 재배포 패키지는 Licensable=false로 분류된다");

        using var db = new SqliteTestDatabase();
        using (var setup = db.CreateContext())
        {
            setup.Devices.Add(DeviceTestData.Device(deviceId: "DEV-A", computerName: "PC-A"));
            setup.InstalledSoftwareSnapshots.Add(DeviceTestData.Snapshot("DEV-A", MsvcDisplayName));
            setup.ProhibitedSoftware.Add(new ProhibitedSoftwareEntity
            {
                ProductName = normalized.ProductName,
                ProductNameKey = ProhibitedSoftwareEntity.ComputeKey(normalized.ProductName),
                Reason = "구버전 런타임 금지",
                AddedAtUtc = DateTime.UtcNow,
            });
            await setup.SaveChangesAsync();
        }

        var alerts = await BuildAlertsAsync(db);

        alerts.Should().ContainSingle(a => a.ProductName == normalized.ProductName);
    }

    [Fact]
    public async Task BuildAlertsAsync_UnclassifiedProductRegisteredAsProhibited_StillAppearsInAlerts()
    {
        var normalizer = LicensingTestData.CreateRealNormalizer();
        var normalized = normalizer.Normalize(new InstalledSoftware(UnclassifiedDisplayName, "1.0", null, null, null));
        normalized!.Unclassified.Should().BeTrue("사전 조건: 아무 규칙에도 안 걸려 Unclassified=true가 되는 제품이어야 한다");

        using var db = new SqliteTestDatabase();
        using (var setup = db.CreateContext())
        {
            setup.Devices.Add(DeviceTestData.Device(deviceId: "DEV-A", computerName: "PC-A"));
            setup.InstalledSoftwareSnapshots.Add(DeviceTestData.Snapshot("DEV-A", UnclassifiedDisplayName));
            setup.ProhibitedSoftware.Add(new ProhibitedSoftwareEntity
            {
                ProductName = normalized.ProductName,
                ProductNameKey = ProhibitedSoftwareEntity.ComputeKey(normalized.ProductName),
                Reason = "미분류 사설 도구 금지",
                AddedAtUtc = DateTime.UtcNow,
            });
            await setup.SaveChangesAsync();
        }

        var alerts = await BuildAlertsAsync(db);

        alerts.Should().ContainSingle(a => a.ProductName == normalized.ProductName);
    }

    [Fact]
    public async Task BuildAlertsAsync_ProhibitedProductWithNoInstalledDevices_DoesNotAppearInAlerts()
    {
        using var db = new SqliteTestDatabase();
        using (var setup = db.CreateContext())
        {
            setup.Devices.Add(DeviceTestData.Device(deviceId: "DEV-A", computerName: "PC-A"));
            // 설치된 SW는 금지 목록과 무관한 제품 하나뿐.
            setup.InstalledSoftwareSnapshots.Add(DeviceTestData.Snapshot("DEV-A", UnclassifiedDisplayName));
            setup.ProhibitedSoftware.Add(new ProhibitedSoftwareEntity
            {
                ProductName = Ms365ProductName,
                ProductNameKey = ProhibitedSoftwareEntity.ComputeKey(Ms365ProductName),
                Reason = "금지",
                AddedAtUtc = DateTime.UtcNow,
            });
            await setup.SaveChangesAsync();
        }

        var alerts = await BuildAlertsAsync(db);

        alerts.Should().BeEmpty();
    }

    [Fact]
    public async Task BuildAlertsAsync_ProhibitedNameDifferentCase_StillMatches()
    {
        using var db = new SqliteTestDatabase();
        using (var setup = db.CreateContext())
        {
            setup.Devices.Add(DeviceTestData.Device(deviceId: "DEV-A", computerName: "PC-A"));
            setup.InstalledSoftwareSnapshots.Add(DeviceTestData.Snapshot("DEV-A", Ms365DisplayName));
            setup.ProhibitedSoftware.Add(new ProhibitedSoftwareEntity
            {
                ProductName = Ms365ProductName.ToUpperInvariant(),
                ProductNameKey = ProhibitedSoftwareEntity.ComputeKey(Ms365ProductName.ToUpperInvariant()),
                Reason = "금지",
                AddedAtUtc = DateTime.UtcNow,
            });
            await setup.SaveChangesAsync();
        }

        var alerts = await BuildAlertsAsync(db);

        alerts.Should().ContainSingle(a => a.ProductName == Ms365ProductName.ToUpperInvariant());
        alerts.Single().Devices.Should().ContainSingle(d => d.DeviceId == "DEV-A");
    }

    // 회귀 테스트: 전에는 ProhibitedSoftwareEntity.ProductName(원본, 정규화 안 됨)을
    // StringComparer.OrdinalIgnoreCase로 비교했다. 관리자가 앞뒤 공백/연속 공백이 있는
    // 이름으로 금지 SW를 등록하면, 정규화 결과(깨끗한 "Microsoft 365 Apps")와 공백 차이로
    // 영원히 안 매칭돼 알림이 절대 안 뜨는 버그였다. 지금은 양쪽 다 ProductNameKey
    // (Trim+공백축약+대문자화)로 비교해서 이 문제가 없어야 한다.
    [Fact]
    public async Task BuildAlertsAsync_ProhibitedNameHasLeadingAndTrailingWhitespace_StillMatches()
    {
        const string ProhibitedNameWithWhitespace = "  Microsoft 365 Apps  ";

        using var db = new SqliteTestDatabase();
        using (var setup = db.CreateContext())
        {
            setup.Devices.Add(DeviceTestData.Device(deviceId: "DEV-A", computerName: "PC-A"));
            setup.InstalledSoftwareSnapshots.Add(DeviceTestData.Snapshot("DEV-A", Ms365DisplayName));
            setup.ProhibitedSoftware.Add(new ProhibitedSoftwareEntity
            {
                ProductName = ProhibitedNameWithWhitespace,
                ProductNameKey = ProhibitedSoftwareEntity.ComputeKey(ProhibitedNameWithWhitespace),
                Reason = "공백 포함 등록 - 회귀 테스트",
                AddedAtUtc = DateTime.UtcNow,
            });
            await setup.SaveChangesAsync();
        }

        var alerts = await BuildAlertsAsync(db);

        var alert = alerts.Should().ContainSingle(a => a.ProductName == ProhibitedNameWithWhitespace).Subject;
        alert.Devices.Should().ContainSingle(d => d.DeviceId == "DEV-A");
    }

    [Fact]
    public async Task BuildAlertsAsync_ProhibitedNameHasCollapsedInternalWhitespace_StillMatches()
    {
        const string ProhibitedNameWithDoubleSpace = "Microsoft  365  Apps";

        using var db = new SqliteTestDatabase();
        using (var setup = db.CreateContext())
        {
            setup.Devices.Add(DeviceTestData.Device(deviceId: "DEV-A", computerName: "PC-A"));
            setup.InstalledSoftwareSnapshots.Add(DeviceTestData.Snapshot("DEV-A", Ms365DisplayName));
            setup.ProhibitedSoftware.Add(new ProhibitedSoftwareEntity
            {
                ProductName = ProhibitedNameWithDoubleSpace,
                ProductNameKey = ProhibitedSoftwareEntity.ComputeKey(ProhibitedNameWithDoubleSpace),
                Reason = "연속 공백 등록 - 회귀 테스트",
                AddedAtUtc = DateTime.UtcNow,
            });
            await setup.SaveChangesAsync();
        }

        var alerts = await BuildAlertsAsync(db);

        var alert = alerts.Should().ContainSingle(a => a.ProductName == ProhibitedNameWithDoubleSpace).Subject;
        alert.Devices.Should().ContainSingle(d => d.DeviceId == "DEV-A");
    }
}
