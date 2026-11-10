namespace NetSamLite.Agent.Collectors;

/// <summary>
/// 레지스트리 Uninstall 키에서 읽은 가공 전 값. SystemComponent/ParentKeyName은
/// 제외 판단에만 쓰고 Shared.InstalledSoftware로는 넘기지 않는다.
/// </summary>
internal sealed record RawUninstallEntry(
    string? DisplayName,
    string? DisplayVersion,
    string? Publisher,
    string? InstallDate,
    string? InstallLocation,
    int? SystemComponent,
    string? ParentKeyName);
