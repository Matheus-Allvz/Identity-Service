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
public class M06ImpersonationTests
{
    private readonly TestcontainersFixture _fixture;
    private readonly TestSigningKey _signingKey = new("key-2026-01");
    private readonly TestTimeProvider _timeProvider = new(new DateTimeOffset(2026, 11, 2, 22, 0, 0, TimeSpan.Zero));

    public M06ImpersonationTests(TestcontainersFixture fixture)
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
    public async Task CA_06_1_Borda_EmissaoBemSucedidaDeTokenDeImpersonationComContextoCompletoEAuditoria()
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();

        var opUserId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        await using (var db = _fixture.CreateDbContext())
        {
            var opUser = new UserAccount { Id = opUserId, Email = "op@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Operador Suporte", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.SUPPORT_AGENT, CreatedAt = now, UpdatedAt = now };
            var targetUser = new UserAccount { Id = targetUserId, Email = "target@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Utilizador Alvo", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.NONE, CreatedAt = now, UpdatedAt = now };
            var group = new EconomicGroup { Id = groupId, Name = "Grupo 1", Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var tenant = new Tenant { Id = tenantId, GroupId = groupId, Cnpj = "99000001000101", LegalName = "Tenant 1", BranchType = BranchType.HQ, GeoLat = 0, GeoLng = 0, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var scope = new TenantScope { Id = Guid.NewGuid(), TenantId = tenantId, Scope = OperationalScope.BUYER, IsActive = true, CreatedAt = now };
            var member = new TenantMember { Id = Guid.NewGuid(), TenantId = tenantId, UserId = targetUserId, IsOwner = true, CanViewGroup = true, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };

            db.UserAccounts.AddRange(opUser, targetUser);
            db.EconomicGroups.Add(group);
            db.Tenants.Add(tenant);
            db.TenantScopes.Add(scope);
            db.TenantMembers.Add(member);
            await db.SaveChangesAsync();
        }

        var issuer = CreateIssuer();
        var opToken = issuer.Issue(new AccessTokenClaims(
            Sub: opUserId,
            FamilyId: Guid.NewGuid(),
            PlatformRole: PlatformRole.SUPPORT_AGENT,
            ViewMode: ViewMode.NONE,
            GroupId: null,
            TenantId: null,
            AllowedTenants: [],
            ActiveScope: null,
            Roles: [],
            Permissions: [],
            Act: null
        ));

        await using var app = CreateTestApp();
        await app.StartAsync();
        var client = app.GetTestClient();

        var payload = new
        {
            target_user_id = targetUserId.ToString("D"),
            target_tenant_id = tenantId.ToString("D"),
            target_scope = "BUYER",
            ticket_id = "CHAMADO-123"
        };
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/impersonate");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", opToken.AccessToken);
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("Bearer", root.GetProperty("token_type").GetString());
        Assert.Equal(900, root.GetProperty("expires_in").GetInt32());
        Assert.False(root.TryGetProperty("refresh_token", out _));

        var ctx = root.GetProperty("impersonation_context");
        Assert.Equal(targetUserId.ToString("D"), ctx.GetProperty("sub").GetString());
        Assert.Equal("SINGLE", ctx.GetProperty("view_mode").GetString());
        Assert.Equal("BUYER", ctx.GetProperty("active_scope").GetString());

        var act = ctx.GetProperty("act");
        Assert.Equal(opUserId.ToString("D"), act.GetProperty("sub").GetString());
        Assert.Equal("CHAMADO-123", act.GetProperty("ticket_id").GetString());

        await using (var db = _fixture.CreateDbContext())
        {
            var audit = await db.AuthAuditLogs.FirstOrDefaultAsync(a => a.EventType == AuditEventType.IMPERSONATION_STARTED && a.TicketId == "CHAMADO-123");
            Assert.NotNull(audit);
            Assert.Equal(targetUserId, audit.UserId);
            Assert.Equal(opUserId, audit.ActorUserId);
        }
    }

