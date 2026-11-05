using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using IdentityService.Modules.M00Transversal.Auth;
using IdentityService.Modules.M00Transversal.Errors;
using IdentityService.Modules.M00Transversal.Redis;
using IdentityService.Modules.M11Esquema.Persistence.Enums;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace IdentityService.Modules.M00Transversal.Middleware;

public partial class AuthContextMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IConfiguration _configuration;

    public AuthContextMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        _configuration = configuration;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IAuthContext authContext,
        ISigningKey signingKey,
        IDenylist denylist,
        TimeProvider timeProvider)
    {
        var method = context.Request.Method;
        var path = context.Request.Path.Value ?? string.Empty;

        var isTokenRequired = IsTokenRequiredEndpoint(method, path);
        var hasAuthHeader = context.Request.Headers.TryGetValue("Authorization", out var authHeaders) &&
                            !string.IsNullOrWhiteSpace(authHeaders);
        var authHeaderStr = hasAuthHeader ? authHeaders.ToString() : string.Empty;
        var hasBearer = hasAuthHeader && authHeaderStr.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase);

        if (isTokenRequired || hasBearer)
        {
            // PRS-71: Read signing key BEFORE checking Authorization header if token is required.
            // If the signing key is unavailable (CryptographicException), it must throw here.
            RsaSecurityKey key;
            try
            {
                key = signingKey.SigningKey;
            }
            catch (CryptographicException)
            {
                throw;
            }

            // RG-06: Authorization header verification
            if (!hasBearer)
            {
                throw new ApiErrorException("ERR-AUTH-002", "Token de acesso ausente, inválido, expirado ou revogado");
            }

            var jwtString = authHeaderStr[7..].Trim();
            if (string.IsNullOrEmpty(jwtString))
            {
                throw new ApiErrorException("ERR-AUTH-002", "Token de acesso ausente, inválido, expirado ou revogado");
            }

            var handler = new JsonWebTokenHandler();
            var validationParameters = new TokenValidationParameters
            {
                ValidIssuer = _configuration["JWT_ISSUER"] ?? "identity-service",
                ValidAudience = _configuration["JWT_AUDIENCE"] ?? "b2b-platform",
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ClockSkew = TimeSpan.Zero,
                LifetimeValidator = (notBefore, expires, securityToken, validationParameters) =>
                {
                    if (expires == null) return false;
                    return expires > timeProvider.GetUtcNow().UtcDateTime;
                }
            };

            var validationResult = await handler.ValidateTokenAsync(jwtString, validationParameters);
            if (!validationResult.IsValid || validationResult.SecurityToken is not JsonWebToken jwt)
            {
                var detail = validationResult.Exception?.ToString() ?? "Token de acesso ausente, inválido, expirado ou revogado";
                throw new ApiErrorException("ERR-AUTH-002", detail);
            }

            if (jwt.Alg != "RS256" || jwt.Kid != signingKey.KeyId || jwt.Typ != "JWT")
            {
                throw new ApiErrorException("ERR-AUTH-002", "Token de acesso ausente, inválido, expirado ou revogado");
            }

            var parsedClaims = ParseAndValidateClaims(jwt);

            // RG-06 denylist:jti check
            if (await denylist.IsJtiRevokedAsync(parsedClaims.Jti))
            {
                throw new ApiErrorException("ERR-AUTH-002", "Token de acesso ausente, inválido, expirado ou revogado");
            }

            // RG-07 denylist:user check
            var userRevokedAt = await denylist.GetUserRevokedAtAsync(parsedClaims.UserId);
            if (userRevokedAt.HasValue && parsedClaims.Iat <= userRevokedAt.Value)
            {
                throw new ApiErrorException("ERR-AUTH-004", "Utilizador suspenso ou revogado globalmente");
            }

            if (parsedClaims.ActorUserId.HasValue)
            {
                var actorRevokedAt = await denylist.GetUserRevokedAtAsync(parsedClaims.ActorUserId.Value);
                if (actorRevokedAt.HasValue && parsedClaims.Iat <= actorRevokedAt.Value)
                {
                    throw new ApiErrorException("ERR-AUTH-004", "Utilizador suspenso ou revogado globalmente");
                }
            }

            // RG-08 denylist:tenant check
            if (parsedClaims.AllowedTenants.Count > 0)
            {
                var blocked = await denylist.GetBlockedTenantsAsync(parsedClaims.AllowedTenants);
                if (blocked.Count > 0)
                {
                    throw new ApiErrorException("ERR-CTX-002", "Tenant ou Grupo Económico suspenso");
                }
            }

            // Hydrate AuthContext
            if (authContext is AuthContext mutable)
            {
                mutable.IsAuthenticated = true;
                mutable.Jti = parsedClaims.Jti;
                mutable.UserId = parsedClaims.UserId;
                mutable.FamilyId = parsedClaims.FamilyId;
                mutable.PlatformRole = parsedClaims.PlatformRole;
                mutable.ViewMode = parsedClaims.ViewMode;
                mutable.GroupId = parsedClaims.GroupId;
                mutable.ActiveTenantId = parsedClaims.TenantId;
                mutable.AllowedTenants = parsedClaims.AllowedTenants;
                mutable.ActiveScope = parsedClaims.ActiveScope;
                mutable.Roles = parsedClaims.Roles;
                mutable.Permissions = parsedClaims.Permissions;
                mutable.ActorUserId = parsedClaims.ActorUserId;
                mutable.TicketId = parsedClaims.TicketId;
                mutable.Iat = parsedClaims.Iat;
                mutable.Exp = parsedClaims.Exp;

                mutable.InitializeWriteTenants(parsedClaims.AllowedTenants);

                mutable.BypassTenantFilter = IsBypassRoute(path) || parsedClaims.PlatformRole == PlatformRole.PLATFORM_ADMIN;
            }
        }
        else
        {
            if (authContext is AuthContext mutable)
            {
                mutable.IsAuthenticated = false;
                mutable.BypassTenantFilter = IsBypassRoute(path);
            }
        }

        await _next(context);
    }

    private static bool IsTokenRequiredEndpoint(string method, string path)
    {
        if (path.Equals("/api/v1/auth/login", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/api/v1/auth/refresh", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/.well-known/jwks.json", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/health/live", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/health/ready", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (path.Equals("/api/v1/auth/switch-context", StringComparison.OrdinalIgnoreCase) && method.Equals("POST", StringComparison.OrdinalIgnoreCase))
            return true;
        if (path.Equals("/api/v1/auth/logout", StringComparison.OrdinalIgnoreCase) && method.Equals("POST", StringComparison.OrdinalIgnoreCase))
            return true;
        if (path.Equals("/api/v1/auth/impersonate", StringComparison.OrdinalIgnoreCase) && method.Equals("POST", StringComparison.OrdinalIgnoreCase))
            return true;
        if (path.Equals("/api/v1/tenants", StringComparison.OrdinalIgnoreCase) && method.Equals("POST", StringComparison.OrdinalIgnoreCase))
            return true;
        if (path.StartsWith("/api/v1/tenants/", StringComparison.OrdinalIgnoreCase) && path.EndsWith("/members", StringComparison.OrdinalIgnoreCase) && method.Equals("POST", StringComparison.OrdinalIgnoreCase))
            return true;
        if (path.StartsWith("/api/v1/tenants/", StringComparison.OrdinalIgnoreCase) && path.EndsWith("/status", StringComparison.OrdinalIgnoreCase) && method.Equals("PATCH", StringComparison.OrdinalIgnoreCase))
            return true;
        if (path.StartsWith("/api/v1/users/", StringComparison.OrdinalIgnoreCase) && path.EndsWith("/status", StringComparison.OrdinalIgnoreCase) && method.Equals("PATCH", StringComparison.OrdinalIgnoreCase))
            return true;

        // Also if route is protected prefix
        if (path.StartsWith("/api/v1/auth/switch-context", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/v1/auth/logout", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/v1/auth/impersonate", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/v1/tenants", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/v1/users", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static bool IsBypassRoute(string path)
    {
        return path.StartsWith("/api/v1/auth/", StringComparison.OrdinalIgnoreCase);
    }

    private static ParsedClaims ParseAndValidateClaims(JsonWebToken jwt)
    {
        try
        {
            var payloadJson = Base64UrlEncoder.Decode(jwt.EncodedPayload);
            using var doc = JsonDocument.Parse(payloadJson);
            var root = doc.RootElement;

            var jti = root.GetProperty("jti").GetString()!;
            var sub = Guid.Parse(root.GetProperty("sub").GetString()!);
            var iat = root.GetProperty("iat").GetInt64();
            var exp = root.GetProperty("exp").GetInt64();

            var platformRoleStr = root.GetProperty("platform_role").GetString()!;
            if (!Enum.TryParse<PlatformRole>(platformRoleStr, false, out var platformRole))
            {
                throw new ApiErrorException("ERR-AUTH-002");
            }

            var viewModeStr = root.GetProperty("view_mode").GetString()!;
            if (!Enum.TryParse<ViewMode>(viewModeStr, false, out var viewMode))
            {
                throw new ApiErrorException("ERR-AUTH-002");
            }

            Guid? groupId = null;
            if (root.TryGetProperty("group_id", out var gElem) && gElem.ValueKind == JsonValueKind.String)
            {
                groupId = Guid.Parse(gElem.GetString()!);
            }

            Guid? tenantId = null;
            if (root.TryGetProperty("tenant_id", out var tElem) && tElem.ValueKind == JsonValueKind.String)
            {
                tenantId = Guid.Parse(tElem.GetString()!);
            }

            var allowedTenants = new List<Guid>();
            if (root.TryGetProperty("allowed_tenants", out var aElem) && aElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var elem in aElem.EnumerateArray())
                {
                    allowedTenants.Add(Guid.Parse(elem.GetString()!));
                }
            }

            OperationalScope? activeScope = null;
            if (root.TryGetProperty("active_scope", out var sElem) && sElem.ValueKind == JsonValueKind.String)
            {
                if (Enum.TryParse<OperationalScope>(sElem.GetString(), false, out var parsedScope))
                {
                    activeScope = parsedScope;
                }
                else
                {
                    throw new ApiErrorException("ERR-AUTH-002");
                }
            }

            var roles = new List<string>();
            if (root.TryGetProperty("roles", out var rElem) && rElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var elem in rElem.EnumerateArray())
                {
                    roles.Add(elem.GetString()!);
                }
            }

            var permissions = new List<string>();
            if (root.TryGetProperty("permissions", out var pElem) && pElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var elem in pElem.EnumerateArray())
                {
                    permissions.Add(elem.GetString()!);
                }
            }

            Guid? actorUserId = null;
            string? ticketId = null;
            bool hasAct = root.TryGetProperty("act", out var actElem) && actElem.ValueKind == JsonValueKind.Object;
            if (hasAct)
            {
                if (actElem.TryGetProperty("sub", out var actSub) &&
                    actElem.TryGetProperty("ticket_id", out var actTicket))
                {
                    actorUserId = Guid.Parse(actSub.GetString()!);
                    ticketId = actTicket.GetString();
                }
            }

            Guid? familyId = null;
            bool hasFid = root.TryGetProperty("fid", out var fElem) && fElem.ValueKind == JsonValueKind.String;
            if (hasFid)
            {
                familyId = Guid.Parse(fElem.GetString()!);
            }

            // Check invariantes
            if (hasAct && hasFid)
            {
                throw new ApiErrorException("ERR-AUTH-002");
            }
            if (!hasAct && !hasFid)
            {
                throw new ApiErrorException("ERR-AUTH-002");
            }

            if (viewMode == ViewMode.NONE)
            {
                if (groupId != null || tenantId != null || allowedTenants.Count > 0 || activeScope != null || roles.Count > 0 || permissions.Count > 0)
                {
                    throw new ApiErrorException("ERR-AUTH-002");
                }
            }
            else if (viewMode == ViewMode.SINGLE)
            {
                if (groupId == null || tenantId == null || activeScope == null || allowedTenants.Count != 1 || allowedTenants[0] != tenantId)
                {
                    throw new ApiErrorException("ERR-AUTH-002");
                }
            }
            else if (viewMode == ViewMode.MACRO_GROUP)
            {
                if (groupId == null || tenantId != null || activeScope == null || allowedTenants.Count is < 1 or > 50)
                {
                    throw new ApiErrorException("ERR-AUTH-002");
                }
            }

            return new ParsedClaims(
                jti,
                sub,
                iat,
                exp,
                platformRole,
                viewMode,
                groupId,
                tenantId,
                allowedTenants,
                activeScope,
                roles,
                permissions,
                familyId,
                actorUserId,
                ticketId);
        }
        catch (Exception ex) when (ex is not ApiErrorException)
        {
            throw new ApiErrorException("ERR-AUTH-002", "Token de acesso ausente, inválido, expirado ou revogado");
        }
    }

    private sealed record ParsedClaims(
        string Jti,
        Guid UserId,
        long Iat,
        long Exp,
        PlatformRole PlatformRole,
        ViewMode ViewMode,
        Guid? GroupId,
        Guid? TenantId,
        IReadOnlyList<Guid> AllowedTenants,
        OperationalScope? ActiveScope,
        IReadOnlyList<string> Roles,
        IReadOnlyList<string> Permissions,
        Guid? FamilyId,
        Guid? ActorUserId,
        string? TicketId);
}
