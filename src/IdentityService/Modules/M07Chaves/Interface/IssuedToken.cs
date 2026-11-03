namespace IdentityService.Modules.M07Chaves.Interface;

public record IssuedToken(
    string AccessToken,
    string Jti,
    long Iat,
    long Exp,
    AccessTokenClaims Claims);
