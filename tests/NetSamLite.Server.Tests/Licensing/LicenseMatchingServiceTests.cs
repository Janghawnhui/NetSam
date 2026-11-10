using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NetSamLite.Server.Data;
using NetSamLite.Server.Licensing;
using NetSamLite.Server.Tests.Inventory;
using NetSamLite.Shared;

namespace NetSamLite.Server.Tests.Licensing;

/// <summary>
/// LicenseMatchingService.BuildReportAsync 통합 테스트.
/// EF InMemory 프로바이더 대신 실제 SQLite 엔진(SqliteTestDatabase)을 쓰고,
/// SoftwareNormalizer도 테스트용 가짜 규칙이 아니라 실제 normalization-rules.json을 그대로 로드해서
/// 쓴다 — "분류/제외/미분류" 판정이 실제 배포 규칙과 어긋나지 않는지까지 같이 검증하기 위해서다.
///
/// 실제 규칙 파일 안에서 "Licensable=true && Unclassified=false"가 되는 유일한 경로는
/// alias.ms365-apps-kr 규칙(엔터프라이즈용 Microsoft 365 앱 → "Microsoft 365 Apps")이다.
/// productPattern 규칙 2개(Chrome 웹앱, MSVC 재배포 패키지)는 전부 Licensable=false로 태그되므로
/// "정상 매칭" 계열 테스트에는 Microsoft 365 Apps alias를, "제외" 계열 테스트에는 MSVC 패턴을 쓴다.
/// </summary>
public sealed class LicenseMatchingServiceTests
{
    private const string Ms365DisplayName = "엔터프라이즈용 Microsoft 365 앱";
    private const string Ms365DisplayNameWithLocale = "엔터프라이즈용 Microsoft 365 앱 - ko-kr";
    private const string Ms365ProductName = "Microsoft 365 Apps";
    private const string MsvcDisplayName = "Microsoft Visual C++ 2015 Redistributable (x64) - 14.0.24215.1";

    private static async Task<LicenseMatchReport> BuildReportAsync(SqliteTestDatabase db)
    {
        using var context = db.CreateContext();
        var service = new LicenseMatchingService(
            context, LicensingTestData.CreateRealNormalizer(), NullLogger<LicenseMatchingService>.Instance);
        return await service.BuildReportAsync(CancellationToken.None);
    }

    [Fact]
    public async Task BuildReportAsync_InstalledCountEqualsLicensedQuantity_ReturnsMatchedWithStatusNormal()
    {
        using var db = new SqliteTestDatabase();
        using (var setup = db.CreateContext())
        {
            setup.InstalledSoftwareSnapshots.Add(LicensingTestData.Snapshot("DEV-A", Ms365DisplayName));
            setup.Licenses.Add(LicensingTestData.License(Ms365ProductName, quantity: 1));
            await setup.SaveChangesAsync();
        }

        var report = await BuildReportAsync(db);

        report.Matched.Should().ContainSingle(m =>
            m.ProductName == Ms365ProductName &&
            m.InstalledCount == 1 &&
            m.LicensedQuantity == 1 &&
            m.Status == "정상" &&
            m.Shortage == 0);
        report.UnlicensedProducts.Should().BeEmpty();
    }

    [Fact]
    public async Task BuildReportAsync_InstalledCountExceedsLicensedQuantity_ReturnsStatusOverInstalled()
    {
        using var db = new SqliteTestDatabase();
        using (var setup = db.CreateContext())
        {
            setup.InstalledSoftwareSnapshots.AddRange(
                LicensingTestData.Snapshot("DEV-A", Ms365DisplayName),
                LicensingTestData.Snapshot("DEV-B", Ms365DisplayName),
                LicensingTestData.Snapshot("DEV-C", Ms365DisplayName));
            setup.Licenses.Add(LicensingTestData.License(Ms365ProductName, quantity: 1));
            await setup.SaveChangesAsync();
        }

        var report = await BuildReportAsync(db);

        report.Matched.Should().ContainSingle(m =>
            m.ProductName == Ms365ProductName &&
            m.InstalledCount == 3 &&
            m.LicensedQuantity == 1 &&
            m.Status == "초과 설치" &&
            m.Shortage == 2);
    }

