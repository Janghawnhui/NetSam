using FluentAssertions;
using Microsoft.Win32;
using NetSamLite.Agent.Collectors;

namespace NetSamLite.Agent.Tests.Collectors;

/// <summary>
/// RegistrySoftwareCollector는 레지스트리 I/O를 IRegistryReader 뒤에 두고 있으므로
/// 실제 레지스트리 대신 FakeRegistryReader를 주입해서 병합·필터링 로직만 검증한다.
/// </summary>
public class RegistrySoftwareCollectorTests
{
    private const string ExpectedUninstallKeyPath =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    // ----- 정상 케이스 -----

    [Fact]
    public void Collect_NormalEntry_MapsAllFieldsToInstalledSoftware()
    {
        var reader = new FakeRegistryReader();
        reader.SetResult(RegistryHive.LocalMachine, RegistryView.Registry64,
        [
            new RawUninstallEntry(
                DisplayName: "Contoso App",
                DisplayVersion: "1.2.3",
                Publisher: "Contoso",
                InstallDate: "20250101",
                InstallLocation: @"C:\Program Files\Contoso",
                SystemComponent: null,
                ParentKeyName: null)
        ]);
        var sut = new RegistrySoftwareCollector(reader);

        var result = sut.Collect();

        result.Should().ContainSingle();
        result[0].DisplayName.Should().Be("Contoso App");
        result[0].DisplayVersion.Should().Be("1.2.3");
        result[0].Publisher.Should().Be("Contoso");
        result[0].InstallDate.Should().Be("20250101");
        result[0].InstallLocation.Should().Be(@"C:\Program Files\Contoso");
    }

    [Fact]
    public void Collect_NoEntriesFromAnySource_ReturnsEmptyList()
    {
        var reader = new FakeRegistryReader();
        var sut = new RegistrySoftwareCollector(reader);

        var result = sut.Collect();

        result.Should().BeEmpty();
    }

    [Fact]
    public void Collect_Always_ReadsHklm64AndHklm32AndEnumeratesLoadedUserHives()
    {
        // Phase 6: HKCU 단일 읽기 대신 HKEY_USERS를 열거해서 로드돼 있는 사용자 SID마다
        // Uninstall 키를 읽는다. 사용자 하이브도 HKLM과 동일하게 WOW64 리다이렉션 대상이라
        // 64/32비트 뷰를 둘 다 읽어야 한다. HKEY_USERS 루트 조회(GetSubKeyNames)도 한 번
        // 일어나야 한다(그건 64비트 뷰 한 번으로 충분 — 뷰에 따라 사용자 SID 목록 자체가
        // 달라지지는 않으므로).
        const string sid = "S-1-5-21-111-222-333-1001";
        var reader = new FakeRegistryReader();
        reader.SetSubKeyNames(RegistryHive.Users, RegistryView.Registry64, string.Empty, [sid]);
        var sut = new RegistrySoftwareCollector(reader);

        sut.Collect();

        reader.Calls.Should().BeEquivalentTo(
        [
            (RegistryHive.LocalMachine, RegistryView.Registry64, ExpectedUninstallKeyPath),
            (RegistryHive.LocalMachine, RegistryView.Registry32, ExpectedUninstallKeyPath),
            (RegistryHive.Users, RegistryView.Registry64, $@"{sid}\{ExpectedUninstallKeyPath}"),
            (RegistryHive.Users, RegistryView.Registry32, $@"{sid}\{ExpectedUninstallKeyPath}"),
        ]);
        reader.SubKeyNameCalls.Should().BeEquivalentTo(
        [
            (RegistryHive.Users, RegistryView.Registry64, string.Empty),
        ]);
    }

    // ----- 제외 규칙: DisplayName -----

