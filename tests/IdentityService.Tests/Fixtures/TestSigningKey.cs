using System.Security.Cryptography;
using IdentityService.Modules.M00Transversal.Auth;
using Microsoft.IdentityModel.Tokens;

namespace IdentityService.Tests.Fixtures;

public class TestSigningKey : ISigningKey
{
    private readonly RSA _rsa;
    private readonly RsaSecurityKey _securityKey;

    public bool ThrowOnKeyAccess { get; set; }

    public TestSigningKey(string keyId = "key-2026-01")
    {
        KeyId = keyId;
        _rsa = RSA.Create(2048);
        _securityKey = new RsaSecurityKey(_rsa) { KeyId = keyId };
    }

    public string KeyId { get; }

    public RsaSecurityKey SigningKey
    {
        get
        {
            if (ThrowOnKeyAccess)
            {
                throw new CryptographicException("Chave criptográfica inacessível para teste.");
            }
            return _securityKey;
        }
    }

    public string Modulus
    {
        get
        {
            if (ThrowOnKeyAccess)
            {
                throw new CryptographicException("Chave criptográfica inacessível para teste.");
            }
            var parameters = _rsa.ExportParameters(false);
            return Base64UrlEncoder.Encode(parameters.Modulus);
        }
    }

    public string Exponent
    {
        get
        {
            if (ThrowOnKeyAccess)
            {
                throw new CryptographicException("Chave criptográfica inacessível para teste.");
            }
            var parameters = _rsa.ExportParameters(false);
            return Base64UrlEncoder.Encode(parameters.Exponent);
        }
    }

    public RSA GetRsa() => _rsa;
}
