using IdentityService.Modules.M11Esquema.Persistence.Enums;

namespace IdentityService.Modules.M00Transversal.Auth;

public class AuthContext : IAuthContext
{
    private readonly HashSet<Guid> _writeTenants = [];

    public string? Jti { get; set; }
    public string? TicketId { get; set; }
    public Guid? UserId { get; set; }
    public Guid? FamilyId { get; set; }
    public Guid? GroupId { get; set; }
    public Guid? ActiveTenantId { get; set; }
    public Guid? ActorUserId { get; set; }
    public long? Iat { get; set; }
    public long? Exp { get; set; }
    public PlatformRole? PlatformRole { get; set; }
    public ViewMode? ViewMode { get; set; }
    public OperationalScope? ActiveScope { get; set; }
    public IReadOnlyList<Guid> AllowedTenants { get; set; } = [];
    public IReadOnlyList<string> Roles { get; set; } = [];
    public IReadOnlyList<string> Permissions { get; set; } = [];
    public bool IsAuthenticated { get; set; }
    public bool BypassTenantFilter { get; set; }
    public Guid CorrelationId { get; set; }
    public string IpAddress { get; set; } = "0.0.0.0";
    public IReadOnlySet<Guid> WriteTenants => _writeTenants;

    public void InitializeWriteTenants(IEnumerable<Guid> allowedTenants)
    {
        _writeTenants.Clear();
        foreach (var tenant in allowedTenants)
        {
            _writeTenants.Add(tenant);
        }
    }

    public void AuthorizeTenantWrite(Guid tenantId)
    {
        _writeTenants.Add(tenantId);
    }
}