    [Fact]
    public void Collect_EmptyDisplayName_IsExcluded()
    {
        var reader = new FakeRegistryReader();
        reader.SetResult(RegistryHive.LocalMachine, RegistryView.Registry64,
        [
            new RawUninstallEntry("", "1.0", "Pub", "20250101", null, null, null)
        ]);
        var sut = new RegistrySoftwareCollector(reader);

        var result = sut.Collect();

        result.Should().BeEmpty();
    }

    [Fact]
    public void Collect_NullDisplayName_IsExcluded()
    {
        var reader = new FakeRegistryReader();
        reader.SetResult(RegistryHive.LocalMachine, RegistryView.Registry64,
        [
            new RawUninstallEntry(null, "1.0", "Pub", "20250101", null, null, null)
        ]);
        var sut = new RegistrySoftwareCollector(reader);

        var result = sut.Collect();

        result.Should().BeEmpty();
    }

    [Fact]
    public void Collect_WhitespaceOnlyDisplayName_IsExcluded()
    {
        var reader = new FakeRegistryReader();
        reader.SetResult(RegistryHive.LocalMachine, RegistryView.Registry64,
        [
            new RawUninstallEntry("   ", "1.0", "Pub", "20250101", null, null, null)
        ]);
        var sut = new RegistrySoftwareCollector(reader);

        var result = sut.Collect();

        result.Should().BeEmpty();
    }

    // ----- 제외 규칙: SystemComponent / ParentKeyName -----

    [Fact]
    public void Collect_SystemComponentEntry_IsExcluded()
    {
        var reader = new FakeRegistryReader();
        reader.SetResult(RegistryHive.LocalMachine, RegistryView.Registry64,
        [
            new RawUninstallEntry("Hidden Update", "1.0", "MS", "20250101", null,
                SystemComponent: 1, ParentKeyName: null)
        ]);
        var sut = new RegistrySoftwareCollector(reader);

        var result = sut.Collect();

        result.Should().BeEmpty();
    }

    [Fact]
    public void Collect_SystemComponentZero_IsIncluded()
    {
        // 경계값: SystemComponent==1만 제외 대상이지 0은 포함돼야 한다.
        var reader = new FakeRegistryReader();
        reader.SetResult(RegistryHive.LocalMachine, RegistryView.Registry64,
        [
            new RawUninstallEntry("Normal App", "1.0", "Pub", "20250101", null,
                SystemComponent: 0, ParentKeyName: null)
        ]);
        var sut = new RegistrySoftwareCollector(reader);

        var result = sut.Collect();

        result.Should().ContainSingle(s => s.DisplayName == "Normal App");
    }

    [Fact]
    public void Collect_SystemComponentNull_IsIncluded()
    {
        var reader = new FakeRegistryReader();
        reader.SetResult(RegistryHive.LocalMachine, RegistryView.Registry64,
        [
            new RawUninstallEntry("Normal App", "1.0", "Pub", "20250101", null,
                SystemComponent: null, ParentKeyName: null)
        ]);
        var sut = new RegistrySoftwareCollector(reader);

        var result = sut.Collect();

        result.Should().ContainSingle(s => s.DisplayName == "Normal App");
    }

    [Fact]
    public void Collect_ParentKeyNamePresent_IsExcluded()
    {
        var reader = new FakeRegistryReader();
        reader.SetResult(RegistryHive.LocalMachine, RegistryView.Registry64,
        [
            new RawUninstallEntry("Update for App", "1.0", "MS", "20250101", null,
                SystemComponent: null, ParentKeyName: "{SOME-APP-GUID}")
        ]);
        var sut = new RegistrySoftwareCollector(reader);

        var result = sut.Collect();

        result.Should().BeEmpty();
    }

    [Fact]
    public void Collect_ParentKeyNameEmptyString_IsIncluded()
    {
        // 경계값: ParentKeyName이 null이 아니라 ""인 경우도 "존재하지 않음"으로 취급돼
        // 포함돼야 한다 (IsNullOrEmpty 기준).
        var reader = new FakeRegistryReader();
        reader.SetResult(RegistryHive.LocalMachine, RegistryView.Registry64,
        [
            new RawUninstallEntry("Normal App", "1.0", "Pub", "20250101", null,
                SystemComponent: null, ParentKeyName: "")
        ]);
        var sut = new RegistrySoftwareCollector(reader);

        var result = sut.Collect();

        result.Should().ContainSingle(s => s.DisplayName == "Normal App");
    }

