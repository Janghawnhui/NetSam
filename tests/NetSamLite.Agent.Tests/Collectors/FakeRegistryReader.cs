using Microsoft.Win32;
using NetSamLite.Agent.Collectors;

namespace NetSamLite.Agent.Tests.Collectors;

/// <summary>
/// 실제 레지스트리 없이 RegistrySoftwareCollector를 테스트하기 위한 가짜 IRegistryReader.
/// (hive, view, path) 조합별로 반환할 값을 미리 설정하고, 어떤 조합으로 몇 번 호출됐는지도 기록한다.
/// 2-인자 SetResult/SetThrows 오버로드는 HKLM64/HKLM32가 항상 쓰는 고정 Uninstall 경로를
/// 기본값으로 쓴다 — 기존 테스트가 그대로 동작하게 하기 위함.
/// </summary>
internal sealed class FakeRegistryReader : IRegistryReader
{
    private const string DefaultUninstallKeyPath =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    private readonly Dictionary<(RegistryHive Hive, RegistryView View, string Path), IReadOnlyList<RawUninstallEntry>> _results = new();
    private readonly Dictionary<(RegistryHive Hive, RegistryView View, string Path), Exception> _throws = new();
    private readonly Dictionary<(RegistryHive Hive, RegistryView View, string Path), IReadOnlyList<string>> _subKeyNames = new();
    private readonly Dictionary<(RegistryHive Hive, RegistryView View, string Path), Exception> _subKeyNameThrows = new();

    public List<(RegistryHive Hive, RegistryView View, string UninstallKeyPath)> Calls { get; } = [];
    public List<(RegistryHive Hive, RegistryView View, string Path)> SubKeyNameCalls { get; } = [];

    public void SetResult(RegistryHive hive, RegistryView view, IReadOnlyList<RawUninstallEntry> entries) =>
        SetResult(hive, view, DefaultUninstallKeyPath, entries);

    public void SetResult(RegistryHive hive, RegistryView view, string path, IReadOnlyList<RawUninstallEntry> entries)
    {
        _results[(hive, view, path)] = entries;
    }

    public void SetThrows(RegistryHive hive, RegistryView view, Exception exception) =>
        SetThrows(hive, view, DefaultUninstallKeyPath, exception);

    public void SetThrows(RegistryHive hive, RegistryView view, string path, Exception exception)
    {
        _throws[(hive, view, path)] = exception;
    }

    public void SetSubKeyNames(RegistryHive hive, RegistryView view, string path, IReadOnlyList<string> names)
    {
        _subKeyNames[(hive, view, path)] = names;
    }

    public void SetSubKeyNamesThrows(RegistryHive hive, RegistryView view, string path, Exception exception)
    {
        _subKeyNameThrows[(hive, view, path)] = exception;
    }

    public IReadOnlyList<RawUninstallEntry> ReadUninstallEntries(
        RegistryHive hive, RegistryView view, string uninstallKeyPath)
    {
        Calls.Add((hive, view, uninstallKeyPath));
        if (_throws.TryGetValue((hive, view, uninstallKeyPath), out var exception))
        {
            throw exception;
        }

        return _results.TryGetValue((hive, view, uninstallKeyPath), out var entries) ? entries : [];
    }

    public IReadOnlyList<string> GetSubKeyNames(RegistryHive hive, RegistryView view, string path)
    {
        SubKeyNameCalls.Add((hive, view, path));
        if (_subKeyNameThrows.TryGetValue((hive, view, path), out var exception))
        {
            throw exception;
        }

        return _subKeyNames.TryGetValue((hive, view, path), out var names) ? names : [];
    }
}
