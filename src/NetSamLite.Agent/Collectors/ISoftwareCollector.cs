using NetSamLite.Shared;

namespace NetSamLite.Agent.Collectors;

public interface ISoftwareCollector
{
    IReadOnlyList<InstalledSoftware> Collect();
}
