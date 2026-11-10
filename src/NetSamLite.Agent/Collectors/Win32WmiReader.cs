using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace NetSamLite.Agent.Collectors;

/// <summary>
/// ManagementObjectSearcher로 조회만 한다(절대 규칙 2). 한 WMI 클래스 조회가 실패해도
/// 예외를 삼키고 빈 목록을 반환해 — Registry 쪽 Win32RegistryReader와 동일하게 —
/// 다른 수집 항목에 영향을 주지 않는다.
/// </summary>
internal sealed class Win32WmiReader : IWmiReader
{
    private readonly ILogger<Win32WmiReader> _logger;

    public Win32WmiReader(ILogger<Win32WmiReader> logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<IReadOnlyDictionary<string, object?>> Query(string wmiClass)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT * FROM {wmiClass}");
            using var results = searcher.Get();

            var rows = new List<IReadOnlyDictionary<string, object?>>();
            foreach (ManagementBaseObject instance in results)
            {
                using (instance)
                {
                    var row = new Dictionary<string, object?>();
                    foreach (var property in instance.Properties)
                    {
                        row[property.Name] = property.Value;
                    }

                    rows.Add(row);
                }
            }

            return rows;
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or COMException)
        {
            _logger.LogWarning(ex, "WMI 클래스 조회 실패, 건너뜀: {WmiClass}", wmiClass);
            return [];
        }
    }
}
