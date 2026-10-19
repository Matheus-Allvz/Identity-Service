using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;
using Xunit.Abstractions;

namespace IdentityService.Tests;

public class Cunhagem(ITestOutputHelper o)
{
    [Fact]
    public async Task CunharComPayloadLiteral()
    {
        using var rsa = RSA.Create(2048);
        var pem = rsa.ExportPkcs8PrivateKeyPem();
        var cred = new SigningCredentials(new RsaSecurityKey(rsa) { KeyId = "key-2026-01" }, SecurityAlgorithms.RsaSha256);
        var payload = "{\"jti\":\"0192\",\"sub\":\"u\",\"iat\":1000,\"exp\":1900,\"tenant_id\":null,\"allowed_tenants\":[],\"iss\":\"identity-service\",\"aud\":\"b2b-platform\"}";
        var t = new JsonWebTokenHandler().CreateToken(payload, cred);
        var p = t.Split('.');
        string D(string s) => Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(s));

        var header = D(p[0]);
        var claims = D(p[1]);

        o.WriteLine("Header: " + header);
        o.WriteLine("Claims: " + claims);

        Assert.Contains("\"alg\":\"RS256\"", header);
        Assert.Contains("\"kid\":\"key-2026-01\"", header);
        Assert.Contains("\"typ\":\"JWT\"", header);
        Assert.Contains("\"tenant_id\":null", claims);
        Assert.Contains("\"sub\":\"u\"", claims);
        Assert.StartsWith("-----BEGIN PRIVATE KEY-----", pem);

        var v = await new JsonWebTokenHandler().ValidateTokenAsync(t, new TokenValidationParameters
        {
            IssuerSigningKey = new RsaSecurityKey(rsa.ExportParameters(false)) { KeyId = "key-2026-01" },
            ValidIssuer = "identity-service",
            ValidAudience = "b2b-platform",
            ValidateLifetime = false
        });

        Assert.True(v.IsValid);
        Assert.Null(v.Exception);
    }
}
