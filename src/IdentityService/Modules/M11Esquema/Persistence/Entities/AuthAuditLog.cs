using IdentityService.Modules.M11Esquema.Persistence.Enums;

namespace IdentityService.Modules.M11Esquema.Persistence.Entities;

public class AuthAuditLog
{
    public Guid Id { get; set; }
    public AuditEventType EventType { get; set; }
    public Guid? UserId { get; set; }
    public Guid? ActorUserId { get; set; }
    public Guid? TenantId { get; set; }
    public Guid? GroupId { get; set; }
    public string? TicketId { get; set; }
    public string IpAddress { get; set; } = string.Empty;
    public Guid CorrelationId { get; set; }
    public string Metadata { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }

    public UserAccount? User { get; set; }
    public UserAccount? ActorUser { get; set; }
    public Tenant? Tenant { get; set; }
    public EconomicGroup? Group { get; set; }
}