    [Fact]
    public async Task BuildReportAsync_LicensedQuantityExceedsInstalledCount_ReturnsStatusUnusedLicense()
    {
        using var db = new SqliteTestDatabase();
        using (var setup = db.CreateContext())
        {
            setup.InstalledSoftwareSnapshots.Add(LicensingTestData.Snapshot("DEV-A", Ms365DisplayName));
            setup.Licenses.Add(LicensingTestData.License(Ms365ProductName, quantity: 3));
            await setup.SaveChangesAsync();
        }

        var report = await BuildReportAsync(db);

        report.Matched.Should().ContainSingle(m =>
            m.ProductName == Ms365ProductName &&
            m.InstalledCount == 1 &&
            m.LicensedQuantity == 3 &&
            m.Status == "미사용 라이선스" &&
            m.Shortage == -2);
    }

    [Fact]
    public async Task BuildReportAsync_ClassifiedProductWithoutAnyLicenseRecord_ReturnsUnlicensedNotMatched()
    {
        using var db = new SqliteTestDatabase();
        using (var setup = db.CreateContext())
        {
            setup.InstalledSoftwareSnapshots.Add(LicensingTestData.Snapshot("DEV-A", Ms365DisplayName));
            await setup.SaveChangesAsync();
        }

        var report = await BuildReportAsync(db);

        report.UnlicensedProducts.Should().ContainSingle(u =>
            u.ProductName == Ms365ProductName && u.InstalledCount == 1);
        report.Matched.Should().BeEmpty();
    }

    [Fact]
    public async Task BuildReportAsync_LicensableFalseProduct_ReturnsExcludedFromLicensingOnly()
    {
        using var db = new SqliteTestDatabase();
        using (var setup = db.CreateContext())
        {
            setup.InstalledSoftwareSnapshots.Add(LicensingTestData.Snapshot("DEV-A", MsvcDisplayName));
            await setup.SaveChangesAsync();
        }

        var report = await BuildReportAsync(db);

        report.ExcludedFromLicensing.Should().ContainSingle(e =>
            e.Category == "Runtime(무료)" && e.InstalledCount == 1);
        report.Matched.Should().BeEmpty();
        report.UnlicensedProducts.Should().BeEmpty();
        report.UnclassifiedSoftware.Should().BeEmpty();
    }

    [Fact]
    public async Task BuildReportAsync_LicensableFalseProductWithMatchingLicenseRecord_StillExcludedNotMatched()
    {
        // MSVC 재배포 패키지처럼 Licensable=false로 태그된 제품은, 우연히 같은 이름으로
        // License 레코드가 등록돼 있더라도 Matched/UnlicensedProducts로는 절대 새지 않아야 한다.
        var normalizer = LicensingTestData.CreateRealNormalizer();
        var normalized = normalizer.Normalize(new InstalledSoftware(MsvcDisplayName, "14.0.24215.1", null, null, null));
        normalized!.Licensable.Should().BeFalse("사전 조건: 이 테스트는 MSVC가 실제로 Licensable=false로 분류된다는 가정 위에 있다");

        using var db = new SqliteTestDatabase();
        using (var setup = db.CreateContext())
        {
            setup.InstalledSoftwareSnapshots.Add(LicensingTestData.Snapshot("DEV-A", MsvcDisplayName));
            setup.Licenses.Add(LicensingTestData.License(normalized.ProductName, quantity: 100));
            await setup.SaveChangesAsync();
        }

        var report = await BuildReportAsync(db);

        report.ExcludedFromLicensing.Should().ContainSingle(e => e.InstalledCount == 1);
        report.Matched.Should().BeEmpty();
        report.UnlicensedProducts.Should().BeEmpty();
    }

    [Fact]
    public async Task BuildReportAsync_DisplayNameMatchesNoRule_ReturnsUnclassifiedOnly()
    {
        const string unknownDisplayName = "Totally Random Internal Tool 9000";

        using var db = new SqliteTestDatabase();
        using (var setup = db.CreateContext())
        {
            setup.InstalledSoftwareSnapshots.Add(LicensingTestData.Snapshot("DEV-A", unknownDisplayName));
            await setup.SaveChangesAsync();
        }

        var report = await BuildReportAsync(db);

        report.UnclassifiedSoftware.Should().ContainSingle(u =>
            u.ProductName == unknownDisplayName && u.InstalledCount == 1);
        report.Matched.Should().BeEmpty();
        report.UnlicensedProducts.Should().BeEmpty();
        report.ExcludedFromLicensing.Should().BeEmpty();
    }

