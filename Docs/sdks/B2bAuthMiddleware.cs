// Validação offline (RS256 + JWKS) dos access tokens do Identity-Service para ASP.NET Core.
// Copie este arquivo para o seu serviço. Dependência: Microsoft.AspNetCore.Authentication.JwtBearer.
//
//   builder.Services.AddB2bAuth();                       // lê IDENTITY_BASE_URL
//   app.UseAuthentication(); app.UseAuthorization();
//   app.MapPost("/ofertas", (ClaimsPrincipal u) => u.ToB2bContext().TenantId)
//      .RequireScope("SUPPLIER").RequirePermission("fornecimento:publicar");
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace B2bAuth;

/// <summary>Claim <c>act</c> (RFC 8693): presente apenas em tokens de impersonation.</summary>
public sealed record B2bActor(Guid Sub, string? TicketId);

/// <summary>Contexto tipado extraído do token já validado.</summary>
public sealed record B2bContext(
    Guid UserId, string Jti, string PlatformRole, string ViewMode, Guid? GroupId, Guid? TenantId,
    IReadOnlyList<Guid> AllowedTenants, string? ActiveScope, IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions, Guid? FamilyId, B2bActor? Actor);

public static class B2bAuthExtensions
{
    private const string Issuer = "identity-service";
    private const string Audience = "b2b-platform";
    private const string ErrorTypeBase = "https://identity.b2b.local/errors/"; // mesmo prefixo usado pelo Identity-Service
    private static readonly JsonSerializerOptions ProblemJson = new();

