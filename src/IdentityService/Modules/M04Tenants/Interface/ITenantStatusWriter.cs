using IdentityService.Modules.M11Esquema.Persistence.Enums;

namespace IdentityService.Modules.M04Tenants.Interface;

public interface ITenantStatusWriter
{
    Task<TenantStatusChange?> SetStatusAsync(
        Guid tenantId,
        EntityStatus status,
        CancellationToken cancellationToken = default);
}
