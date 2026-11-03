using IdentityService.Modules.M11Esquema.Persistence.Enums;

namespace IdentityService.Modules.M11Esquema.Persistence.Entities;

public class EconomicGroup
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public EntityStatus Status { get; set; } = EntityStatus.ACTIVE;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<Tenant> Tenants { get; set; } = new List<Tenant>();
}