    [Theory(Timeout = 60000)]
    [Trait("Critico", "sim")]
    [InlineData("SUPPORT_AGENT", true, "NONE", false)]
    [InlineData("NONE", false, "NONE", false)]
    [InlineData("SUPPORT_AGENT", false, "SUPPORT_AGENT", false)]
    [InlineData("SUPPORT_AGENT", false, "PLATFORM_ADMIN", false)]
    [InlineData("SUPPORT_AGENT", false, "NONE", true)]
    public async Task CA_06_2_Borda_RejeicaoDeImpersonationQuandoChamadorOuAlvoViolamRegrasDePrivilegio(
        string roleOpStr, bool actOp, string roleAlvoStr, bool autoDel)
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();

        var opRole = Enum.Parse<PlatformRole>(roleOpStr);
        var targetRole = Enum.Parse<PlatformRole>(roleAlvoStr);

        var opUserId = Guid.NewGuid();
        var targetUserId = autoDel ? opUserId : Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        await using (var db = _fixture.CreateDbContext())
        {
            var opUser = new UserAccount { Id = opUserId, Email = "op@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Operador", Status = UserStatus.ACTIVE, PlatformRole = opRole, CreatedAt = now, UpdatedAt = now };
            db.UserAccounts.Add(opUser);

            if (!autoDel)
            {
                var targetUser = new UserAccount { Id = targetUserId, Email = "target@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Alvo", Status = UserStatus.ACTIVE, PlatformRole = targetRole, CreatedAt = now, UpdatedAt = now };
                db.UserAccounts.Add(targetUser);
            }

            var group = new EconomicGroup { Id = groupId, Name = "Grupo 1", Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var tenant = new Tenant { Id = tenantId, GroupId = groupId, Cnpj = "99000001000101", LegalName = "Tenant 1", BranchType = BranchType.HQ, GeoLat = 0, GeoLng = 0, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var scope = new TenantScope { Id = Guid.NewGuid(), TenantId = tenantId, Scope = OperationalScope.BUYER, IsActive = true, CreatedAt = now };
            var member = new TenantMember { Id = Guid.NewGuid(), TenantId = tenantId, UserId = targetUserId, IsOwner = true, CanViewGroup = true, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };

            db.EconomicGroups.Add(group);
            db.Tenants.Add(tenant);
            db.TenantScopes.Add(scope);
            db.TenantMembers.Add(member);
            await db.SaveChangesAsync();
        }

        var issuer = CreateIssuer();
        var opToken = issuer.Issue(new AccessTokenClaims(
            Sub: opUserId,
            FamilyId: actOp ? null : Guid.NewGuid(),
            PlatformRole: opRole,
            ViewMode: ViewMode.NONE,
            GroupId: null,
            TenantId: null,
            AllowedTenants: [],
            ActiveScope: null,
            Roles: [],
            Permissions: [],
            Act: actOp ? new ActorClaim(Guid.NewGuid(), "SUP-1") : null
        ));

        await using var app = CreateTestApp();
        await app.StartAsync();
        var client = app.GetTestClient();

        var payload = new
        {
            target_user_id = targetUserId.ToString("D"),
            target_tenant_id = tenantId.ToString("D"),
            target_scope = "BUYER",
            ticket_id = "CHAMADO-123"
        };
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/impersonate");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", opToken.AccessToken);
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("ERR-AUTH-010", doc.RootElement.GetProperty("code").GetString());
    }

    [Theory(Timeout = 60000)]
    [Trait("Critico", "sim")]
    [InlineData("SUSPENDED", null, false)]
    [InlineData("ACTIVE", null, true)]
    [InlineData("LOCKED", "futuro", false)]
    public async Task CA_06_3_Borda_RejeicaoDeImpersonationQuandoUtilizadorAlvoEstaIndisponivelOuBloqueado(
        string statusAlvoStr, string? bloqueio, bool naDenylist)
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();

