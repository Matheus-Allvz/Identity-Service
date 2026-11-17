using System.Net;
using System.Text;
using System.Text.Json;
using IdentityService.Modules.M00Transversal;
using IdentityService.Modules.M00Transversal.Auth;
using IdentityService.Modules.M01Login;
using IdentityService.Modules.M02Refresh;
using IdentityService.Modules.M03Contexto;
using IdentityService.Modules.M07Chaves;
using IdentityService.Modules.M08Acessos;
using IdentityService.Modules.M11Esquema;
using IdentityService.Modules.M11Esquema.Persistence.Entities;
using IdentityService.Modules.M11Esquema.Persistence.Enums;
using IdentityService.Modules.M13Isolamento;
using IdentityService.Modules.M14Eventos;
using IdentityService.Tests.Fixtures;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace IdentityService.Tests;

// QW-01: auto-contexto opt-in no POST /api/v1/auth/login
[Trait("Fase", "F06")]
[Collection("SharedContainers")]
public class AuthLoginAutoContextTests
{
    private const string Email = "comprador@example.test";
    private const string Password = "PasswordSegura123!";
    private static readonly Guid RoleBuyerOperator = Guid.Parse("01900000-0000-7000-8000-000000000001");

    private readonly TestcontainersFixture _fixture;
    private readonly TestSigningKey _signingKey = new("key-2026-01");
    private readonly TestTimeProvider _timeProvider = new(new DateTimeOffset(2026, 11, 2, 22, 0, 0, TimeSpan.Zero));
    private readonly PasswordHasher _passwordHasher = new();

    public AuthLoginAutoContextTests(TestcontainersFixture fixture)
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
        builder.Services.AddM03Contexto();
        builder.Services.AddM07Chaves();
        builder.Services.AddM08Acessos();
        builder.Services.AddM11Esquema(builder.Configuration);
        builder.Services.AddM13Isolamento();
        builder.Services.AddM14Eventos(builder.Configuration);

