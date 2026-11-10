using FluentAssertions;
using Microsoft.Extensions.Logging;
using NetSamLite.Server.Licensing;
using NetSamLite.Server.Normalization;
using NetSamLite.Server.Tests.Inventory;
using NetSamLite.Server.Tests.Normalization;

namespace NetSamLite.Server.Tests.Licensing;

/// <summary>
/// BuildReportAsync가 SoftwareNormalizer.Normalize() 중 던지는 예외를 항목 단위로 격리하는지 검증한다.
///
/// 실제 normalization-rules.json의 규칙은 모두 유효한 match.field/match.type만 쓰므로 그 파일로는
/// 예외를 재현할 수 없다. 그래서 여기서는 테스트 전용 NormalizationRuleDocument를 직접 만든다.
///
/// 핵심 트릭: SoftwareNormalizer.IsMatch는 match.field로 조회한 값이 null이면 match.type을
/// 평가하기 전에 false를 반환한다(SoftwareNormalizer.cs의 IsMatch 참고). 그래서
/// match.field="Publisher", match.type="BadType"(존재하지 않는 타입)인 깨진 규칙을 두면:
/// - Publisher가 null인 "정상" 항목 → value가 null이라 타입 평가 전에 false로 빠져 영향 없음.
/// - Publisher가 채워진 "깨진" 항목 → 타입 switch가 평가되어 InvalidOperationException이 던져짐.
/// 덕분에 "같은 호출 안에서 한 항목은 성공하고 다른 항목은 실패"하는 상황을 그대로 재현할 수 있다.
/// </summary>
public sealed class LicenseMatchingServiceNormalizationFailureTests
{
    private const string GoodDisplayName = "Good App";
    private const string BrokenDisplayName = "Broken App";

    private static SoftwareNormalizer CreateNormalizerWithBrokenRule()
    {
        var document = NormalizationTestData.RuleDocument(
            NormalizationTestData.Group(
                "productPattern",
                NormalizationTestData.Rule(
                    "good-app",
                    "DisplayName",
                    "exact",
                    GoodDisplayName,
                    actions:
                    [
                        NormalizationTestData.Action("setCategory", "Software"),
                        NormalizationTestData.Action("setLicensable", "true"),
                    ]),
                NormalizationTestData.Rule("broken-publisher-rule", "Publisher", "BadType", "x")));

        return new SoftwareNormalizer(document);
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
        }
    }

    [Fact]
    public async Task BuildReportAsync_OneItemNormalizationThrows_SkipsOnlyThatItemAndIncrementsFailedCount()
    {
        using var db = new SqliteTestDatabase();
        using (var setup = db.CreateContext())
        {
            setup.InstalledSoftwareSnapshots.AddRange(
                LicensingTestData.Snapshot("DEV-GOOD", GoodDisplayName, publisher: null),
                LicensingTestData.Snapshot("DEV-BROKEN", BrokenDisplayName, publisher: "Some Vendor"));
            await setup.SaveChangesAsync();
        }

        using var context = db.CreateContext();
        var service = new LicenseMatchingService(
            context, CreateNormalizerWithBrokenRule(), new RecordingLogger<LicenseMatchingService>());

        // Normalize()가 던진 예외가 BuildReportAsync 밖으로 새어나가면 이 await 자체가 실패한다.
        var report = await service.BuildReportAsync(CancellationToken.None);

        report.FailedCount.Should().Be(1);
        report.UnlicensedProducts.Should().ContainSingle(u =>
            u.ProductName == GoodDisplayName && u.InstalledCount == 1);
        report.Matched.Should().BeEmpty();
        report.ExcludedFromLicensing.Should().BeEmpty();
        report.UnclassifiedSoftware.Should().NotContain(u => u.ProductName == BrokenDisplayName);
    }

    [Fact]
    public async Task BuildReportAsync_OneItemNormalizationThrows_LogsWarningForTheSkippedItemOnly()
    {
        using var db = new SqliteTestDatabase();
        using (var setup = db.CreateContext())
        {
            setup.InstalledSoftwareSnapshots.AddRange(
                LicensingTestData.Snapshot("DEV-GOOD", GoodDisplayName, publisher: null),
                LicensingTestData.Snapshot("DEV-BROKEN", BrokenDisplayName, publisher: "Some Vendor"));
            await setup.SaveChangesAsync();
        }

        using var context = db.CreateContext();
        var logger = new RecordingLogger<LicenseMatchingService>();
        var service = new LicenseMatchingService(context, CreateNormalizerWithBrokenRule(), logger);

        await service.BuildReportAsync(CancellationToken.None);

        logger.Warnings.Should().ContainSingle(w => w.Contains(BrokenDisplayName));
    }

    [Fact]
    public async Task BuildReportAsync_AllItemsNormalizeSuccessfully_FailedCountIsZero()
    {
        using var db = new SqliteTestDatabase();
        using (var setup = db.CreateContext())
        {
            setup.InstalledSoftwareSnapshots.Add(
                LicensingTestData.Snapshot("DEV-GOOD", GoodDisplayName, publisher: null));
            await setup.SaveChangesAsync();
        }

        using var context = db.CreateContext();
        var service = new LicenseMatchingService(
            context, CreateNormalizerWithBrokenRule(), new RecordingLogger<LicenseMatchingService>());

        var report = await service.BuildReportAsync(CancellationToken.None);

        report.FailedCount.Should().Be(0);
    }
}