    // ----- 중복(32/64비트 동시 설치) -----

    [Fact]
    public void Collect_SameSoftwareInBothHklm32And64Views_ReturnsBothAsDuplicates()
    {
        // Collect()는 3개 소스를 단순 병합만 하고 중복 제거는 하지 않는다(정규화는 Phase 3).
        // 같은 SW가 64비트/32비트 Uninstall 키에 모두 있으면 현재는 2건으로 반환되는 것이 맞는 동작이다.
        var reader = new FakeRegistryReader();
        var entry = new RawUninstallEntry(
            "Same App", "2.0.0", "Vendor", "20250101", @"C:\Program Files\Same App", null, null);
        reader.SetResult(RegistryHive.LocalMachine, RegistryView.Registry64, [entry]);
        reader.SetResult(RegistryHive.LocalMachine, RegistryView.Registry32, [entry]);
        var sut = new RegistrySoftwareCollector(reader);

        var result = sut.Collect();

        result.Should().HaveCount(2);
        result.Should().AllSatisfy(s => s.DisplayName.Should().Be("Same App"));
    }

    // ----- 이상한 버전 문자열 -----

    [Theory]
    [InlineData("")]
    [InlineData("1.0.0.0 (x64)")]
    [InlineData(null)]
    public void Collect_UnusualDisplayVersionString_IsPassedThroughUnchanged(string? version)
    {
        // Collector는 버전 문자열을 해석/검증하지 않고 그대로 전달한다 (해석은 Phase 3 정규화).
        var reader = new FakeRegistryReader();
        reader.SetResult(RegistryHive.LocalMachine, RegistryView.Registry64,
        [
            new RawUninstallEntry("Weird Version App", version, "Pub", "20250101", null, null, null)
        ]);
        var sut = new RegistrySoftwareCollector(reader);

        var result = sut.Collect();

        result.Should().ContainSingle();
        result[0].DisplayVersion.Should().Be(version);
    }

    // ----- 여러 소스 병합 -----

    [Fact]
    public void Collect_EntriesFromHklmAndUserHive_MergesIntoSingleList()
    {
        const string sid = "S-1-5-21-111-222-333-1001";
        var reader = new FakeRegistryReader();
        reader.SetResult(RegistryHive.LocalMachine, RegistryView.Registry64,
        [
            new RawUninstallEntry("Hklm64 App", "1.0", "Pub", null, null, null, null)
        ]);
        reader.SetResult(RegistryHive.LocalMachine, RegistryView.Registry32,
        [
            new RawUninstallEntry("Hklm32 App", "1.0", "Pub", null, null, null, null)
        ]);
        reader.SetSubKeyNames(RegistryHive.Users, RegistryView.Registry64, string.Empty, [sid]);
        reader.SetResult(RegistryHive.Users, RegistryView.Registry64, $@"{sid}\{ExpectedUninstallKeyPath}",
        [
            new RawUninstallEntry("User App", "1.0", "Pub", null, null, null, null)
        ]);
        var sut = new RegistrySoftwareCollector(reader);

        var result = sut.Collect();

        result.Select(s => s.DisplayName).Should().BeEquivalentTo(
            "Hklm64 App", "Hklm32 App", "User App");
    }

    // ----- 소스 하나가 예외를 던지는 경우 -----

