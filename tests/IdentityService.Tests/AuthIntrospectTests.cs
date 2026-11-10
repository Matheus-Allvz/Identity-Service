using System.Net;
using System.Text;
using System.Text.Json;
using IdentityService.Modules.M00Transversal;
using IdentityService.Modules.M00Transversal.Auth;
using IdentityService.Modules.M00Transversal.Redis;
using IdentityService.Modules.M01Login;
using IdentityService.Modules.M02Refresh;
using IdentityService.Modules.M03Contexto;
using IdentityService.Modules.M06Impersonation;
using IdentityService.Modules.M07Chaves;
using IdentityService.Modules.M07Chaves.Interface;
using IdentityService.Modules.M08Acessos;
using IdentityService.Modules.M09Logout;
using IdentityService.Modules.M11Esquema;
using IdentityService.Modules.M11Esquema.Persistence.Enums;
using IdentityService.Modules.M13Isolamento;
using IdentityService.Modules.M14Eventos;
using IdentityService.Tests.Fixtures;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IdentityService.Tests;

[Trait("Fase", "F01")]
[Collection("SharedContainers")]
public class AuthIntrospectTests
{
    private readonly TestcontainersFixture _fixture;
    private readonly TestSigningKey _signingKey = new("key-2026-01");
    private readonly TestTimeProvider _timeProvider = new(new DateTimeOffset(2026, 11, 2, 22, 0, 0, TimeSpan.Zero));

    public AuthIntrospectTests(TestcontainersFixture fixture)
    {
        _fixture = fixture;
    }

    private WebApplication CreateTestApp()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRouting();

        builder.Configuration["DB_CONNECTION_STRING"] = _fixture.GetConnectionString();
        builder.Configuration["REDIS_CONNECTION_STRING"] = _fixture.GetRedisConnectionString();
        builder.Configuration["KAFKA_BOOTSTRAP_SERVERS"] = _fixture.GetKafkaBootstrapServers();
        builder.Configuration["JWT_ISSUER"] = "identity-service";
        builder.Configuration["JWT_AUDIENCE"] = "b2b-platform";

        builder.Services.AddSingleton<ISigningKey>(_signingKey);
        builder.Services.AddSingleton<TimeProvider>(_timeProvider);

        builder.Services.AddM00Transversal(builder.Configuration);
        builder.Services.AddM01Login();
        builder.Services.AddM02Refresh();
        builder.Services.AddM03Contexto();
        builder.Services.AddM06Impersonation();
        builder.Services.AddM07Chaves();
        builder.Services.AddM08Acessos();
        builder.Services.AddM09Logout();
        builder.Services.AddM11Esquema(builder.Configuration);
        builder.Services.AddM13Isolamento();
        builder.Services.AddM14Eventos(builder.Configuration);

        var app = builder.Build();
        app.UseM00Transversal();
        app.MapM01Login();
        app.MapM02Refresh();
        app.MapM03Contexto();
        app.MapM06Impersonation();
        app.MapM07Chaves();
        app.MapM09Logout();

