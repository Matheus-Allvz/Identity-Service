using IdentityService.Modules.M11Esquema.Persistence.Enums;

namespace IdentityService.Modules.M11Esquema.Persistence.Entities;

public class Tenant
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public string Cnpj { get; set; } = string.Empty;
    public string LegalName { get; set; } = string.Empty;
    public BranchType BranchType { get; set; }
    public decimal GeoLat { get; set; }
    public decimal GeoLng { get; set; }
    public EntityStatus Status { get; set; } = EntityStatus.ACTIVE;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public EconomicGroup Group { get; set; } = null!;
    public ICollection<TenantScope> Scopes { get; set; } = new List<TenantScope>();
    public ICollection<TenantMember> Members { get; set; } = new List<TenantMember>();
}
