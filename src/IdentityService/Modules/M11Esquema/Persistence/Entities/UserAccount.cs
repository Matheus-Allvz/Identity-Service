using IdentityService.Modules.M11Esquema.Persistence.Enums;

namespace IdentityService.Modules.M11Esquema.Persistence.Entities;

public class UserAccount
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public UserStatus Status { get; set; } = UserStatus.ACTIVE;
    public PlatformRole PlatformRole { get; set; } = PlatformRole.NONE;
    public int FailedLoginAttempts { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<TenantMember> Memberships { get; set; } = new List<TenantMember>();
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
}