        var opUserId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        var statusAlvo = Enum.Parse<UserStatus>(statusAlvoStr);
        DateTimeOffset? lockedUntil = bloqueio == "futuro" ? now.AddMinutes(15) : null;

        await using (var db = _fixture.CreateDbContext())
        {
            var opUser = new UserAccount { Id = opUserId, Email = "op@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Operador", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.SUPPORT_AGENT, CreatedAt = now, UpdatedAt = now };
            var targetUser = new UserAccount { Id = targetUserId, Email = "target@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Alvo", Status = statusAlvo, PlatformRole = PlatformRole.NONE, LockedUntil = lockedUntil, CreatedAt = now, UpdatedAt = now };

            var group = new EconomicGroup { Id = groupId, Name = "Grupo 1", Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var tenant = new Tenant { Id = tenantId, GroupId = groupId, Cnpj = "99000001000101", LegalName = "Tenant 1", BranchType = BranchType.HQ, GeoLat = 0, GeoLng = 0, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var scope = new TenantScope { Id = Guid.NewGuid(), TenantId = tenantId, Scope = OperationalScope.BUYER, IsActive = true, CreatedAt = now };
            var member = new TenantMember { Id = Guid.NewGuid(), TenantId = tenantId, UserId = targetUserId, IsOwner = true, CanViewGroup = true, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };

            db.UserAccounts.AddRange(opUser, targetUser);
            db.EconomicGroups.Add(group);
            db.Tenants.Add(tenant);
            db.TenantScopes.Add(scope);
            db.TenantMembers.Add(member);
            await db.SaveChangesAsync();
        }

        if (naDenylist)
        {
            var redis = ConnectionMultiplexer.Connect(_fixture.GetRedisConnectionString());
            var redisDb = redis.GetDatabase();
            await redisDb.StringSetAsync($"denylist:user:{targetUserId:D}", now.ToUnixTimeSeconds().ToString(), TimeSpan.FromMinutes(15));
        }

        var issuer = CreateIssuer();
        var opToken = issuer.Issue(new AccessTokenClaims(
            Sub: opUserId,
            FamilyId: Guid.NewGuid(),
            PlatformRole: PlatformRole.SUPPORT_AGENT,
            ViewMode: ViewMode.NONE,
            GroupId: null,
            TenantId: null,
            AllowedTenants: [],
            ActiveScope: null,
            Roles: [],
            Permissions: [],
            Act: null
        ));

        await using var app = CreateTestApp();
        await app.StartAsync();
        var client = app.GetTestClient();

        var payload = new
        {
            target_user_id = targetUserId.ToString("D"),
            target_tenant_id = tenantId.ToString("D"),
            target_scope = "BUYER",
            ticket_id = "CHAMADO-123"
        };
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/impersonate");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", opToken.AccessToken);
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("ERR-AUTH-004", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact(Timeout = 60000)]
    public async Task CA_06_4_Borda_UtilizadorAlvoComBloqueioLockedExpiradoTemImpersonationAprovada()
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();

        var opUserId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        await using (var db = _fixture.CreateDbContext())
        {
            var opUser = new UserAccount { Id = opUserId, Email = "op@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Operador", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.SUPPORT_AGENT, CreatedAt = now, UpdatedAt = now };
            var targetUser = new UserAccount { Id = targetUserId, Email = "target@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Alvo", Status = UserStatus.LOCKED, PlatformRole = PlatformRole.NONE, LockedUntil = now.AddMinutes(-5), CreatedAt = now, UpdatedAt = now };

            var group = new EconomicGroup { Id = groupId, Name = "Grupo 1", Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var tenant = new Tenant { Id = tenantId, GroupId = groupId, Cnpj = "99000001000101", LegalName = "Tenant 1", BranchType = BranchType.HQ, GeoLat = 0, GeoLng = 0, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var scope = new TenantScope { Id = Guid.NewGuid(), TenantId = tenantId, Scope = OperationalScope.BUYER, IsActive = true, CreatedAt = now };
            var member = new TenantMember { Id = Guid.NewGuid(), TenantId = tenantId, UserId = targetUserId, IsOwner = true, CanViewGroup = true, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };

            db.UserAccounts.AddRange(opUser, targetUser);
            db.EconomicGroups.Add(group);
            db.Tenants.Add(tenant);
            db.TenantScopes.Add(scope);
            db.TenantMembers.Add(member);
            await db.SaveChangesAsync();
        }

        var issuer = CreateIssuer();
        var opToken = issuer.Issue(new AccessTokenClaims(
            Sub: opUserId,
            FamilyId: Guid.NewGuid(),
            PlatformRole: PlatformRole.SUPPORT_AGENT,
            ViewMode: ViewMode.NONE,
            GroupId: null,
            TenantId: null,
            AllowedTenants: [],
            ActiveScope: null,
            Roles: [],
            Permissions: [],
            Act: null
        ));

        await using var app = CreateTestApp();
        await app.StartAsync();
        var client = app.GetTestClient();

        var payload = new
        {
            target_user_id = targetUserId.ToString("D"),
            target_tenant_id = tenantId.ToString("D"),
            target_scope = "BUYER",
            ticket_id = "CHAMADO-123"
        };
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/impersonate");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", opToken.AccessToken);
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var sub = doc.RootElement.GetProperty("impersonation_context").GetProperty("sub").GetString();
        Assert.Equal(targetUserId.ToString("D"), sub);
    }

    [Theory(Timeout = 60000)]
    [Trait("Critico", "sim")]
    [InlineData("SUSPENDED", false, "ACTIVE", true, "ERR-CTX-002")]
    [InlineData("ACTIVE", true, "ACTIVE", true, "ERR-CTX-002")]
    [InlineData("ACTIVE", false, "SUSPENDED", true, "ERR-CTX-001")]
    [InlineData("ACTIVE", false, "inativo", true, "ERR-CTX-001")]
    [InlineData("ACTIVE", false, "ACTIVE", false, "ERR-CTX-003")]
    public async Task CA_06_5_Borda_RejeicaoDeImpersonationQuandoTenantAlvoOuVinculoNaoCumpremRequisitos(
        string statusTenantStr, bool tenantDenylist, string statusVinculoStr, bool escopoAtivo, string codigoErro)
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();

        var opUserId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        var tenantStatus = Enum.Parse<EntityStatus>(statusTenantStr);

        await using (var db = _fixture.CreateDbContext())
        {
            var opUser = new UserAccount { Id = opUserId, Email = "op@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Operador", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.SUPPORT_AGENT, CreatedAt = now, UpdatedAt = now };
            var targetUser = new UserAccount { Id = targetUserId, Email = "target@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Alvo", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.NONE, CreatedAt = now, UpdatedAt = now };

            var group = new EconomicGroup { Id = groupId, Name = "Grupo 1", Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var tenant = new Tenant { Id = tenantId, GroupId = groupId, Cnpj = "99000001000101", LegalName = "Tenant 1", BranchType = BranchType.HQ, GeoLat = 0, GeoLng = 0, Status = tenantStatus, CreatedAt = now, UpdatedAt = now };

            db.UserAccounts.AddRange(opUser, targetUser);
            db.EconomicGroups.Add(group);
            db.Tenants.Add(tenant);

            if (escopoAtivo)
            {
                var scope = new TenantScope { Id = Guid.NewGuid(), TenantId = tenantId, Scope = OperationalScope.BUYER, IsActive = true, CreatedAt = now };
                db.TenantScopes.Add(scope);
            }

            if (statusVinculoStr != "inativo")
            {
                var memberStatus = Enum.Parse<EntityStatus>(statusVinculoStr);
                var member = new TenantMember { Id = Guid.NewGuid(), TenantId = tenantId, UserId = targetUserId, IsOwner = true, CanViewGroup = true, Status = memberStatus, CreatedAt = now, UpdatedAt = now };
                db.TenantMembers.Add(member);
            }

            await db.SaveChangesAsync();
        }

        if (tenantDenylist)
        {
            var redis = ConnectionMultiplexer.Connect(_fixture.GetRedisConnectionString());
            var redisDb = redis.GetDatabase();
            await redisDb.StringSetAsync($"denylist:tenant:{tenantId:D}", "1", TimeSpan.FromMinutes(15));
        }

        var issuer = CreateIssuer();
        var opToken = issuer.Issue(new AccessTokenClaims(
            Sub: opUserId,
            FamilyId: Guid.NewGuid(),
            PlatformRole: PlatformRole.SUPPORT_AGENT,
            ViewMode: ViewMode.NONE,
            GroupId: null,
            TenantId: null,
            AllowedTenants: [],
            ActiveScope: null,
            Roles: [],
            Permissions: [],
            Act: null
        ));

        await using var app = CreateTestApp();
        await app.StartAsync();
        var client = app.GetTestClient();

        var payload = new
        {
            target_user_id = targetUserId.ToString("D"),
            target_tenant_id = tenantId.ToString("D"),
            target_scope = "BUYER",
            ticket_id = "CHAMADO-123"
        };
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/impersonate");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", opToken.AccessToken);
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(codigoErro, doc.RootElement.GetProperty("code").GetString());
    }

