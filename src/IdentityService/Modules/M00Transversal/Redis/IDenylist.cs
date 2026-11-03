namespace IdentityService.Modules.M00Transversal.Redis;

public interface IDenylist
{
    Task RevokeJtiAsync(string jti, long exp, CancellationToken cancellationToken = default);
    Task<bool> IsJtiRevokedAsync(string jti, CancellationToken cancellationToken = default);
    Task<long> BlockUserAsync(Guid userId, CancellationToken cancellationToken = default);
    Task BlockTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task UnblockTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<long?> GetUserRevokedAtAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlySet<Guid>> GetBlockedTenantsAsync(IReadOnlyList<Guid> tenantIds, CancellationToken cancellationToken = default);
}
