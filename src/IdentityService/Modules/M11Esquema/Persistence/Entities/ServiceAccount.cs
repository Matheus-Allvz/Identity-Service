using IdentityService.Modules.M11Esquema.Persistence.Enums;
using IdentityService.Modules.M13Isolamento.Interface;

namespace IdentityService.Modules.M11Esquema.Persistence.Entities;

// ENT-11 (NEW-06): identidade de um serviço para o fluxo Client Credentials.
// Atua em nome de um tenant, com um único papel (do qual derivam escopo e permissões).
public class ServiceAccount : ITenantScoped
{
    public Guid Id { get; set; }
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecretHash { get; set; } = string.Empty;
    public Guid TenantId { get; set; }
    public Guid RoleId { get; set; }
    public EntityStatus Status { get; set; } = EntityStatus.ACTIVE;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Tenant Tenant { get; set; } = null!;
    public Role Role { get; set; } = null!;
}
