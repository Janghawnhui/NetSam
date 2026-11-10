using Microsoft.EntityFrameworkCore;
using NetSamLite.Server.Data;
using NetSamLite.Server.Data.Entities;

namespace NetSamLite.Server.Licensing;

/// <summary>
/// License CRUD. API 엔드포인트와 라이선스 현황 화면(Blazor)이 같은 검증/저장 로직을 쓰도록 분리했다.
/// </summary>
public sealed class LicenseService(NetSamLiteDbContext dbContext)
{
    public static bool IsValid(LicenseRequest request) =>
        !string.IsNullOrWhiteSpace(request.ProductName)
        && !string.IsNullOrWhiteSpace(request.LicenseType)
        && request.Quantity >= 0;

    public Task<List<LicenseEntity>> ListAsync(CancellationToken cancellationToken) =>
        dbContext.Licenses.AsNoTracking().ToListAsync(cancellationToken);

    public async Task<LicenseEntity> CreateAsync(LicenseRequest request, CancellationToken cancellationToken)
    {
        var license = new LicenseEntity
        {
            ProductName = request.ProductName,
            LicenseType = request.LicenseType,
            Quantity = request.Quantity,
            ExpiresAtUtc = request.ExpiresAtUtc,
            Notes = request.Notes,
        };
        dbContext.Licenses.Add(license);
        await dbContext.SaveChangesAsync(cancellationToken);
        return license;
    }

    public async Task<LicenseEntity?> UpdateAsync(int id, LicenseRequest request, CancellationToken cancellationToken)
    {
        var license = await dbContext.Licenses.FindAsync([id], cancellationToken);
        if (license is null)
        {
            return null;
        }

        license.ProductName = request.ProductName;
        license.LicenseType = request.LicenseType;
        license.Quantity = request.Quantity;
        license.ExpiresAtUtc = request.ExpiresAtUtc;
        license.Notes = request.Notes;
        await dbContext.SaveChangesAsync(cancellationToken);
        return license;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        var license = await dbContext.Licenses.FindAsync([id], cancellationToken);
        if (license is null)
        {
            return false;
        }

        dbContext.Licenses.Remove(license);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
