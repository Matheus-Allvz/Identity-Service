using IdentityService.Modules.M11Esquema.Persistence.Enums;
using IdentityService.Modules.M13Isolamento.Interface;

namespace IdentityService.Modules.M11Esquema.Persistence.Entities;

public class TenantMember : ITenantScoped
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public bool IsOwner { get; set; }
    public bool CanViewGroup { get; set; }
    public EntityStatus Status { get; set; } = EntityStatus.ACTIVE;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Tenant Tenant { get; set; } = null!;
    public UserAccount User { get; set; } = null!;
    public ICollection<MemberRole> MemberRoles { get; set; } = new List<MemberRole>();
}
