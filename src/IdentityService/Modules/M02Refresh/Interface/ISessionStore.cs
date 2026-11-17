using IdentityService.Modules.M11Esquema.Persistence.Enums;

namespace IdentityService.Modules.M02Refresh.Interface;

public record NewSession(string RefreshToken, Guid FamilyId, DateTimeOffset ExpiresAt);

public record InitialSingleContext(Guid GroupId, Guid TenantId, OperationalScope Scope);

public interface ISessionStore
{
    // initialContext (QW-01): a família nasce já com contexto SINGLE ativo; null mantém ViewMode.NONE.
    Task<NewSession> CreateFamilyAsync(Guid userId, CancellationToken cancellationToken = default, InitialSingleContext? initialContext = null);
    Task<bool> UpdateActiveContextAsync(Guid familyId, ViewMode viewMode, Guid groupId, Guid? activeTenantId, IReadOnlyList<Guid> allowedTenants, OperationalScope activeScope, CancellationToken cancellationToken = default);
    Task<int> RevokeFamilyAsync(Guid familyId, CancellationToken cancellationToken = default);
    Task<int> RevokeByUserAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<int> RevokeByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<int> RevokeByUserAndTenantsAsync(Guid userId, IReadOnlyList<Guid> tenantIds, CancellationToken cancellationToken = default);
}
