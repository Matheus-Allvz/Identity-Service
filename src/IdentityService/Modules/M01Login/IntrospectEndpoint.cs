using System.Text;
using System.Text.Json;
using IdentityService.Modules.M00Transversal.Auth;
using IdentityService.Modules.M00Transversal.Redis;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace IdentityService.Modules.M01Login;

public static class IntrospectEndpoint
{
    private const string NoticeHeader = "Diagnostic-Only; Local JWKS caching required for production microservices";
    private const string WarningText = "Este endpoint destina-se estritamente a diagnóstico em desenvolvimento. Microsserviços em produção DEVEM validar tokens localmente via JWKS (RS256) com cache em memória.";

    public static async Task<IResult> HandleAsync(
        HttpContext httpContext,
        ISigningKey signingKey,
        IDenylist denylist,
        IConfiguration configuration,
        TimeProvider timeProvider)
    {
        httpContext.Response.Headers["X-B2B-Notice"] = NoticeHeader;

        var token = await ExtractTokenAsync(httpContext.Request);
        if (string.IsNullOrWhiteSpace(token))
        {
            return Results.Ok(new { active = false });
        }

        token = token.Trim();
        if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            token = token["Bearer ".Length..].Trim();
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            return Results.Ok(new { active = false });
        }

        RsaSecurityKey rsaKey;
        try
        {
            rsaKey = signingKey.SigningKey;
        }
        catch
        {
            return Results.Ok(new { active = false });
        }

        var issuer = configuration["JWT_ISSUER"] ?? "identity-service";
        var audience = configuration["JWT_AUDIENCE"] ?? "b2b-platform";

        var handler = new JsonWebTokenHandler();
        var validationParameters = new TokenValidationParameters
        {
            ValidIssuer = issuer,
            ValidAudience = audience,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = rsaKey,
            ClockSkew = TimeSpan.Zero,
            LifetimeValidator = (notBefore, expires, securityToken, parameters) =>
            {
                if (expires == null) return false;
                return expires > timeProvider.GetUtcNow().UtcDateTime;
            }
        };

        TokenValidationResult validationResult;
        try
        {
            validationResult = await handler.ValidateTokenAsync(token, validationParameters);
        }
        catch
        {
            return Results.Ok(new { active = false });
        }

        if (!validationResult.IsValid || validationResult.SecurityToken is not JsonWebToken jwt)
        {
            return Results.Ok(new { active = false });
        }

