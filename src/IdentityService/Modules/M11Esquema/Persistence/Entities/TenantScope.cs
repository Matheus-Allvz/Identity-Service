using IdentityService.Modules.M11Esquema.Persistence.Enums;
using IdentityService.Modules.M13Isolamento.Interface;

namespace IdentityService.Modules.M11Esquema.Persistence.Entities;

public class TenantScope : ITenantScoped
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public OperationalScope Scope { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }

    public Tenant Tenant { get; set; } = null!;
}
