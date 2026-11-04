using IdentityService.Modules.M04Tenants.Interface;
using IdentityService.Modules.M11Esquema.Persistence;
using IdentityService.Modules.M11Esquema.Persistence.Enums;
using Microsoft.EntityFrameworkCore;

namespace IdentityService.Modules.M04Tenants;

public class TenantStatusWriter : ITenantStatusWriter
{
    private readonly IdentityDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public TenantStatusWriter(IdentityDbContext dbContext, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
    }

    public async Task<TenantStatusChange?> SetStatusAsync(
        Guid tenantId,
        EntityStatus status,
        CancellationToken cancellationToken = default)
    {
        var tenant = await _dbContext.Tenants
            .FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);

        if (tenant == null)
        {
            return null;
        }

        var previousStatus = tenant.Status;

        if (tenant.Status != status)
        {
            tenant.Status = status;
            tenant.UpdatedAt = _timeProvider.GetUtcNow();
        }

        return new TenantStatusChange(
            previousStatus,
            tenant.GroupId,
            tenant.Cnpj,
            tenant.UpdatedAt
        );
    }
}
