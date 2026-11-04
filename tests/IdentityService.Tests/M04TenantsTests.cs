using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using IdentityService.Modules.M00Transversal;
using IdentityService.Modules.M00Transversal.Auth;
using IdentityService.Modules.M01Login;
using IdentityService.Modules.M02Refresh;
using IdentityService.Modules.M03Contexto;
using IdentityService.Modules.M04Tenants;
using IdentityService.Modules.M04Tenants.Interface;
using IdentityService.Modules.M05Membros;
using IdentityService.Modules.M06Impersonation;
using IdentityService.Modules.M07Chaves;
using IdentityService.Modules.M07Chaves.Interface;
using IdentityService.Modules.M08Acessos;
using IdentityService.Modules.M09Logout;
using IdentityService.Modules.M11Esquema;
using IdentityService.Modules.M11Esquema.Persistence;
using IdentityService.Modules.M11Esquema.Persistence.Entities;
using IdentityService.Modules.M11Esquema.Persistence.Enums;
using IdentityService.Modules.M13Isolamento;
using IdentityService.Modules.M13Isolamento.Interface;
using IdentityService.Modules.M14Eventos;
using IdentityService.Tests.Fixtures;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IdentityService.Tests;

[Trait("Fase", "F02")]
[Collection("SharedContainers")]
public class M04TenantsTests
{
    private readonly TestcontainersFixture _fixture;
    private readonly TestSigningKey _signingKey = new("key-2026-01");
    private readonly TestTimeProvider _timeProvider = new(new DateTimeOffset(2026, 11, 2, 22, 0, 0, TimeSpan.Zero));

    public M04TenantsTests(TestcontainersFixture fixture)
    {
        _fixture = fixture;
    }

    private WebApplication CreateTestApp(Action<IServiceCollection>? configureServices = null)
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
        builder.Services.AddM04Tenants();
        builder.Services.AddM05Membros();
        builder.Services.AddM06Impersonation();
        builder.Services.AddM07Chaves();
        builder.Services.AddM08Acessos();
        builder.Services.AddM09Logout();
        builder.Services.AddDbContext<IdentityDbContext>((sp, options) =>
        {
            var isolation = sp.GetRequiredService<ITenantIsolation>();
            var connStr = builder.Configuration["DB_CONNECTION_STRING"];
            options.UseNpgsql(connStr)
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(isolation.CreateInterceptors(sp));

            var interceptors = sp.GetServices<IInterceptor>();
            foreach (var interceptor in interceptors)
            {
                options.AddInterceptors(interceptor);
            }
        });
        builder.Services.AddM13Isolamento();
        builder.Services.AddM14Eventos(builder.Configuration);

        configureServices?.Invoke(builder.Services);

        var app = builder.Build();
        app.UseM00Transversal();
        app.MapM01Login();
        app.MapM02Refresh();
        app.MapM03Contexto();
        app.MapM04Tenants();
        app.MapM05Membros();
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
    public async Task CA_07_1_SucessoPorTenantAdminAcrescentandoEscopoAdministrativo()
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();

        var groupId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcde");
        var userId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcd0");
        var initialTenantId = Guid.NewGuid();

