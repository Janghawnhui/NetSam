using Microsoft.Win32;

namespace NetSamLite.Agent.Collectors;

/// <summary>
/// 실제 레지스트리 I/O를 추상화한다. RegistrySoftwareCollector의 필터링 로직을
/// 진짜 레지스트리 없이 테스트하기 위한 경계(seam)다.
/// </summary>
internal interface IRegistryReader
{
    IReadOnlyList<RawUninstallEntry> ReadUninstallEntries(
        RegistryHive hive, RegistryView view, string uninstallKeyPath);

    /// <summary>
    /// 지정한 키의 바로 아래 하위 키 이름 목록을 조회만 한다(읽기 전용).
    /// HKEY_USERS 아래 로드된 사용자 SID 목록을 얻는 용도로 쓴다.
    /// </summary>
    IReadOnlyList<string> GetSubKeyNames(RegistryHive hive, RegistryView view, string path);
}
