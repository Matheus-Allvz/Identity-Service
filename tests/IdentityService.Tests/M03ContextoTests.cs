using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using IdentityService.Modules.M00Transversal;
using IdentityService.Modules.M00Transversal.Auth;
using IdentityService.Modules.M01Login;
using IdentityService.Modules.M02Refresh;
using IdentityService.Modules.M03Contexto;
using IdentityService.Modules.M06Impersonation;
using IdentityService.Modules.M07Chaves;
using IdentityService.Modules.M07Chaves.Interface;
using IdentityService.Modules.M08Acessos;
using IdentityService.Modules.M09Logout;
using IdentityService.Modules.M11Esquema;
using IdentityService.Modules.M11Esquema.Persistence.Entities;
using IdentityService.Modules.M11Esquema.Persistence.Enums;
using IdentityService.Modules.M13Isolamento;
using IdentityService.Modules.M14Eventos;
using IdentityService.Tests.Fixtures;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Xunit;

namespace IdentityService.Tests;

[Trait("Fase", "F01")]
[Collection("SharedContainers")]
public class M03ContextoTests
{
    private readonly TestcontainersFixture _fixture;
    private readonly TestSigningKey _signingKey = new("key-2026-01");
    private readonly TestTimeProvider _timeProvider = new(new DateTimeOffset(2026, 11, 2, 22, 0, 0, TimeSpan.Zero));

    public M03ContextoTests(TestcontainersFixture fixture)
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

    private TokenIssuer CreateIssuer()
    {
        return new TokenIssuer(_signingKey, _timeProvider, new ConfigurationBuilder().Build());
    }

    [Fact(Timeout = 60000)]
    public async Task CA_02_1_Borda_TrocaParaSingleNoUltimoSegundoDoToken()
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();

        var userId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var familyId = Guid.NewGuid();

