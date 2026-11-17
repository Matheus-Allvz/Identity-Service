using IdentityService.Modules.M11Esquema.Persistence.Enums;

namespace IdentityService.Modules.M07Chaves.Interface;

public record AccessTokenClaims(
    Guid Sub,
    Guid? FamilyId,
    PlatformRole PlatformRole,
    ViewMode ViewMode,
    Guid? GroupId,
    Guid? TenantId,
    IReadOnlyList<Guid> AllowedTenants,
    OperationalScope? ActiveScope,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions,
    ActorClaim? Act = null,
    // NEW-06: preenchido apenas em tokens de serviço (client credentials); emite client_id e token_use=service.
    string? ServiceClientId = null);
