using IdentityService.Modules.M11Esquema.Persistence.Enums;

namespace IdentityService.Modules.M04Tenants.Interface;

public record TenantStatusChange(
    EntityStatus Previous,
    Guid GroupId,
    string Cnpj,
    DateTimeOffset UpdatedAt
);
