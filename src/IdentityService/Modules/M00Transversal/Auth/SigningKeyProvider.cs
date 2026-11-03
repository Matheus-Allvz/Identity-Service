using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace IdentityService.Modules.M00Transversal.Auth;

public class SigningKeyProvider : ISigningKey
{
    private readonly RSA _rsa;
    private readonly RsaSecurityKey _signingKey;
    private readonly string _modulus;
    private readonly string _exponent;

    public string KeyId { get; }
    public RsaSecurityKey SigningKey => _signingKey;
    public string Modulus => _modulus;
    public string Exponent => _exponent;

    public SigningKeyProvider(IConfiguration configuration, IHostEnvironment environment)
    {
        KeyId = configuration["JWT_KEY_ID"] ?? "key-2026-01";
        var pem = configuration["JWT_RSA_PRIVATE_KEY_PEM"];

        if (!string.IsNullOrWhiteSpace(pem))
        {
            _rsa = RSA.Create();
            _rsa.ImportFromPem(pem);
        }
        else
        {
            if (environment.IsDevelopment() || environment.IsEnvironment("Testing"))
            {
                _rsa = RSA.Create(2048);
            }
            else
            {
                throw new InvalidOperationException("JWT_RSA_PRIVATE_KEY_PEM is mandatory in non-development/testing environments.");
            }
        }

        _signingKey = new RsaSecurityKey(_rsa) { KeyId = KeyId };
        var parameters = _rsa.ExportParameters(false);
        _modulus = Base64UrlEncoder.Encode(parameters.Modulus);
        _exponent = Base64UrlEncoder.Encode(parameters.Exponent);
    }
}