        await using (var db = _fixture.CreateDbContext())
        {
            var group = new EconomicGroup { Id = groupId, Name = "Grupo Teste 01", Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var tenant = new Tenant { Id = tenantId, GroupId = groupId, Cnpj = "99000001000101", LegalName = "Tenant Teste 01", BranchType = BranchType.HQ, GeoLat = 0, GeoLng = 0, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var scope = new TenantScope { Id = Guid.NewGuid(), TenantId = tenantId, Scope = OperationalScope.BUYER, IsActive = true, CreatedAt = now };
            var user = new UserAccount { Id = userId, Email = "u1@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Utilizador Teste 01", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.NONE, CreatedAt = now, UpdatedAt = now };
            var member = new TenantMember { Id = Guid.NewGuid(), TenantId = tenantId, UserId = userId, IsOwner = true, CanViewGroup = true, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };

            var rt = new RefreshToken
            {
                Id = Guid.NewGuid(),
                FamilyId = familyId,
                UserId = userId,
                TokenHash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                Status = RefreshTokenStatus.ACTIVE,
                ViewMode = ViewMode.NONE,
                CreatedAt = now,
                ExpiresAt = now.AddSeconds(604800)
            };

            db.EconomicGroups.Add(group);
            db.Tenants.Add(tenant);
            db.TenantScopes.Add(scope);
            db.UserAccounts.Add(user);
            db.TenantMembers.Add(member);
            db.RefreshTokens.Add(rt);
            await db.SaveChangesAsync();
        }

        var issuer = CreateIssuer();
        var claims = new AccessTokenClaims(
            Sub: userId,
            FamilyId: familyId,
            PlatformRole: PlatformRole.NONE,
            ViewMode: ViewMode.NONE,
            GroupId: null,
            TenantId: null,
            AllowedTenants: [],
            ActiveScope: null,
            Roles: [],
            Permissions: [],
            Act: null
        );
        var token = issuer.Issue(claims);

        await using var app = CreateTestApp();
        await app.StartAsync();
        var client = app.GetTestClient();

        var payload = new
        {
            view_mode = "SINGLE",
            target_scope = "BUYER",
            target_tenant_id = tenantId.ToString("D")
        };
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/switch-context");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(900, root.GetProperty("expires_in").GetInt32());
        var contextElem = root.GetProperty("context");
        Assert.Equal("SINGLE", contextElem.GetProperty("view_mode").GetString());
        Assert.Equal("BUYER", contextElem.GetProperty("active_scope").GetString());
        Assert.Equal(tenantId.ToString("D"), contextElem.GetProperty("tenant_id").GetString());

        // Verificar que jti antigo está na denylist no Redis
        var redis = ConnectionMultiplexer.Connect(_fixture.GetRedisConnectionString());
        var redisDb = redis.GetDatabase();
        var jtiRevoked = await redisDb.StringGetAsync($"denylist:jti:{token.Jti}");
        Assert.True(jtiRevoked.HasValue);

        // Verificar que RT no banco foi atualizado
        await using (var db = _fixture.CreateDbContext())
        {
            var rt = await db.RefreshTokens.FirstOrDefaultAsync(r => r.FamilyId == familyId && r.Status == RefreshTokenStatus.ACTIVE);
            Assert.NotNull(rt);
            Assert.Equal(ViewMode.SINGLE, rt.ViewMode);
            Assert.Equal(tenantId, rt.ActiveTenantId);
        }
    }

    [Fact(Timeout = 60000)]
    [Trait("Critico", "sim")]
    public async Task CA_02_2_Borda_RejeicaoDeTokenSobImpersonation()
    {
        var issuer = CreateIssuer();
        var tenantId = Guid.NewGuid();
        var claims = new AccessTokenClaims(
            Sub: Guid.NewGuid(),
            FamilyId: null,
            PlatformRole: PlatformRole.NONE,
            ViewMode: ViewMode.SINGLE,
            GroupId: Guid.NewGuid(),
            TenantId: tenantId,
            AllowedTenants: [tenantId],
            ActiveScope: OperationalScope.BUYER,
            Roles: ["BUYER_OPERATOR"],
            Permissions: ["orders:read"],
            Act: new ActorClaim(Guid.NewGuid(), "TICKET-123")
        );
        var token = issuer.Issue(claims);

        await using var app = CreateTestApp();
        await app.StartAsync();
        var client = app.GetTestClient();

        var payload = new
        {
            view_mode = "SINGLE",
            target_scope = "BUYER",
            target_tenant_id = Guid.NewGuid().ToString("D")
        };
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/switch-context");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("ERR-CTX-007", doc.RootElement.GetProperty("code").GetString());
    }

    [Theory(Timeout = 60000)]
    [InlineData("{\"view_mode\":\"NONE\",\"target_scope\":\"BUYER\"}")]
    [InlineData("{\"view_mode\":\"SINGLE\",\"target_scope\":\"BUYER\",\"target_tenants\":[\"01900000-0000-7000-8000-000000000010\"]}")]
    [InlineData("{\"view_mode\":\"MACRO_GROUP\",\"target_scope\":\"BUYER\",\"target_tenant_id\":\"01900000-0000-7000-8000-000000000010\"}")]
    [InlineData("{\"view_mode\":\"MACRO_GROUP\",\"target_scope\":\"BUYER\",\"target_group_id\":\"01900000-0000-7000-8000-000000000001\",\"target_tenants\":\"invalido\"}")]
    public async Task CA_02_3_Borda_RejeicaoDePayloadInvalido(string jsonPayload)
    {
        var issuer = CreateIssuer();
        var claims = new AccessTokenClaims(
            Sub: Guid.NewGuid(),
            FamilyId: Guid.NewGuid(),
            PlatformRole: PlatformRole.NONE,
            ViewMode: ViewMode.NONE,
            GroupId: null,
            TenantId: null,
            AllowedTenants: [],
            ActiveScope: null,
            Roles: [],
            Permissions: [],
            Act: null
        );
        var token = issuer.Issue(claims);

        await using var app = CreateTestApp();
        await app.StartAsync();
        var client = app.GetTestClient();

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/switch-context");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        req.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        var response = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("ERR-VAL-001", doc.RootElement.GetProperty("code").GetString());
    }

    [Theory(Timeout = 60000)]
    [InlineData("tenant inexistente", 403, "ERR-CTX-001")]
    [InlineData("tenant suspenso", 403, "ERR-CTX-002")]
    [InlineData("sem vínculo", 403, "ERR-CTX-001")]
    [InlineData("vínculo suspenso", 403, "ERR-CTX-001")]
    [InlineData("sem papéis", 403, "ERR-CTX-003")]
    public async Task CA_02_4_Borda_InelegibilidadeEmSingle(string motivo, int statusEsperado, string codigoEsperado)
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();

        var userId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var familyId = Guid.NewGuid();

        await using (var db = _fixture.CreateDbContext())
        {
            var user = new UserAccount { Id = userId, Email = "u1@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Utilizador Teste 01", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.NONE, CreatedAt = now, UpdatedAt = now };
            var rt = new RefreshToken { Id = Guid.NewGuid(), FamilyId = familyId, UserId = userId, TokenHash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", Status = RefreshTokenStatus.ACTIVE, ViewMode = ViewMode.NONE, CreatedAt = now, ExpiresAt = now.AddSeconds(604800) };
            db.UserAccounts.Add(user);
            db.RefreshTokens.Add(rt);

            if (motivo != "tenant inexistente")
            {
                var tenantStatus = motivo == "tenant suspenso" ? EntityStatus.SUSPENDED : EntityStatus.ACTIVE;
                var group = new EconomicGroup { Id = groupId, Name = "Grupo 1", Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
                var tenant = new Tenant { Id = tenantId, GroupId = groupId, Cnpj = "99000001000101", LegalName = "Tenant 1", BranchType = BranchType.HQ, GeoLat = 0, GeoLng = 0, Status = tenantStatus, CreatedAt = now, UpdatedAt = now };
                db.EconomicGroups.Add(group);
                db.Tenants.Add(tenant);

                if (motivo != "sem papéis")
                {
                    var scope = new TenantScope { Id = Guid.NewGuid(), TenantId = tenantId, Scope = OperationalScope.BUYER, IsActive = true, CreatedAt = now };
                    db.TenantScopes.Add(scope);
                }

                if (motivo != "sem vínculo")
                {
                    var memberStatus = motivo == "vínculo suspenso" ? EntityStatus.SUSPENDED : EntityStatus.ACTIVE;
                    var member = new TenantMember { Id = Guid.NewGuid(), TenantId = tenantId, UserId = userId, IsOwner = true, CanViewGroup = true, Status = memberStatus, CreatedAt = now, UpdatedAt = now };
                    db.TenantMembers.Add(member);
                }
            }

            await db.SaveChangesAsync();
        }

        var issuer = CreateIssuer();
        var claims = new AccessTokenClaims(
            Sub: userId,
            FamilyId: familyId,
            PlatformRole: PlatformRole.NONE,
            ViewMode: ViewMode.NONE,
            GroupId: null,
            TenantId: null,
            AllowedTenants: [],
            ActiveScope: null,
            Roles: [],
            Permissions: [],
            Act: null
        );
        var token = issuer.Issue(claims);

        await using var app = CreateTestApp();
        await app.StartAsync();
        var client = app.GetTestClient();

        var payload = new
        {
            view_mode = "SINGLE",
            target_scope = "BUYER",
            target_tenant_id = tenantId.ToString("D")
        };
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/switch-context");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.SendAsync(req);
        Assert.Equal((HttpStatusCode)statusEsperado, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(codigoEsperado, doc.RootElement.GetProperty("code").GetString());
    }

    [Fact(Timeout = 60000)]
    public async Task CA_03_1_Borda_TrocaParaMacroGroupNoLimiteDe50Tenants()
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();

        var userId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var familyId = Guid.NewGuid();
        var tenantIds = new List<Guid>();

        await using (var db = _fixture.CreateDbContext())
        {
            var group = new EconomicGroup { Id = groupId, Name = "Grupo 1", Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var user = new UserAccount { Id = userId, Email = "u1@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Utilizador Teste 01", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.NONE, CreatedAt = now, UpdatedAt = now };
            var rt = new RefreshToken { Id = Guid.NewGuid(), FamilyId = familyId, UserId = userId, TokenHash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", Status = RefreshTokenStatus.ACTIVE, ViewMode = ViewMode.NONE, CreatedAt = now, ExpiresAt = now.AddSeconds(604800) };

            db.EconomicGroups.Add(group);
            db.UserAccounts.Add(user);
            db.RefreshTokens.Add(rt);

            for (int i = 1; i <= 50; i++)
            {
                var tId = Guid.NewGuid();
                tenantIds.Add(tId);
                var tenant = new Tenant { Id = tId, GroupId = groupId, Cnpj = $"99000001{i:D6}", LegalName = $"Tenant {i}", BranchType = BranchType.BRANCH, GeoLat = 0, GeoLng = 0, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
                var scope = new TenantScope { Id = Guid.NewGuid(), TenantId = tId, Scope = OperationalScope.BUYER, IsActive = true, CreatedAt = now };
                var member = new TenantMember { Id = Guid.NewGuid(), TenantId = tId, UserId = userId, IsOwner = true, CanViewGroup = true, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };

                db.Tenants.Add(tenant);
                db.TenantScopes.Add(scope);
                db.TenantMembers.Add(member);
            }

            await db.SaveChangesAsync();
        }

        var issuer = CreateIssuer();
        var claims = new AccessTokenClaims(
            Sub: userId,
            FamilyId: familyId,
            PlatformRole: PlatformRole.NONE,
            ViewMode: ViewMode.NONE,
            GroupId: null,
            TenantId: null,
            AllowedTenants: [],
            ActiveScope: null,
            Roles: [],
            Permissions: [],
            Act: null
        );
        var token = issuer.Issue(claims);

        await using var app = CreateTestApp();
        await app.StartAsync();
        var client = app.GetTestClient();

        var payload = new
        {
            view_mode = "MACRO_GROUP",
            target_scope = "BUYER",
            target_group_id = groupId.ToString("D"),
            target_tenants = tenantIds.Select(t => t.ToString("D")).ToArray()
        };
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/switch-context");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var context = root.GetProperty("context");

        var allowedTenantsElem = context.GetProperty("allowed_tenants");
        Assert.Equal(50, allowedTenantsElem.GetArrayLength());
    }

    [Theory(Timeout = 60000)]
    [Trait("Critico", "sim")]
    [InlineData("array vazio", 403, "ERR-CTX-004")]
    [InlineData("tenant outro grupo", 403, "ERR-CTX-004")]
    [InlineData("can_view_group falso", 403, "ERR-CTX-004")]
    [InlineData("permissões vazias", 403, "ERR-CTX-003")]
    [InlineData("tenant duplicado", 403, "ERR-CTX-004")]
    public async Task CA_03_2_Borda_InelegibilidadeEmMacroGroup(string motivo, int statusEsperado, string codigoEsperado)
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();

        var userId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var otherGroupId = Guid.NewGuid();
        var familyId = Guid.NewGuid();
        var tenantId1 = Guid.NewGuid();
        var tenantId2 = Guid.NewGuid();

        await using (var db = _fixture.CreateDbContext())
        {
            var group = new EconomicGroup { Id = groupId, Name = "Grupo 1", Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var otherGroup = new EconomicGroup { Id = otherGroupId, Name = "Grupo 2", Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var user = new UserAccount { Id = userId, Email = "u1@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Utilizador Teste 01", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.NONE, CreatedAt = now, UpdatedAt = now };
            var rt = new RefreshToken { Id = Guid.NewGuid(), FamilyId = familyId, UserId = userId, TokenHash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", Status = RefreshTokenStatus.ACTIVE, ViewMode = ViewMode.NONE, CreatedAt = now, ExpiresAt = now.AddSeconds(604800) };

            db.EconomicGroups.AddRange(group, otherGroup);
            db.UserAccounts.Add(user);
            db.RefreshTokens.Add(rt);

            var t1Group = motivo == "tenant outro grupo" ? otherGroupId : groupId;
            var t1 = new Tenant { Id = tenantId1, GroupId = t1Group, Cnpj = "99000001000101", LegalName = "Tenant 1", BranchType = BranchType.HQ, GeoLat = 0, GeoLng = 0, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var t2 = new Tenant { Id = tenantId2, GroupId = groupId, Cnpj = "99000001000102", LegalName = "Tenant 2", BranchType = BranchType.BRANCH, GeoLat = 0, GeoLng = 0, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };

            var canView = motivo != "can_view_group falso";
            var m1 = new TenantMember { Id = Guid.NewGuid(), TenantId = tenantId1, UserId = userId, IsOwner = true, CanViewGroup = canView, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var m2 = new TenantMember { Id = Guid.NewGuid(), TenantId = tenantId2, UserId = userId, IsOwner = true, CanViewGroup = true, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };

            db.Tenants.AddRange(t1, t2);
            db.TenantMembers.AddRange(m1, m2);

            if (motivo != "permissões vazias")
            {
                var s1 = new TenantScope { Id = Guid.NewGuid(), TenantId = tenantId1, Scope = OperationalScope.BUYER, IsActive = true, CreatedAt = now };
                var s2 = new TenantScope { Id = Guid.NewGuid(), TenantId = tenantId2, Scope = OperationalScope.BUYER, IsActive = true, CreatedAt = now };
                db.TenantScopes.AddRange(s1, s2);
            }

            await db.SaveChangesAsync();
        }

        var issuer = CreateIssuer();
        var claims = new AccessTokenClaims(
            Sub: userId,
            FamilyId: familyId,
            PlatformRole: PlatformRole.NONE,
            ViewMode: ViewMode.NONE,
            GroupId: null,
            TenantId: null,
            AllowedTenants: [],
            ActiveScope: null,
            Roles: [],
            Permissions: [],
            Act: null
        );
        var token = issuer.Issue(claims);

        await using var app = CreateTestApp();
        await app.StartAsync();
        var client = app.GetTestClient();

        object tenantsPayload = motivo switch
        {
            "array vazio" => Array.Empty<string>(),
            "tenant duplicado" => new[] { tenantId1.ToString("D"), tenantId1.ToString("D") },
            _ => new[] { tenantId1.ToString("D"), tenantId2.ToString("D") }
        };

        var payload = new
        {
            view_mode = "MACRO_GROUP",
            target_scope = "BUYER",
            target_group_id = groupId.ToString("D"),
            target_tenants = tenantsPayload
        };

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/switch-context");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.SendAsync(req);
        Assert.Equal((HttpStatusCode)statusEsperado, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(codigoEsperado, doc.RootElement.GetProperty("code").GetString());
    }

    [Fact(Timeout = 60000)]
    [Trait("Critico", "sim")]
    public async Task CA_03_3_Borda_Rejeicao401SemRtAtivo()
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();

        var userId = Guid.NewGuid();
        var familyId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        await using (var db = _fixture.CreateDbContext())
        {
            var user = new UserAccount { Id = userId, Email = "u1@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Utilizador Teste 01", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.NONE, CreatedAt = now, UpdatedAt = now };
            var group = new EconomicGroup { Id = groupId, Name = "Grupo Teste 01", Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var tenant = new Tenant { Id = tenantId, GroupId = groupId, Cnpj = "99000001000101", LegalName = "Tenant Teste 01", BranchType = BranchType.HQ, GeoLat = 0, GeoLng = 0, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var scope = new TenantScope { Id = Guid.NewGuid(), TenantId = tenantId, Scope = OperationalScope.BUYER, IsActive = true, CreatedAt = now };
            var member = new TenantMember { Id = Guid.NewGuid(), TenantId = tenantId, UserId = userId, IsOwner = true, CanViewGroup = true, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            // Família sem RT ACTIVE (só REVOKED)
            var rt = new RefreshToken { Id = Guid.NewGuid(), FamilyId = familyId, UserId = userId, TokenHash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", Status = RefreshTokenStatus.REVOKED, ViewMode = ViewMode.NONE, CreatedAt = now, ExpiresAt = now.AddSeconds(604800), RevokedAt = now };

            db.UserAccounts.Add(user);
            db.EconomicGroups.Add(group);
            db.Tenants.Add(tenant);
            db.TenantScopes.Add(scope);
            db.TenantMembers.Add(member);
            db.RefreshTokens.Add(rt);
            await db.SaveChangesAsync();
        }

        var issuer = CreateIssuer();
        var claims = new AccessTokenClaims(
            Sub: userId,
            FamilyId: familyId,
            PlatformRole: PlatformRole.NONE,
            ViewMode: ViewMode.NONE,
            GroupId: null,
            TenantId: null,
            AllowedTenants: [],
            ActiveScope: null,
            Roles: [],
            Permissions: [],
            Act: null
        );
        var token = issuer.Issue(claims);

        await using var app = CreateTestApp();
        await app.StartAsync();
        var client = app.GetTestClient();

        var payload = new
        {
            view_mode = "SINGLE",
            target_scope = "BUYER",
            target_tenant_id = tenantId.ToString("D")
        };
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/switch-context");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("ERR-AUTH-002", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact(Timeout = 60000)]
    [Trait("Critico", "sim")]
    public async Task CA_03_4_Borda_ConcorrenciaComMesmoToken()
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();

        var userId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var familyId = Guid.NewGuid();

        await using (var db = _fixture.CreateDbContext())
        {
            var group = new EconomicGroup { Id = groupId, Name = "Grupo 1", Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var tenant = new Tenant { Id = tenantId, GroupId = groupId, Cnpj = "99000001000101", LegalName = "Tenant 1", BranchType = BranchType.HQ, GeoLat = 0, GeoLng = 0, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var scope = new TenantScope { Id = Guid.NewGuid(), TenantId = tenantId, Scope = OperationalScope.BUYER, IsActive = true, CreatedAt = now };
            var user = new UserAccount { Id = userId, Email = "u1@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Utilizador Teste 01", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.NONE, CreatedAt = now, UpdatedAt = now };
            var member = new TenantMember { Id = Guid.NewGuid(), TenantId = tenantId, UserId = userId, IsOwner = true, CanViewGroup = true, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var rt = new RefreshToken { Id = Guid.NewGuid(), FamilyId = familyId, UserId = userId, TokenHash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", Status = RefreshTokenStatus.ACTIVE, ViewMode = ViewMode.NONE, CreatedAt = now, ExpiresAt = now.AddSeconds(604800) };

            db.EconomicGroups.Add(group);
            db.Tenants.Add(tenant);
            db.TenantScopes.Add(scope);
            db.UserAccounts.Add(user);
            db.TenantMembers.Add(member);
            db.RefreshTokens.Add(rt);
            await db.SaveChangesAsync();
        }

        var issuer = CreateIssuer();
        var claims = new AccessTokenClaims(
            Sub: userId,
            FamilyId: familyId,
            PlatformRole: PlatformRole.NONE,
            ViewMode: ViewMode.NONE,
            GroupId: null,
            TenantId: null,
            AllowedTenants: [],
            ActiveScope: null,
            Roles: [],
            Permissions: [],
            Act: null
        );
        var token = issuer.Issue(claims);

        await using var app = CreateTestApp();
        await app.StartAsync();

        var client1 = app.GetTestClient();
        var client2 = app.GetTestClient();

        var payload = new
        {
            view_mode = "SINGLE",
            target_scope = "BUYER",
            target_tenant_id = tenantId.ToString("D")
        };

        var barrier = new Barrier(2);

        var task1 = Task.Run(async () =>
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/switch-context");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            barrier.SignalAndWait();
            return await client1.SendAsync(req);
        });

        var task2 = Task.Run(async () =>
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/switch-context");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            barrier.SignalAndWait();
            return await client2.SendAsync(req);
        });

        var responses = await Task.WhenAll(task1, task2);
        var statusCodes = responses.Select(r => r.StatusCode).ToList();

        Assert.Contains(HttpStatusCode.OK, statusCodes);
    }
}
