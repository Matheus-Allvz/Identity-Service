namespace IdentityService.Modules.M14Eventos.Interface;

public record SessionCompromisedPayload(
    Guid UserId,
    Guid FamilyId,
    Guid ReusedRefreshTokenId,
    string IpAddress,
    DateTimeOffset DetectedAt
);
