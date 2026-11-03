using IdentityService.Modules.M00Transversal.Redis;
using IdentityService.Modules.M08Acessos.Interface;
using IdentityService.Modules.M11Esquema.Persistence;
using IdentityService.Modules.M11Esquema.Persistence.Entities;
using IdentityService.Modules.M11Esquema.Persistence.Enums;
using IdentityService.Modules.M13Isolamento.Interface;
using Microsoft.EntityFrameworkCore;

namespace IdentityService.Modules.M08Acessos;

public class AccessQueries : IAccessQueries
{
    private static readonly OperationalScope[] AllScopes =
    [
        OperationalScope.BUYER,
        OperationalScope.SUPPLIER,
        OperationalScope.CARRIER,
        OperationalScope.TENANT_ADMIN
    ];

    private readonly IdentityDbContext _dbContext;
    private readonly IDenylist _denylist;
    private readonly ITenantIsolation _tenantIsolation;

    public AccessQueries(
        IdentityDbContext dbContext,
        IDenylist denylist,
        ITenantIsolation tenantIsolation)
    {
        _dbContext = dbContext;
        _denylist = denylist;
        _tenantIsolation = tenantIsolation;
    }

    public async Task<AccessResult> CheckUserAsync(Guid userId, long? tRef, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.UserAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user == null || user.Status == UserStatus.SUSPENDED)
        {
            return AccessResult.Failure("ERR-AUTH-004");
        }

        if (tRef.HasValue)
        {
            var revokedAt = await _denylist.GetUserRevokedAtAsync(userId, cancellationToken);
            if (revokedAt.HasValue)
            {
                if (tRef.Value == 0 || tRef.Value <= revokedAt.Value)
                {
                    return AccessResult.Failure("ERR-AUTH-004");
                }
            }
        }