        return app;
    }

    private IssuedToken IssueSampleToken(ISigningKey? signingKey = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JWT_ISSUER"] = "identity-service",
                ["JWT_AUDIENCE"] = "b2b-platform"
            })
            .Build();

        var issuer = new TokenIssuer(signingKey ?? _signingKey, _timeProvider, config);
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
            Permissions: new[] { "demanda:criar", "produtos:criar" }
        );

        return issuer.Issue(claims);
    }

    [Fact(Timeout = 60000)]
    public async Task Introspect_ComTokenValido_RetornaActiveTrueEWarningEHeader()
    {
        await using var app = CreateTestApp();
        await app.StartAsync();
        var client = app.GetTestClient();

        var issued = IssueSampleToken();
        var payload = JsonSerializer.Serialize(new { token = issued.AccessToken });
        var content = new StringContent(payload, Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/v1/auth/introspect", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("X-B2B-Notice"));
        Assert.Equal(
            "Diagnostic-Only; Local JWKS caching required for production microservices",
            response.Headers.GetValues("X-B2B-Notice").Single());

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.GetProperty("active").GetBoolean());
        Assert.Equal(issued.Claims.Sub.ToString("D"), root.GetProperty("sub").GetString());
        Assert.Equal(issued.Claims.TenantId?.ToString("D"), root.GetProperty("tenant_id").GetString());
        Assert.Equal(issued.Claims.GroupId?.ToString("D"), root.GetProperty("group_id").GetString());
        Assert.Equal("BUYER", root.GetProperty("active_scope").GetString());
        Assert.Equal("SINGLE", root.GetProperty("view_mode").GetString());
        Assert.Equal(issued.Exp, root.GetProperty("exp").GetInt64());
        Assert.Equal(issued.Iat, root.GetProperty("iat").GetInt64());

        var roles = root.GetProperty("roles").EnumerateArray().Select(x => x.GetString()).ToArray();
        Assert.Contains("BUYER_OPERATOR", roles);

        var perms = root.GetProperty("permissions").EnumerateArray().Select(x => x.GetString()).ToArray();
        Assert.Contains("produtos:criar", perms);
        Assert.Contains("demanda:criar", perms);

        Assert.Equal(
            "Este endpoint destina-se estritamente a diagnóstico em desenvolvimento. Microsserviços em produção DEVEM validar tokens localmente via JWKS (RS256) com cache em memória.",
            root.GetProperty("warning").GetString());
    }

    [Fact(Timeout = 60000)]
    public async Task Introspect_ComFormUrlEncoded_RetornaActiveTrue()
    {
        await using var app = CreateTestApp();
        await app.StartAsync();
        var client = app.GetTestClient();

        var issued = IssueSampleToken();
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["token"] = issued.AccessToken
        });

        var response = await client.PostAsync("/api/v1/auth/introspect", form);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("active").GetBoolean());
    }

    [Fact(Timeout = 60000)]
    public async Task Introspect_ComBearerPrefixNoToken_RetornaActiveTrue()
    {
        await using var app = CreateTestApp();
        await app.StartAsync();
        var client = app.GetTestClient();

        var issued = IssueSampleToken();
        var payload = JsonSerializer.Serialize(new { token = $"Bearer {issued.AccessToken}" });
        var content = new StringContent(payload, Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/v1/auth/introspect", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("active").GetBoolean());
    }

    [Fact(Timeout = 60000)]
    public async Task Introspect_ComTokenExpirado_RetornaActiveFalse()
    {
        await using var app = CreateTestApp();
        await app.StartAsync();
        var client = app.GetTestClient();

        var issued = IssueSampleToken();

        // Advance time beyond expiration (token lifespan is 900 seconds)
        _timeProvider.Advance(TimeSpan.FromSeconds(950));

        try
        {
            var payload = JsonSerializer.Serialize(new { token = issued.AccessToken });
            var content = new StringContent(payload, Encoding.UTF8, "application/json");

            var response = await client.PostAsync("/api/v1/auth/introspect", content);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.Contains("X-B2B-Notice"));

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            Assert.False(doc.RootElement.GetProperty("active").GetBoolean());
        }
        finally
        {
            _timeProvider.SetUtcNow(new DateTimeOffset(2026, 11, 2, 22, 0, 0, TimeSpan.Zero));
        }
    }

    [Fact(Timeout = 60000)]
    public async Task Introspect_ComAssinaturaForjada_RetornaActiveFalse()
    {
        await using var app = CreateTestApp();
        await app.StartAsync();
        var client = app.GetTestClient();

        var forgedKey = new TestSigningKey("forged-key");
        var issued = IssueSampleToken(forgedKey);

        var payload = JsonSerializer.Serialize(new { token = issued.AccessToken });
        var content = new StringContent(payload, Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/v1/auth/introspect", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("X-B2B-Notice"));

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("active").GetBoolean());
    }

    [Fact(Timeout = 60000)]
    public async Task Introspect_ComTokenRevogadoNoRedis_RetornaActiveFalse()
    {
        await using var app = CreateTestApp();
        await app.StartAsync();
        var client = app.GetTestClient();

        var issued = IssueSampleToken();

        // Revoke token JTI in Redis denylist
        var denylist = app.Services.GetRequiredService<IDenylist>();
        await denylist.RevokeJtiAsync(issued.Jti, issued.Exp);

        var payload = JsonSerializer.Serialize(new { token = issued.AccessToken });
        var content = new StringContent(payload, Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/v1/auth/introspect", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("X-B2B-Notice"));

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("active").GetBoolean());
    }

    [Theory(Timeout = 60000)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid-jwt-token")]
    [InlineData("{}")]
    [InlineData("{\"token\": null}")]
    [InlineData("{\"token\": \"\"}")]
    public async Task Introspect_ComTokenInvalidoOuVazio_RetornaActiveFalse(string body)
    {
        await using var app = CreateTestApp();
        await app.StartAsync();
        var client = app.GetTestClient();

        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/api/v1/auth/introspect", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("X-B2B-Notice"));

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("active").GetBoolean());
    }

    [Fact(Timeout = 60000)]
    public async Task Introspect_AnonimoSemAuthHeader_ExecutaComSucesso()
    {
        await using var app = CreateTestApp();
        await app.StartAsync();
        var client = app.GetTestClient();

        // Verify request does NOT send Authorization header
        client.DefaultRequestHeaders.Authorization = null;

        var issued = IssueSampleToken();
        var payload = JsonSerializer.Serialize(new { token = issued.AccessToken });
        var content = new StringContent(payload, Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/v1/auth/introspect", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("active").GetBoolean());
    }
}
