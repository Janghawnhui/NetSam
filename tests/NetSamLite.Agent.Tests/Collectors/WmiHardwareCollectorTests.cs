using FluentAssertions;
using NetSamLite.Agent.Collectors;
using NetSamLite.Shared;

namespace NetSamLite.Agent.Tests.Collectors;

/// <summary>
/// WmiHardwareCollector는 실제 WMI/레지스트리 I/O를 IWmiReader/IMachineGuidReader 뒤에 두고 있으므로
/// 실제 환경 대신 FakeWmiReader/FakeMachineGuidReader를 주입해서 조립·DeviceId 계산 로직만 검증한다.
/// </summary>
public class WmiHardwareCollectorTests
{
    // ----- 정상 케이스 -----

    [Fact]
    public void Collect_AllWmiClassesHaveValues_MapsAllFieldsToDeviceInfo()
    {
        var wmi = new FakeWmiReader();
        wmi.SetResult("Win32_ComputerSystem",
        [
            FakeWmiReader.Row(("Manufacturer", "Contoso"), ("Model", "ContosoBook 14"), ("Name", "DESKTOP-ABC123"))
        ]);
        wmi.SetResult("Win32_BIOS",
        [
            FakeWmiReader.Row(("SerialNumber", "SN-12345"))
        ]);
        wmi.SetResult("Win32_Processor",
        [
            FakeWmiReader.Row(("Name", "Contoso CPU X1"), ("NumberOfCores", (uint)8))
        ]);
        wmi.SetResult("Win32_OperatingSystem",
        [
            FakeWmiReader.Row(("Caption", "Microsoft Windows 11 Pro"), ("Version", "10.0.26200"), ("BuildNumber", "26200"))
        ]);
        wmi.SetResult("Win32_PhysicalMemory",
        [
            FakeWmiReader.Row(("Capacity", (ulong)17179869184)) // 16GB
        ]);
        wmi.SetResult("Win32_DiskDrive",
        [
            FakeWmiReader.Row(("Model", "Contoso SSD 512GB"), ("Size", (ulong)512110190592))
        ]);
        var guidReader = new FakeMachineGuidReader(machineGuid: null);
        var sut = new WmiHardwareCollector(wmi, guidReader);

        var result = sut.Collect();

        result.ComputerName.Should().Be("DESKTOP-ABC123");
        result.Manufacturer.Should().Be("Contoso");
        result.Model.Should().Be("ContosoBook 14");
        result.SerialNumber.Should().Be("SN-12345");
        result.DeviceId.Should().Be("SN-12345-DESKTOP-ABC123");
        result.ProcessorName.Should().Be("Contoso CPU X1");
        result.ProcessorCoreCount.Should().Be(8);
        result.OperatingSystemCaption.Should().Be("Microsoft Windows 11 Pro");
        result.OperatingSystemVersion.Should().Be("10.0.26200");
        result.OperatingSystemBuildNumber.Should().Be("26200");
        result.TotalPhysicalMemoryBytes.Should().Be(17179869184);
        result.Disks.Should().ContainSingle();
        result.Disks[0].Model.Should().Be("Contoso SSD 512GB");
        result.Disks[0].SizeBytes.Should().Be(512110190592);
    }

    [Fact]
    public void Collect_Always_QueriesAllSixExpectedWmiClasses()
    {
        var wmi = new FakeWmiReader();
        var sut = new WmiHardwareCollector(wmi, new FakeMachineGuidReader(null));

        sut.Collect();

        wmi.Queries.Should().BeEquivalentTo(
        [
            "Win32_ComputerSystem",
            "Win32_BIOS",
            "Win32_Processor",
            "Win32_OperatingSystem",
            "Win32_PhysicalMemory",
            "Win32_DiskDrive",
        ]);
    }

    // ----- Win32_Processor: 소켓 여러 개 -----

    [Fact]
    public void Collect_MultipleProcessorSockets_SumsNumberOfCoresAndUsesFirstProcessorName()
    {
        var wmi = new FakeWmiReader();
        wmi.SetResult("Win32_Processor",
        [
            FakeWmiReader.Row(("Name", "Socket0 CPU"), ("NumberOfCores", (uint)4)),
            FakeWmiReader.Row(("Name", "Socket1 CPU"), ("NumberOfCores", (uint)6)),
        ]);
        var sut = new WmiHardwareCollector(wmi, new FakeMachineGuidReader(null));

        var result = sut.Collect();

        result.ProcessorCoreCount.Should().Be(10);
        result.ProcessorName.Should().Be("Socket0 CPU");
    }

