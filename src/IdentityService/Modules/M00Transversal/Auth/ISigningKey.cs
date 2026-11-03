using Microsoft.IdentityModel.Tokens;

namespace IdentityService.Modules.M00Transversal.Auth;

public interface ISigningKey
{
    string KeyId { get; }
    RsaSecurityKey SigningKey { get; }
    string Modulus { get; }
    string Exponent { get; }
}
