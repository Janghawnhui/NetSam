using NetSamLite.Agent.Collectors;

namespace NetSamLite.Agent.Tests.Collectors;

/// <summary>
/// 실제 레지스트리 없이 WmiHardwareCollector의 DeviceId 폴백 로직을 테스트하기 위한
/// 가짜 IMachineGuidReader. 고정된 MachineGuid(또는 null)를 반환하고, 호출 횟수를 기록한다.
/// </summary>
internal sealed class FakeMachineGuidReader : IMachineGuidReader
{
    private readonly string? _machineGuid;

    public FakeMachineGuidReader(string? machineGuid)
    {
        _machineGuid = machineGuid;
    }

    public int CallCount { get; private set; }

    public string? ReadMachineGuid()
    {
        CallCount++;
        return _machineGuid;
    }
}
