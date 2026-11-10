using System.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace NetSamLite.Agent.Collectors;

/// <summary>
/// HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid를 읽기 전용으로 조회한다.
/// 이 키는 WOW6432Node로 분리되지 않으므로 64비트 뷰 하나만 읽으면 된다.
/// </summary>
internal sealed class Win32MachineGuidReader : IMachineGuidReader
{
    private const string CryptographyKeyPath = @"SOFTWARE\Microsoft\Cryptography";

    private readonly ILogger<Win32MachineGuidReader> _logger;

    public Win32MachineGuidReader(ILogger<Win32MachineGuidReader> logger)
    {
        _logger = logger;
    }

    public string? ReadMachineGuid()
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var cryptographyKey = baseKey.OpenSubKey(CryptographyKeyPath, writable: false);
            return cryptographyKey?.GetValue("MachineGuid") as string;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            _logger.LogWarning(ex, "MachineGuid를 읽을 수 없어 건너뜀");
            return null;
        }
    }
}
