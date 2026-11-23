using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NetSamLite.Server.Realtime;
using NetSamLite.Server.Tests.Inventory;

namespace NetSamLite.Server.Tests.Realtime;

/// <summary>
/// 토큰 발급(enroll)·검증 로직. 등록 비밀값은 실제로는 user-secrets/환경변수에서 읽지만
/// (절대 규칙 4), 테스트에서는 in-memory 설정으로 주입한다.
/// </summary>
public sealed class DeviceTokenServiceTests
{
    private const string EnrollmentSecret = "correct-enrollment-secret";

    private static DeviceTokenService CreateSut(NetSamLite.Server.Data.NetSamLiteDbContext context) =>
        new(
            context,
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Realtime:EnrollmentSecret"] = EnrollmentSecret })
                .Build(),
            NullLogger<DeviceTokenService>.Instance);

    private static string ComputeHash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    [Fact]
    public async Task IssueAsync_NewDevice_ReturnsPlaintextTokenAndStoresOnlyHash()
    {
        using var db = new SqliteTestDatabase();
        using var context = db.CreateContext();
        var sut = CreateSut(context);

        var token = await sut.IssueAsync("DEV-1", EnrollmentSecret, CancellationToken.None);

        token.Should().NotBeNullOrEmpty();
        using var verifyContext = db.CreateContext();
        var entity = await verifyContext.DeviceTokens.SingleAsync(t => t.DeviceId == "DEV-1");
        entity.TokenHash.Should().NotBe(token);
        entity.TokenHash.Should().Be(ComputeHash(token!));
    }

    [Fact]
    public async Task ValidateAsync_CorrectToken_ReturnsDeviceId()
    {
        using var db = new SqliteTestDatabase();
        using var context = db.CreateContext();
        var sut = CreateSut(context);
        var token = await sut.IssueAsync("DEV-1", EnrollmentSecret, CancellationToken.None);

        var deviceId = await sut.ValidateAsync(token, CancellationToken.None);

        deviceId.Should().Be("DEV-1");
    }

    [Fact]
    public async Task IssueAsync_SameDeviceTwice_ReplacesHashAndOldTokenStopsWorking()
    {
        using var db = new SqliteTestDatabase();
        using var context = db.CreateContext();
        var sut = CreateSut(context);
        var firstToken = await sut.IssueAsync("DEV-1", EnrollmentSecret, CancellationToken.None);

        var secondToken = await sut.IssueAsync("DEV-1", EnrollmentSecret, CancellationToken.None);

        secondToken.Should().NotBe(firstToken);
        (await sut.ValidateAsync(firstToken, CancellationToken.None)).Should().BeNull();
        (await sut.ValidateAsync(secondToken, CancellationToken.None)).Should().Be("DEV-1");
        using var verifyContext = db.CreateContext();
        (await verifyContext.DeviceTokens.CountAsync(t => t.DeviceId == "DEV-1")).Should().Be(1);
    }

    [Fact]
    public async Task IssueAsync_TwoDevices_ProduceDifferentTokensAndHashes()
    {
        using var db = new SqliteTestDatabase();
        using var context = db.CreateContext();
        var sut = CreateSut(context);

        var tokenA = await sut.IssueAsync("DEV-A", EnrollmentSecret, CancellationToken.None);
        var tokenB = await sut.IssueAsync("DEV-B", EnrollmentSecret, CancellationToken.None);

        tokenA.Should().NotBe(tokenB);
        using var verifyContext = db.CreateContext();
        var hashA = (await verifyContext.DeviceTokens.SingleAsync(t => t.DeviceId == "DEV-A")).TokenHash;
        var hashB = (await verifyContext.DeviceTokens.SingleAsync(t => t.DeviceId == "DEV-B")).TokenHash;
        hashA.Should().NotBe(hashB);
    }

    [Fact]
    public async Task ValidateAsync_UnknownOrEmptyOrTamperedToken_ReturnsNull()
    {
        using var db = new SqliteTestDatabase();
        using var context = db.CreateContext();
        var sut = CreateSut(context);
        var token = await sut.IssueAsync("DEV-1", EnrollmentSecret, CancellationToken.None);
        var tampered = token![..^1] + (token[^1] == 'A' ? 'B' : 'A');

        (await sut.ValidateAsync("unknown-token", CancellationToken.None)).Should().BeNull();
        (await sut.ValidateAsync(string.Empty, CancellationToken.None)).Should().BeNull();
        (await sut.ValidateAsync(tampered, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task IssueAsync_WrongEnrollmentSecret_Rejected()
    {
        using var db = new SqliteTestDatabase();
        using var context = db.CreateContext();
        var sut = CreateSut(context);

        var token = await sut.IssueAsync("DEV-1", "wrong-secret", CancellationToken.None);

        token.Should().BeNull();
        using var verifyContext = db.CreateContext();
        (await verifyContext.DeviceTokens.AnyAsync(t => t.DeviceId == "DEV-1")).Should().BeFalse();
    }
}
