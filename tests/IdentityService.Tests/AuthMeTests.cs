using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using IdentityService.Modules.M00Transversal;
using IdentityService.Modules.M00Transversal.Auth;
using IdentityService.Modules.M00Transversal.Redis;
using IdentityService.Modules.M01Login;
using IdentityService.Modules.M02Refresh;
using IdentityService.Modules.M07Chaves;
using IdentityService.Modules.M07Chaves.Interface;
using IdentityService.Modules.M08Acessos;
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

// QW-02 / EP-11: GET /api/v1/auth/me
[Trait("Fase", "F06")]
[Collection("SharedContainers")]
public class AuthMeTests
{
    private readonly TestcontainersFixture _fixture;
    private readonly TestSigningKey _signingKey = new("key-2026-01");
    private readonly TestTimeProvider _timeProvider = new(new DateTimeOffset(2026, 11, 2, 22, 0, 0, TimeSpan.Zero));

    public AuthMeTests(TestcontainersFixture fixture)
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

        builder.Services.AddSingleton<ISigningKey>(_signingKey);
        builder.Services.AddSingleton<TimeProvider>(_timeProvider);

        builder.Services.AddM00Transversal(builder.Configuration);
        builder.Services.AddM01Login();
        builder.Services.AddM02Refresh();
        builder.Services.AddM07Chaves();
        builder.Services.AddM08Acessos();
        builder.Services.AddM11Esquema(builder.Configuration);
        builder.Services.AddM13Isolamento();
        builder.Services.AddM14Eventos(builder.Configuration);

        var app = builder.Build();
        app.UseM00Transversal();
        app.MapM01Login();
        return app;
    }

    private IssuedToken Issue(AccessTokenClaims claims) =>
        new TokenIssuer(_signingKey, _timeProvider, new ConfigurationBuilder().Build()).Issue(claims);

    private static async Task<(HttpResponseMessage Response, JsonElement Body)> GetMeAsync(HttpClient client, string? token)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        if (token != null)
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        var response = await client.SendAsync(req);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        return (response, body);
    }

    [Fact(Timeout = 60000)]
    public async Task CA_QW02_1_TokenSingle_DevolveClaimsDoTokenENoStore()
    {
        var tenantId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var issued = Issue(new AccessTokenClaims(
            Sub: Guid.NewGuid(),
            FamilyId: Guid.NewGuid(),
            PlatformRole: PlatformRole.NONE,
            ViewMode: ViewMode.SINGLE,
            GroupId: groupId,
            TenantId: tenantId,
            AllowedTenants: [tenantId],
            ActiveScope: OperationalScope.BUYER,
            Roles: ["BUYER_OPERATOR"],
            Permissions: ["produtos:criar", "demanda:criar"]));

        await using var app = CreateTestApp();
        await app.StartAsync();
        var (response, body) = await GetMeAsync(app.GetTestClient(), issued.AccessToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.Equal(issued.Claims.Sub.ToString("D"), body.GetProperty("sub").GetString());
        Assert.Equal("NONE", body.GetProperty("platform_role").GetString());
        Assert.Equal("SINGLE", body.GetProperty("view_mode").GetString());
        Assert.Equal(groupId.ToString("D"), body.GetProperty("group_id").GetString());
        Assert.Equal(tenantId.ToString("D"), body.GetProperty("tenant_id").GetString());
        Assert.Equal(tenantId.ToString("D"), body.GetProperty("allowed_tenants")[0].GetString());
        Assert.Equal("BUYER", body.GetProperty("active_scope").GetString());
        Assert.Equal("BUYER_OPERATOR", body.GetProperty("roles")[0].GetString());
        // RN-QW02.3: mesma ordenação do token (ordinal ascendente)
        Assert.Equal(["demanda:criar", "produtos:criar"], body.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()));
        Assert.Equal(JsonValueKind.Null, body.GetProperty("act").ValueKind);
        Assert.Equal(issued.Jti, body.GetProperty("jti").GetString());
        Assert.Equal(issued.Iat, body.GetProperty("iat").GetInt64());
        Assert.Equal(issued.Exp, body.GetProperty("exp").GetInt64());
    }

    [Fact(Timeout = 60000)]
    public async Task CA_QW02_2_TokenNone_DevolveNulosEListasVazias()
    {
        var issued = Issue(new AccessTokenClaims(
            Guid.NewGuid(), Guid.NewGuid(), PlatformRole.PLATFORM_ADMIN, ViewMode.NONE,
            null, null, [], null, [], []));

        await using var app = CreateTestApp();
        await app.StartAsync();
        var (response, body) = await GetMeAsync(app.GetTestClient(), issued.AccessToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("NONE", body.GetProperty("view_mode").GetString());
        Assert.Equal("PLATFORM_ADMIN", body.GetProperty("platform_role").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("group_id").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("tenant_id").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("active_scope").ValueKind);
        Assert.Equal(0, body.GetProperty("allowed_tenants").GetArrayLength());
        Assert.Equal(0, body.GetProperty("roles").GetArrayLength());
        Assert.Equal(0, body.GetProperty("permissions").GetArrayLength());
    }

    [Fact(Timeout = 60000)]
    public async Task CA_QW02_3_TokenDeImpersonation_DevolveAct()
    {
        var tenantId = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var issued = Issue(new AccessTokenClaims(
            Guid.NewGuid(), null, PlatformRole.NONE, ViewMode.SINGLE,
            Guid.NewGuid(), tenantId, [tenantId], OperationalScope.SUPPLIER, [], [],
            new ActorClaim(actor, "SUP-123")));

        await using var app = CreateTestApp();
        await app.StartAsync();
        var (response, body) = await GetMeAsync(app.GetTestClient(), issued.AccessToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(actor.ToString("D"), body.GetProperty("act").GetProperty("sub").GetString());
        Assert.Equal("SUP-123", body.GetProperty("act").GetProperty("ticket_id").GetString());
    }

    [Fact(Timeout = 60000)]
    public async Task CA_QW02_4_SemAuthorization_Devolve401Problem()
    {
        await using var app = CreateTestApp();
        await app.StartAsync();
        var (response, body) = await GetMeAsync(app.GetTestClient(), null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("ERR-AUTH-002", body.GetProperty("code").GetString());
    }

    [Fact(Timeout = 60000)]
    public async Task CA_QW02_5_JtiRevogado_Devolve401()
    {
        var issued = Issue(new AccessTokenClaims(
            Guid.NewGuid(), Guid.NewGuid(), PlatformRole.NONE, ViewMode.NONE,
            null, null, [], null, [], []));

        await using var app = CreateTestApp();
        await app.StartAsync();
        using (var scope = app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IDenylist>().RevokeJtiAsync(issued.Jti, issued.Exp);
        }

        var (response, body) = await GetMeAsync(app.GetTestClient(), issued.AccessToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("ERR-AUTH-002", body.GetProperty("code").GetString());
    }
}