    [Fact]
    public void Collect_OneSourceThrows_RemainingSourcesAreStillCollected()
    {
        const string sid = "S-1-5-21-111-222-333-1001";
        var reader = new FakeRegistryReader();
        reader.SetThrows(RegistryHive.LocalMachine, RegistryView.Registry64,
            new IOException("손상된 하위 키"));
        reader.SetResult(RegistryHive.LocalMachine, RegistryView.Registry32,
        [
            new RawUninstallEntry("Hklm32 App", "1.0", "Pub", null, null, null, null)
        ]);
        reader.SetSubKeyNames(RegistryHive.Users, RegistryView.Registry64, string.Empty, [sid]);
        reader.SetResult(RegistryHive.Users, RegistryView.Registry64, $@"{sid}\{ExpectedUninstallKeyPath}",
        [
            new RawUninstallEntry("User App", "1.0", "Pub", null, null, null, null)
        ]);
        var sut = new RegistrySoftwareCollector(reader);

        var result = sut.Collect();

        result.Select(s => s.DisplayName).Should().BeEquivalentTo("Hklm32 App", "User App");
    }

    [Fact]
    public void Collect_MixOfValidAndExcludedEntriesAcrossSources_OnlyValidEntriesRemain()
    {
        const string sid = "S-1-5-21-111-222-333-1001";
        var reader = new FakeRegistryReader();
        reader.SetResult(RegistryHive.LocalMachine, RegistryView.Registry64,
        [
            new RawUninstallEntry("Valid App", "1.0", "Pub", "20250101", null, null, null),
            new RawUninstallEntry("Hidden Component", "1.0", "MS", null, null, 1, null),
        ]);
        reader.SetResult(RegistryHive.LocalMachine, RegistryView.Registry32,
        [
            new RawUninstallEntry(null, "1.0", "Pub", null, null, null, null),
        ]);
        reader.SetSubKeyNames(RegistryHive.Users, RegistryView.Registry64, string.Empty, [sid]);
        reader.SetResult(RegistryHive.Users, RegistryView.Registry64, $@"{sid}\{ExpectedUninstallKeyPath}",
        [
            new RawUninstallEntry("Update Patch", "1.0", "MS", null, null, null, "{PARENT-GUID}"),
        ]);
        var sut = new RegistrySoftwareCollector(reader);

        var result = sut.Collect();

        result.Should().ContainSingle(s => s.DisplayName == "Valid App");
    }

    // ----- HKEY_USERS 사용자 하이브 enumeration (Phase 6) -----

    [Fact]
    public void Collect_HkeyUsersRootHasNonUserEntries_OnlyRealUserSidsAreRead()
    {
        // .DEFAULT, SYSTEM(S-1-5-18), "<SID>_Classes" 보조 하이브는 UserSidPattern에 안 맞아
        // ReadUninstallEntries 호출 대상에서 제외돼야 한다. 실제 사용자 SID 각각에 대해서는
        // HKLM과 동일하게 64/32비트 뷰를 둘 다 읽어야 한다.
        const string sid1 = "S-1-5-21-111-222-333-1001";
        const string sid2 = "S-1-5-21-999-888-777-5002";
        var reader = new FakeRegistryReader();
        reader.SetSubKeyNames(RegistryHive.Users, RegistryView.Registry64, string.Empty,
        [
            sid1,
            $"{sid1}_Classes",
            sid2,
            ".DEFAULT",
            "S-1-5-18",
        ]);
        var sut = new RegistrySoftwareCollector(reader);

        sut.Collect();

        reader.Calls.Should().BeEquivalentTo(
        [
            (RegistryHive.LocalMachine, RegistryView.Registry64, ExpectedUninstallKeyPath),
            (RegistryHive.LocalMachine, RegistryView.Registry32, ExpectedUninstallKeyPath),
            (RegistryHive.Users, RegistryView.Registry64, $@"{sid1}\{ExpectedUninstallKeyPath}"),
            (RegistryHive.Users, RegistryView.Registry32, $@"{sid1}\{ExpectedUninstallKeyPath}"),
            (RegistryHive.Users, RegistryView.Registry64, $@"{sid2}\{ExpectedUninstallKeyPath}"),
            (RegistryHive.Users, RegistryView.Registry32, $@"{sid2}\{ExpectedUninstallKeyPath}"),
        ]);
    }

