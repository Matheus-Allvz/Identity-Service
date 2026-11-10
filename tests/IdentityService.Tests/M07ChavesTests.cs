using System.Net;
using System.Text.Json;
using IdentityService.Modules.M00Transversal.Auth;
using IdentityService.Modules.M00Transversal.Errors;
using IdentityService.Modules.M07Chaves;
using IdentityService.Modules.M07Chaves.Interface;
using IdentityService.Modules.M11Esquema.Persistence.Enums;
using IdentityService.Tests.Fixtures;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace IdentityService.Tests;

[Trait("Fase", "F00")]
public class M07ChavesTests
{
    private readonly TestSigningKey _signingKey = new("key-2026-01");
    private readonly TestTimeProvider _timeProvider = new(new DateTimeOffset(2026, 11, 2, 22, 0, 0, TimeSpan.Zero));
    private readonly IConfiguration _configuration = new ConfigurationBuilder().Build();

    private TokenIssuer CreateIssuer(ISigningKey? signingKey = null)
    {
        return new TokenIssuer(signingKey ?? _signingKey, _timeProvider, _configuration);
    }

    [Fact(Timeout = 60000)]
    public async Task CA_11_1_Borda_EmissaoTokenAcesso_SingleTenant()
    {
        var issuer = CreateIssuer();
        var sub = Guid.NewGuid();
        var familyId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var groupId = Guid.NewGuid();

        var claims = new AccessTokenClaims(
            Sub: sub,
            FamilyId: familyId,
            PlatformRole: PlatformRole.NONE,
            ViewMode: ViewMode.SINGLE,
            GroupId: groupId,
            TenantId: tenantId,
            AllowedTenants: new[] { tenantId },
            ActiveScope: OperationalScope.BUYER,
            Roles: new[] { "BUYER_OPERATOR" },
            Permissions: new[] { "orders:read" }
        );

        var token = issuer.Issue(claims);

        Assert.NotNull(token.AccessToken);
        Assert.Equal(claims.Sub, token.Claims.Sub);
        Assert.Equal(token.Iat + 900, token.Exp);

        var parts = token.AccessToken.Split('.');
        using var headerDoc = JsonDocument.Parse(Base64UrlEncoder.Decode(parts[0]));
        using var payloadDoc = JsonDocument.Parse(Base64UrlEncoder.Decode(parts[1]));

        Assert.Equal("RS256", headerDoc.RootElement.GetProperty("alg").GetString());
        Assert.Equal("key-2026-01", headerDoc.RootElement.GetProperty("kid").GetString());
        Assert.True(headerDoc.RootElement.TryGetProperty("typ", out var typElem) && typElem.GetString() == "JWT", headerDoc.RootElement.GetRawText());
        Assert.Equal(sub.ToString("D"), payloadDoc.RootElement.GetProperty("sub").GetString());
        Assert.Equal(tenantId.ToString("D"), payloadDoc.RootElement.GetProperty("tenant_id").GetString());
        await Task.CompletedTask;
    }

    [Fact(Timeout = 60000)]
    public async Task CA_11_2_Borda_EmissaoToken_RepresentacaoCanonicaListasVazias()
    {
        var issuer = CreateIssuer();
        var tenantId = Guid.NewGuid();
        var groupId = Guid.NewGuid();

        var claims = new AccessTokenClaims(
            Sub: Guid.NewGuid(),
            FamilyId: Guid.NewGuid(),
            PlatformRole: PlatformRole.NONE,
            ViewMode: ViewMode.SINGLE,
            GroupId: groupId,
            TenantId: tenantId,
            AllowedTenants: new[] { tenantId },
            ActiveScope: OperationalScope.BUYER,
            Roles: Array.Empty<string>(),
            Permissions: Array.Empty<string>()
        );

        var token = issuer.Issue(claims);
        var parts = token.AccessToken.Split('.');
        using var payloadDoc = JsonDocument.Parse(Base64UrlEncoder.Decode(parts[1]));

        var rolesJson = payloadDoc.RootElement.GetProperty("roles").GetRawText();
        var permsJson = payloadDoc.RootElement.GetProperty("permissions").GetRawText();
        Assert.Equal("[]", rolesJson);
        Assert.Equal("[]", permsJson);
        await Task.CompletedTask;
    }

