using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace IdentityService.Acceptance.Infrastructure;

public static class TokenHelper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public static string CunharToken(
        RSA rsa,
        string keyId,
        string jti,
        string sub,
        string? fid,
        long iat,
        long exp,
        string platformRole = "NONE",
        string viewMode = "NONE",
        string? groupId = null,
        string? tenantId = null,
        IEnumerable<string>? allowedTenants = null,
        string? activeScope = null,
        IEnumerable<string>? roles = null,
        IEnumerable<string>? permissions = null,
        object? act = null,
        string issuer = "identity-service",
        string audience = "b2b-platform")
    {
        var dict = new Dictionary<string, object?>
        {
            ["jti"] = jti,
            ["sub"] = sub,
            ["iat"] = iat,
            ["exp"] = exp,
            ["iss"] = issuer,
            ["aud"] = audience,
            ["platform_role"] = platformRole,
            ["view_mode"] = viewMode,
            ["group_id"] = groupId,
            ["tenant_id"] = tenantId,
            ["allowed_tenants"] = (allowedTenants ?? Array.Empty<string>()).ToArray(),
            ["active_scope"] = activeScope,
            ["roles"] = (roles ?? Array.Empty<string>()).ToArray(),
            ["permissions"] = (permissions ?? Array.Empty<string>()).ToArray()
        };

        if (fid != null)
        {
            dict["fid"] = fid;
        }

        if (act != null)
        {
            dict["act"] = act;
        }

        var payloadJson = JsonSerializer.Serialize(dict, JsonOptions);
        var credentials = new SigningCredentials(new RsaSecurityKey(rsa) { KeyId = keyId }, SecurityAlgorithms.RsaSha256);
        return new JsonWebTokenHandler().CreateToken(payloadJson, credentials);
    }

    public static string GerarRefreshTokenAleatorio()
    {
        return Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
    }

    public static string CalcularSha256Hex(string input)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }

    public static RSA CriarRsa2048() => RSA.Create(2048);
}
