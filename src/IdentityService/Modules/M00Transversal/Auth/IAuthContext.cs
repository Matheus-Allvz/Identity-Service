using IdentityService.Modules.M11Esquema.Persistence.Enums;

namespace IdentityService.Modules.M00Transversal.Auth;

public interface IAuthContext
{
    string? Jti { get; }
    string? TicketId { get; }
    Guid? UserId { get; }
    Guid? FamilyId { get; }
    Guid? GroupId { get; }
    Guid? ActiveTenantId { get; }
    Guid? ActorUserId { get; }
    long? Iat { get; }
    long? Exp { get; }
    PlatformRole? PlatformRole { get; }
    ViewMode? ViewMode { get; }
    OperationalScope? ActiveScope { get; }
    IReadOnlyList<Guid> AllowedTenants { get; }
    IReadOnlyList<string> Roles { get; }
    IReadOnlyList<string> Permissions { get; }
    bool IsAuthenticated { get; }
    bool BypassTenantFilter { get; }
    Guid CorrelationId { get; }
    string IpAddress { get; }
    IReadOnlySet<Guid> WriteTenants { get; }
    void AuthorizeTenantWrite(Guid tenantId);
}