        return AccessResult.Success();
    }

    public async Task<AccessResult> CheckTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var tenant = await _dbContext.Tenants
            .Include(t => t.Group)
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);

        if (tenant == null)
        {
            return AccessResult.Failure("ERR-CTX-001");
        }

        if (tenant.Status == EntityStatus.SUSPENDED || tenant.Group.Status == EntityStatus.SUSPENDED)
        {
            return AccessResult.Failure("ERR-CTX-002");
        }

        var blockedTenants = await _denylist.GetBlockedTenantsAsync([tenantId], cancellationToken);
        if (blockedTenants.Contains(tenantId))
        {
            return AccessResult.Failure("ERR-CTX-002");
        }

        return AccessResult.Success();
    }

    public async Task<AccessResult> CheckMembershipAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken = default)
    {
        var member = await _dbContext.TenantMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.UserId == userId && m.Status == EntityStatus.ACTIVE, cancellationToken);

        if (member == null)
        {
            return AccessResult.Failure("ERR-CTX-001");
        }

        return AccessResult.Success();
    }

    public async Task<AccessResult<RoleSet>> ResolveRolesAsync(
        Guid userId,
        Guid tenantId,
        OperationalScope scope,
        CancellationToken cancellationToken = default)
    {
        var scopeActive = await _dbContext.TenantScopes
            .AsNoTracking()
            .AnyAsync(s => s.TenantId == tenantId && s.Scope == scope && s.IsActive, cancellationToken);

        if (!scopeActive)
        {
            return AccessResult<RoleSet>.Failure("ERR-CTX-003");
        }

        var member = await _dbContext.TenantMembers
            .Include(m => m.MemberRoles)
            .ThenInclude(mr => mr.Role)
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.UserId == userId && m.Status == EntityStatus.ACTIVE, cancellationToken);

        if (member == null)
        {
            return AccessResult<RoleSet>.Failure("ERR-CTX-003");
        }

        List<Role> rolesList;
        if (member.IsOwner)
        {
            rolesList = await _dbContext.Roles
                .AsNoTracking()
                .Where(r => r.Scope == scope)
                .ToListAsync(cancellationToken);
        }
        else
        {
            rolesList = member.MemberRoles
                .Where(mr => mr.Role.Scope == scope)
                .Select(mr => mr.Role)
                .ToList();
        }

        if (rolesList.Count == 0)
        {
            return AccessResult<RoleSet>.Failure("ERR-CTX-003");
        }

        var roles = rolesList.Select(r => r.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
        var permissions = rolesList.SelectMany(r => r.Permissions).Distinct().OrderBy(p => p, StringComparer.Ordinal).ToList();

        return AccessResult<RoleSet>.Success(new RoleSet(roles, permissions));
    }

    public async Task<AccessResult<MacroContext>> ResolveMacroGroupAsync(
        Guid userId,
        Guid groupId,
        IReadOnlyList<Guid> tenantIds,
        OperationalScope scope,
        CancellationToken cancellationToken = default)
    {
        // 1. Cardinality and duplicates check
        if (tenantIds.Count is < 1 or > 50 || tenantIds.Distinct().Count() != tenantIds.Count)
        {
            return AccessResult<MacroContext>.Failure("ERR-CTX-004");
        }

        // 2. Group check
        var group = await _dbContext.EconomicGroups
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == groupId, cancellationToken);

        if (group == null)
        {
            return AccessResult<MacroContext>.Failure("ERR-CTX-004");
        }

        if (group.Status == EntityStatus.SUSPENDED)
        {
            return AccessResult<MacroContext>.Failure("ERR-CTX-002");
        }

        // 3. Per-tenant checks
        var allRolesList = await _dbContext.Roles
            .AsNoTracking()
            .Where(r => r.Scope == scope)
            .ToListAsync(cancellationToken);

        var tenantRoles = new List<HashSet<string>>();
        var tenantPermissions = new List<HashSet<string>>();

        foreach (var tenantId in tenantIds)
        {
            var tenant = await _dbContext.Tenants
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);

            if (tenant == null || tenant.GroupId != groupId)
            {
                return AccessResult<MacroContext>.Failure("ERR-CTX-004");
            }

            var member = await _dbContext.TenantMembers
                .Include(m => m.MemberRoles)
                .ThenInclude(mr => mr.Role)
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.UserId == userId, cancellationToken);

            if (member == null || member.Status != EntityStatus.ACTIVE || !member.CanViewGroup)
            {
                return AccessResult<MacroContext>.Failure("ERR-CTX-004");
            }

            if (tenant.Status == EntityStatus.SUSPENDED)
            {
                return AccessResult<MacroContext>.Failure("ERR-CTX-002");
            }

            var blocked = await _denylist.GetBlockedTenantsAsync([tenantId], cancellationToken);
            if (blocked.Contains(tenantId))
            {
                return AccessResult<MacroContext>.Failure("ERR-CTX-002");
            }

            var scopeActive = await _dbContext.TenantScopes
                .AsNoTracking()
                .AnyAsync(s => s.TenantId == tenantId && s.Scope == scope && s.IsActive, cancellationToken);

            if (!scopeActive)
            {
                return AccessResult<MacroContext>.Failure("ERR-CTX-003");
            }

            List<Role> rolesForTenant;
            if (member.IsOwner)
            {
                rolesForTenant = allRolesList;
            }
            else
            {
                rolesForTenant = member.MemberRoles
                    .Where(mr => mr.Role.Scope == scope)
                    .Select(mr => mr.Role)
                    .ToList();
            }

            if (rolesForTenant.Count == 0)
            {
                return AccessResult<MacroContext>.Failure("ERR-CTX-003");
            }

            tenantRoles.Add(rolesForTenant.Select(r => r.Name).ToHashSet(StringComparer.Ordinal));
            tenantPermissions.Add(rolesForTenant.SelectMany(r => r.Permissions).ToHashSet(StringComparer.Ordinal));
        }

        // 4. Intersections
        var intersectedRoles = new HashSet<string>(tenantRoles[0], StringComparer.Ordinal);
        for (var i = 1; i < tenantRoles.Count; i++)
        {
            intersectedRoles.IntersectWith(tenantRoles[i]);
        }

        var intersectedPermissions = new HashSet<string>(tenantPermissions[0], StringComparer.Ordinal);
        for (var i = 1; i < tenantPermissions.Count; i++)
        {
            intersectedPermissions.IntersectWith(tenantPermissions[i]);
        }

        if (intersectedPermissions.Count == 0)
        {
            return AccessResult<MacroContext>.Failure("ERR-CTX-003");
        }

        // 5. Ordinal sort
        var sortedTenants = tenantIds.OrderBy(g => g).ToList();
        var sortedRoles = intersectedRoles.OrderBy(r => r, StringComparer.Ordinal).ToList();
        var sortedPermissions = intersectedPermissions.OrderBy(p => p, StringComparer.Ordinal).ToList();

        return AccessResult<MacroContext>.Success(new MacroContext(sortedTenants, sortedRoles, sortedPermissions));
    }

    public async Task<AccessResult<IReadOnlyList<AvailableContext>>> ListContextsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var members = await _dbContext.TenantMembers
            .Include(m => m.Tenant)
            .ThenInclude(t => t.Group)
            .Include(m => m.Tenant)
            .ThenInclude(t => t.Scopes)
            .Include(m => m.MemberRoles)
            .ThenInclude(mr => mr.Role)
            .AsNoTracking()
            .Where(m => m.UserId == userId && m.Status == EntityStatus.ACTIVE)
            .ToListAsync(cancellationToken);

        var allRoles = await _dbContext.Roles
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var tenantIds = members.Select(m => m.TenantId).Distinct().ToList();
        var blockedTenants = await _denylist.GetBlockedTenantsAsync(tenantIds, cancellationToken);

        var contexts = new List<AvailableContext>();

        foreach (var member in members)
        {
            var tenant = member.Tenant;
            if (tenant.Status == EntityStatus.SUSPENDED || tenant.Group.Status == EntityStatus.SUSPENDED)
            {
                continue;
            }

            if (blockedTenants.Contains(tenant.Id))
            {
                continue;
            }

            var availableScopes = new List<OperationalScope>();
            var rolesSet = new HashSet<string>(StringComparer.Ordinal);

            foreach (var scope in AllScopes)
            {
                var isScopeActive = tenant.Scopes.Any(s => s.Scope == scope && s.IsActive);
                if (!isScopeActive)
                {
                    continue;
                }

                List<string> rolesForScope;
                if (member.IsOwner)
                {
                    rolesForScope = allRoles.Where(r => r.Scope == scope).Select(r => r.Name).ToList();
                }
                else
                {
                    rolesForScope = member.MemberRoles.Where(mr => mr.Role.Scope == scope).Select(mr => mr.Role.Name).ToList();
                }

                if (rolesForScope.Count > 0)
                {
                    availableScopes.Add(scope);
                    foreach (var role in rolesForScope)
                    {
                        rolesSet.Add(role);
                    }
                }
            }

            if (availableScopes.Count > 0)
            {
                contexts.Add(new AvailableContext(
                    tenant.GroupId,
                    tenant.Group.Name,
                    tenant.Id,
                    tenant.Cnpj,
                    tenant.LegalName,
                    tenant.BranchType,
                    member.IsOwner,
                    member.CanViewGroup,
                    availableScopes.OrderBy(s => s).ToList(),
                    rolesSet.OrderBy(r => r, StringComparer.Ordinal).ToList()));
            }
        }

        // PRS-28: Sort ordinal by group_name, legal_name, tenant_id
        var sortedContexts = contexts
            .OrderBy(c => c.GroupName, StringComparer.Ordinal)
            .ThenBy(c => c.LegalName, StringComparer.Ordinal)
            .ThenBy(c => c.TenantId)
            .ToList();

        return AccessResult<IReadOnlyList<AvailableContext>>.Success(sortedContexts);
    }

    public async Task<GroupMembershipKind> GetGroupMembershipAsync(Guid userId, Guid groupId, CancellationToken cancellationToken = default)
    {
        return await _tenantIsolation.ExecuteWithBypassAsync(_dbContext, async db =>
        {
            var members = await db.TenantMembers
                .Include(m => m.Tenant)
                .AsNoTracking()
                .Where(m => m.UserId == userId && m.Tenant.GroupId == groupId)
                .ToListAsync(cancellationToken);

            if (members.Count == 0)
            {
                return GroupMembershipKind.NONE;
            }

            if (members.Any(m => m.IsOwner))
            {
                return GroupMembershipKind.OWNER;
            }

            return GroupMembershipKind.MEMBER;
        });
    }

    public async Task<TenantInfo?> ResolveTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var tenant = await _dbContext.Tenants
            .Include(t => t.Group)
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);

        if (tenant == null)
        {
            return null;
        }

        return new TenantInfo(tenant.Id, tenant.GroupId, tenant.Status, tenant.Group.Status);
    }
}
