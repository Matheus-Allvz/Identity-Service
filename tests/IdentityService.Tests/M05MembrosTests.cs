using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using IdentityService.Modules.M00Transversal;
using IdentityService.Modules.M00Transversal.Auth;
using IdentityService.Modules.M01Login;
using IdentityService.Modules.M02Refresh;
using IdentityService.Modules.M03Contexto;
using IdentityService.Modules.M04Tenants;
using IdentityService.Modules.M05Membros;
using IdentityService.Modules.M05Membros.Interface;
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
public class M05MembrosTests
{
    private readonly TestcontainersFixture _fixture;
    private readonly TestSigningKey _signingKey = new("key-2026-01");
    private readonly TestTimeProvider _timeProvider = new(new DateTimeOffset(2026, 11, 2, 22, 0, 0, TimeSpan.Zero));

    private static readonly Guid BuyerRoleId = Guid.Parse("01900000-0000-7000-8000-000000000001");
    private static readonly Guid CarrierRoleId = Guid.Parse("01900000-0000-7000-8000-000000000003");
    private static readonly Guid GroupManagerRoleId = Guid.Parse("01900000-0000-7000-8000-000000000004");

    public M05MembrosTests(TestcontainersFixture fixture)
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
    public async Task CA_08_1_VinculacaoDeMembroComSucessoPorTenantAdmin()
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();

        var groupId = Guid.NewGuid();
        var tenantId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcde");
        var adminUserId = Guid.NewGuid();
        var targetUserId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcd0");

