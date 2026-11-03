using IdentityService.Modules.M11Esquema.Persistence.Enums;

namespace IdentityService.Modules.M05Membros.Interface;

public interface IMembershipWriter
{
    void AddCreatorMembership(Guid tenantId, Guid userId);

    Task AddCreatorMemberAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Guid>> SetGroupMembershipsStatusAsync(
        Guid userId,
        Guid groupId,
        EntityStatus status,
        CancellationToken cancellationToken = default);
}
