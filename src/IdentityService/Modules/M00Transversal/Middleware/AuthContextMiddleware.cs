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

        if (isTokenRequired)
        {
            // PRS-71: Read signing key BEFORE checking Authorization header.
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
            if (!context.Request.Headers.TryGetValue("Authorization", out var authHeaders) ||
                string.IsNullOrWhiteSpace(authHeaders))
            {
                throw new ApiErrorException("ERR-AUTH-002", "Token de acesso ausente, inválido, expirado ou revogado");
            }

            var authHeaderStr = authHeaders.ToString();
            if (!authHeaderStr.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
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
                throw new ApiErrorException("ERR-AUTH-002", "Token de acesso ausente, inválido, expirado ou revogado");
            }

            if (jwt.Alg != "RS256" || jwt.Kid != signingKey.KeyId || jwt.Typ != "JWT")
            {
                throw new ApiErrorException("ERR-AUTH-002", "Token de acesso ausente, inválido, expirado ou revogado");
            }

            var parsedClaims = ParseAndValidateClaims(jwt);

            // RG-06 denylist:jti check
            var jtiRevokedAt = await denylist.GetUserRevokedAtAsync(Guid.Empty); // dummy call check
            var db = context.RequestServices.GetRequiredService<StackExchange.Redis.IConnectionMultiplexer>().GetDatabase();
            if (await db.KeyExistsAsync($"denylist:jti:{parsedClaims.Jti}"))
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
            path.Equals("/.well-known/jwks.json", StringComparison.OrdinalIgnoreCase))
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
            var jti = jwt.GetClaim("jti").Value;
            var sub = Guid.Parse(jwt.GetClaim("sub").Value);
            var iat = long.Parse(jwt.GetClaim("iat").Value);
            var exp = long.Parse(jwt.GetClaim("exp").Value);

            var platformRoleStr = jwt.GetClaim("platform_role").Value;
            if (!Enum.TryParse<PlatformRole>(platformRoleStr, false, out var platformRole))
            {
                throw new ApiErrorException("ERR-AUTH-002");
            }

            var viewModeStr = jwt.GetClaim("view_mode").Value;
            if (!Enum.TryParse<ViewMode>(viewModeStr, false, out var viewMode))
            {
                throw new ApiErrorException("ERR-AUTH-002");
            }

            Guid? groupId = null;
            var groupClaim = jwt.GetClaim("group_id");
            if (groupClaim != null && !string.IsNullOrEmpty(groupClaim.Value))
            {
                groupId = Guid.Parse(groupClaim.Value);
            }

            Guid? tenantId = null;
            var tenantClaim = jwt.GetClaim("tenant_id");
            if (tenantClaim != null && !string.IsNullOrEmpty(tenantClaim.Value))
            {
                tenantId = Guid.Parse(tenantClaim.Value);
            }

            var allowedTenants = new List<Guid>();
            var allowedClaim = jwt.GetClaim("allowed_tenants");
            if (allowedClaim != null && !string.IsNullOrEmpty(allowedClaim.Value))
            {
                using var doc = JsonDocument.Parse(allowedClaim.Value);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var elem in doc.RootElement.EnumerateArray())
                    {
                        allowedTenants.Add(Guid.Parse(elem.GetString()!));
                    }
                }
            }

            OperationalScope? activeScope = null;
            var scopeClaim = jwt.GetClaim("active_scope");
            if (scopeClaim != null && !string.IsNullOrEmpty(scopeClaim.Value))
            {
                if (Enum.TryParse<OperationalScope>(scopeClaim.Value, false, out var parsedScope))
                {
                    activeScope = parsedScope;
                }
                else
                {
                    throw new ApiErrorException("ERR-AUTH-002");
                }
            }

            var roles = new List<string>();
            var rolesClaim = jwt.GetClaim("roles");
            if (rolesClaim != null && !string.IsNullOrEmpty(rolesClaim.Value))
            {
                using var doc = JsonDocument.Parse(rolesClaim.Value);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var elem in doc.RootElement.EnumerateArray())
                    {
                        roles.Add(elem.GetString()!);
                    }
                }
            }

            var permissions = new List<string>();
            var permissionsClaim = jwt.GetClaim("permissions");
            if (permissionsClaim != null && !string.IsNullOrEmpty(permissionsClaim.Value))
            {
                using var doc = JsonDocument.Parse(permissionsClaim.Value);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var elem in doc.RootElement.EnumerateArray())
                    {
                        permissions.Add(elem.GetString()!);
                    }
                }
            }

            Guid? actorUserId = null;
            string? ticketId = null;
            var actClaim = jwt.GetClaim("act");
            if (actClaim != null && !string.IsNullOrEmpty(actClaim.Value))
            {
                using var doc = JsonDocument.Parse(actClaim.Value);
                if (doc.RootElement.TryGetProperty("sub", out var actSub) &&
                    doc.RootElement.TryGetProperty("ticket_id", out var actTicket))
                {
                    actorUserId = Guid.Parse(actSub.GetString()!);
                    ticketId = actTicket.GetString();
                }
            }

            Guid? familyId = null;
            var fidClaim = jwt.GetClaim("fid");
            if (fidClaim != null && !string.IsNullOrEmpty(fidClaim.Value))
            {
                familyId = Guid.Parse(fidClaim.Value);
            }

            // Check invariantes
            if (actClaim != null && familyId != null)
            {
                throw new ApiErrorException("ERR-AUTH-002");
            }
            if (actClaim == null && familyId == null)
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
            throw new ApiErrorException("ERR-AUTH-002");
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
