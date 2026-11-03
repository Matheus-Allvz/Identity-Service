using IdentityService.Modules.M11Esquema.Persistence.Enums;

namespace IdentityService.Modules.M14Eventos.Interface;

public record FamilyContext(
    Guid UserId,
    ViewMode ViewMode,
    Guid? GroupId,
    Guid? ActiveTenantId,
    IReadOnlyList<Guid> AllowedTenants,
    OperationalScope? ActiveScope
);