        var app = builder.Build();
        app.UseM00Transversal();
        app.MapM01Login();
        app.MapM02Refresh();
        app.MapM03Contexto();
        return app;
    }

    // Semeia um utilizador BUYER_OPERATOR vinculado a `tenantCount` tenants do mesmo grupo (escopo BUYER em cada um).
    private async Task<(Guid UserId, Guid GroupId, Guid[] TenantIds)> SeedAsync(int tenantCount)
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();
        var userId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var tenantIds = Enumerable.Range(0, tenantCount).Select(_ => Guid.NewGuid()).ToArray();

        await using var db = _fixture.CreateDbContext();
        db.EconomicGroups.Add(new EconomicGroup { Id = groupId, Name = "Grupo QW01", Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now });
        db.UserAccounts.Add(new UserAccount
        {
            Id = userId, Email = Email, PasswordHash = _passwordHasher.HashPassword(Password), Name = "Comprador QW01",
            Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.NONE, CreatedAt = now, UpdatedAt = now
        });

        for (var i = 0; i < tenantIds.Length; i++)
        {
            var memberId = Guid.NewGuid();
            db.Tenants.Add(new Tenant
            {
                Id = tenantIds[i], GroupId = groupId, Cnpj = $"9900000{i}000101", LegalName = $"Tenant QW01 {i}",
                BranchType = i == 0 ? BranchType.HQ : BranchType.BRANCH, GeoLat = 0, GeoLng = 0,
                Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now
            });
            db.TenantScopes.Add(new TenantScope { Id = Guid.NewGuid(), TenantId = tenantIds[i], Scope = OperationalScope.BUYER, IsActive = true, CreatedAt = now });
            db.TenantMembers.Add(new TenantMember { Id = memberId, TenantId = tenantIds[i], UserId = userId, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now });
            db.MemberRoles.Add(new MemberRole { Id = Guid.NewGuid(), TenantId = tenantIds[i], TenantMemberId = memberId, RoleId = RoleBuyerOperator, CreatedAt = now });
        }

        await db.SaveChangesAsync();
        return (userId, groupId, tenantIds);
    }

    private static async Task<(HttpResponseMessage Response, JsonElement Body)> LoginAsync(HttpClient client, object payload)
    {
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/api/v1/auth/login", content);
        return (response, JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement);
    }

    private static JsonElement Claims(JsonElement body) =>
        JsonDocument.Parse(Base64UrlEncoder.Decode(body.GetProperty("access_token").GetString()!.Split('.')[1])).RootElement;

    [Fact(Timeout = 60000)]
    public async Task CA_QW01_1_SemCamposNovos_MantemRespostaETokenNone()
    {
        await SeedAsync(1);
        await using var app = CreateTestApp();
        await app.StartAsync();

        var (response, body) = await LoginAsync(app.GetTestClient(), new { email = Email, password = Password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            ["access_token", "token_type", "expires_in", "refresh_token", "refresh_expires_in", "user", "available_contexts"],
            body.EnumerateObject().Select(p => p.Name));
        Assert.Equal("NONE", Claims(body).GetProperty("view_mode").GetString());
    }

    [Fact(Timeout = 60000)]
    public async Task CA_QW01_2_AlvoExplicito_EmiteTokenSingleEPersisteContextoNaFamilia()
    {
        var (userId, groupId, tenants) = await SeedAsync(2);
        await using var app = CreateTestApp();
        await app.StartAsync();
        var client = app.GetTestClient();

        var (response, body) = await LoginAsync(client, new
        {
            email = Email, password = Password, target_tenant_id = tenants[1].ToString("D"), target_scope = "BUYER"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var claims = Claims(body);
        Assert.Equal("SINGLE", claims.GetProperty("view_mode").GetString());
        Assert.Equal(tenants[1].ToString("D"), claims.GetProperty("tenant_id").GetString());
        Assert.Equal(groupId.ToString("D"), claims.GetProperty("group_id").GetString());
        Assert.Equal("BUYER", claims.GetProperty("active_scope").GetString());
        Assert.Equal("BUYER_OPERATOR", claims.GetProperty("roles")[0].GetString());
        Assert.Contains("demanda:criar", claims.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()));

        var context = body.GetProperty("context");
        Assert.Equal("SINGLE", context.GetProperty("view_mode").GetString());
        Assert.Equal(tenants[1].ToString("D"), context.GetProperty("tenant_id").GetString());
        Assert.Equal(tenants[1].ToString("D"), context.GetProperty("allowed_tenants")[0].GetString());

        await using (var db = _fixture.CreateDbContext())
        {
            var family = await db.RefreshTokens.SingleAsync(r => r.UserId == userId);
            Assert.Equal(ViewMode.SINGLE, family.ViewMode);
            Assert.Equal(tenants[1], family.ActiveTenantId);
            Assert.Equal(OperationalScope.BUYER, family.ActiveScope);

            var events = await db.AuthAuditLogs.Where(l => l.UserId == userId).Select(l => l.EventType).ToListAsync();
            Assert.Contains(AuditEventType.LOGIN_SUCCESS, events);
            Assert.Contains(AuditEventType.CONTEXT_SWITCHED, events);
        }

        // O refresh herda o contexto com que a família nasceu.
        var refresh = await client.PostAsync("/api/v1/auth/refresh", new StringContent(
            JsonSerializer.Serialize(new { refresh_token = body.GetProperty("refresh_token").GetString() }), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        var refreshed = Claims(JsonDocument.Parse(await refresh.Content.ReadAsStringAsync()).RootElement);
        Assert.Equal("SINGLE", refreshed.GetProperty("view_mode").GetString());
        Assert.Equal(tenants[1].ToString("D"), refreshed.GetProperty("tenant_id").GetString());
    }

    [Fact(Timeout = 60000)]
    public async Task CA_QW01_3_AutoContextoComUmUnicoContexto_EmiteTokenSingle()
    {
        var (_, _, tenants) = await SeedAsync(1);
        await using var app = CreateTestApp();
        await app.StartAsync();

        var (response, body) = await LoginAsync(app.GetTestClient(), new { email = Email, password = Password, auto_context = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("SINGLE", Claims(body).GetProperty("view_mode").GetString());
        Assert.Equal(tenants[0].ToString("D"), body.GetProperty("context").GetProperty("tenant_id").GetString());
    }

    [Fact(Timeout = 60000)]
    public async Task CA_QW01_4_AutoContextoAmbiguo_CaiNoLoginPadrao()
    {
        await SeedAsync(2);
        await using var app = CreateTestApp();
        await app.StartAsync();

        var (response, body) = await LoginAsync(app.GetTestClient(), new { email = Email, password = Password, auto_context = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(body.TryGetProperty("context", out _));
        Assert.Equal("NONE", Claims(body).GetProperty("view_mode").GetString());
        Assert.Equal(2, body.GetProperty("available_contexts").GetArrayLength());
    }

    [Fact(Timeout = 60000)]
    public async Task CA_QW01_5_AlvoSemVinculo_Devolve403ENaoCriaSessao()
    {
        var (userId, _, _) = await SeedAsync(1);
        await using var app = CreateTestApp();
        await app.StartAsync();

        var (response, body) = await LoginAsync(app.GetTestClient(), new
        {
            email = Email, password = Password, target_tenant_id = Guid.NewGuid().ToString("D"), target_scope = "BUYER"
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("ERR-CTX-001", body.GetProperty("code").GetString());

        await using var db = _fixture.CreateDbContext();
        Assert.False(await db.RefreshTokens.AnyAsync(r => r.UserId == userId));
        Assert.False(await db.AuthAuditLogs.AnyAsync(l => l.UserId == userId));
    }

    [Theory(Timeout = 60000)]
    [InlineData("""{"target_scope":"BUYER"}""")]
    [InlineData("""{"target_tenant_id":"01900000-0000-7000-8000-000000000010"}""")]
    [InlineData("""{"target_tenant_id":"nao-e-uuid","target_scope":"BUYER"}""")]
    [InlineData("""{"target_tenant_id":"01900000-0000-7000-8000-000000000010","target_scope":"ADMIN"}""")]
    [InlineData("""{"target_tenant_id":"01900000-0000-7000-8000-000000000010","target_scope":"1"}""")]
    [InlineData("""{"auto_context":"true"}""")]
    [InlineData("""{"auto_context":1}""")]
    public async Task CA_QW01_6_CamposNovosMalformados_Devolvem400(string extraJson)
    {
        await SeedAsync(1);
        await using var app = CreateTestApp();
        await app.StartAsync();

        var json = $$"""{"email":"{{Email}}","password":"{{Password}}",{{extraJson[1..]}}""";
        var response = await app.GetTestClient().PostAsync("/api/v1/auth/login", new StringContent(json, Encoding.UTF8, "application/json"));
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("ERR-VAL-001", body.GetProperty("code").GetString());
    }

    [Fact(Timeout = 60000)]
    public async Task CA_QW01_7_PalavraPasseErradaComAlvoValido_Devolve401()
    {
        var (_, _, tenants) = await SeedAsync(1);
        await using var app = CreateTestApp();
        await app.StartAsync();

        var (response, body) = await LoginAsync(app.GetTestClient(), new
        {
            email = Email, password = "PasswordErrada123!", target_tenant_id = tenants[0].ToString("D"), target_scope = "BUYER"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("ERR-AUTH-001", body.GetProperty("code").GetString());
    }
}
