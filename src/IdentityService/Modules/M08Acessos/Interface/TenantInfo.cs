using IdentityService.Modules.M11Esquema.Persistence.Enums;

namespace IdentityService.Modules.M08Acessos.Interface;

public record TenantInfo(
    Guid TenantId,
    Guid GroupId,
    EntityStatus Status,
    EntityStatus GroupStatus);
