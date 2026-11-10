using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NetSamLite.Server.Data;
using NetSamLite.Server.Data.Entities;

namespace NetSamLite.Server.Policy;

/// <summary>
/// ProhibitedSoftware CRUD. API 엔드포인트와 금지 SW 알림 화면(Blazor)이 같은 로직을 쓰도록 분리했다.
/// </summary>
public sealed class ProhibitedSoftwareService(NetSamLiteDbContext dbContext)
{
    public static bool IsValid(ProhibitedSoftwareRequest request) =>
        !string.IsNullOrWhiteSpace(request.ProductName) && !string.IsNullOrWhiteSpace(request.Reason);

    public Task<List<ProhibitedSoftwareEntity>> ListAsync(CancellationToken cancellationToken) =>
        dbContext.ProhibitedSoftware.AsNoTracking().ToListAsync(cancellationToken);

    /// <exception cref="DuplicateProhibitedSoftwareException">같은 ProductName이 이미 등록돼 있을 때.</exception>
    public async Task<ProhibitedSoftwareEntity> CreateAsync(
        ProhibitedSoftwareRequest request, CancellationToken cancellationToken)
    {
        // 앱(여기)과 DB(유니크 인덱스)가 똑같은 ComputeKey 규칙으로 중복을 판단한다 —
        // 둘이 다른 기준을 쓰면 아래 DbUpdateException 안전망이 뚫리는 경우가 생긴다.
        var key = ProhibitedSoftwareEntity.ComputeKey(request.ProductName);

        // 저장 전에 먼저 존재 여부를 조회해서 보통은 여기서 바로 409로 끝낸다.
        var alreadyExists = await dbContext.ProhibitedSoftware
            .AsNoTracking()
            .AnyAsync(p => p.ProductNameKey == key, cancellationToken);
        if (alreadyExists)
        {
            throw new DuplicateProhibitedSoftwareException(request.ProductName);
        }

        var entity = new ProhibitedSoftwareEntity
        {
            ProductName = request.ProductName,
            ProductNameKey = key,
            Reason = request.Reason,
            AddedAtUtc = DateTime.UtcNow,
        };
        dbContext.ProhibitedSoftware.Add(entity);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            // 위에서 조회한 시점과 Insert 사이에 다른 요청이 먼저 들어온 경우(동시 요청)를 위한 안전망.
            throw new DuplicateProhibitedSoftwareException(request.ProductName);
        }

        return entity;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        var entity = await dbContext.ProhibitedSoftware.FindAsync([id], cancellationToken);
        if (entity is null)
        {
            return false;
        }

        dbContext.ProhibitedSoftware.Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex) =>
        ex.InnerException is SqliteException { SqliteErrorCode: 19 }; // SQLITE_CONSTRAINT
}