    [Fact(Timeout = 60000)]
    public async Task CA_11_3_Borda_EmissaoTokenAcesso_MacroGrupo()
    {
        var issuer = CreateIssuer();
        var groupId = Guid.NewGuid();
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        var claims = new AccessTokenClaims(
            Sub: Guid.NewGuid(),
            FamilyId: Guid.NewGuid(),
            PlatformRole: PlatformRole.NONE,
            ViewMode: ViewMode.MACRO_GROUP,
            GroupId: groupId,
            TenantId: null,
            AllowedTenants: new[] { t1, t2 },
            ActiveScope: OperationalScope.BUYER,
            Roles: new[] { "GROUP_OPERATOR" },
            Permissions: new[] { "macro:read" }
        );

        var token = issuer.Issue(claims);
        var parts = token.AccessToken.Split('.');
        using var payloadDoc = JsonDocument.Parse(Base64UrlEncoder.Decode(parts[1]));

        Assert.Equal("MACRO_GROUP", payloadDoc.RootElement.GetProperty("view_mode").GetString());
        Assert.Equal(JsonValueKind.Null, payloadDoc.RootElement.GetProperty("tenant_id").ValueKind);
        Assert.Equal(groupId.ToString("D"), payloadDoc.RootElement.GetProperty("group_id").GetString());
        await Task.CompletedTask;
    }

    [Fact(Timeout = 60000)]
    public async Task CA_11_4_Borda_ValidacaoInvariantes_Impersonation()
    {
        var issuer = CreateIssuer();
        var tenantId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var act = new ActorClaim(Guid.NewGuid(), "TICKET-12345");

        // Impersonation with FamilyId present must throw ArgumentException
        var invalidClaims = new AccessTokenClaims(
            Sub: Guid.NewGuid(),
            FamilyId: Guid.NewGuid(),
            PlatformRole: PlatformRole.NONE,
            ViewMode: ViewMode.SINGLE,
            GroupId: groupId,
            TenantId: tenantId,
            AllowedTenants: new[] { tenantId },
            ActiveScope: OperationalScope.BUYER,
            Roles: new[] { "BUYER_OPERATOR" },
            Permissions: new[] { "orders:read" },
            Act: act
        );

        Assert.Throws<ArgumentException>(() => issuer.Issue(invalidClaims));

        // Valid impersonation token
        var validClaims = invalidClaims with { FamilyId = null };
        var token = issuer.Issue(validClaims);
        var parts = token.AccessToken.Split('.');
        using var payloadDoc = JsonDocument.Parse(Base64UrlEncoder.Decode(parts[1]));

        Assert.False(payloadDoc.RootElement.TryGetProperty("fid", out _));
        Assert.True(payloadDoc.RootElement.TryGetProperty("act", out var actElem));
        Assert.Equal(act.Sub.ToString("D"), actElem.GetProperty("sub").GetString());
        Assert.Equal("TICKET-12345", actElem.GetProperty("ticket_id").GetString());
        await Task.CompletedTask;
    }

    [Fact(Timeout = 60000)]
    public async Task CA_11_5_Borda_ValidacaoInvariantes_TokenOrdinario()
    {
        var issuer = CreateIssuer();
        var tenantId = Guid.NewGuid();
        var groupId = Guid.NewGuid();

        // Ordinary token with null FamilyId must throw ArgumentException
        var invalidClaims = new AccessTokenClaims(
            Sub: Guid.NewGuid(),
            FamilyId: null,
            PlatformRole: PlatformRole.NONE,
            ViewMode: ViewMode.SINGLE,
            GroupId: groupId,
            TenantId: tenantId,
            AllowedTenants: new[] { tenantId },
            ActiveScope: OperationalScope.BUYER,
            Roles: new[] { "BUYER_OPERATOR" },
            Permissions: new[] { "orders:read" }
        );

        Assert.Throws<ArgumentException>(() => issuer.Issue(invalidClaims));

        // Valid ordinary token
        var familyId = Guid.NewGuid();
        var validClaims = invalidClaims with { FamilyId = familyId };
        var token = issuer.Issue(validClaims);
        var parts = token.AccessToken.Split('.');
        using var payloadDoc = JsonDocument.Parse(Base64UrlEncoder.Decode(parts[1]));

        Assert.Equal(familyId.ToString("D"), payloadDoc.RootElement.GetProperty("fid").GetString());
        Assert.False(payloadDoc.RootElement.TryGetProperty("act", out _));
        await Task.CompletedTask;
    }