    [Theory(Timeout = 60000)]
    [InlineData("AB-1", 200, null)]
    [InlineData("ABCDEFGHIJ-1234567890", 200, null)]
    [InlineData("A-1", 422, "ERR-VAL-004")]
    [InlineData("AB-", 422, "ERR-VAL-004")]
    [InlineData("AB-12345678901", 422, "ERR-VAL-004")]
    [InlineData("ab-123", 422, "ERR-VAL-004")]
    public async Task CA_06_6_Borda_ValidacaoDeBordaNoFormatoDoChamadoTecnicoTicketId(
        string ticketTeste, int statusHttp, string? codigoErro)
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();

        var opUserId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        await using (var db = _fixture.CreateDbContext())
        {
            var opUser = new UserAccount { Id = opUserId, Email = "op@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Operador", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.SUPPORT_AGENT, CreatedAt = now, UpdatedAt = now };
            var targetUser = new UserAccount { Id = targetUserId, Email = "target@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Alvo", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.NONE, CreatedAt = now, UpdatedAt = now };
            var group = new EconomicGroup { Id = groupId, Name = "Grupo 1", Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var tenant = new Tenant { Id = tenantId, GroupId = groupId, Cnpj = "99000001000101", LegalName = "Tenant 1", BranchType = BranchType.HQ, GeoLat = 0, GeoLng = 0, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var scope = new TenantScope { Id = Guid.NewGuid(), TenantId = tenantId, Scope = OperationalScope.BUYER, IsActive = true, CreatedAt = now };
            var member = new TenantMember { Id = Guid.NewGuid(), TenantId = tenantId, UserId = targetUserId, IsOwner = true, CanViewGroup = true, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };

            db.UserAccounts.AddRange(opUser, targetUser);
            db.EconomicGroups.Add(group);
            db.Tenants.Add(tenant);
            db.TenantScopes.Add(scope);
            db.TenantMembers.Add(member);
            await db.SaveChangesAsync();
        }

        var issuer = CreateIssuer();
        var opToken = issuer.Issue(new AccessTokenClaims(
            Sub: opUserId,
            FamilyId: Guid.NewGuid(),
            PlatformRole: PlatformRole.SUPPORT_AGENT,
            ViewMode: ViewMode.NONE,
            GroupId: null,
            TenantId: null,
            AllowedTenants: [],
            ActiveScope: null,
            Roles: [],
            Permissions: [],
            Act: null
        ));

        await using var app = CreateTestApp();
        await app.StartAsync();
        var client = app.GetTestClient();

        var payload = new
        {
            target_user_id = targetUserId.ToString("D"),
            target_tenant_id = tenantId.ToString("D"),
            target_scope = "BUYER",
            ticket_id = ticketTeste
        };
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/impersonate");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", opToken.AccessToken);
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.SendAsync(req);
        Assert.Equal((HttpStatusCode)statusHttp, response.StatusCode);

        if (codigoErro != null)
        {
            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            Assert.Equal(codigoErro, doc.RootElement.GetProperty("code").GetString());
        }
    }