    [Fact]
    public void Collect_TwoLoggedInUsersWithDifferentSoftware_BothAreMergedIntoResult()
    {
        const string sid1 = "S-1-5-21-111-222-333-1001";
        const string sid2 = "S-1-5-21-999-888-777-5002";
        var reader = new FakeRegistryReader();
        reader.SetSubKeyNames(RegistryHive.Users, RegistryView.Registry64, string.Empty, [sid1, sid2]);
        reader.SetResult(RegistryHive.Users, RegistryView.Registry64, $@"{sid1}\{ExpectedUninstallKeyPath}",
        [
            new RawUninstallEntry("Alice's App", "1.0", "Pub", null, null, null, null)
        ]);
        reader.SetResult(RegistryHive.Users, RegistryView.Registry64, $@"{sid2}\{ExpectedUninstallKeyPath}",
        [
            new RawUninstallEntry("Bob's App", "1.0", "Pub", null, null, null, null)
        ]);
        var sut = new RegistrySoftwareCollector(reader);

        var result = sut.Collect();

        result.Select(s => s.DisplayName).Should().BeEquivalentTo("Alice's App", "Bob's App");
    }

    [Fact]
    public void Collect_HkeyUsersRootEnumerationThrows_HklmSourcesAreStillCollected()
    {
        var reader = new FakeRegistryReader();
        reader.SetSubKeyNamesThrows(RegistryHive.Users, RegistryView.Registry64, string.Empty,
            new UnauthorizedAccessException("HKEY_USERS 접근 거부"));
        reader.SetResult(RegistryHive.LocalMachine, RegistryView.Registry64,
        [
            new RawUninstallEntry("Hklm64 App", "1.0", "Pub", null, null, null, null)
        ]);
        reader.SetResult(RegistryHive.LocalMachine, RegistryView.Registry32,
        [
            new RawUninstallEntry("Hklm32 App", "1.0", "Pub", null, null, null, null)
        ]);
        var sut = new RegistrySoftwareCollector(reader);

        var result = sut.Collect();

        result.Select(s => s.DisplayName).Should().BeEquivalentTo("Hklm64 App", "Hklm32 App");
    }

    [Fact]
    public void Collect_OneUserHiveThrows_OtherUserAndHklmSourcesAreStillCollected()
    {
        const string sid1 = "S-1-5-21-111-222-333-1001";
        const string sid2 = "S-1-5-21-999-888-777-5002";
        var reader = new FakeRegistryReader();
        reader.SetSubKeyNames(RegistryHive.Users, RegistryView.Registry64, string.Empty, [sid1, sid2]);
        reader.SetThrows(RegistryHive.Users, RegistryView.Registry64, $@"{sid1}\{ExpectedUninstallKeyPath}",
            new IOException("사용자 하이브 손상"));
        reader.SetResult(RegistryHive.Users, RegistryView.Registry64, $@"{sid2}\{ExpectedUninstallKeyPath}",
        [
            new RawUninstallEntry("Bob's App", "1.0", "Pub", null, null, null, null)
        ]);
        reader.SetResult(RegistryHive.LocalMachine, RegistryView.Registry64,
        [
            new RawUninstallEntry("Hklm64 App", "1.0", "Pub", null, null, null, null)
        ]);
        var sut = new RegistrySoftwareCollector(reader);

        var result = sut.Collect();

        result.Select(s => s.DisplayName).Should().BeEquivalentTo("Hklm64 App", "Bob's App");
    }

