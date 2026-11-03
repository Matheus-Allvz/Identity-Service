using IdentityService.Modules.M05Membros.Interface;
using IdentityService.Modules.M11Esquema.Persistence;
using IdentityService.Modules.M11Esquema.Persistence.Entities;
using IdentityService.Modules.M11Esquema.Persistence.Enums;
using IdentityService.Modules.M13Isolamento.Interface;
using Microsoft.EntityFrameworkCore;

namespace IdentityService.Modules.M05Membros;

public class MembershipWriter : IMembershipWriter
{
    private static readonly Guid GroupManagerRoleId = Guid.Parse("01900000-0000-7000-8000-000000000004");

    private readonly IdentityDbContext _dbContext;
    private readonly ITenantIsolation _tenantIsolation;
    private readonly TimeProvider _timeProvider;

    public MembershipWriter(
        IdentityDbContext dbContext,
        ITenantIsolation tenantIsolation,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _tenantIsolation = tenantIsolation;
        _timeProvider = timeProvider;
    }

    public void AddCreatorMembership(Guid tenantId, Guid userId)
    {
        var now = _timeProvider.GetUtcNow();
        var memberId = Guid.CreateVersion7(now);

        var member = new TenantMember
        {
            Id = memberId,
            TenantId = tenantId,
            UserId = userId,
            IsOwner = false,
            CanViewGroup = false,
            Status = EntityStatus.ACTIVE,
            CreatedAt = now,
            UpdatedAt = now
        };

        var memberRole = new MemberRole
        {
            Id = Guid.CreateVersion7(now),
            TenantId = tenantId,
            TenantMemberId = memberId,
            RoleId = GroupManagerRoleId,
            CreatedAt = now
        };

        _dbContext.TenantMembers.Add(member);
        _dbContext.MemberRoles.Add(memberRole);
    }

    public Task AddCreatorMemberAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default)
    {
        AddCreatorMembership(tenantId, userId);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<Guid>> SetGroupMembershipsStatusAsync(
        Guid userId,
        Guid groupId,
        EntityStatus status,
        CancellationToken cancellationToken = default)
    {
        return await _tenantIsolation.ExecuteWithBypassAsync(_dbContext, async db =>
        {
            var now = _timeProvider.GetUtcNow();

            // RN-75: Only update memberships with is_owner = false and status different
            var affectedMembers = await db.TenantMembers
                .AsNoTracking()
                .Where(m => m.UserId == userId &&
                            !m.IsOwner &&
                            m.Status != status &&
                            db.Tenants.Any(t => t.Id == m.TenantId && t.GroupId == groupId))
                .Select(m => new { m.Id, m.TenantId })
                .ToListAsync(cancellationToken);

            if (affectedMembers.Count == 0)
            {
                return Array.Empty<Guid>();
            }

            var memberIds = affectedMembers.Select(m => m.Id).ToList();

            await db.TenantMembers
                .Where(m => memberIds.Contains(m.Id))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.Status, status)
                    .SetProperty(m => m.UpdatedAt, now),
                    cancellationToken);

            IReadOnlyList<Guid> tenantIds = affectedMembers.Select(m => m.TenantId).Distinct().ToList();
            return tenantIds;
        });
    }
}
