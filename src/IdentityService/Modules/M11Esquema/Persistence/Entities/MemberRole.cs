using IdentityService.Modules.M13Isolamento.Interface;

namespace IdentityService.Modules.M11Esquema.Persistence.Entities;

public class MemberRole : ITenantScoped
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid TenantMemberId { get; set; }
    public Guid RoleId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public TenantMember TenantMember { get; set; } = null!;
    public Role Role { get; set; } = null!;
}
