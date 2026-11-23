using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NetSamLite.Agent.Realtime;

namespace NetSamLite.Agent.Tests.Realtime;

/// <summary>
/// DeviceTokenStore는 실제 파일 시스템(임시 디렉터리)을 사용한다. PendingReportQueueTests와
/// 같은 패턴 — 테스트별로 고유한 임시 파일을 쓰고 끝나면 삭제한다. 등록 비밀값은 환경변수로만
/// 읽히므로(절대 규칙 4) 테스트 안에서 직접 설정/복원한다.
/// </summary>
public sealed class DeviceTokenStoreTests : IDisposable
{
    private const string EnvVarName = "NETSAMLITE_ENROLLMENT_SECRET";
    private readonly string _tokenFilePath;
    private readonly string? _originalEnvValue;

    public DeviceTokenStoreTests()
    {
        _tokenFilePath = Path.Combine(
            Path.GetTempPath(), "NetSamLiteTests", Guid.NewGuid().ToString("N"), "device-token.dat");
        _originalEnvValue = Environment.GetEnvironmentVariable(EnvVarName);
        Environment.SetEnvironmentVariable(EnvVarName, "test-enrollment-secret");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(EnvVarName, _originalEnvValue);
        var directory = Path.GetDirectoryName(_tokenFilePath);
        if (directory is not null && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class FakeDeviceEnrollmentClient(string? tokenToReturn) : IDeviceEnrollmentClient
    {
        public int CallCount { get; private set; }

        public Task<string?> EnrollAsync(string deviceId, string enrollmentSecret, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(tokenToReturn);
        }
    }

    private DeviceTokenStore CreateSut(IDeviceEnrollmentClient enrollmentClient) =>
        new(
            enrollmentClient,
            Options.Create(new AgentOptions { TokenFilePath = _tokenFilePath }),
            NullLogger<DeviceTokenStore>.Instance);

    [Fact]
    public async Task GetOrEnrollAsync_TokenFileExists_ReturnsStoredTokenWithoutEnrolling()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_tokenFilePath)!);
        await File.WriteAllTextAsync(_tokenFilePath, "existing-token");
        var enrollmentClient = new FakeDeviceEnrollmentClient("should-not-be-used");
        var sut = CreateSut(enrollmentClient);

        var token = await sut.GetOrEnrollAsync("DEV-1", CancellationToken.None);

        token.Should().Be("existing-token");
        enrollmentClient.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task GetOrEnrollAsync_NoTokenFile_EnrollsAndPersistsToken()
    {
        var enrollmentClient = new FakeDeviceEnrollmentClient("new-token");
        var sut = CreateSut(enrollmentClient);

        var firstToken = await sut.GetOrEnrollAsync("DEV-1", CancellationToken.None);
        var secondToken = await sut.GetOrEnrollAsync("DEV-1", CancellationToken.None);

        firstToken.Should().Be("new-token");
        secondToken.Should().Be("new-token");
        enrollmentClient.CallCount.Should().Be(1);
        File.Exists(_tokenFilePath).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetOrEnrollAsync_EmptyOrWhitespaceTokenFile_TreatsAsMissingAndEnrolls(string existingContent)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_tokenFilePath)!);
        await File.WriteAllTextAsync(_tokenFilePath, existingContent);
        var enrollmentClient = new FakeDeviceEnrollmentClient("re-enrolled-token");
        var sut = CreateSut(enrollmentClient);

        var token = await sut.GetOrEnrollAsync("DEV-1", CancellationToken.None);

        token.Should().Be("re-enrolled-token");
        enrollmentClient.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task GetOrEnrollAsync_EnrollmentFails_ThrowsOrReturnsNullWithoutWritingFile()
    {
        var enrollmentClient = new FakeDeviceEnrollmentClient(tokenToReturn: null);
        var sut = CreateSut(enrollmentClient);

        var token = await sut.GetOrEnrollAsync("DEV-1", CancellationToken.None);

        token.Should().BeNull();
        File.Exists(_tokenFilePath).Should().BeFalse();
    }

    [Fact]
    public async Task GetOrEnrollAsync_EnrollmentSecretEnvVarMissing_ReturnsNullWithoutCallingEnrollmentClient()
    {
        Environment.SetEnvironmentVariable(EnvVarName, null);
        var enrollmentClient = new FakeDeviceEnrollmentClient("unused-token");
        var sut = CreateSut(enrollmentClient);

        var token = await sut.GetOrEnrollAsync("DEV-1", CancellationToken.None);

        token.Should().BeNull();
        enrollmentClient.CallCount.Should().Be(0);
    }
}
