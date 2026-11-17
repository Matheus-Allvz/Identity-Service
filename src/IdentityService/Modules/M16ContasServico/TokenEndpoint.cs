using System.Text;
using System.Text.Json;
using IdentityService.Modules.M00Transversal.Audit;
using IdentityService.Modules.M00Transversal.Errors;
using IdentityService.Modules.M01Login.Interface;
using IdentityService.Modules.M07Chaves.Interface;
using IdentityService.Modules.M08Acessos.Interface;
using IdentityService.Modules.M11Esquema.Persistence;
using IdentityService.Modules.M11Esquema.Persistence.Enums;
using Microsoft.EntityFrameworkCore;

namespace IdentityService.Modules.M16ContasServico;

// EP-12 (NEW-06): OAuth 2.0 Client Credentials (RFC 6749 §4.4) para identidades de serviço.
// Erros no padrão RFC 9457 da plataforma (RN-NEW06.1), sem refresh token (§4.4.3).
public static class TokenEndpoint
{
    private const int MaxCredentialLength = 256;

    public static async Task<IResult> HandleAsync(
        HttpContext httpContext,
        IdentityDbContext dbContext,
        IPasswordHasher passwordHasher,
        ITokenIssuer tokenIssuer,
        IAccessQueries accessQueries,
        IAuditLog auditLog,
        CancellationToken cancellationToken)
    {
        var (grantType, clientId, clientSecret) = await ReadRequestAsync(httpContext.Request);

        if (grantType != "client_credentials" ||
            string.IsNullOrEmpty(clientId) || clientId.Length > MaxCredentialLength || clientId.Contains('\0') ||
            string.IsNullOrEmpty(clientSecret) || clientSecret.Length > MaxCredentialLength)
        {
            throw new ApiErrorException("ERR-VAL-001", "Pedido inválido ou malformado");
        }

        var account = await dbContext.ServiceAccounts
            .Include(a => a.Role)
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.ClientId == clientId, cancellationToken);

        // Resposta e tempo indistinguíveis entre cliente desconhecido, segredo errado e conta suspensa.
        if (account == null)
        {
            passwordHasher.VerifyPasswordDummy(clientSecret);
            throw new ApiErrorException("ERR-AUTH-001", "Credenciais inválidas");
        }

        if (!passwordHasher.VerifyPassword(clientSecret, account.ClientSecretHash) ||
            account.Status != EntityStatus.ACTIVE)
        {
            throw new ApiErrorException("ERR-AUTH-001", "Credenciais inválidas");
        }

        var tenantCheck = await accessQueries.CheckTenantAsync(account.TenantId, cancellationToken);
        if (!tenantCheck.Ok)
        {
            throw new ApiErrorException(tenantCheck.ErrorCode ?? "ERR-CTX-002");
        }

        var scope = account.Role.Scope;
        var scopeActive = await dbContext.TenantScopes
            .AsNoTracking()
            .AnyAsync(s => s.TenantId == account.TenantId && s.Scope == scope && s.IsActive, cancellationToken);
        if (!scopeActive)
        {
            throw new ApiErrorException("ERR-CTX-003");
        }

        var tenantInfo = await accessQueries.ResolveTenantAsync(account.TenantId, cancellationToken)
            ?? throw new ApiErrorException("ERR-CTX-002");

        var issuedToken = tokenIssuer.Issue(new AccessTokenClaims(
            Sub: account.Id,
            FamilyId: null,
            PlatformRole: PlatformRole.NONE,
            ViewMode: ViewMode.SINGLE,
            GroupId: tenantInfo.GroupId,
            TenantId: account.TenantId,
            AllowedTenants: [account.TenantId],
            ActiveScope: scope,
            Roles: [account.Role.Name],
            Permissions: account.Role.Permissions,
            Act: null,
            ServiceClientId: account.ClientId));

        // A conta de serviço não é um user_account: user_id fica nulo e o client_id vai nos metadados.
        auditLog.Append(
            AuditEventType.SERVICE_TOKEN_ISSUED,
            null,
            null,
            account.TenantId,
            tenantInfo.GroupId,
            null,
            new { client_id = account.ClientId });
        await dbContext.SaveChangesAsync(cancellationToken);

        httpContext.Response.Headers.CacheControl = "no-store";
        httpContext.Response.Headers.Pragma = "no-cache";

        return Results.Ok(new
        {
            access_token = issuedToken.AccessToken,
            token_type = "Bearer",
            expires_in = 900
        });
    }

    // Aceita form-urlencoded (padrão OAuth) ou JSON; credenciais no corpo ou em Authorization: Basic.
    private static async Task<(string? GrantType, string? ClientId, string? ClientSecret)> ReadRequestAsync(HttpRequest request)
    {
        string? grantType = null, clientId = null, clientSecret = null;

        if (request.HasFormContentType)
        {
            var form = await request.ReadFormAsync();
            grantType = form["grant_type"];
            clientId = form["client_id"];
            clientSecret = form["client_secret"];
        }
        else
        {
            using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
            var text = await reader.ReadToEndAsync();
            try
            {
                using var doc = JsonDocument.Parse(text);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    throw new ApiErrorException("ERR-VAL-001", "Pedido inválido ou malformado");
                }

                grantType = ReadString(doc.RootElement, "grant_type");
                clientId = ReadString(doc.RootElement, "client_id");
                clientSecret = ReadString(doc.RootElement, "client_secret");
            }
            catch (JsonException)
            {
                throw new ApiErrorException("ERR-VAL-001", "Pedido inválido ou malformado");
            }
        }

        var authorization = request.Headers.Authorization.ToString();
        if (authorization.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(authorization[6..].Trim()));
                var separator = decoded.IndexOf(':');
                if (separator <= 0)
                {
                    throw new ApiErrorException("ERR-VAL-001", "Pedido inválido ou malformado");
                }

                // RFC 6749 §2.3.1: id e segredo vêm form-urlencoded dentro do Basic.
                clientId = Uri.UnescapeDataString(decoded[..separator]);
                clientSecret = Uri.UnescapeDataString(decoded[(separator + 1)..]);
            }
            catch (FormatException)
            {
                throw new ApiErrorException("ERR-VAL-001", "Pedido inválido ou malformado");
            }
        }

        return (grantType, clientId, clientSecret);
    }

    private static string? ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String ? prop.GetString() : null;
}
