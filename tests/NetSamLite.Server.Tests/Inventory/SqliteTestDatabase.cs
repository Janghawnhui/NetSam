using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NetSamLite.Server.Data;

namespace NetSamLite.Server.Tests.Inventory;

/// <summary>
/// EF Core InMemory 프로바이더는 유니크 인덱스/FK/Cascade delete 같은 관계형 제약을
/// 전혀 검증하지 않으므로, InventoryIngestionService처럼 그 제약에 의존하는 로직은
/// 실제 SQLite 엔진으로 검증해야 한다. "Data Source=:memory:" + 연결을 테스트 동안
/// 열어 두는 방식으로 진짜 관계형 DB를 흉내 낸다.
///
/// 연결은 테스트 전체에서 하나만 열어 두고, DbContext는 매번 새로 만들어 반환한다.
/// 실제 서버에서도 요청마다 새 스코프의 DbContext를 쓰므로(Program.cs의 AddDbContext),
/// 같은 DbContext 인스턴스를 재사용해 변경 추적(change tracking) 때문에 우연히
/// 테스트가 통과하는 상황을 피하기 위함이다.
/// </summary>
public sealed class SqliteTestDatabase : IDisposable
{
    private readonly SqliteConnection _connection;

    public SqliteTestDatabase()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        using var context = CreateContext();
        context.Database.Migrate();
    }

    public NetSamLiteDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<NetSamLiteDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new NetSamLiteDbContext(options);
    }

    public void Dispose() => _connection.Dispose();
}