    [Fact]
    public void Collect_UserHiveHasDifferentSoftwareIn64And32Views_BothAreMergedIntoResult()
    {
        // HKLM과 동일한 이유(WOW64 리다이렉션)로 사용자 하이브도 64/32비트 뷰에 각각 다른
        // SW가 있을 수 있고, 둘 다 결과에 합쳐져야 한다(중복 제거는 Phase 3에 위임).
        const string sid = "S-1-5-21-111-222-333-1001";
        var reader = new FakeRegistryReader();
        reader.SetSubKeyNames(RegistryHive.Users, RegistryView.Registry64, string.Empty, [sid]);
        reader.SetResult(RegistryHive.Users, RegistryView.Registry64, $@"{sid}\{ExpectedUninstallKeyPath}",
        [
            new RawUninstallEntry("User64 App", "1.0", "Pub", null, null, null, null)
        ]);
        reader.SetResult(RegistryHive.Users, RegistryView.Registry32, $@"{sid}\{ExpectedUninstallKeyPath}",
        [
            new RawUninstallEntry("User32 App", "1.0", "Pub", null, null, null, null)
        ]);
        var sut = new RegistrySoftwareCollector(reader);

        var result = sut.Collect();

        result.Select(s => s.DisplayName).Should().BeEquivalentTo("User64 App", "User32 App");
    }

    [Fact]
    public void Collect_OneUserRegistry32ViewThrows_SameUserRegistry64AndOtherSourcesAreStillCollected()
    {
        // 한 사용자의 Registry32 읽기만 예외를 던져도, 같은 사용자의 Registry64, 다른 사용자,
        // HKLM 소스는 영향받지 않고 계속 수집돼야 한다(소스별 격리).
        const string sid1 = "S-1-5-21-111-222-333-1001";
        const string sid2 = "S-1-5-21-999-888-777-5002";
        var reader = new FakeRegistryReader();
        reader.SetSubKeyNames(RegistryHive.Users, RegistryView.Registry64, string.Empty, [sid1, sid2]);
        reader.SetThrows(RegistryHive.Users, RegistryView.Registry32, $@"{sid1}\{ExpectedUninstallKeyPath}",
            new IOException("사용자1 32비트 하이브 손상"));
        reader.SetResult(RegistryHive.Users, RegistryView.Registry64, $@"{sid1}\{ExpectedUninstallKeyPath}",
        [
            new RawUninstallEntry("Alice's 64bit App", "1.0", "Pub", null, null, null, null)
        ]);
        reader.SetResult(RegistryHive.Users, RegistryView.Registry64, $@"{sid2}\{ExpectedUninstallKeyPath}",
        [
            new RawUninstallEntry("Bob's App", "1.0", "Pub", null, null, null, null)
        ]);
        reader.SetResult(RegistryHive.LocalMachine, RegistryView.Registry64,
        [
            new RawUninstallEntry("Hklm64 App", "1.0", "Pub", null, null, null, null)
        ]);
        var sut = new RegistrySoftwareCollector(reader);

        var act = () => sut.Collect();

        var result = act.Should().NotThrow().Subject;
        result.Select(s => s.DisplayName).Should().BeEquivalentTo(
            "Alice's 64bit App", "Bob's App", "Hklm64 App");
    }

    [Fact]
    public void Collect_NoLoggedInUsers_ReturnsOnlyHklmResultsWithoutThrowing()
    {
        // HKEY_USERS 루트 열거 결과가 빈 리스트인 경우(FakeRegistryReader의 기본값) —
        // 사용자별 소스 읽기는 전혀 호출되지 않고 HKLM 결과만 반환돼야 한다.
        var reader = new FakeRegistryReader();
        reader.SetResult(RegistryHive.LocalMachine, RegistryView.Registry64,
        [
            new RawUninstallEntry("Hklm64 App", "1.0", "Pub", null, null, null, null)
        ]);
        var sut = new RegistrySoftwareCollector(reader);

        var act = () => sut.Collect();

        var result = act.Should().NotThrow().Subject;
        result.Select(s => s.DisplayName).Should().BeEquivalentTo("Hklm64 App");
        reader.Calls.Should().NotContain(c => c.Hive == RegistryHive.Users);
    }
}