    /// <summary>
    /// Registra autenticação JwtBearer contra o JWKS do Identity-Service e a política padrão de autorização.
    /// Tokens pré-contexto (<c>view_mode = NONE</c>) recebem 403, a menos que <paramref name="allowPreContext"/> seja true.
    /// </summary>
    public static IServiceCollection AddB2bAuth(
        this IServiceCollection services, string? identityBaseUrl = null, bool allowPreContext = false)
    {
        identityBaseUrl ??= Environment.GetEnvironmentVariable("IDENTITY_BASE_URL") ?? "https://identity.matheus-alves.dev";
        var jwksUrl = identityBaseUrl.TrimEnd('/') + "/.well-known/jwks.json";

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.MapInboundClaims = false; // mantém os nomes originais dos claims (sub, roles, ...)
            // O serviço expõe só o JWKS (sem discovery OIDC): o retriever abaixo lê as chaves direto dele.
            // Cache de 1h (max-age do JWKS); `kid` desconhecido dispara um refetch (no máx. 1x a cada 30s).
            options.ConfigurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                jwksUrl, new JwksRetriever(),
                new HttpDocumentRetriever { RequireHttps = jwksUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) })
            {
                AutomaticRefreshInterval = TimeSpan.FromHours(1),
                RefreshInterval = TimeSpan.FromSeconds(30),
            };
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = Issuer,
                ValidAudience = Audience,
                ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                ValidTypes = ["JWT"],
                ClockSkew = TimeSpan.FromSeconds(60),
                RequireExpirationTime = true,
                RequireSignedTokens = true,
                NameClaimType = "sub",
                RoleClaimType = "roles",
            };
            options.Events = new JwtBearerEvents
            {
                OnChallenge = ctx =>
                {
                    ctx.HandleResponse(); // substitui o 401 vazio padrão pelo problem+json
                    ctx.Response.Headers.WWWAuthenticate = "Bearer error=\"invalid_token\"";
                    return WriteProblem(ctx.HttpContext, 401, "ERR-AUTH-002",
                        "Token de acesso ausente, inválido, expirado ou revogado", "Token ausente, inválido ou expirado");
                },
                OnForbidden = ctx => WriteProblem(ctx.HttpContext, 403, "ERR-CTX-007",
                    "Violação de isolamento de contexto ou permissão insuficiente",
                    "Permissão, escopo ou contexto ativo insuficiente para este recurso"),
            };
        });

        // Política padrão ([Authorize] e RequireAuthorization): autenticado e com contexto ativo.
        services.AddAuthorizationBuilder().SetDefaultPolicy(new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireAssertion(c => allowPreContext || c.User.FindFirstValue("view_mode") != "NONE")
            .Build());
        return services;
    }

    /// <summary>Exige TODAS as permissões informadas (formato <c>dominio:recurso:acao</c>).</summary>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder endpoint, params string[] permissions)
        where TBuilder : IEndpointConventionBuilder =>
        // RequireAuthorization também aplica a política padrão (bloqueio de token pré-contexto).
        endpoint.RequireAuthorization(p => p.RequireAssertion(c =>
            permissions.All(perm => c.User.HasClaim("permissions", perm))));

    /// <summary>Exige que o escopo ativo seja um dos informados.</summary>
    public static TBuilder RequireScope<TBuilder>(this TBuilder endpoint, params string[] scopes)
        where TBuilder : IEndpointConventionBuilder =>
        endpoint.RequireAuthorization(p => p.RequireAssertion(c =>
            scopes.Contains(c.User.FindFirstValue("active_scope"))));

    /// <summary>Converte o <see cref="ClaimsPrincipal"/> autenticado no contexto tipado.</summary>
    public static B2bContext ToB2bContext(this ClaimsPrincipal user)
    {
        // Claims nulos no JWT (tenant_id, group_id, active_scope) chegam como string vazia.
        string? Str(string type) => user.FindFirstValue(type) is { Length: > 0 } v ? v : null;
        Guid? Id(string type) => Guid.TryParse(Str(type), out var g) ? g : null;
        List<string> Many(string type) => user.FindAll(type).Select(c => c.Value).Where(v => v.Length > 0).ToList();

        B2bActor? actor = null;
        if (Str("act") is { } actJson) // claim objeto chega como JSON bruto
        {
            using var doc = JsonDocument.Parse(actJson);
            var ticket = doc.RootElement.TryGetProperty("ticket_id", out var t) && t.ValueKind == JsonValueKind.String
                ? t.GetString() : null;
            actor = new B2bActor(doc.RootElement.GetProperty("sub").GetGuid(), ticket);
        }

        return new B2bContext(
            Id("sub") ?? throw new InvalidOperationException("Usuário não autenticado (claim sub ausente)."),
            Str("jti") ?? "", Str("platform_role") ?? "NONE", Str("view_mode") ?? "NONE", Id("group_id"), Id("tenant_id"),
            Many("allowed_tenants").Select(Guid.Parse).ToList(), Str("active_scope"), Many("roles"), Many("permissions"),
            Id("fid"), actor);
    }

    /// <summary>Resposta RFC 9457; propaga X-Correlation-Id se for um UUID válido, senão gera um novo.</summary>
    private static Task WriteProblem(HttpContext http, int status, string code, string title, string detail)
    {
        var cid = (Guid.TryParseExact(http.Request.Headers["X-Correlation-Id"].ToString(), "D", out var received)
            ? received : Guid.NewGuid()).ToString("D");
        http.Response.StatusCode = status;
        http.Response.Headers["X-Correlation-Id"] = cid;
        var body = new Dictionary<string, object>
        {
            ["type"] = ErrorTypeBase + code, ["title"] = title, ["status"] = status,
            ["code"] = code, ["detail"] = detail, ["correlation_id"] = cid,
        };
        return http.Response.WriteAsJsonAsync(body, ProblemJson, "application/problem+json");
    }

    /// <summary>Monta a "configuração" do JwtBearer só com as chaves do JWKS.</summary>
    private sealed class JwksRetriever : IConfigurationRetriever<OpenIdConnectConfiguration>
    {
        public async Task<OpenIdConnectConfiguration> GetConfigurationAsync(
            string address, IDocumentRetriever retriever, CancellationToken cancel)
        {
            var config = new OpenIdConnectConfiguration { Issuer = Issuer };
            foreach (var key in new JsonWebKeySet(await retriever.GetDocumentAsync(address, cancel)).GetSigningKeys())
                config.SigningKeys.Add(key);
            return config;
        }
    }
}