        await using (var db = _fixture.CreateDbContext())
        {
            var group = new EconomicGroup { Id = groupId, Name = "GRP-01", Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var tenant = new Tenant { Id = tenantId, GroupId = groupId, Cnpj = "99000001000199", LegalName = "Tenant Comprador", BranchType = BranchType.HQ, GeoLat = 0, GeoLng = 0, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var scopeBuyer = new TenantScope { Id = Guid.NewGuid(), TenantId = tenantId, Scope = OperationalScope.BUYER, IsActive = true, CreatedAt = now };
            var scopeTenantAdmin = new TenantScope { Id = Guid.NewGuid(), TenantId = tenantId, Scope = OperationalScope.TENANT_ADMIN, IsActive = true, CreatedAt = now };

            var adminUser = new UserAccount { Id = adminUserId, Email = "admin@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Admin Teste", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.NONE, CreatedAt = now, UpdatedAt = now };
            var targetUser = new UserAccount { Id = targetUserId, Email = "target@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Target User", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.NONE, CreatedAt = now, UpdatedAt = now };

            var adminMember = new TenantMember { Id = Guid.NewGuid(), TenantId = tenantId, UserId = adminUserId, IsOwner = true, CanViewGroup = true, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var adminMemberRole = new MemberRole { Id = Guid.NewGuid(), TenantId = tenantId, TenantMemberId = adminMember.Id, RoleId = GroupManagerRoleId, CreatedAt = now };

            db.EconomicGroups.Add(group);
            db.Tenants.Add(tenant);
            db.TenantScopes.AddRange(scopeBuyer, scopeTenantAdmin);
            db.UserAccounts.AddRange(adminUser, targetUser);
            db.TenantMembers.Add(adminMember);
            db.MemberRoles.Add(adminMemberRole);
            await db.SaveChangesAsync();
        }

        var issuer = CreateIssuer();
        var token = issuer.Issue(new AccessTokenClaims(
            Sub: adminUserId,
            FamilyId: Guid.NewGuid(),
            PlatformRole: PlatformRole.NONE,
            ViewMode: ViewMode.SINGLE,
            GroupId: groupId,
            TenantId: tenantId,
            AllowedTenants: [tenantId],
            ActiveScope: OperationalScope.TENANT_ADMIN,
            Roles: ["GROUP_MANAGER"],
            Permissions: ["membro:vincular"],
            Act: null
        ));

        await using var app = CreateTestApp();
        await app.StartAsync();
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        var payload = new
        {
            user_id = targetUserId.ToString("D"),
            is_owner = false,
            can_view_group = true,
            role_ids = new[] { BuyerRoleId.ToString("D") }
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.PostAsync($"/api/v1/tenants/{tenantId:D}/members", content);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(Guid.TryParse(root.GetProperty("member_id").GetString(), out var memberId));
        Assert.Equal(tenantId.ToString("D"), root.GetProperty("tenant_id").GetString());
        Assert.Equal(targetUserId.ToString("D"), root.GetProperty("user_id").GetString());
        Assert.False(root.GetProperty("is_owner").GetBoolean());
        Assert.True(root.GetProperty("can_view_group").GetBoolean());
        Assert.Equal("ACTIVE", root.GetProperty("status").GetString());

        string[] rolesJson = root.GetProperty("roles").EnumerateArray().Select(r => r.GetString()!).ToArray();
        Assert.Equal(new[] { "BUYER_OPERATOR" }, rolesJson);

        var createdAt = root.GetProperty("created_at").GetString();
        Assert.NotNull(createdAt);

        // Assert database state
        await using (var db = _fixture.CreateDbContext())
        {
            var memberInDb = await db.TenantMembers.Include(m => m.MemberRoles).ThenInclude(mr => mr.Role).FirstOrDefaultAsync(m => m.Id == memberId);
            Assert.NotNull(memberInDb);
            Assert.Equal(tenantId, memberInDb.TenantId);
            Assert.Equal(targetUserId, memberInDb.UserId);
            Assert.False(memberInDb.IsOwner);
            Assert.True(memberInDb.CanViewGroup);
            Assert.Equal(EntityStatus.ACTIVE, memberInDb.Status);

            Assert.Single(memberInDb.MemberRoles);
            Assert.Equal(BuyerRoleId, memberInDb.MemberRoles.First().RoleId);
            Assert.Equal("BUYER_OPERATOR", memberInDb.MemberRoles.First().Role.Name);

            var auditInDb = await db.AuthAuditLogs.FirstOrDefaultAsync(a => a.TenantId == tenantId && a.EventType == AuditEventType.MEMBER_ADDED && a.UserId == targetUserId);
            Assert.NotNull(auditInDb);
            Assert.Equal(adminUserId, auditInDb.ActorUserId);
            Assert.Equal(groupId, auditInDb.GroupId);
        }
    }

    [Fact(Timeout = 60000)]
    public async Task CA_08_2_VinculacaoComSucessoPorPlatformAdmin()
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();

        var groupId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var adminUserId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();

        await using (var db = _fixture.CreateDbContext())
        {
            var group = new EconomicGroup { Id = groupId, Name = "GRP-02", Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var tenant = new Tenant { Id = tenantId, GroupId = groupId, Cnpj = "99000001000199", LegalName = "Tenant Teste", BranchType = BranchType.HQ, GeoLat = 0, GeoLng = 0, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var scopeBuyer = new TenantScope { Id = Guid.NewGuid(), TenantId = tenantId, Scope = OperationalScope.BUYER, IsActive = true, CreatedAt = now };

            var adminUser = new UserAccount { Id = adminUserId, Email = "platform_admin@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Platform Admin", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.PLATFORM_ADMIN, CreatedAt = now, UpdatedAt = now };
            var targetUser = new UserAccount { Id = targetUserId, Email = "target2@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Target User 2", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.NONE, CreatedAt = now, UpdatedAt = now };

            db.EconomicGroups.Add(group);
            db.Tenants.Add(tenant);
            db.TenantScopes.Add(scopeBuyer);
            db.UserAccounts.AddRange(adminUser, targetUser);
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
            user_id = targetUserId.ToString("D"),
            is_owner = true,
            can_view_group = false,
            role_ids = new[] { BuyerRoleId.ToString("D") }
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.PostAsync($"/api/v1/tenants/{tenantId:D}/members", content);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.GetProperty("is_owner").GetBoolean());
        Assert.False(root.GetProperty("can_view_group").GetBoolean());
        Assert.Equal("ACTIVE", root.GetProperty("status").GetString());

        await using (var db = _fixture.CreateDbContext())
        {
            var memberInDb = await db.TenantMembers.FirstOrDefaultAsync(m => m.TenantId == tenantId && m.UserId == targetUserId);
            Assert.NotNull(memberInDb);
            Assert.True(memberInDb.IsOwner);
            Assert.False(memberInDb.CanViewGroup);
            Assert.Equal(EntityStatus.ACTIVE, memberInDb.Status);

            var auditInDb = await db.AuthAuditLogs.FirstOrDefaultAsync(a => a.TenantId == tenantId && a.EventType == AuditEventType.MEMBER_ADDED && a.UserId == targetUserId);
            Assert.NotNull(auditInDb);
            Assert.Equal(adminUserId, auditInDb.ActorUserId);
        }
    }

    [Fact(Timeout = 60000)]
    [Trait("Critico", "sim")]
    public async Task CA_08_3_ConflitoDeUnicidadeAoVincularMembroJaExistente()
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();

        var groupId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var adminUserId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();

        await using (var db = _fixture.CreateDbContext())
        {
            var group = new EconomicGroup { Id = groupId, Name = "GRP-03", Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var tenant = new Tenant { Id = tenantId, GroupId = groupId, Cnpj = "99000001000199", LegalName = "Tenant Unicidade", BranchType = BranchType.HQ, GeoLat = 0, GeoLng = 0, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var scopeBuyer = new TenantScope { Id = Guid.NewGuid(), TenantId = tenantId, Scope = OperationalScope.BUYER, IsActive = true, CreatedAt = now };

            var adminUser = new UserAccount { Id = adminUserId, Email = "platform_admin@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Platform Admin", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.PLATFORM_ADMIN, CreatedAt = now, UpdatedAt = now };
            var targetUser = new UserAccount { Id = targetUserId, Email = "existing_member@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Target User", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.NONE, CreatedAt = now, UpdatedAt = now };

            // Already existing membership
            var existingMember = new TenantMember { Id = Guid.NewGuid(), TenantId = tenantId, UserId = targetUserId, IsOwner = false, CanViewGroup = false, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };

            db.EconomicGroups.Add(group);
            db.Tenants.Add(tenant);
            db.TenantScopes.Add(scopeBuyer);
            db.UserAccounts.AddRange(adminUser, targetUser);
            db.TenantMembers.Add(existingMember);
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
            user_id = targetUserId.ToString("D"),
            is_owner = false,
            can_view_group = true,
            role_ids = new[] { BuyerRoleId.ToString("D") }
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.PostAsync($"/api/v1/tenants/{tenantId:D}/members", content);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("ERR-CTX-005", doc.RootElement.GetProperty("code").GetString());

        // Assert no new records created
        await using (var db = _fixture.CreateDbContext())
        {
            var count = await db.TenantMembers.CountAsync(m => m.TenantId == tenantId && m.UserId == targetUserId);
            Assert.Equal(1, count);
        }
    }

    [Fact(Timeout = 60000)]
    [Trait("Critico", "sim")]
    public async Task CA_08_4_RejeicaoAoAtribuirPapelComEscopoInativoNoTenant()
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();

        var groupId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var adminUserId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();

        await using (var db = _fixture.CreateDbContext())
        {
            var group = new EconomicGroup { Id = groupId, Name = "GRP-04", Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var tenant = new Tenant { Id = tenantId, GroupId = groupId, Cnpj = "99000001000199", LegalName = "Tenant Buyer Only", BranchType = BranchType.HQ, GeoLat = 0, GeoLng = 0, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            // Tenant has BUYER scope active, but CarrierRoleId has CARRIER scope
            var scopeBuyer = new TenantScope { Id = Guid.NewGuid(), TenantId = tenantId, Scope = OperationalScope.BUYER, IsActive = true, CreatedAt = now };

            var adminUser = new UserAccount { Id = adminUserId, Email = "platform_admin@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Platform Admin", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.PLATFORM_ADMIN, CreatedAt = now, UpdatedAt = now };
            var targetUser = new UserAccount { Id = targetUserId, Email = "target_carrier@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Target User", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.NONE, CreatedAt = now, UpdatedAt = now };

            db.EconomicGroups.Add(group);
            db.Tenants.Add(tenant);
            db.TenantScopes.Add(scopeBuyer);
            db.UserAccounts.AddRange(adminUser, targetUser);
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

        // Attempt to assign CARRIER role to a tenant that only has BUYER active scope
        var payload = new
        {
            user_id = targetUserId.ToString("D"),
            is_owner = false,
            can_view_group = false,
            role_ids = new[] { CarrierRoleId.ToString("D") }
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.PostAsync($"/api/v1/tenants/{tenantId:D}/members", content);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("ERR-CTX-006", doc.RootElement.GetProperty("code").GetString());

        // Assert nothing persisted
        await using (var db = _fixture.CreateDbContext())
        {
            var memberExists = await db.TenantMembers.AnyAsync(m => m.TenantId == tenantId && m.UserId == targetUserId);
            Assert.False(memberExists);
        }
    }

    private class FailureOnMemberRoleInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<MemberRole>().Any() == true)
            {
                throw new InvalidOperationException("Simulated persistence failure in member_role");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    [Fact(Timeout = 60000)]
    [Trait("Critico", "sim")]
    public async Task CA_08_5_RollbackTotalNaVinculacaoDeMembroAposFalhaSimulada()
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();

        var groupId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var adminUserId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();

        await using (var db = _fixture.CreateDbContext())
        {
            var group = new EconomicGroup { Id = groupId, Name = "GRP-05", Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var tenant = new Tenant { Id = tenantId, GroupId = groupId, Cnpj = "99000001000199", LegalName = "Tenant Rollback", BranchType = BranchType.HQ, GeoLat = 0, GeoLng = 0, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var scopeBuyer = new TenantScope { Id = Guid.NewGuid(), TenantId = tenantId, Scope = OperationalScope.BUYER, IsActive = true, CreatedAt = now };

            var adminUser = new UserAccount { Id = adminUserId, Email = "platform_admin@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Platform Admin", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.PLATFORM_ADMIN, CreatedAt = now, UpdatedAt = now };
            var targetUser = new UserAccount { Id = targetUserId, Email = "target_rollback@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Target User", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.NONE, CreatedAt = now, UpdatedAt = now };

            db.EconomicGroups.Add(group);
            db.Tenants.Add(tenant);
            db.TenantScopes.Add(scopeBuyer);
            db.UserAccounts.AddRange(adminUser, targetUser);
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

        // Inject interceptor that fails during member_role save
        await using var app = CreateTestApp(services =>
        {
            services.AddSingleton<IInterceptor>(new FailureOnMemberRoleInterceptor());
        });
        await app.StartAsync();
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        var payload = new
        {
            user_id = targetUserId.ToString("D"),
            is_owner = false,
            can_view_group = false,
            role_ids = new[] { BuyerRoleId.ToString("D") }
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.PostAsync($"/api/v1/tenants/{tenantId:D}/members", content);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("ERR-SYS-001", doc.RootElement.GetProperty("code").GetString());

        // Assert nothing persisted in DB
        await using (var db = _fixture.CreateDbContext())
        {
            var memberExists = await db.TenantMembers.AnyAsync(m => m.TenantId == tenantId && m.UserId == targetUserId);
            Assert.False(memberExists);

            var auditExists = await db.AuthAuditLogs.AnyAsync(a => a.TenantId == tenantId && a.UserId == targetUserId);
            Assert.False(auditExists);
        }
    }

    [Theory(Timeout = 60000)]
    [Trait("Critico", "sim")]
    [InlineData("BUYER")]
    [InlineData("TENANT_ADMIN_OUTRO_TENANT")]
    public async Task CA_08_6_NegacaoDeAutorizacaoParaChamadoresSemPermissao(string perfil)
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();

        var groupId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var callerUserId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();

        await using (var db = _fixture.CreateDbContext())
        {
            var group = new EconomicGroup { Id = groupId, Name = "GRP-06", Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var tenant = new Tenant { Id = tenantId, GroupId = groupId, Cnpj = "99000001000199", LegalName = "Tenant Principal", BranchType = BranchType.HQ, GeoLat = 0, GeoLng = 0, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var otherTenant = new Tenant { Id = otherTenantId, GroupId = groupId, Cnpj = "99000001000101", LegalName = "Outro Tenant", BranchType = BranchType.BRANCH, GeoLat = 0, GeoLng = 0, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var scopeBuyer = new TenantScope { Id = Guid.NewGuid(), TenantId = tenantId, Scope = OperationalScope.BUYER, IsActive = true, CreatedAt = now };

            var callerUser = new UserAccount { Id = callerUserId, Email = "caller@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Caller User", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.NONE, CreatedAt = now, UpdatedAt = now };
            var targetUser = new UserAccount { Id = targetUserId, Email = "target_auth@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Target User", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.NONE, CreatedAt = now, UpdatedAt = now };

            db.EconomicGroups.Add(group);
            db.Tenants.AddRange(tenant, otherTenant);
            db.TenantScopes.Add(scopeBuyer);
            db.UserAccounts.AddRange(callerUser, targetUser);
            await db.SaveChangesAsync();
        }

        AccessTokenClaims claims;
        if (perfil == "BUYER")
        {
            claims = new AccessTokenClaims(
                Sub: callerUserId,
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
            // TENANT_ADMIN_OUTRO_TENANT: active on otherTenantId, trying to write to tenantId
            claims = new AccessTokenClaims(
                Sub: callerUserId,
                FamilyId: Guid.NewGuid(),
                PlatformRole: PlatformRole.NONE,
                ViewMode: ViewMode.SINGLE,
                GroupId: groupId,
                TenantId: otherTenantId,
                AllowedTenants: [otherTenantId],
                ActiveScope: OperationalScope.TENANT_ADMIN,
                Roles: ["GROUP_MANAGER"],
                Permissions: ["membro:vincular"],
                Act: null
            );
        }

        var issuer = CreateIssuer();
        var token = issuer.Issue(claims);

        await using var app = CreateTestApp();
        await app.StartAsync();
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        var payload = new
        {
            user_id = targetUserId.ToString("D"),
            is_owner = false,
            can_view_group = false,
            role_ids = new[] { BuyerRoleId.ToString("D") }
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.PostAsync($"/api/v1/tenants/{tenantId:D}/members", content);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("ERR-CTX-007", doc.RootElement.GetProperty("code").GetString());

        await using (var db = _fixture.CreateDbContext())
        {
            var memberExists = await db.TenantMembers.AnyAsync(m => m.TenantId == tenantId && m.UserId == targetUserId);
            Assert.False(memberExists);
        }
    }

    [Theory(Timeout = 60000)]
    [InlineData("tenant")]
    [InlineData("usuario")]
    [InlineData("papel")]
    public async Task CA_08_7_RejeicaoAoReferenciarRecursoInexistente(string tipoRecurso)
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();

        var groupId = Guid.NewGuid();
        var existingTenantId = Guid.NewGuid();
        var adminUserId = Guid.NewGuid();
        var existingUserId = Guid.NewGuid();

        await using (var db = _fixture.CreateDbContext())
        {
            var group = new EconomicGroup { Id = groupId, Name = "GRP-07", Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var tenant = new Tenant { Id = existingTenantId, GroupId = groupId, Cnpj = "99000001000199", LegalName = "Tenant Existente", BranchType = BranchType.HQ, GeoLat = 0, GeoLng = 0, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var scopeBuyer = new TenantScope { Id = Guid.NewGuid(), TenantId = existingTenantId, Scope = OperationalScope.BUYER, IsActive = true, CreatedAt = now };

            var adminUser = new UserAccount { Id = adminUserId, Email = "platform_admin@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Platform Admin", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.PLATFORM_ADMIN, CreatedAt = now, UpdatedAt = now };
            var existingUser = new UserAccount { Id = existingUserId, Email = "target_inexistente@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Target User", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.NONE, CreatedAt = now, UpdatedAt = now };

            db.EconomicGroups.Add(group);
            db.Tenants.Add(tenant);
            db.TenantScopes.Add(scopeBuyer);
            db.UserAccounts.AddRange(adminUser, existingUser);
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

        var targetTenantId = (tipoRecurso == "tenant") ? Guid.NewGuid() : existingTenantId;
        var targetUserId = (tipoRecurso == "usuario") ? Guid.NewGuid() : existingUserId;
        var targetRoleId = (tipoRecurso == "papel") ? Guid.NewGuid() : BuyerRoleId;

        var payload = new
        {
            user_id = targetUserId.ToString("D"),
            is_owner = false,
            can_view_group = false,
            role_ids = new[] { targetRoleId.ToString("D") }
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.PostAsync($"/api/v1/tenants/{targetTenantId:D}/members", content);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("ERR-CTX-008", doc.RootElement.GetProperty("code").GetString());
    }

    [Theory(Timeout = 60000)]
    [InlineData("EMPTY_ROLES")]
    [InlineData("DUPLICATE_ROLES")]
    [InlineData("ELEVEN_ROLES")]
    [InlineData("NON_UUID_USER_ID")]
    [InlineData("NON_UUID_PATH_TENANT_ID")]
    public async Task CA_08_8_RejeicaoDeFormatoDeEntradaMalformada(string caso)
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();

        var adminUserId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();

        await using (var db = _fixture.CreateDbContext())
        {
            var adminUser = new UserAccount { Id = adminUserId, Email = "platform_admin@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Platform Admin", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.PLATFORM_ADMIN, CreatedAt = now, UpdatedAt = now };
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

        string requestPath = $"/api/v1/tenants/{tenantId:D}/members";
        string jsonBody;

        switch (caso)
        {
            case "EMPTY_ROLES":
                jsonBody = JsonSerializer.Serialize(new
                {
                    user_id = targetUserId.ToString("D"),
                    is_owner = false,
                    can_view_group = false,
                    role_ids = Array.Empty<string>()
                });
                break;
            case "DUPLICATE_ROLES":
                jsonBody = JsonSerializer.Serialize(new
                {
                    user_id = targetUserId.ToString("D"),
                    is_owner = false,
                    can_view_group = false,
                    role_ids = new[] { BuyerRoleId.ToString("D"), BuyerRoleId.ToString("D") }
                });
                break;
            case "ELEVEN_ROLES":
                var elevenIds = Enumerable.Range(1, 11).Select(_ => Guid.NewGuid().ToString("D")).ToArray();
                jsonBody = JsonSerializer.Serialize(new
                {
                    user_id = targetUserId.ToString("D"),
                    is_owner = false,
                    can_view_group = false,
                    role_ids = elevenIds
                });
                break;
            case "NON_UUID_USER_ID":
                jsonBody = JsonSerializer.Serialize(new
                {
                    user_id = "nao-uuid",
                    is_owner = false,
                    can_view_group = false,
                    role_ids = new[] { BuyerRoleId.ToString("D") }
                });
                break;
            case "NON_UUID_PATH_TENANT_ID":
                requestPath = "/api/v1/tenants/nao-uuid/members";
                jsonBody = JsonSerializer.Serialize(new
                {
                    user_id = targetUserId.ToString("D"),
                    is_owner = false,
                    can_view_group = false,
                    role_ids = new[] { BuyerRoleId.ToString("D") }
                });
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(caso));
        }

        var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        var response = await client.PostAsync(requestPath, content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("ERR-VAL-001", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact(Timeout = 60000)]
    public async Task CA_08_Boundary_Exatamente10PapeisDistintos_Sucesso201()
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();

        var groupId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var adminUserId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();

        var roles10 = new List<Role>();
        for (int i = 1; i <= 10; i++)
        {
            roles10.Add(new Role
            {
                Id = Guid.CreateVersion7(now),
                Name = $"BUYER_ROLE_{i:D2}",
                Scope = OperationalScope.BUYER,
                Permissions = new[] { "demanda:criar" },
                CreatedAt = now
            });
        }

        await using (var db = _fixture.CreateDbContext())
        {
            var group = new EconomicGroup { Id = groupId, Name = "GRP-BOUND-10", Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var tenant = new Tenant { Id = tenantId, GroupId = groupId, Cnpj = "99000001000199", LegalName = "Tenant 10 Papeis", BranchType = BranchType.HQ, GeoLat = 0, GeoLng = 0, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var scopeBuyer = new TenantScope { Id = Guid.NewGuid(), TenantId = tenantId, Scope = OperationalScope.BUYER, IsActive = true, CreatedAt = now };

            var adminUser = new UserAccount { Id = adminUserId, Email = "platform_admin@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Platform Admin", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.PLATFORM_ADMIN, CreatedAt = now, UpdatedAt = now };
            var targetUser = new UserAccount { Id = targetUserId, Email = "target10@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Target User 10", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.NONE, CreatedAt = now, UpdatedAt = now };

            db.EconomicGroups.Add(group);
            db.Tenants.Add(tenant);
            db.TenantScopes.Add(scopeBuyer);
            db.UserAccounts.AddRange(adminUser, targetUser);
            db.Roles.AddRange(roles10);
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
            user_id = targetUserId.ToString("D"),
            is_owner = false,
            can_view_group = false,
            role_ids = roles10.Select(r => r.Id.ToString("D")).ToArray()
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.PostAsync($"/api/v1/tenants/{tenantId:D}/members", content);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var rolesReturned = root.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).ToArray();
        Assert.Equal(10, rolesReturned.Length);

        await using (var db = _fixture.CreateDbContext())
        {
            var memberRolesCount = await db.MemberRoles.CountAsync(mr => mr.TenantId == tenantId);
            Assert.Equal(10, memberRolesCount);
        }
    }

    [Fact(Timeout = 60000)]
    public async Task IMembershipWriter_AddCreatorMembership_AdicionaEntidadesNoChangeTracker()
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();

        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await using var db = _fixture.CreateDbContext();
        var tenantIsolation = new TenantIsolation();
        var writer = new MembershipWriter(db, tenantIsolation, _timeProvider);

        writer.AddCreatorMembership(tenantId, userId);

        var memberEntry = db.ChangeTracker.Entries<TenantMember>().FirstOrDefault();
        Assert.NotNull(memberEntry);
        Assert.Equal(EntityState.Added, memberEntry.State);
        Assert.Equal(tenantId, memberEntry.Entity.TenantId);
        Assert.Equal(userId, memberEntry.Entity.UserId);
        Assert.False(memberEntry.Entity.IsOwner);
        Assert.False(memberEntry.Entity.CanViewGroup);
        Assert.Equal(EntityStatus.ACTIVE, memberEntry.Entity.Status);

        var memberRoleEntry = db.ChangeTracker.Entries<MemberRole>().FirstOrDefault();
        Assert.NotNull(memberRoleEntry);
        Assert.Equal(EntityState.Added, memberRoleEntry.State);
        Assert.Equal(tenantId, memberRoleEntry.Entity.TenantId);
        Assert.Equal(GroupManagerRoleId, memberRoleEntry.Entity.RoleId);
        Assert.Equal(memberEntry.Entity.Id, memberRoleEntry.Entity.TenantMemberId);
    }

    [Fact(Timeout = 60000)]
    public async Task IMembershipWriter_SetGroupMembershipsStatusAsync_AlteraApenasNaoOwnersERetornaTenantsAfetados()
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();

        var groupId = Guid.NewGuid();
        var tenant1Id = Guid.NewGuid();
        var tenant2Id = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await using (var db = _fixture.CreateDbContext())
        {
            var group = new EconomicGroup { Id = groupId, Name = "GRP-WRITER", Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var tenant1 = new Tenant { Id = tenant1Id, GroupId = groupId, Cnpj = "99000001000199", LegalName = "Tenant 1", BranchType = BranchType.HQ, GeoLat = 0, GeoLng = 0, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var tenant2 = new Tenant { Id = tenant2Id, GroupId = groupId, Cnpj = "99000001000101", LegalName = "Tenant 2", BranchType = BranchType.BRANCH, GeoLat = 0, GeoLng = 0, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var user = new UserAccount { Id = userId, Email = "user_writer@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "User Writer", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.NONE, CreatedAt = now, UpdatedAt = now };

            // Tenant 1: user is OWNER -> MUST NOT BE ALTERED (RN-75 / PRS-151)
            var memberOwner = new TenantMember { Id = Guid.NewGuid(), TenantId = tenant1Id, UserId = userId, IsOwner = true, CanViewGroup = true, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            // Tenant 2: user is regular member (IsOwner = false) -> MUST BE ALTERED
            var memberNonOwner = new TenantMember { Id = Guid.NewGuid(), TenantId = tenant2Id, UserId = userId, IsOwner = false, CanViewGroup = false, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };

            db.EconomicGroups.Add(group);
            db.Tenants.AddRange(tenant1, tenant2);
            db.UserAccounts.Add(user);
            db.TenantMembers.AddRange(memberOwner, memberNonOwner);
            await db.SaveChangesAsync();
        }

        await using (var db = _fixture.CreateDbContext())
        {
            var tenantIsolation = new TenantIsolation();
            var writer = new MembershipWriter(db, tenantIsolation, _timeProvider);

            var affectedTenants = await writer.SetGroupMembershipsStatusAsync(userId, groupId, EntityStatus.SUSPENDED);

            // Tenant 1 (owner) not altered; Tenant 2 altered
            Assert.Single(affectedTenants);
            Assert.Contains(tenant2Id, affectedTenants);
        }

        // Verify in DB
        await using (var db = _fixture.CreateDbContext())
        {
            var member1 = await db.TenantMembers.FirstOrDefaultAsync(m => m.TenantId == tenant1Id && m.UserId == userId);
            Assert.NotNull(member1);
            Assert.True(member1.IsOwner);
            Assert.Equal(EntityStatus.ACTIVE, member1.Status); // Preserved!

            var member2 = await db.TenantMembers.FirstOrDefaultAsync(m => m.TenantId == tenant2Id && m.UserId == userId);
            Assert.NotNull(member2);
            Assert.False(member2.IsOwner);
            Assert.Equal(EntityStatus.SUSPENDED, member2.Status); // Changed!
        }
    }
}
