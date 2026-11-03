using IdentityService.Modules.M11Esquema.Persistence.Enums;

namespace IdentityService.Modules.M11Esquema.Persistence.Entities;

public class Role
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public OperationalScope Scope { get; set; }
    public string[] Permissions { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<MemberRole> MemberRoles { get; set; } = new List<MemberRole>();
}
