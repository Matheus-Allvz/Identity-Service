using IdentityService.Modules.M11Esquema.Persistence.Enums;

namespace IdentityService.Modules.M14Eventos.Interface;

public record TenantCreatedPayload(
    Guid TenantId,
    Guid GroupId,
    string Cnpj,
    string LegalName,
    BranchType BranchType,
    decimal GeoLat,
    decimal GeoLng,
    IReadOnlyList<OperationalScope> Scopes,
    DateTimeOffset CreatedAt
);