    [Fact]
    public void Collect_FirstProcessorNameIsNull_UsesFirstNonNullProcessorName()
    {
        var wmi = new FakeWmiReader();
        wmi.SetResult("Win32_Processor",
        [
            FakeWmiReader.Row(("Name", null), ("NumberOfCores", (uint)4)),
            FakeWmiReader.Row(("Name", "Socket1 CPU"), ("NumberOfCores", (uint)6)),
        ]);
        var sut = new WmiHardwareCollector(wmi, new FakeMachineGuidReader(null));

        var result = sut.Collect();

        result.ProcessorName.Should().Be("Socket1 CPU");
        result.ProcessorCoreCount.Should().Be(10);
    }

    // ----- Win32_PhysicalMemory: 여러 모듈 -----

    [Fact]
    public void Collect_MultipleMemoryModules_SumsCapacityIntoTotalPhysicalMemoryBytes()
    {
        var wmi = new FakeWmiReader();
        wmi.SetResult("Win32_PhysicalMemory",
        [
            FakeWmiReader.Row(("Capacity", (ulong)8589934592)),  // 8GB
            FakeWmiReader.Row(("Capacity", (ulong)8589934592)),  // 8GB
        ]);
        var sut = new WmiHardwareCollector(wmi, new FakeMachineGuidReader(null));

        var result = sut.Collect();

        result.TotalPhysicalMemoryBytes.Should().Be(17179869184);
    }

    // ----- Win32_DiskDrive: 여러 디스크 -----

    [Fact]
    public void Collect_MultipleDiskDrives_MapsEachRowToDiskInfo()
    {
        var wmi = new FakeWmiReader();
        wmi.SetResult("Win32_DiskDrive",
        [
            FakeWmiReader.Row(("Model", "Disk A"), ("Size", (ulong)1000)),
            FakeWmiReader.Row(("Model", "Disk B"), ("Size", (ulong)2000)),
        ]);
        var sut = new WmiHardwareCollector(wmi, new FakeMachineGuidReader(null));

        var result = sut.Collect();

        result.Disks.Should().BeEquivalentTo(
        [
            new DiskInfo("Disk A", 1000),
            new DiskInfo("Disk B", 2000),
        ]);
    }

    // ----- DeviceId: 정상 시리얼 -----

    [Fact]
    public void Collect_NormalBiosSerial_DeviceIdIsSerialDashComputerName()
    {
        var wmi = new FakeWmiReader();
        wmi.SetResult("Win32_ComputerSystem", [FakeWmiReader.Row(("Name", "PC-001"))]);
        wmi.SetResult("Win32_BIOS", [FakeWmiReader.Row(("SerialNumber", "ABC-REAL-SERIAL"))]);
        var guidReader = new FakeMachineGuidReader("SOME-MACHINE-GUID");
        var sut = new WmiHardwareCollector(wmi, guidReader);

        var result = sut.Collect();

        result.DeviceId.Should().Be("ABC-REAL-SERIAL-PC-001");
        guidReader.CallCount.Should().Be(0); // 정상 시리얼이면 MachineGuid는 조회하지 않아야 한다.
    }

    // ----- DeviceId: 더미 시리얼 → MachineGuid로 대체 -----

    [Theory]
    [InlineData("To be filled by O.E.M.")]
    [InlineData("TO BE FILLED BY O.E.M.")] // 대소문자 무시
    [InlineData("System Serial Number")]
    [InlineData("Default string")]
    [InlineData("0")]
    [InlineData("")]
    [InlineData("   ")]
    public void Collect_DummyBiosSerial_FallsBackToMachineGuidForDeviceId(string dummySerial)
    {
        var wmi = new FakeWmiReader();
        wmi.SetResult("Win32_ComputerSystem", [FakeWmiReader.Row(("Name", "PC-002"))]);
        wmi.SetResult("Win32_BIOS", [FakeWmiReader.Row(("SerialNumber", dummySerial))]);
        var guidReader = new FakeMachineGuidReader("REAL-MACHINE-GUID");
        var sut = new WmiHardwareCollector(wmi, guidReader);

        var result = sut.Collect();

        result.DeviceId.Should().Be("REAL-MACHINE-GUID-PC-002");
        result.SerialNumber.Should().Be(dummySerial); // 원본 시리얼은 그대로 보존돼야 한다.
        guidReader.CallCount.Should().Be(1);
    }

    [Fact]
    public void Collect_DummyBiosSerialAndMachineGuidNull_FallsBackToOriginalSerial()
    {
        var wmi = new FakeWmiReader();
        wmi.SetResult("Win32_ComputerSystem", [FakeWmiReader.Row(("Name", "PC-003"))]);
        wmi.SetResult("Win32_BIOS", [FakeWmiReader.Row(("SerialNumber", "To be filled by O.E.M."))]);
        var sut = new WmiHardwareCollector(wmi, new FakeMachineGuidReader(machineGuid: null));

        var result = sut.Collect();

        result.DeviceId.Should().Be("To be filled by O.E.M.-PC-003");
    }

