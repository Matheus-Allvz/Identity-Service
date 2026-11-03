using IdentityService.Modules.M11Esquema.Persistence.Enums;

namespace IdentityService.Modules.M00Transversal.Audit;

public interface IAuditLog
{
    void Append(
        AuditEventType eventType,
        Guid? userId,
        Guid? actorUserId,
        Guid? tenantId,
        Guid? groupId,
        string? ticketId,
        object? metadata);
}