        if (!string.Equals(jwt.Alg, "RS256", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Ok(new { active = false });
        }

        if (!string.IsNullOrEmpty(signingKey.KeyId) &&
            !string.IsNullOrEmpty(jwt.Kid) &&
            !string.Equals(jwt.Kid, signingKey.KeyId, StringComparison.Ordinal))
        {
            return Results.Ok(new { active = false });
        }

        string payloadJson;
        try
        {
            payloadJson = Base64UrlEncoder.Decode(jwt.EncodedPayload);
        }
        catch
        {
            return Results.Ok(new { active = false });
        }

        using var doc = JsonDocument.Parse(payloadJson);
        var root = doc.RootElement;

        // Check jti in Redis denylist
        var jti = jwt.Id;
        if (string.IsNullOrEmpty(jti) && root.TryGetProperty("jti", out var jtiProp))
        {
            jti = jtiProp.GetString();
        }

        if (!string.IsNullOrEmpty(jti))
        {
            try
            {
                if (await denylist.IsJtiRevokedAsync(jti, httpContext.RequestAborted))
                {
                    return Results.Ok(new { active = false });
                }
            }
            catch
            {
                return Results.Ok(new { active = false });
            }
        }

        // Check if user is revoked
        if (root.TryGetProperty("sub", out var subProp) && Guid.TryParse(subProp.GetString(), out var subGuid))
        {
            try
            {
                var userRevokedAt = await denylist.GetUserRevokedAtAsync(subGuid, httpContext.RequestAborted);
                var iatVal = root.TryGetProperty("iat", out var iatProp) ? iatProp.GetInt64() : 0;
                if (userRevokedAt.HasValue && iatVal <= userRevokedAt.Value)
                {
                    return Results.Ok(new { active = false });
                }
            }
            catch
            {
                return Results.Ok(new { active = false });
            }
        }

        // Check if tenant is blocked
        if (root.TryGetProperty("tenant_id", out var tenantProp) && Guid.TryParse(tenantProp.GetString(), out var tenantGuid))
        {
            try
            {
                var blocked = await denylist.GetBlockedTenantsAsync(new[] { tenantGuid }, httpContext.RequestAborted);
                if (blocked.Count > 0)
                {
                    return Results.Ok(new { active = false });
                }
            }
            catch
            {
                return Results.Ok(new { active = false });
            }
        }

        var sub = root.TryGetProperty("sub", out var s) ? s.GetString() : null;
        var tenantId = root.TryGetProperty("tenant_id", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
        var groupId = root.TryGetProperty("group_id", out var g) && g.ValueKind == JsonValueKind.String ? g.GetString() : null;
        var activeScope = root.TryGetProperty("active_scope", out var sc) && sc.ValueKind == JsonValueKind.String ? sc.GetString() : null;
        var viewMode = root.TryGetProperty("view_mode", out var vm) && vm.ValueKind == JsonValueKind.String ? vm.GetString() : null;

        var roles = root.TryGetProperty("roles", out var r) && r.ValueKind == JsonValueKind.Array
            ? r.EnumerateArray().Select(x => x.GetString()!).Where(x => x != null).ToArray()
            : Array.Empty<string>();

        var permissions = root.TryGetProperty("permissions", out var p) && p.ValueKind == JsonValueKind.Array
            ? p.EnumerateArray().Select(x => x.GetString()!).Where(x => x != null).ToArray()
            : Array.Empty<string>();

        var exp = root.TryGetProperty("exp", out var e) ? e.GetInt64() : 0;
        var iat = root.TryGetProperty("iat", out var i) ? i.GetInt64() : 0;

        var responsePayload = new
        {
            active = true,
            sub,
            tenant_id = tenantId,
            group_id = groupId,
            active_scope = activeScope,
            view_mode = viewMode,
            roles,
            permissions,
            exp,
            iat,
            warning = WarningText
        };

        return Results.Ok(responsePayload);
    }

    private static async Task<string?> ExtractTokenAsync(HttpRequest request)
    {
        if (request.HasJsonContentType())
        {
            try
            {
                request.EnableBuffering();
                using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
                var body = await reader.ReadToEndAsync();
                request.Body.Position = 0;

                if (!string.IsNullOrWhiteSpace(body))
                {
                    using var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                        doc.RootElement.TryGetProperty("token", out var tokenProp) &&
                        tokenProp.ValueKind == JsonValueKind.String)
                    {
                        return tokenProp.GetString();
                    }
                }
            }
            catch
            {
                return null;
            }
        }
        else if (request.HasFormContentType)
        {
            try
            {
                var form = await request.ReadFormAsync();
                if (form.TryGetValue("token", out var formToken))
                {
                    return formToken.ToString();
                }
            }
            catch
            {
                return null;
            }
        }
        else
        {
            try
            {
                request.EnableBuffering();
                using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
                var body = await reader.ReadToEndAsync();
                request.Body.Position = 0;

                if (!string.IsNullOrWhiteSpace(body))
                {
                    var trimmed = body.Trim();
                    if (trimmed.StartsWith("{") && trimmed.EndsWith("}"))
                    {
                        using var doc = JsonDocument.Parse(trimmed);
                        if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                            doc.RootElement.TryGetProperty("token", out var tokenProp) &&
                            tokenProp.ValueKind == JsonValueKind.String)
                        {
                            return tokenProp.GetString();
                        }
                    }
                    else if (trimmed.Contains("token="))
                    {
                        var parsed = System.Web.HttpUtility.ParseQueryString(trimmed);
                        return parsed["token"];
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        return null;
    }
}