    [Fact]
    public async Task BuildReportAsync_SameDeviceTwoRawRowsNormalizeToSameProduct_CountsInstalledOnce()
    {
        // 32/64비트 중복 설치나 로케일 접미사가 붙은 중복 설치처럼, raw DisplayName은 다르지만
        // 정규화 후 같은 ProductName이 되는 경우 — (DeviceId, ProductName) 단위 HashSet 중복 제거를 검증한다.
        using var db = new SqliteTestDatabase();
        using (var setup = db.CreateContext())
        {
            setup.InstalledSoftwareSnapshots.AddRange(
                LicensingTestData.Snapshot("DEV-A", Ms365DisplayName),
                LicensingTestData.Snapshot("DEV-A", Ms365DisplayNameWithLocale));
            await setup.SaveChangesAsync();
        }

        var report = await BuildReportAsync(db);

        report.UnlicensedProducts.Should().ContainSingle(u =>
            u.ProductName == Ms365ProductName && u.InstalledCount == 1);
    }

    [Fact]
    public async Task BuildReportAsync_SameProductOnTwoDifferentDevices_InstalledCountIsTwo()
    {
        using var db = new SqliteTestDatabase();
        using (var setup = db.CreateContext())
        {
            setup.InstalledSoftwareSnapshots.AddRange(
                LicensingTestData.Snapshot("DEV-A", Ms365DisplayName),
                LicensingTestData.Snapshot("DEV-B", Ms365DisplayName));
            setup.Licenses.Add(LicensingTestData.License(Ms365ProductName, quantity: 2));
            await setup.SaveChangesAsync();
        }

        var report = await BuildReportAsync(db);

        report.Matched.Should().ContainSingle(m =>
            m.ProductName == Ms365ProductName && m.InstalledCount == 2 && m.Status == "정상");
    }

    [Fact]
    public async Task BuildReportAsync_LicenseExpiredInThePast_IsExcludedFromLicensedQuantitySum()
    {
        using var db = new SqliteTestDatabase();
        using (var setup = db.CreateContext())
        {
            setup.InstalledSoftwareSnapshots.Add(LicensingTestData.Snapshot("DEV-A", Ms365DisplayName));
            setup.Licenses.Add(LicensingTestData.License(
                Ms365ProductName, quantity: 5, expiresAtUtc: DateTime.UtcNow.AddDays(-1)));
            await setup.SaveChangesAsync();
        }

        var report = await BuildReportAsync(db);

        report.UnlicensedProducts.Should().ContainSingle(u =>
            u.ProductName == Ms365ProductName && u.InstalledCount == 1);
        report.Matched.Should().BeEmpty();
    }

    [Fact]
    public async Task BuildReportAsync_LicenseExpiresInTheFuture_IsIncludedInLicensedQuantitySum()
    {
        using var db = new SqliteTestDatabase();
        using (var setup = db.CreateContext())
        {
            setup.InstalledSoftwareSnapshots.Add(LicensingTestData.Snapshot("DEV-A", Ms365DisplayName));
            setup.Licenses.Add(LicensingTestData.License(
                Ms365ProductName, quantity: 1, expiresAtUtc: DateTime.UtcNow.AddDays(30)));
            await setup.SaveChangesAsync();
        }

        var report = await BuildReportAsync(db);

        report.Matched.Should().ContainSingle(m =>
            m.ProductName == Ms365ProductName && m.LicensedQuantity == 1 && m.Status == "정상");
    }

    [Fact]
    public async Task BuildReportAsync_MultipleLicenseRecordsForSameProduct_SumsQuantity()
    {
        using var db = new SqliteTestDatabase();
        using (var setup = db.CreateContext())
        {
            setup.InstalledSoftwareSnapshots.Add(LicensingTestData.Snapshot("DEV-A", Ms365DisplayName));
            setup.Licenses.AddRange(
                LicensingTestData.License(Ms365ProductName, quantity: 2),
                LicensingTestData.License(Ms365ProductName, quantity: 3));
            await setup.SaveChangesAsync();
        }

        var report = await BuildReportAsync(db);

        report.Matched.Should().ContainSingle(m =>
            m.ProductName == Ms365ProductName &&
            m.LicensedQuantity == 5 &&
            m.Status == "미사용 라이선스" &&
            m.Shortage == -4);
    }

    [Fact]
    public async Task BuildReportAsync_LicenseProductNameDifferentCase_StillMatches()
    {
        using var db = new SqliteTestDatabase();
        using (var setup = db.CreateContext())
        {
            setup.InstalledSoftwareSnapshots.Add(LicensingTestData.Snapshot("DEV-A", Ms365DisplayName));
            setup.Licenses.Add(LicensingTestData.License(Ms365ProductName.ToLowerInvariant(), quantity: 1));
            await setup.SaveChangesAsync();
        }

        var report = await BuildReportAsync(db);

        report.Matched.Should().ContainSingle(m =>
            m.ProductName == Ms365ProductName && m.LicensedQuantity == 1 && m.Status == "정상");
    }
}
