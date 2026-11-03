namespace IdentityService.Modules.M14Eventos.Interface;

public record TenantBlockedPayload(
    Guid TenantId,
    Guid GroupId,
    string Cnpj,
    string Reason,
    Guid BlockedByUserId,
    DateTimeOffset BlockedAt
);
