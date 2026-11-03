using IdentityService.Modules.M11Esquema.Persistence.Enums;

namespace IdentityService.Modules.M08Acessos.Interface;

public interface IAccessQueries
{
    Task<AccessResult> CheckUserAsync(Guid userId, long? tRef, CancellationToken cancellationToken = default);
    Task<AccessResult> CheckTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<AccessResult> CheckMembershipAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken = default);
    Task<AccessResult<RoleSet>> ResolveRolesAsync(Guid userId, Guid tenantId, OperationalScope scope, CancellationToken cancellationToken = default);
    Task<AccessResult<MacroContext>> ResolveMacroGroupAsync(Guid userId, Guid groupId, IReadOnlyList<Guid> tenantIds, OperationalScope scope, CancellationToken cancellationToken = default);
    Task<AccessResult<IReadOnlyList<AvailableContext>>> ListContextsAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<GroupMembershipKind> GetGroupMembershipAsync(Guid userId, Guid groupId, CancellationToken cancellationToken = default);
    Task<TenantInfo?> ResolveTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
