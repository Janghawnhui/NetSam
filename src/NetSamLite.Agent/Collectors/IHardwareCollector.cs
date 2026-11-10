using NetSamLite.Shared;

namespace NetSamLite.Agent.Collectors;

public interface IHardwareCollector
{
    DeviceInfo Collect();
}
