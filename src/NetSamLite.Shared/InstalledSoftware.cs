namespace NetSamLite.Shared;

/// <summary>
/// 레지스트리 Uninstall 키 한 항목을 정규화 전 상태로 표현한다.
/// SystemComponent/ParentKeyName 같은 제외 판단용 값은 Agent 내부에서만 쓰고 여기 넣지 않는다.
/// </summary>
public sealed record InstalledSoftware(
    string DisplayName,
    string? DisplayVersion,
    string? Publisher,
    // 레지스트리 원본이 yyyyMMdd 외의 형식(빈 값, MM/dd/yyyy 등)으로도 들어와 DateOnly로 강제 변환하면
    // 파싱 예외가 날 수 있어 원본 문자열 그대로 둔다. 정규화 단계(Phase 3)에서 해석한다.
    string? InstallDate,
    string? InstallLocation);
