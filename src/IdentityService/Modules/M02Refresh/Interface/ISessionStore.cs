using IdentityService.Modules.M11Esquema.Persistence.Enums;

namespace IdentityService.Modules.M02Refresh.Interface;

public record NewSession(string RefreshToken, Guid FamilyId, DateTimeOffset ExpiresAt);

public interface ISessionStore
{
    Task<NewSession> CreateFamilyAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<bool> UpdateActiveContextAsync(Guid familyId, ViewMode viewMode, Guid groupId, Guid? activeTenantId, IReadOnlyList<Guid> allowedTenants, OperationalScope activeScope, CancellationToken cancellationToken = default);
    Task<int> RevokeFamilyAsync(Guid familyId, CancellationToken cancellationToken = default);
    Task<int> RevokeByUserAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<int> RevokeByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<int> RevokeByUserAndTenantsAsync(Guid userId, IReadOnlyList<Guid> tenantIds, CancellationToken cancellationToken = default);
}
