namespace IdentityService.Modules.M07Chaves.Interface;

public interface ITokenIssuer
{
    IssuedToken Issue(AccessTokenClaims claims);
}
