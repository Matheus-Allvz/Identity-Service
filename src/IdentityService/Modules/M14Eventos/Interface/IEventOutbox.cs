namespace IdentityService.Modules.M14Eventos.Interface;

public interface IEventOutbox
{
    Guid EnqueueTenantCreated(TenantCreatedPayload payload);
    Guid EnqueueTenantBlocked(TenantBlockedPayload payload);
    Guid EnqueueUserLocked(Guid userId, DateTimeOffset lockedUntil, DateTimeOffset blockedAt);
    Guid EnqueueUserSuspended(Guid userId, string reason, DateTimeOffset blockedAt);
    Guid EnqueueSessionCompromised(SessionCompromisedPayload payload, FamilyContext family);
}