    [Theory(Timeout = 60000)]
    [InlineData("01900000-0000-7000-8000-999999999999", "01900000-0000-7000-8000-000000000100", 404, "ERR-CTX-008")]
    [InlineData("01900000-0000-7000-8000-000000000002", "01900000-0000-7000-8000-999999999999", 404, "ERR-CTX-008")]
    [InlineData("id-invalido", "01900000-0000-7000-8000-000000000100", 400, "ERR-VAL-001")]
    public async Task CA_06_7_Borda_ValidacaoDeExistenciaDeUtilizadorETenantAlvo(
        string alvoId, string tenantId, int statusHttp, string codigoErro)
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();

        var opUserId = Guid.NewGuid();
        var validTargetId = Guid.Parse("01900000-0000-7000-8000-000000000002");
        var validTenantId = Guid.Parse("01900000-0000-7000-8000-000000000100");
        var groupId = Guid.NewGuid();

        await using (var db = _fixture.CreateDbContext())
        {
            var opUser = new UserAccount { Id = opUserId, Email = "op@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Operador", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.SUPPORT_AGENT, CreatedAt = now, UpdatedAt = now };
            var targetUser = new UserAccount { Id = validTargetId, Email = "target@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Alvo", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.NONE, CreatedAt = now, UpdatedAt = now };
            var group = new EconomicGroup { Id = groupId, Name = "Grupo 1", Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var tenant = new Tenant { Id = validTenantId, GroupId = groupId, Cnpj = "99000001000101", LegalName = "Tenant 1", BranchType = BranchType.HQ, GeoLat = 0, GeoLng = 0, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var scope = new TenantScope { Id = Guid.NewGuid(), TenantId = validTenantId, Scope = OperationalScope.BUYER, IsActive = true, CreatedAt = now };
            var member = new TenantMember { Id = Guid.NewGuid(), TenantId = validTenantId, UserId = validTargetId, IsOwner = true, CanViewGroup = true, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };

            db.UserAccounts.AddRange(opUser, targetUser);
            db.EconomicGroups.Add(group);
            db.Tenants.Add(tenant);
            db.TenantScopes.Add(scope);
            db.TenantMembers.Add(member);
            await db.SaveChangesAsync();
        }

        var issuer = CreateIssuer();
        var opToken = issuer.Issue(new AccessTokenClaims(
            Sub: opUserId,
            FamilyId: Guid.NewGuid(),
            PlatformRole: PlatformRole.SUPPORT_AGENT,
            ViewMode: ViewMode.NONE,
            GroupId: null,
            TenantId: null,
            AllowedTenants: [],
            ActiveScope: null,
            Roles: [],
            Permissions: [],
            Act: null
        ));

        await using var app = CreateTestApp();
        await app.StartAsync();
        var client = app.GetTestClient();

        var payload = new
        {
            target_user_id = alvoId,
            target_tenant_id = tenantId,
            target_scope = "BUYER",
            ticket_id = "CHAMADO-123"
        };
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/impersonate");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", opToken.AccessToken);
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.SendAsync(req);
        Assert.Equal((HttpStatusCode)statusHttp, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(codigoErro, doc.RootElement.GetProperty("code").GetString());
    }
}
