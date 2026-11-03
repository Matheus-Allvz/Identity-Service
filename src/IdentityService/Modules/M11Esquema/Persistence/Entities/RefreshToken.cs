using IdentityService.Modules.M11Esquema.Persistence.Enums;

namespace IdentityService.Modules.M11Esquema.Persistence.Entities;

public class RefreshToken
{
    public Guid Id { get; set; }
    public Guid FamilyId { get; set; }
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public RefreshTokenStatus Status { get; set; } = RefreshTokenStatus.ACTIVE;
    public Guid? ReplacedById { get; set; }
    public ViewMode ViewMode { get; set; } = ViewMode.NONE;
    public Guid? GroupId { get; set; }
    public Guid? ActiveTenantId { get; set; }
    public Guid[] AllowedTenants { get; set; } = [];
    public OperationalScope? ActiveScope { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public UserAccount User { get; set; } = null!;
    public RefreshToken? ReplacedBy { get; set; }
    public EconomicGroup? Group { get; set; }
    public Tenant? ActiveTenant { get; set; }
}
