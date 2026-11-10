namespace NetSamLite.Agent.Collectors;

/// <summary>
/// BIOS 시리얼이 더미값일 때 장치 식별자 대체용으로 쓰는 MachineGuid를 읽는다.
/// </summary>
internal interface IMachineGuidReader
{
    string? ReadMachineGuid();
}
