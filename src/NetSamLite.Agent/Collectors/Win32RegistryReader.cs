using System.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace NetSamLite.Agent.Collectors;

/// <summary>
/// 읽기 전용(OpenSubKey(..., writable: false))으로만 연다 — 쓰기/삭제 금지(절대 규칙 2).
/// 키 하나가 손상되거나 권한으로 막혀 있어도 그 키만 건너뛰고 나머지 소스는 계속 수집한다.
/// </summary>
internal sealed class Win32RegistryReader : IRegistryReader
{
    private readonly ILogger<Win32RegistryReader> _logger;

    public Win32RegistryReader(ILogger<Win32RegistryReader> logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<RawUninstallEntry> ReadUninstallEntries(
        RegistryHive hive, RegistryView view, string uninstallKeyPath)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var uninstallKey = baseKey.OpenSubKey(uninstallKeyPath, writable: false);
            if (uninstallKey is null)
            {
                return [];
            }

            var entries = new List<RawUninstallEntry>();
            foreach (var subKeyName in uninstallKey.GetSubKeyNames())
            {
                var entry = TryReadEntry(uninstallKey, subKeyName);
                if (entry is not null)
                {
                    entries.Add(entry);
                }
            }

            return entries;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            // 이 hive/view 전체를 열 수 없는 경우(정책으로 막힘 등) — 이 소스만 건너뛰고
            // 나머지 두 소스는 RegistrySoftwareCollector에서 그대로 수집된다.
            _logger.LogWarning(ex,
                "레지스트리 Uninstall 키를 열 수 없어 건너뜀: {Hive} {View} {Path}",
                hive, view, uninstallKeyPath);
            return [];
        }
    }

    public IReadOnlyList<string> GetSubKeyNames(RegistryHive hive, RegistryView view, string path)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var key = string.IsNullOrEmpty(path) ? baseKey : baseKey.OpenSubKey(path, writable: false);
            return key?.GetSubKeyNames() ?? [];
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            _logger.LogWarning(ex, "하위 키 목록을 읽을 수 없어 건너뜀: {Hive} {View} {Path}", hive, view, path);
            return [];
        }
    }

    private RawUninstallEntry? TryReadEntry(RegistryKey uninstallKey, string subKeyName)
    {
        try
        {
            using var subKey = uninstallKey.OpenSubKey(subKeyName, writable: false);
            if (subKey is null)
            {
                return null;
            }

            return new RawUninstallEntry(
                DisplayName: subKey.GetValue("DisplayName") as string,
                DisplayVersion: subKey.GetValue("DisplayVersion") as string,
                Publisher: subKey.GetValue("Publisher") as string,
                InstallDate: subKey.GetValue("InstallDate") as string,
                InstallLocation: subKey.GetValue("InstallLocation") as string,
                SystemComponent: subKey.GetValue("SystemComponent") as int?,
                ParentKeyName: subKey.GetValue("ParentKeyName") as string);
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            // 하위 키 하나가 손상/권한 문제여도 나머지 하위 키 수집은 계속한다.
            _logger.LogWarning(ex, "Uninstall 하위 키를 읽을 수 없어 건너뜀: {SubKeyName}", subKeyName);
            return null;
        }
    }
}