        await using (var db = _fixture.CreateDbContext())
        {
            var group = new EconomicGroup { Id = groupId, Name = "GRP-1", Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var initialTenant = new Tenant { Id = initialTenantId, GroupId = groupId, Cnpj = "99000001000199", LegalName = "Tenant Inicial", BranchType = BranchType.HQ, GeoLat = 0, GeoLng = 0, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var user = new UserAccount { Id = userId, Email = "admin@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Utilizador Teste 01", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.NONE, CreatedAt = now, UpdatedAt = now };
            var member = new TenantMember { Id = Guid.NewGuid(), TenantId = initialTenantId, UserId = userId, IsOwner = true, CanViewGroup = true, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };

            db.EconomicGroups.Add(group);
            db.Tenants.Add(initialTenant);
            db.UserAccounts.Add(user);
            db.TenantMembers.Add(member);
            await db.SaveChangesAsync();
        }

        var issuer = CreateIssuer();
        var claims = new AccessTokenClaims(
            Sub: userId,
            FamilyId: Guid.NewGuid(),
            PlatformRole: PlatformRole.NONE,
            ViewMode: ViewMode.SINGLE,
            GroupId: groupId,
            TenantId: initialTenantId,
            AllowedTenants: [initialTenantId],
            ActiveScope: OperationalScope.TENANT_ADMIN,
            Roles: ["GROUP_MANAGER"],
            Permissions: ["tenant:criar"],
            Act: null
        );
        var token = issuer.Issue(claims);

        await using var app = CreateTestApp();
        await app.StartAsync();
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        var payload = new
        {
            group_id = groupId.ToString("D"),
            cnpj = "99000001000101",
            legal_name = "Logística Alvorada LTDA",
            branch_type = "HQ",
            geo_lat = -23.55052m,
            geo_lng = -46.633308m,
            scopes = new[] { "BUYER" }
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/v1/tenants", content);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var location = response.Headers.Location?.ToString();
        Assert.NotNull(location);
        Assert.Matches(@"^/api/v1/tenants/[0-9a-f-]{36}$", location);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("ACTIVE", root.GetProperty("status").GetString());

        string[] scopesJson = root.GetProperty("scopes").EnumerateArray().Select(s => s.GetString()!).ToArray();
        Assert.Equal(new[] { "BUYER", "TENANT_ADMIN" }, scopesJson);

        var newTenantId = Guid.Parse(root.GetProperty("id").GetString()!);

        // Assert database state: tenant, tenant_scope, membership with GROUP_MANAGER, EV-01, audit log
        await using (var db = _fixture.CreateDbContext())
        {
            var tenantInDb = await db.Tenants.Include(t => t.Scopes).FirstOrDefaultAsync(t => t.Id == newTenantId);
            Assert.NotNull(tenantInDb);
            Assert.Equal("ACTIVE", tenantInDb.Status.ToString());
            Assert.Equal(2, tenantInDb.Scopes.Count);

            var memberInDb = await db.TenantMembers.Include(m => m.MemberRoles).ThenInclude(mr => mr.Role).FirstOrDefaultAsync(m => m.TenantId == newTenantId && m.UserId == userId);
            Assert.NotNull(memberInDb);
            Assert.False(memberInDb.IsOwner);
            Assert.False(memberInDb.CanViewGroup);
            Assert.Equal(EntityStatus.ACTIVE, memberInDb.Status);
            Assert.Single(memberInDb.MemberRoles);
            Assert.Equal("GROUP_MANAGER", memberInDb.MemberRoles.First().Role.Name);

            var audits = await db.AuthAuditLogs.Where(a => a.TenantId == newTenantId).ToListAsync();
            Assert.Equal(2, audits.Count);
            Assert.Contains(audits, a => a.EventType == AuditEventType.TENANT_CREATED);
            var memberAddedAudit = audits.FirstOrDefault(a => a.EventType == AuditEventType.MEMBER_ADDED);
            Assert.NotNull(memberAddedAudit);
            Assert.Equal(userId, memberAddedAudit.UserId);

            var outboxMsg = await db.OutboxMessages.FirstOrDefaultAsync(o => o.Topic == "empresa_cadastrada" && o.PartitionKey == newTenantId.ToString("D"));
            Assert.NotNull(outboxMsg);
            Assert.Equal(OutboxStatus.PENDING, outboxMsg.Status);
        }
    }

    [Fact(Timeout = 60000)]
    public async Task CA_07_2_SucessoPorPlatformAdminSemVinculoDeMembro()
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();

        var groupId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcde");
        var adminUserId = Guid.NewGuid();

        await using (var db = _fixture.CreateDbContext())
        {
            var group = new EconomicGroup { Id = groupId, Name = "GRP-1", Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var adminUser = new UserAccount { Id = adminUserId, Email = "platform_admin@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Utilizador Teste 02", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.PLATFORM_ADMIN, CreatedAt = now, UpdatedAt = now };

            db.EconomicGroups.Add(group);
            db.UserAccounts.Add(adminUser);
            await db.SaveChangesAsync();
        }

        var issuer = CreateIssuer();
        var claims = new AccessTokenClaims(
            Sub: adminUserId,
            FamilyId: Guid.NewGuid(),
            PlatformRole: PlatformRole.PLATFORM_ADMIN,
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
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        var payload = new
        {
            group_id = groupId.ToString("D"),
            cnpj = "99000002000186",
            legal_name = "Transportes Expressos Filial",
            branch_type = "BRANCH",
            geo_lat = 90.0m,
            geo_lng = 180.0m,
            scopes = new[] { "CARRIER" }
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/v1/tenants", content);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("ACTIVE", root.GetProperty("status").GetString());
        string[] scopesJson = root.GetProperty("scopes").EnumerateArray().Select(s => s.GetString()!).ToArray();
        Assert.Equal(new[] { "CARRIER" }, scopesJson);

        var newTenantId = Guid.Parse(root.GetProperty("id").GetString()!);

        // Assert database state: sem membros em tenant_member, EV-01 criado
        await using (var db = _fixture.CreateDbContext())
        {
            var membersCount = await db.TenantMembers.CountAsync(m => m.TenantId == newTenantId);
            Assert.Equal(0, membersCount);

            var outboxMsg = await db.OutboxMessages.FirstOrDefaultAsync(o => o.Topic == "empresa_cadastrada" && o.PartitionKey == newTenantId.ToString("D"));
            Assert.NotNull(outboxMsg);

            var audits = await db.AuthAuditLogs.Where(a => a.TenantId == newTenantId).ToListAsync();
            Assert.Single(audits);
            Assert.Equal(AuditEventType.TENANT_CREATED, audits[0].EventType);
        }
    }

    [Fact(Timeout = 60000)]
    [Trait("Critico", "sim")]
    public async Task CA_07_3_ConflitoDeUnicidadeAoCadastrarCnpjExistente()
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();

        var groupId = Guid.NewGuid();
        var adminUserId = Guid.NewGuid();
        var existingTenantId = Guid.NewGuid();

        await using (var db = _fixture.CreateDbContext())
        {
            var group = new EconomicGroup { Id = groupId, Name = "GRP-1", Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var existingTenant = new Tenant { Id = existingTenantId, GroupId = groupId, Cnpj = "99000001000101", LegalName = "Tenant Existente", BranchType = BranchType.HQ, GeoLat = 0, GeoLng = 0, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var adminUser = new UserAccount { Id = adminUserId, Email = "platform_admin@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Utilizador Teste 03", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.PLATFORM_ADMIN, CreatedAt = now, UpdatedAt = now };

            db.EconomicGroups.Add(group);
            db.Tenants.Add(existingTenant);
            db.UserAccounts.Add(adminUser);
            await db.SaveChangesAsync();
        }

        var issuer = CreateIssuer();
        var claims = new AccessTokenClaims(
            Sub: adminUserId,
            FamilyId: Guid.NewGuid(),
            PlatformRole: PlatformRole.PLATFORM_ADMIN,
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
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        var payload = new
        {
            group_id = groupId.ToString("D"),
            cnpj = "99000001000101",
            legal_name = "Tenant Duplicado",
            branch_type = "BRANCH",
            geo_lat = 0.0m,
            geo_lng = 0.0m,
            scopes = new[] { "BUYER" }
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/v1/tenants", content);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("ERR-CTX-005", doc.RootElement.GetProperty("code").GetString());

        await using (var db = _fixture.CreateDbContext())
        {
            var tenantsCount = await db.Tenants.CountAsync(t => t.Cnpj == "99000001000101");
            Assert.Equal(1, tenantsCount);
        }
    }

    [Theory(Timeout = 60000)]
    [InlineData("1111111" + "1111111", 0.0, 0.0)]
    [InlineData("99000001000199", 0.0, 0.0)]
    [InlineData("99000001000101", -90.000001, 0.0)]
    [InlineData("99000001000101", 90.000001, 0.0)]
    [InlineData("99000001000101", 0.0, -180.000001)]
    [InlineData("99000001000101", 0.0, 180.000001)]
    public async Task CA_07_4_RejeicaoPorDadosInvalidosDeCnpjOuGeolocalizacao(string cnpjInv, double lat, double lng)
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();

        var groupId = Guid.NewGuid();
        var adminUserId = Guid.NewGuid();

        await using (var db = _fixture.CreateDbContext())
        {
            var group = new EconomicGroup { Id = groupId, Name = "GRP-1", Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var adminUser = new UserAccount { Id = adminUserId, Email = "platform_admin@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Utilizador Teste 04", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.PLATFORM_ADMIN, CreatedAt = now, UpdatedAt = now };

            db.EconomicGroups.Add(group);
            db.UserAccounts.Add(adminUser);
            await db.SaveChangesAsync();
        }

        var issuer = CreateIssuer();
        var claims = new AccessTokenClaims(
            Sub: adminUserId,
            FamilyId: Guid.NewGuid(),
            PlatformRole: PlatformRole.PLATFORM_ADMIN,
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
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        var payload = new
        {
            group_id = groupId.ToString("D"),
            cnpj = cnpjInv,
            legal_name = "Tenant Invalido",
            branch_type = "HQ",
            geo_lat = (decimal)lat,
            geo_lng = (decimal)lng,
            scopes = new[] { "BUYER" }
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/v1/tenants", content);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("ERR-VAL-002", doc.RootElement.GetProperty("code").GetString());

        await using (var db = _fixture.CreateDbContext())
        {
            var count = await db.Tenants.CountAsync(t => t.LegalName == "Tenant Invalido");
            Assert.Equal(0, count);
        }
    }

    [Fact(Timeout = 60000)]
    public async Task CA_07_4_Borda_LimitesGeolocalizacaoValidos()
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();

        var groupId = Guid.NewGuid();
        var adminUserId = Guid.NewGuid();

        await using (var db = _fixture.CreateDbContext())
        {
            var group = new EconomicGroup { Id = groupId, Name = "GRP-1", Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var adminUser = new UserAccount { Id = adminUserId, Email = "platform_admin@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Utilizador Teste 04B", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.PLATFORM_ADMIN, CreatedAt = now, UpdatedAt = now };

            db.EconomicGroups.Add(group);
            db.UserAccounts.Add(adminUser);
            await db.SaveChangesAsync();
        }

        var issuer = CreateIssuer();
        var token = issuer.Issue(new AccessTokenClaims(
            Sub: adminUserId,
            FamilyId: Guid.NewGuid(),
            PlatformRole: PlatformRole.PLATFORM_ADMIN,
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
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        // Test boundary values: -90.0, -180.0
        var payload = new
        {
            group_id = groupId.ToString("D"),
            cnpj = "99000001000101",
            legal_name = "Tenant Limite",
            branch_type = "HQ",
            geo_lat = -90.0m,
            geo_lng = -180.0m,
            scopes = new[] { "BUYER" }
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/v1/tenants", content);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private class FailureOnTenantScopeInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<TenantScope>().Any() == true)
            {
                throw new InvalidOperationException("Simulated persistence failure in tenant_scope");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    [Fact(Timeout = 60000)]
    [Trait("Critico", "sim")]
    public async Task CA_07_5_RollbackTotalNaCriacaoDeTenantAposFalhaSimulada()
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();

        var groupId = Guid.NewGuid();
        var adminUserId = Guid.NewGuid();

        await using (var db = _fixture.CreateDbContext())
        {
            var group = new EconomicGroup { Id = groupId, Name = "GRP-1", Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var adminUser = new UserAccount { Id = adminUserId, Email = "platform_admin@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Utilizador Teste 05", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.PLATFORM_ADMIN, CreatedAt = now, UpdatedAt = now };

            db.EconomicGroups.Add(group);
            db.UserAccounts.Add(adminUser);
            await db.SaveChangesAsync();
        }

        var issuer = CreateIssuer();
        var token = issuer.Issue(new AccessTokenClaims(
            Sub: adminUserId,
            FamilyId: Guid.NewGuid(),
            PlatformRole: PlatformRole.PLATFORM_ADMIN,
            ViewMode: ViewMode.NONE,
            GroupId: null,
            TenantId: null,
            AllowedTenants: [],
            ActiveScope: null,
            Roles: [],
            Permissions: [],
            Act: null
        ));

        // Inject interceptor that fails during tenant_scope save
        await using var app = CreateTestApp(services =>
        {
            services.AddSingleton<IInterceptor>(new FailureOnTenantScopeInterceptor());
        });
        await app.StartAsync();
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        var payload = new
        {
            group_id = groupId.ToString("D"),
            cnpj = "99000001000101",
            legal_name = "Tenant Falha",
            branch_type = "HQ",
            geo_lat = 0.0m,
            geo_lng = 0.0m,
            scopes = new[] { "BUYER" }
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/v1/tenants", content);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("ERR-SYS-001", doc.RootElement.GetProperty("code").GetString());

        // Assert nothing persisted in DB
        await using (var db = _fixture.CreateDbContext())
        {
            var tenantCount = await db.Tenants.CountAsync(t => t.Cnpj == "99000001000101");
            Assert.Equal(0, tenantCount);

            var scopeCount = await db.TenantScopes.CountAsync();
            Assert.Equal(0, scopeCount);

            var outboxCount = await db.OutboxMessages.CountAsync();
            Assert.Equal(0, outboxCount);

            var auditCount = await db.AuthAuditLogs.CountAsync();
            Assert.Equal(0, auditCount);
        }
    }

    [Theory(Timeout = 60000)]
    [InlineData("BUYER")]
    [InlineData("TENANT_ADMIN_OUTRO_GRUPO")]
    [Trait("Critico", "sim")]
    public async Task CA_07_6_NegacaoDeAutorizacaoParaChamadoresSemPrivilegio(string perfil)
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();

        var groupId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcde");
        var otherGroupId = Guid.Parse("01923456-789a-7bc8-9012-3456789abce0");
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        await using (var db = _fixture.CreateDbContext())
        {
            var group1 = new EconomicGroup { Id = groupId, Name = "GRP-1", Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var group2 = new EconomicGroup { Id = otherGroupId, Name = "GRP-2", Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var tenant = new Tenant { Id = tenantId, GroupId = (perfil == "BUYER" ? groupId : otherGroupId), Cnpj = "99000001000101", LegalName = "Tenant 1", BranchType = BranchType.HQ, GeoLat = 0, GeoLng = 0, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var user = new UserAccount { Id = userId, Email = "user@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Utilizador Teste 06", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.NONE, CreatedAt = now, UpdatedAt = now };

            db.EconomicGroups.AddRange(group1, group2);
            db.Tenants.Add(tenant);
            db.UserAccounts.Add(user);
            await db.SaveChangesAsync();
        }

        var issuer = CreateIssuer();
        AccessTokenClaims claims;

        if (perfil == "BUYER")
        {
            claims = new AccessTokenClaims(
                Sub: userId,
                FamilyId: Guid.NewGuid(),
                PlatformRole: PlatformRole.NONE,
                ViewMode: ViewMode.SINGLE,
                GroupId: groupId,
                TenantId: tenantId,
                AllowedTenants: [tenantId],
                ActiveScope: OperationalScope.BUYER,
                Roles: ["BUYER_OPERATOR"],
                Permissions: ["demanda:criar"],
                Act: null
            );
        }
        else
        {
            // TENANT_ADMIN of other group
            claims = new AccessTokenClaims(
                Sub: userId,
                FamilyId: Guid.NewGuid(),
                PlatformRole: PlatformRole.NONE,
                ViewMode: ViewMode.SINGLE,
                GroupId: otherGroupId,
                TenantId: tenantId,
                AllowedTenants: [tenantId],
                ActiveScope: OperationalScope.TENANT_ADMIN,
                Roles: ["GROUP_MANAGER"],
                Permissions: ["tenant:criar"],
                Act: null
            );
        }

        var token = issuer.Issue(claims);

        await using var app = CreateTestApp();
        await app.StartAsync();
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        var payload = new
        {
            group_id = groupId.ToString("D"),
            cnpj = "99000002000186",
            legal_name = "Tenant Nao Autorizado",
            branch_type = "HQ",
            geo_lat = 0.0m,
            geo_lng = 0.0m,
            scopes = new[] { "BUYER" }
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/v1/tenants", content);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("ERR-CTX-007", doc.RootElement.GetProperty("code").GetString());
    }

    [Theory(Timeout = 60000)]
    [InlineData("01923456-789a-7bc8-9012-000000000000", HttpStatusCode.NotFound, "ERR-CTX-008")]
    [InlineData("01923456-789a-7bc8-9012-3456789abcdf", HttpStatusCode.Forbidden, "ERR-CTX-002")]
    [Trait("Critico", "sim")]
    public async Task CA_07_7_RejeicaoDeGrupoEconomicoInexistenteOuSuspenso(string grupoStr, HttpStatusCode expectedStatus, string expectedCode)
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();

        var adminUserId = Guid.NewGuid();
        var suspendedGroupId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcdf");

        await using (var db = _fixture.CreateDbContext())
        {
            var suspGroup = new EconomicGroup { Id = suspendedGroupId, Name = "GRP-SUSP", Status = EntityStatus.SUSPENDED, CreatedAt = now, UpdatedAt = now };
            var adminUser = new UserAccount { Id = adminUserId, Email = "platform_admin@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Utilizador Teste 07", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.PLATFORM_ADMIN, CreatedAt = now, UpdatedAt = now };

            db.EconomicGroups.Add(suspGroup);
            db.UserAccounts.Add(adminUser);
            await db.SaveChangesAsync();
        }

        var issuer = CreateIssuer();
        var token = issuer.Issue(new AccessTokenClaims(
            Sub: adminUserId,
            FamilyId: Guid.NewGuid(),
            PlatformRole: PlatformRole.PLATFORM_ADMIN,
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
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        var payload = new
        {
            group_id = grupoStr,
            cnpj = "99000001000101",
            legal_name = "Tenant Teste",
            branch_type = "HQ",
            geo_lat = 0.0m,
            geo_lng = 0.0m,
            scopes = new[] { "BUYER" }
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/v1/tenants", content);
        Assert.Equal(expectedStatus, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(expectedCode, doc.RootElement.GetProperty("code").GetString());
    }

    [Theory(Timeout = 60000)]
    [InlineData("{\"legal_name\":\"A\"}")]
    [InlineData("{\"legal_name\":\" A \"}")]
    [InlineData("{\"scopes\":[]}")]
    [InlineData("{\"scopes\":[\"BUYER\",\"BUYER\"]}")]
    [InlineData("{\"branch_type\":\"INVALID\"}")]
    [InlineData("{\"group_id\":\"nao-uuid\"}")]
    public async Task CA_07_8_RejeicaoDeFormatoDeEntradaMalformada(string caso)
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();

        var adminUserId = Guid.NewGuid();

        await using (var db = _fixture.CreateDbContext())
        {
            var adminUser = new UserAccount { Id = adminUserId, Email = "platform_admin@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Utilizador Teste 08", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.PLATFORM_ADMIN, CreatedAt = now, UpdatedAt = now };
            db.UserAccounts.Add(adminUser);
            await db.SaveChangesAsync();
        }

        var issuer = CreateIssuer();
        var token = issuer.Issue(new AccessTokenClaims(
            Sub: adminUserId,
            FamilyId: Guid.NewGuid(),
            PlatformRole: PlatformRole.PLATFORM_ADMIN,
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
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        // Merge case into full valid payload to test specific field malformation
        var validDict = new Dictionary<string, object>
        {
            ["group_id"] = "01923456-789a-7bc8-9012-3456789abcde",
            ["cnpj"] = "99000001000101",
            ["legal_name"] = "Empresa Valida LTDA",
            ["branch_type"] = "HQ",
            ["geo_lat"] = 0.0,
            ["geo_lng"] = 0.0,
            ["scopes"] = new[] { "BUYER" }
        };

        using var caseDoc = JsonDocument.Parse(caso);
        foreach (var prop in caseDoc.RootElement.EnumerateObject())
        {
            validDict[prop.Name] = JsonSerializer.Deserialize<object>(prop.Value.GetRawText())!;
        }

        var content = new StringContent(JsonSerializer.Serialize(validDict), Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/v1/tenants", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("ERR-VAL-001", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact(Timeout = 60000)]
    public async Task CA_07_8_Borda_ComprimentoLegalName()
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();

        var groupId = Guid.NewGuid();
        var adminUserId = Guid.NewGuid();

        await using (var db = _fixture.CreateDbContext())
        {
            var group = new EconomicGroup { Id = groupId, Name = "GRP-1", Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var adminUser = new UserAccount { Id = adminUserId, Email = "platform_admin@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Utilizador Teste 08B", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.PLATFORM_ADMIN, CreatedAt = now, UpdatedAt = now };

            db.EconomicGroups.Add(group);
            db.UserAccounts.Add(adminUser);
            await db.SaveChangesAsync();
        }

        var issuer = CreateIssuer();
        var token = issuer.Issue(new AccessTokenClaims(
            Sub: adminUserId,
            FamilyId: Guid.NewGuid(),
            PlatformRole: PlatformRole.PLATFORM_ADMIN,
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
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        // 2 chars -> Valid (201)
        var p2 = new
        {
            group_id = groupId.ToString("D"),
            cnpj = "99000001000101",
            legal_name = "AB",
            branch_type = "HQ",
            geo_lat = 0.0m,
            geo_lng = 0.0m,
            scopes = new[] { "BUYER" }
        };
        var r2 = await client.PostAsync("/api/v1/tenants", new StringContent(JsonSerializer.Serialize(p2), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Created, r2.StatusCode);

        // 200 chars -> Valid (201)
        var p200 = new
        {
            group_id = groupId.ToString("D"),
            cnpj = "99000002000186",
            legal_name = new string('A', 200),
            branch_type = "BRANCH",
            geo_lat = 0.0m,
            geo_lng = 0.0m,
            scopes = new[] { "BUYER" }
        };
        var r200 = await client.PostAsync("/api/v1/tenants", new StringContent(JsonSerializer.Serialize(p200), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Created, r200.StatusCode);

        // 201 chars -> Invalid (400)
        var p201 = new
        {
            group_id = groupId.ToString("D"),
            cnpj = "99000001000101",
            legal_name = new string('A', 201),
            branch_type = "HQ",
            geo_lat = 0.0m,
            geo_lng = 0.0m,
            scopes = new[] { "BUYER" }
        };
        var r201 = await client.PostAsync("/api/v1/tenants", new StringContent(JsonSerializer.Serialize(p201), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, r201.StatusCode);
    }

    [Fact(Timeout = 60000)]
    public async Task ITenantStatusWriter_SetStatusAsync_AlteraStatusEUpdatedAt()
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();

        var groupId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        await using (var db = _fixture.CreateDbContext())
        {
            var group = new EconomicGroup { Id = groupId, Name = "GRP-1", Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var tenant = new Tenant { Id = tenantId, GroupId = groupId, Cnpj = "99000001000101", LegalName = "Tenant 1", BranchType = BranchType.HQ, GeoLat = 0, GeoLng = 0, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };

            db.EconomicGroups.Add(group);
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
        }

        await using (var db = _fixture.CreateDbContext())
        {
            var writer = new TenantStatusWriter(db, _timeProvider);
            _timeProvider.Advance(TimeSpan.FromSeconds(10));

            var result = await writer.SetStatusAsync(tenantId, EntityStatus.SUSPENDED);
            Assert.NotNull(result);
            Assert.Equal(EntityStatus.ACTIVE, result.Previous);
            Assert.Equal(groupId, result.GroupId);
            Assert.Equal("99000001000101", result.Cnpj);
            Assert.Equal(_timeProvider.GetUtcNow(), result.UpdatedAt);

            await db.SaveChangesAsync();

            var updatedTenant = await db.Tenants.FindAsync(tenantId);
            Assert.NotNull(updatedTenant);
            Assert.Equal(EntityStatus.SUSPENDED, updatedTenant.Status);
            Assert.Equal(_timeProvider.GetUtcNow(), updatedTenant.UpdatedAt);
        }
    }

    [Fact(Timeout = 60000)]
    public async Task ITenantStatusWriter_SetStatusAsync_EstadoRepetidoNaoAltera()
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();

        var groupId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        await using (var db = _fixture.CreateDbContext())
        {
            var group = new EconomicGroup { Id = groupId, Name = "GRP-1", Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var tenant = new Tenant { Id = tenantId, GroupId = groupId, Cnpj = "99000001000101", LegalName = "Tenant 1", BranchType = BranchType.HQ, GeoLat = 0, GeoLng = 0, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };

            db.EconomicGroups.Add(group);
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
        }

        await using (var db = _fixture.CreateDbContext())
        {
            var writer = new TenantStatusWriter(db, _timeProvider);
            _timeProvider.Advance(TimeSpan.FromSeconds(10));

            var result = await writer.SetStatusAsync(tenantId, EntityStatus.ACTIVE);
            Assert.NotNull(result);
            Assert.Equal(EntityStatus.ACTIVE, result.Previous);
            Assert.Equal(now, result.UpdatedAt);
        }
    }

    [Fact(Timeout = 60000)]
    public async Task ITenantStatusWriter_SetStatusAsync_TenantInexistenteRetornaNull()
    {
        await _fixture.CleanDatabaseAsync();

        await using var db = _fixture.CreateDbContext();
        var writer = new TenantStatusWriter(db, _timeProvider);

        var result = await writer.SetStatusAsync(Guid.NewGuid(), EntityStatus.SUSPENDED);
        Assert.Null(result);
    }
}