    [Fact]
    public void Collect_NoBiosSerialAndMachineGuidNull_DeviceIdFallsBackToComputerNameOnly()
    {
        var wmi = new FakeWmiReader();
        wmi.SetResult("Win32_ComputerSystem", [FakeWmiReader.Row(("Name", "PC-004"))]);
        // Win32_BIOS 결과가 아예 없는 경우 (행 없음)
        var sut = new WmiHardwareCollector(wmi, new FakeMachineGuidReader(machineGuid: null));

        var result = sut.Collect();

        result.DeviceId.Should().Be("-PC-004");
        result.SerialNumber.Should().BeNull();
    }

    // ----- WMI 결과가 비어 있는 경우 (예외 없이 처리) -----

    [Fact]
    public void Collect_AllWmiQueriesReturnNoRows_ReturnsDeviceInfoWithDefaultsAndNoException()
    {
        var wmi = new FakeWmiReader(); // 아무 SetResult도 호출하지 않음 → 모든 조회가 빈 목록
        var sut = new WmiHardwareCollector(wmi, new FakeMachineGuidReader(null));

        var act = () => sut.Collect();

        var result = act.Should().NotThrow().Subject;
        result.Manufacturer.Should().BeNull();
        result.Model.Should().BeNull();
        result.SerialNumber.Should().BeNull();
        result.ProcessorName.Should().BeNull();
        result.ProcessorCoreCount.Should().Be(0);
        result.OperatingSystemCaption.Should().BeNull();
        result.OperatingSystemVersion.Should().BeNull();
        result.OperatingSystemBuildNumber.Should().BeNull();
        result.TotalPhysicalMemoryBytes.Should().Be(0);
        result.Disks.Should().BeEmpty();
        result.ComputerName.Should().Be(Environment.MachineName);
    }

    [Fact]
    public void Collect_ComputerSystemNameMissing_FallsBackToEnvironmentMachineName()
    {
        var wmi = new FakeWmiReader();
        wmi.SetResult("Win32_ComputerSystem", [FakeWmiReader.Row(("Manufacturer", "Contoso"))]);
        var sut = new WmiHardwareCollector(wmi, new FakeMachineGuidReader(null));

        var result = sut.Collect();

        result.ComputerName.Should().Be(Environment.MachineName);
        result.Manufacturer.Should().Be("Contoso");
    }

    // ----- 한 WMI 클래스 조회가 예외를 던지는 경우 -----

    [Fact]
    public void Collect_OneWmiClassThrows_RemainingClassesAreStillCollected()
    {
        var wmi = new FakeWmiReader();
        wmi.SetThrows("Win32_Processor", new InvalidOperationException("WMI 서비스 응답 없음"));
        wmi.SetResult("Win32_ComputerSystem", [FakeWmiReader.Row(("Name", "PC-005"))]);
        wmi.SetResult("Win32_OperatingSystem", [FakeWmiReader.Row(("Caption", "Windows 11"))]);
        var sut = new WmiHardwareCollector(wmi, new FakeMachineGuidReader(null));

        var act = () => sut.Collect();

        var result = act.Should().NotThrow().Subject;
        result.ComputerName.Should().Be("PC-005");
        result.OperatingSystemCaption.Should().Be("Windows 11");
        result.ProcessorName.Should().BeNull();
        result.ProcessorCoreCount.Should().Be(0);
    }

    // ----- Capacity/Size가 숫자가 아닌 문자열(BSTR)로 오는 경우 -----

    [Fact]
    public void Collect_CapacityAndSizeAsNumericStrings_AreParsedCorrectly()
    {
        var wmi = new FakeWmiReader();
        wmi.SetResult("Win32_PhysicalMemory", [FakeWmiReader.Row(("Capacity", "8589934592"))]);
        wmi.SetResult("Win32_DiskDrive", [FakeWmiReader.Row(("Model", "Disk C"), ("Size", "512110190592"))]);
        wmi.SetResult("Win32_Processor", [FakeWmiReader.Row(("NumberOfCores", "6"))]);
        var sut = new WmiHardwareCollector(wmi, new FakeMachineGuidReader(null));

        var result = sut.Collect();

        result.TotalPhysicalMemoryBytes.Should().Be(8589934592);
        result.Disks[0].SizeBytes.Should().Be(512110190592);
        result.ProcessorCoreCount.Should().Be(6);
    }

    [Fact]
    public void Collect_CapacityIsNonNumericValue_FallsBackToZeroWithoutThrowing()
    {
        var wmi = new FakeWmiReader();
        wmi.SetResult("Win32_PhysicalMemory", [FakeWmiReader.Row(("Capacity", "알 수 없음"))]);
        var sut = new WmiHardwareCollector(wmi, new FakeMachineGuidReader(null));

        var act = () => sut.Collect();

        var result = act.Should().NotThrow().Subject;
        result.TotalPhysicalMemoryBytes.Should().Be(0);
    }
}
