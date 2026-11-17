using System.Text.Json;
using IdentityService.Modules.M00Transversal.Auth;
using IdentityService.Modules.M07Chaves.Interface;
using IdentityService.Modules.M11Esquema.Persistence.Enums;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace IdentityService.Modules.M07Chaves;

public class TokenIssuer : ITokenIssuer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly ISigningKey _signingKey;
    private readonly TimeProvider _timeProvider;
    private readonly IConfiguration _configuration;

    public TokenIssuer(
        ISigningKey signingKey,
        TimeProvider timeProvider,
        IConfiguration configuration)
    {
        _signingKey = signingKey;
        _timeProvider = timeProvider;
        _configuration = configuration;
    }

    public IssuedToken Issue(AccessTokenClaims claims)
    {
        ArgumentNullException.ThrowIfNull(claims);

        // RN-95: Validate ViewMode invariants
        if (claims.ViewMode == ViewMode.NONE)
        {
            if (claims.GroupId != null ||
                claims.TenantId != null ||
                claims.AllowedTenants.Count > 0 ||
                claims.ActiveScope != null ||
                claims.Roles.Count > 0 ||
                claims.Permissions.Count > 0)
            {
                throw new ArgumentException("ViewMode.NONE must not contain tenant or scope context.");
            }
        }
        else if (claims.ViewMode == ViewMode.SINGLE)
        {
            if (claims.GroupId == null ||
                claims.TenantId == null ||
                claims.ActiveScope == null ||
                claims.AllowedTenants.Count != 1 ||
                claims.AllowedTenants[0] != claims.TenantId.Value)
            {
                throw new ArgumentException("ViewMode.SINGLE must contain exactly 1 active tenant matching TenantId.");
            }
        }
        else if (claims.ViewMode == ViewMode.MACRO_GROUP)
        {
            if (claims.GroupId == null ||
                claims.TenantId != null ||
                claims.ActiveScope == null ||
                claims.AllowedTenants.Count is < 1 or > 50)
            {
                throw new ArgumentException("ViewMode.MACRO_GROUP must contain 1 to 50 allowed tenants and null TenantId.");
            }
        }

        // RN-96: Validate impersonation invariants
        if (claims.ServiceClientId != null)
        {
            // NEW-06: token de serviço não tem sessão (fid) nem delegação (act) e atua sempre num único tenant.
            if (claims.FamilyId != null || claims.Act != null || claims.ViewMode != ViewMode.SINGLE)
            {
                throw new ArgumentException("Service tokens must be SINGLE and omit family_id and act.");
            }
        }
        else if (claims.Act != null)
        {
            if (claims.FamilyId != null)
            {
                throw new ArgumentException("Impersonation tokens must omit family_id.");
            }
        }
        else
        {
            if (claims.FamilyId == null)
            {
                throw new ArgumentException("Non-impersonation tokens must have family_id.");
            }
        }

        // RN-99: Obtain key (throws CryptographicException if broken)
        var key = _signingKey.SigningKey;

        // RN-93, RN-94
        var now = _timeProvider.GetUtcNow();
        var iat = now.ToUnixTimeSeconds();
        var exp = iat + 900;
        var jti = Guid.CreateVersion7(now).ToString("D");

        // RN-97: Ordinal ascending sorting
        var sortedAllowedTenants = claims.AllowedTenants.OrderBy(g => g).ToList();
        var sortedRoles = claims.Roles.OrderBy(r => r, StringComparer.Ordinal).ToList();
        var sortedPermissions = claims.Permissions.OrderBy(p => p, StringComparer.Ordinal).ToList();

        var normalizedClaims = new AccessTokenClaims(
            claims.Sub,
            claims.FamilyId,
            claims.PlatformRole,
            claims.ViewMode,
            claims.GroupId,
            claims.TenantId,
            sortedAllowedTenants,
            claims.ActiveScope,
            sortedRoles,
            sortedPermissions,
            claims.Act,
            claims.ServiceClientId);

        var issuer = _configuration["JWT_ISSUER"] ?? "identity-service";
        var audience = _configuration["JWT_AUDIENCE"] ?? "b2b-platform";

        var payloadDict = new Dictionary<string, object?>
        {
            ["jti"] = jti,
            ["sub"] = claims.Sub.ToString("D"),
            ["iat"] = iat,
            ["exp"] = exp,
            ["iss"] = issuer,
            ["aud"] = audience,
            ["platform_role"] = claims.PlatformRole.ToString(),
            ["view_mode"] = claims.ViewMode.ToString(),
            ["group_id"] = claims.GroupId?.ToString("D"),
            ["tenant_id"] = claims.TenantId?.ToString("D"),
            ["allowed_tenants"] = sortedAllowedTenants.Select(g => g.ToString("D")).ToArray(),
            ["active_scope"] = claims.ActiveScope?.ToString(),
            ["roles"] = sortedRoles.ToArray(),
            ["permissions"] = sortedPermissions.ToArray()
        };

        if (claims.ServiceClientId != null)
        {
            payloadDict["client_id"] = claims.ServiceClientId;
            payloadDict["token_use"] = "service";
        }
        else if (claims.Act != null)
        {
            payloadDict["act"] = new Dictionary<string, object?>
            {
                ["sub"] = claims.Act.Sub.ToString("D"),
                ["ticket_id"] = claims.Act.TicketId
            };
        }
        else
        {
            payloadDict["fid"] = claims.FamilyId!.Value.ToString("D");
        }

        var payloadJson = JsonSerializer.Serialize(payloadDict, JsonOptions);

        // RN-98: Sign JWT with RS256
        var credentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256);
        var handler = new JsonWebTokenHandler();
        var accessToken = handler.CreateToken(payloadJson, credentials);

        return new IssuedToken(accessToken, jti, iat, exp, normalizedClaims);
    }
}
