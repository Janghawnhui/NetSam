using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NetSamLite.Shared;

namespace NetSamLite.Agent.Collectors;

/// <summary>
/// WMI로 하드웨어 정보를 모아 DeviceInfo를 만든다. 수집 항목 표의 WMI 섹션에 대응한다.
/// </summary>
public sealed class WmiHardwareCollector : IHardwareCollector
{
    // CLAUDE.md: "시리얼이 비거나 'To be filled by O.E.M.' 같은 더미면 MachineGuid로 대체".
    // 흔히 알려진 더미 플레이스홀더 몇 가지를 함께 처리한다(가상 머신/저가 메인보드에서 자주 보임).
    private static readonly string[] DummySerialNumbers =
    [
        "To be filled by O.E.M.",
        "System Serial Number",
        "Default string",
        "0",
    ];

    private static readonly IReadOnlyDictionary<string, object?> EmptyRow = new Dictionary<string, object?>();

    private readonly IWmiReader _wmiReader;
    private readonly IMachineGuidReader _machineGuidReader;
    private readonly ILogger<WmiHardwareCollector> _logger;

    public WmiHardwareCollector()
        : this(new Win32WmiReader(NullLogger<Win32WmiReader>.Instance),
               new Win32MachineGuidReader(NullLogger<Win32MachineGuidReader>.Instance),
               NullLogger<WmiHardwareCollector>.Instance)
    {
    }

    internal WmiHardwareCollector(IWmiReader wmiReader, IMachineGuidReader machineGuidReader)
        : this(wmiReader, machineGuidReader, NullLogger<WmiHardwareCollector>.Instance)
    {
    }

    internal WmiHardwareCollector(
        IWmiReader wmiReader, IMachineGuidReader machineGuidReader, ILogger<WmiHardwareCollector> logger)
    {
        _wmiReader = wmiReader;
        _machineGuidReader = machineGuidReader;
        _logger = logger;
    }

    public DeviceInfo Collect()
    {
        var computerSystem = QueryOrEmpty("Win32_ComputerSystem").FirstOrDefault() ?? EmptyRow;
        var bios = QueryOrEmpty("Win32_BIOS").FirstOrDefault() ?? EmptyRow;
        var processors = QueryOrEmpty("Win32_Processor");
        var os = QueryOrEmpty("Win32_OperatingSystem").FirstOrDefault() ?? EmptyRow;
        var memoryModules = QueryOrEmpty("Win32_PhysicalMemory");
        var disks = QueryOrEmpty("Win32_DiskDrive");

        var computerName = GetString(computerSystem, "Name") ?? Environment.MachineName;
        var serialNumber = GetString(bios, "SerialNumber");

        return new DeviceInfo(
            DeviceId: ComputeDeviceId(serialNumber, computerName),
            ComputerName: computerName,
            Manufacturer: GetString(computerSystem, "Manufacturer"),
            Model: GetString(computerSystem, "Model"),
            SerialNumber: serialNumber,
            ProcessorName: processors.Select(p => GetString(p, "Name")).FirstOrDefault(name => name is not null),
            // Win32_Processor는 소켓(물리 CPU)마다 한 행이라, 소켓이 여러 개면 합산해야 전체 코어 수가 된다.
            ProcessorCoreCount: processors.Sum(p => (int)GetUInt32(p, "NumberOfCores")),
            OperatingSystemCaption: GetString(os, "Caption"),
            OperatingSystemVersion: GetString(os, "Version"),
            OperatingSystemBuildNumber: GetString(os, "BuildNumber"),
            TotalPhysicalMemoryBytes: memoryModules.Sum(m => (long)GetUInt64(m, "Capacity")),
            Disks: disks
                .Select(d => new DiskInfo(GetString(d, "Model"), (long)GetUInt64(d, "Size")))
                .ToList());
    }

    // WMI의 uint64 속성(Capacity, Size 등)은 COM VARIANT 한계로 실제로는 ulong이 아니라
    // 숫자 문자열(BSTR)로 돌아오는 경우가 흔하다. Convert.ToUInt32/64는 이런 입력에서
    // FormatException/OverflowException을 던질 수 있어 TryParse로 안전하게 처리한다.
    private uint GetUInt32(IReadOnlyDictionary<string, object?> row, string key)
    {
        if (!row.TryGetValue(key, out var value) || value is null)
        {
            return 0;
        }

        if (value is uint u)
        {
            return u;
        }

        if (uint.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out var parsed))
        {
            return parsed;
        }

        _logger.LogWarning("{Key} 값을 uint로 해석할 수 없어 0으로 처리: {Value}", key, value);
        return 0;
    }

    private ulong GetUInt64(IReadOnlyDictionary<string, object?> row, string key)
    {
        if (!row.TryGetValue(key, out var value) || value is null)
        {
            return 0;
        }

        if (value is ulong ul)
        {
            return ul;
        }

        if (ulong.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out var parsed))
        {
            return parsed;
        }

        _logger.LogWarning("{Key} 값을 ulong으로 해석할 수 없어 0으로 처리: {Value}", key, value);
        return 0;
    }

    // IWmiReader 구현(특히 실제 WMI를 쓰는 Win32WmiReader)은 스스로 예외를 처리하지만,
    // Win32WmiReader가 잡지 않는 예외 타입이 생기더라도 WMI 클래스 하나 조회 실패가
    // 나머지 하드웨어 정보 수집까지 막으면 안 되므로 여기서도 한 번 더 막는다.
    private IReadOnlyList<IReadOnlyDictionary<string, object?>> QueryOrEmpty(string wmiClass)
    {
        try
        {
            return _wmiReader.Query(wmiClass);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{WmiClass} 조회 실패, 건너뜀", wmiClass);
            return [];
        }
    }

    private string ComputeDeviceId(string? serialNumber, string computerName)
    {
        // 식별자 = (시리얼 또는 더미면 MachineGuid) + 컴퓨터 이름.
        var identifierSource = IsDummySerial(serialNumber)
            ? _machineGuidReader.ReadMachineGuid() ?? serialNumber ?? string.Empty
            : serialNumber ?? string.Empty;

        return $"{identifierSource}-{computerName}";
    }

    private static bool IsDummySerial(string? serial) =>
        string.IsNullOrWhiteSpace(serial) ||
        DummySerialNumbers.Any(dummy => string.Equals(dummy, serial, StringComparison.OrdinalIgnoreCase));

    private static string? GetString(IReadOnlyDictionary<string, object?> row, string key) =>
        row.TryGetValue(key, out var value) ? value as string : null;
}