    [Fact(Timeout = 60000)]
    public async Task CA_11_6_Borda_OrdenacaoDeterministica_Listas()
    {
        var issuer = CreateIssuer();
        var t1 = Guid.Parse("0192e2c0-0002-7000-8000-000000000002");
        var t2 = Guid.Parse("0192e2c0-0001-7000-8000-000000000001");

        var claims = new AccessTokenClaims(
            Sub: Guid.NewGuid(),
            FamilyId: Guid.NewGuid(),
            PlatformRole: PlatformRole.NONE,
            ViewMode: ViewMode.MACRO_GROUP,
            GroupId: Guid.NewGuid(),
            TenantId: null,
            AllowedTenants: new[] { t1, t2 },
            ActiveScope: OperationalScope.BUYER,
            Roles: new[] { "ZEBRA", "ALPHA", "BETA" },
            Permissions: new[] { "write:users", "read:users", "admin:all" }
        );

        var token = issuer.Issue(claims);

        // Verification of sorted lists in normalized claims
        Assert.Equal(new[] { t2, t1 }, token.Claims.AllowedTenants);
        Assert.Equal(new[] { "ALPHA", "BETA", "ZEBRA" }, token.Claims.Roles);
        Assert.Equal(new[] { "admin:all", "read:users", "write:users" }, token.Claims.Permissions);
        await Task.CompletedTask;
    }

    [Fact(Timeout = 60000)]
    public async Task CA_11_7_Borda_RejeicaoChaveAssinaturaInvalida_OuIndisponivel()
    {
        var brokenKey = new TestSigningKey("broken-key") { ThrowOnKeyAccess = true };
        var issuer = CreateIssuer(brokenKey);
        var tenantId = Guid.NewGuid();
        var groupId = Guid.NewGuid();

        var claims = new AccessTokenClaims(
            Sub: Guid.NewGuid(),
            FamilyId: Guid.NewGuid(),
            PlatformRole: PlatformRole.NONE,
            ViewMode: ViewMode.SINGLE,
            GroupId: groupId,
            TenantId: tenantId,
            AllowedTenants: new[] { tenantId },
            ActiveScope: OperationalScope.BUYER,
            Roles: new[] { "BUYER_OPERATOR" },
            Permissions: new[] { "orders:read" }
        );

        var ex = Assert.Throws<System.Security.Cryptography.CryptographicException>(() => issuer.Issue(claims));
        Assert.NotNull(ex);
        await Task.CompletedTask;
    }

    [Fact(Timeout = 60000)]
    public async Task CA_11_8_Borda_ExposicaoChavesPublicas_EndpointJwks()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRouting();
        builder.Services.AddSingleton<ISigningKey>(_signingKey);
        builder.Services.AddM07Chaves();

        await using var app = builder.Build();
        app.UseRouting();
        app.MapM07Chaves();
        await app.StartAsync();

        var client = app.GetTestClient();
        var response = await client.GetAsync("/.well-known/jwks.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("public, max-age=3600, stale-while-revalidate=86400", response.Headers.CacheControl?.ToString());

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var keys = doc.RootElement.GetProperty("keys");
        Assert.Equal(1, keys.GetArrayLength());

        var key = keys[0];
        Assert.Equal("RSA", key.GetProperty("kty").GetString());
        Assert.Equal("sig", key.GetProperty("use").GetString());
        Assert.Equal("RS256", key.GetProperty("alg").GetString());
        Assert.Equal("key-2026-01", key.GetProperty("kid").GetString());
        Assert.False(string.IsNullOrEmpty(key.GetProperty("n").GetString()));
        Assert.False(string.IsNullOrEmpty(key.GetProperty("e").GetString()));
    }
}
