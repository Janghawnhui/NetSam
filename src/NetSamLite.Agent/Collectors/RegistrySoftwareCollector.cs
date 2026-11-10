using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using NetSamLite.Shared;

namespace NetSamLite.Agent.Collectors;

/// <summary>
/// 레지스트리 Uninstall 키를 읽어 설치 SW 목록을 만든다: HKLM 64비트, HKLM 32비트/WOW6432Node,
/// 그리고 HKEY_USERS 아래 현재 로드돼 있는 모든 사용자 하이브(64/32비트 뷰 둘 다 — Phase 6:
/// 서비스가 SYSTEM 계정으로 돌면 단순 HKCU는 SYSTEM 자신의 프로필이 되어버려서 사용자별
/// 설치 SW를 못 본다. HKEY_USERS를 직접 열거하면 콘솔로 직접 실행할 때도(자기 자신의 SID가
/// 항상 로드돼 있음) 동작이 같아서 모드 분기가 필요 없다 — 단, 수집 시점에 로그인해 있는
/// 사용자만 보인다는 한계가 있다).
/// </summary>
public sealed class RegistrySoftwareCollector : ISoftwareCollector
{
    private const string UninstallKeyPath =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    // 실제 사용자 계정 SID만 남긴다 — SYSTEM(S-1-5-18), LOCAL/NETWORK SERVICE(S-1-5-19/20),
    // 서비스 계정(S-1-5-80-...), .DEFAULT, "<SID>_Classes"(COM 등록용 보조 하이브)는 제외.
    private static readonly Regex UserSidPattern = new(
        @"^S-1-5-21-\d+-\d+-\d+-\d+$", RegexOptions.Compiled);

    private readonly IRegistryReader _registryReader;
    private readonly ILogger<RegistrySoftwareCollector> _logger;

    public RegistrySoftwareCollector()
        : this(new Win32RegistryReader(NullLogger<Win32RegistryReader>.Instance),
               NullLogger<RegistrySoftwareCollector>.Instance)
    {
    }

    internal RegistrySoftwareCollector(IRegistryReader registryReader)
        : this(registryReader, NullLogger<RegistrySoftwareCollector>.Instance)
    {
    }

    internal RegistrySoftwareCollector(IRegistryReader registryReader, ILogger<RegistrySoftwareCollector> logger)
    {
        _registryReader = registryReader;
        _logger = logger;
    }

    public IReadOnlyList<InstalledSoftware> Collect()
    {
        var raw = new List<RawUninstallEntry>();

        // HKLM을 64/32 두 뷰로 각각 연다: 뷰를 지정하지 않으면 Agent 프로세스 자신의
        // 비트니스에 해당하는 쪽만 보이기 때문에(WOW64 리다이렉션), 둘 다 명시해야
        // 네이티브 64비트 앱과 WOW6432Node 아래 32비트 앱을 모두 수집할 수 있다.
        // 경로에는 "WOW6432Node"를 직접 넣지 않는다 — Registry32 뷰가 자동으로 리다이렉트한다.
        raw.AddRange(ReadSourceOrEmpty(RegistryHive.LocalMachine, RegistryView.Registry64));
        raw.AddRange(ReadSourceOrEmpty(RegistryHive.LocalMachine, RegistryView.Registry32));

        // 사용자 하이브도 HKLM과 똑같이 WOW64 리다이렉션 대상이라, 64/32비트 뷰를 둘 다 읽어야
        // 32비트 프로세스가 설치한 사용자별 SW까지 놓치지 않는다. 같은 SW가 두 뷰 모두에
        // 나타나는 경우의 중복 제거는 HKLM과 동일하게 여기서 안 하고 Phase 3 정규화에 맡긴다.
        foreach (var sid in GetLoadedUserSidsOrEmpty())
        {
            raw.AddRange(ReadUserSourceOrEmpty(sid, RegistryView.Registry64));
            raw.AddRange(ReadUserSourceOrEmpty(sid, RegistryView.Registry32));
        }

        return raw
            .Where(e => !string.IsNullOrWhiteSpace(e.DisplayName))
            .Where(e => !IsExcluded(e))
            .Select(e => new InstalledSoftware(
                e.DisplayName!, e.DisplayVersion, e.Publisher, e.InstallDate, e.InstallLocation))
            .ToList();
    }

    // IRegistryReader 구현(특히 실제 레지스트리를 쓰는 Win32RegistryReader)은 스스로 I/O 예외를
    // 처리하지만, 그래도 한 소스가 예외를 던지더라도 나머지 두 소스는 계속 수집되도록
    // Collector 레벨에서도 한 번 더 막는다 — 소스 하나가 전체 수집을 중단시키면 안 된다.
    private IReadOnlyList<RawUninstallEntry> ReadSourceOrEmpty(RegistryHive hive, RegistryView view)
    {
        try
        {
            return _registryReader.ReadUninstallEntries(hive, view, UninstallKeyPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Hive} {View} 소스 수집 실패, 건너뜀", hive, view);
            return [];
        }
    }

    private IReadOnlyList<string> GetLoadedUserSidsOrEmpty()
    {
        try
        {
            return _registryReader.GetSubKeyNames(RegistryHive.Users, RegistryView.Registry64, string.Empty)
                .Where(name => UserSidPattern.IsMatch(name))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "HKEY_USERS 사용자 목록 조회 실패, 사용자별 SW는 건너뜀");
            return [];
        }
    }

    private IReadOnlyList<RawUninstallEntry> ReadUserSourceOrEmpty(string sid, RegistryView view)
    {
        try
        {
            return _registryReader.ReadUninstallEntries(
                RegistryHive.Users, view, $@"{sid}\{UninstallKeyPath}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "사용자 {Sid}({View})의 레지스트리 수집 실패, 건너뜀", sid, view);
            return [];
        }
    }

    private static bool IsExcluded(RawUninstallEntry entry) =>
        entry.SystemComponent == 1 || !string.IsNullOrEmpty(entry.ParentKeyName);
}
