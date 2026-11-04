using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IdentityService.Modules.M00Transversal;
using IdentityService.Modules.M00Transversal.Auth;
using IdentityService.Modules.M00Transversal.Redis;
using IdentityService.Modules.M01Login;
using IdentityService.Modules.M02Refresh;
using IdentityService.Modules.M03Contexto;
using IdentityService.Modules.M04Tenants;
using IdentityService.Modules.M05Membros;
using IdentityService.Modules.M06Impersonation;
using IdentityService.Modules.M07Chaves;
using IdentityService.Modules.M07Chaves.Interface;
using IdentityService.Modules.M08Acessos;
using IdentityService.Modules.M09Logout;
using IdentityService.Modules.M10EstadoUsuario;
using IdentityService.Modules.M11Esquema;
using IdentityService.Modules.M11Esquema.Persistence;
using IdentityService.Modules.M11Esquema.Persistence.Entities;
using IdentityService.Modules.M11Esquema.Persistence.Enums;
using IdentityService.Modules.M12EstadoTenant;
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
using StackExchange.Redis;
using Xunit;

namespace IdentityService.Tests;

[Trait("Fase", "F03")]
[Collection("SharedContainers")]
public class M10EstadoUsuarioTests
{
    private readonly TestcontainersFixture _fixture;
    private readonly TestSigningKey _signingKey = new("key-2026-01");
    private readonly TestTimeProvider _timeProvider = new(new DateTimeOffset(2026, 4, 19, 12, 0, 0, TimeSpan.Zero));

    public M10EstadoUsuarioTests(TestcontainersFixture fixture)
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
        builder.Services.AddM10EstadoUsuario();
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
        builder.Services.AddM11Esquema(builder.Configuration);
        builder.Services.AddM12EstadoTenant();
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
        app.MapM10EstadoUsuario();
        app.MapM12EstadoTenant();

        return app;
    }

    private TokenIssuer CreateIssuer()
    {
        return new TokenIssuer(_signingKey, _timeProvider, new ConfigurationBuilder().Build());
    }

    private string CreateToken(
        Guid userId,
        PlatformRole platformRole = PlatformRole.NONE,
        OperationalScope? scope = null,
        Guid? groupId = null,
        Guid? tenantId = null,
        IReadOnlyList<Guid>? allowedTenants = null,
        IReadOnlyList<string>? roles = null,
        IReadOnlyList<string>? permissions = null)
    {
        var issuer = CreateIssuer();
        var claims = new AccessTokenClaims(
            Sub: userId,
            FamilyId: Guid.NewGuid(),
            PlatformRole: platformRole,
            ViewMode: tenantId.HasValue ? ViewMode.SINGLE : ViewMode.NONE,
            GroupId: groupId,
            TenantId: tenantId,
            AllowedTenants: allowedTenants ?? (tenantId.HasValue ? [tenantId.Value] : []),
            ActiveScope: scope,
            Roles: roles ?? [],
            Permissions: permissions ?? [],
            Act: null
        );
        return issuer.Issue(claims).AccessToken;
    }

    [Fact(Timeout = 60000)]
    public async Task CA_10_1_SuspensaoGlobalPorPlatformAdmin()
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();

        var adminId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();
        var familyId = Guid.NewGuid();

        await using (var db = _fixture.CreateDbContext())
        {
            var adminUser = new UserAccount { Id = adminId, Email = "admin@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Utilizador Teste 01", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.PLATFORM_ADMIN, CreatedAt = now, UpdatedAt = now };
            var targetUser = new UserAccount { Id = targetUserId, Email = "target@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Utilizador Teste 02", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.NONE, CreatedAt = now, UpdatedAt = now };

            var rt = new RefreshToken
            {
                Id = Guid.NewGuid(),
                FamilyId = familyId,
                UserId = targetUserId,
                TokenHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("refresh-user-fam-01"))),
                Status = RefreshTokenStatus.ACTIVE,
                CreatedAt = now,
                ExpiresAt = now.AddDays(7)
            };

            db.UserAccounts.AddRange(adminUser, targetUser);
            db.RefreshTokens.Add(rt);
            await db.SaveChangesAsync();
        }

        var token = CreateToken(adminId, platformRole: PlatformRole.PLATFORM_ADMIN);

        await using var app = CreateTestApp();
        await app.StartAsync();
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new
        {
            status = "SUSPENDED",
            reason = "12345"
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.PatchAsync($"/api/v1/users/{targetUserId:D}/status", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(targetUserId.ToString("D"), root.GetProperty("user_id").GetString());
        Assert.Equal("SUSPENDED", root.GetProperty("status").GetString());
        Assert.Equal("GLOBAL", root.GetProperty("scope").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("group_id").ValueKind);
        Assert.Equal("2026-04-19T12:00:00Z", root.GetProperty("updated_at").GetString());

        // Assert DB state: target user SUSPENDED, family REVOKED, EV-03 outbox, audit log
        await using (var db = _fixture.CreateDbContext())
        {
            var userInDb = await db.UserAccounts.FirstOrDefaultAsync(u => u.Id == targetUserId);
            Assert.NotNull(userInDb);
            Assert.Equal(UserStatus.SUSPENDED, userInDb.Status);

            var rtInDb = await db.RefreshTokens.FirstOrDefaultAsync(r => r.FamilyId == familyId);
            Assert.NotNull(rtInDb);
            Assert.Equal(RefreshTokenStatus.REVOKED, rtInDb.Status);

            var outboxMsg = await db.OutboxMessages.FirstOrDefaultAsync(o => o.Topic == "usuario_bloqueado" && o.PartitionKey == targetUserId.ToString("D"));
            Assert.NotNull(outboxMsg);
            Assert.Equal(OutboxStatus.PENDING, outboxMsg.Status);

            using var payloadDoc = JsonDocument.Parse(outboxMsg.Payload);
            Assert.Equal("SUSPENDED", payloadDoc.RootElement.GetProperty("status").GetString());
            Assert.Equal("12345", payloadDoc.RootElement.GetProperty("reason").GetString());
            Assert.Equal(JsonValueKind.Null, payloadDoc.RootElement.GetProperty("locked_until").ValueKind);

            var audit = await db.AuthAuditLogs.FirstOrDefaultAsync(a => a.EventType == AuditEventType.USER_STATUS_CHANGED && a.UserId == targetUserId);
            Assert.NotNull(audit);
            Assert.Equal(adminId, audit.ActorUserId);
            Assert.Null(audit.GroupId);
        }

        // Assert Redis denylist:user
        var redisMux = ConnectionMultiplexer.Connect(_fixture.GetRedisConnectionString());
        var redisDb = redisMux.GetDatabase();
        var isBlocked = await redisDb.KeyExistsAsync($"denylist:user:{targetUserId:D}");
        Assert.True(isBlocked);
    }

    [Fact(Timeout = 60000)]
    public async Task CA_10_2_SuspensaoEmGrupoPorTenantAdminAlteraNaoDonos()
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();

        var groupId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();
        var familyId = Guid.NewGuid();

        await using (var db = _fixture.CreateDbContext())
        {
            var group = new EconomicGroup { Id = groupId, Name = "Grupo Teste", Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var tenant = new Tenant { Id = tenantId, GroupId = groupId, Cnpj = "99000001000101", LegalName = "Tenant Teste 01", BranchType = BranchType.HQ, GeoLat = 0, GeoLng = 0, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var adminUser = new UserAccount { Id = adminId, Email = "admin@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Utilizador Teste 01", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.NONE, CreatedAt = now, UpdatedAt = now };
            var targetUser = new UserAccount { Id = targetUserId, Email = "target@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Utilizador Teste 02", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.NONE, CreatedAt = now, UpdatedAt = now };

            var adminMember = new TenantMember { Id = Guid.NewGuid(), TenantId = tenantId, UserId = adminId, IsOwner = true, CanViewGroup = true, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var targetMember = new TenantMember { Id = Guid.NewGuid(), TenantId = tenantId, UserId = targetUserId, IsOwner = false, CanViewGroup = true, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };

            var rt = new RefreshToken
            {
                Id = Guid.NewGuid(),
                FamilyId = familyId,
                UserId = targetUserId,
                TokenHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("refresh-user-fam-02"))),
                Status = RefreshTokenStatus.ACTIVE,
                ViewMode = ViewMode.SINGLE,
                GroupId = groupId,
                ActiveTenantId = tenantId,
                AllowedTenants = [tenantId],
                ActiveScope = OperationalScope.BUYER,
                CreatedAt = now,
                ExpiresAt = now.AddDays(7)
            };

            db.EconomicGroups.Add(group);
            db.Tenants.Add(tenant);
            db.UserAccounts.AddRange(adminUser, targetUser);
            db.TenantMembers.AddRange(adminMember, targetMember);
            db.RefreshTokens.Add(rt);
            await db.SaveChangesAsync();
        }

        var token = CreateToken(
            adminId,
            platformRole: PlatformRole.NONE,
            scope: OperationalScope.TENANT_ADMIN,
            groupId: groupId,
            tenantId: tenantId,
            allowedTenants: [tenantId],
            roles: ["GROUP_MANAGER"],
            permissions: ["usuario:suspender"]);

        await using var app = CreateTestApp();
        await app.StartAsync();
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new
        {
            status = "SUSPENDED",
            reason = "Suspenso"
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.PatchAsync($"/api/v1/users/{targetUserId:D}/status", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(targetUserId.ToString("D"), root.GetProperty("user_id").GetString());
        Assert.Equal("SUSPENDED", root.GetProperty("status").GetString());
        Assert.Equal("GROUP", root.GetProperty("scope").GetString());
        Assert.Equal(groupId.ToString("D"), root.GetProperty("group_id").GetString());

        // Assert DB state: target membership SUSPENDED, user_account remains ACTIVE, family revoked, no EV-03
        await using (var db = _fixture.CreateDbContext())
        {
            var memberInDb = await db.TenantMembers.FirstOrDefaultAsync(m => m.TenantId == tenantId && m.UserId == targetUserId);
            Assert.NotNull(memberInDb);
            Assert.Equal(EntityStatus.SUSPENDED, memberInDb.Status);

            var userInDb = await db.UserAccounts.FirstOrDefaultAsync(u => u.Id == targetUserId);
            Assert.NotNull(userInDb);
            Assert.Equal(UserStatus.ACTIVE, userInDb.Status);

            var rtInDb = await db.RefreshTokens.FirstOrDefaultAsync(r => r.FamilyId == familyId);
            Assert.NotNull(rtInDb);
            Assert.Equal(RefreshTokenStatus.REVOKED, rtInDb.Status);

            var outbox = await db.OutboxMessages.Where(o => o.Topic == "usuario_bloqueado" && o.PartitionKey == targetUserId.ToString("D")).ToListAsync();
            Assert.Empty(outbox);

            var audit = await db.AuthAuditLogs.FirstOrDefaultAsync(a => a.EventType == AuditEventType.USER_STATUS_CHANGED && a.UserId == targetUserId);
            Assert.NotNull(audit);
            Assert.Equal(adminId, audit.ActorUserId);
            Assert.Equal(groupId, audit.GroupId);
        }

        // Assert no Redis denylist:user
        var redisMux = ConnectionMultiplexer.Connect(_fixture.GetRedisConnectionString());
        var redisDb = redisMux.GetDatabase();
        var isBlocked = await redisDb.KeyExistsAsync($"denylist:user:{targetUserId:D}");
        Assert.False(isBlocked);
    }

    [Theory(Timeout = 60000)]
    [InlineData("proprio_usuario")]
    [InlineData("proprietario")]
    [InlineData("sem_vinculo")]
    public async Task CA_10_3_RejeicaoSeC9ForViolada(string tipo)
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();

        var groupId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var targetOwnerId = Guid.NewGuid();
        var targetNoLinkUserId = Guid.NewGuid();

        await using (var db = _fixture.CreateDbContext())
        {
            var group = new EconomicGroup { Id = groupId, Name = "Grupo Teste", Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var tenant = new Tenant { Id = tenantId, GroupId = groupId, Cnpj = "99000001000101", LegalName = "Tenant Teste 01", BranchType = BranchType.HQ, GeoLat = 0, GeoLng = 0, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };

            var adminUser = new UserAccount { Id = adminId, Email = "admin@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Utilizador Teste 01", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.NONE, CreatedAt = now, UpdatedAt = now };
            var ownerUser = new UserAccount { Id = targetOwnerId, Email = "owner@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Utilizador Teste 02", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.NONE, CreatedAt = now, UpdatedAt = now };
            var noLinkUser = new UserAccount { Id = targetNoLinkUserId, Email = "nolink@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Utilizador Teste 03", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.NONE, CreatedAt = now, UpdatedAt = now };

            var adminMember = new TenantMember { Id = Guid.NewGuid(), TenantId = tenantId, UserId = adminId, IsOwner = true, CanViewGroup = true, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var ownerMember = new TenantMember { Id = Guid.NewGuid(), TenantId = tenantId, UserId = targetOwnerId, IsOwner = true, CanViewGroup = true, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };

            db.EconomicGroups.Add(group);
            db.Tenants.Add(tenant);
            db.UserAccounts.AddRange(adminUser, ownerUser, noLinkUser);
            db.TenantMembers.AddRange(adminMember, ownerMember);
            await db.SaveChangesAsync();
        }

        var token = CreateToken(
            adminId,
            platformRole: PlatformRole.NONE,
            scope: OperationalScope.TENANT_ADMIN,
            groupId: groupId,
            tenantId: tenantId,
            allowedTenants: [tenantId],
            roles: ["GROUP_MANAGER"],
            permissions: ["usuario:suspender"]);

        var targetId = tipo switch
        {
            "proprio_usuario" => adminId,
            "proprietario" => targetOwnerId,
            "sem_vinculo" => targetNoLinkUserId,
            _ => throw new ArgumentOutOfRangeException(nameof(tipo))
        };

        await using var app = CreateTestApp();
        await app.StartAsync();
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new
        {
            status = "SUSPENDED",
            reason = "Tentativa rejeição"
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.PatchAsync($"/api/v1/users/{targetId:D}/status", content);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("ERR-CTX-007", doc.RootElement.GetProperty("code").GetString());
    }

    [Theory(Timeout = 60000)]
    [InlineData("PLATFORM_ADMIN", 404, "ERR-CTX-008")]
    [InlineData("TENANT_ADMIN", 403, "ERR-CTX-007")]
    public async Task CA_10_4_AlvoInexistenteDevolve404Ou403(string ator, int statusEsperado, string erroEsperado)
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();

        var groupId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        var inexistentTargetId = Guid.NewGuid();

        var isPlatformAdmin = ator == "PLATFORM_ADMIN";

        await using (var db = _fixture.CreateDbContext())
        {
            var group = new EconomicGroup { Id = groupId, Name = "Grupo Teste", Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var tenant = new Tenant { Id = tenantId, GroupId = groupId, Cnpj = "99000001000101", LegalName = "Tenant Teste 01", BranchType = BranchType.HQ, GeoLat = 0, GeoLng = 0, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var callerUser = new UserAccount
            {
                Id = callerId,
                Email = "caller@example.test",
                PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g",
                Name = "Utilizador Teste 01",
                Status = UserStatus.ACTIVE,
                PlatformRole = isPlatformAdmin ? PlatformRole.PLATFORM_ADMIN : PlatformRole.NONE,
                CreatedAt = now,
                UpdatedAt = now
            };
            var callerMember = new TenantMember { Id = Guid.NewGuid(), TenantId = tenantId, UserId = callerId, IsOwner = true, CanViewGroup = true, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };

            db.EconomicGroups.Add(group);
            db.Tenants.Add(tenant);
            db.UserAccounts.Add(callerUser);
            db.TenantMembers.Add(callerMember);
            await db.SaveChangesAsync();
        }

        var token = isPlatformAdmin
            ? CreateToken(callerId, platformRole: PlatformRole.PLATFORM_ADMIN)
            : CreateToken(callerId, platformRole: PlatformRole.NONE, scope: OperationalScope.TENANT_ADMIN, groupId: groupId, tenantId: tenantId, allowedTenants: [tenantId], roles: ["GROUP_MANAGER"], permissions: ["usuario:suspender"]);

        await using var app = CreateTestApp();
        await app.StartAsync();
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new
        {
            status = "SUSPENDED",
            reason = "Tentativa inexistente"
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.PatchAsync($"/api/v1/users/{inexistentTargetId:D}/status", content);
        Assert.Equal(statusEsperado, (int)response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(erroEsperado, doc.RootElement.GetProperty("code").GetString());
    }

    [Fact(Timeout = 60000)]
    public async Task CA_10_5_RepeticaoSuspendedEmGlobalReexecutaEfeitos()
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();

        var adminId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();

        await using (var db = _fixture.CreateDbContext())
        {
            var adminUser = new UserAccount { Id = adminId, Email = "admin@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Utilizador Teste 01", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.PLATFORM_ADMIN, CreatedAt = now, UpdatedAt = now };
            var targetUser = new UserAccount { Id = targetUserId, Email = "target@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Utilizador Teste 02", Status = UserStatus.SUSPENDED, PlatformRole = PlatformRole.NONE, CreatedAt = now, UpdatedAt = now };

            db.UserAccounts.AddRange(adminUser, targetUser);
            await db.SaveChangesAsync();
        }

        var redisMux = ConnectionMultiplexer.Connect(_fixture.GetRedisConnectionString());
        var redisDb = redisMux.GetDatabase();
        await redisDb.StringSetAsync($"denylist:user:{targetUserId:D}", now.ToUnixTimeSeconds().ToString(), TimeSpan.FromSeconds(900));

        var token = CreateToken(adminId, platformRole: PlatformRole.PLATFORM_ADMIN);

        await using var app = CreateTestApp();
        await app.StartAsync();
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new
        {
            status = "SUSPENDED",
            reason = "Repetir"
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.PatchAsync($"/api/v1/users/{targetUserId:D}/status", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var isBlocked = await redisDb.KeyExistsAsync($"denylist:user:{targetUserId:D}");
        Assert.True(isBlocked);

        await using (var db = _fixture.CreateDbContext())
        {
            var outboxMsgs = await db.OutboxMessages.Where(o => o.Topic == "usuario_bloqueado" && o.PartitionKey == targetUserId.ToString("D")).ToListAsync();
            Assert.Single(outboxMsgs);

            var audits = await db.AuthAuditLogs.Where(a => a.EventType == AuditEventType.USER_STATUS_CHANGED && a.UserId == targetUserId).ToListAsync();
            Assert.Single(audits);
        }
    }

    [Fact(Timeout = 60000)]
    public async Task CA_13_1_ReativacaoGlobalPorPlatformAdminDesbloqueiaLocked()
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();

        var adminId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();
        var lockedUntil = DateTimeOffset.FromUnixTimeSeconds(1776600900);

        await using (var db = _fixture.CreateDbContext())
        {
            var adminUser = new UserAccount { Id = adminId, Email = "admin@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Utilizador Teste 01", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.PLATFORM_ADMIN, CreatedAt = now, UpdatedAt = now };
            var targetUser = new UserAccount
            {
                Id = targetUserId,
                Email = "target@example.test",
                PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g",
                Name = "Utilizador Teste 02",
                Status = UserStatus.LOCKED,
                PlatformRole = PlatformRole.NONE,
                FailedLoginAttempts = 5,
                LockedUntil = lockedUntil,
                CreatedAt = now,
                UpdatedAt = now
            };

            db.UserAccounts.AddRange(adminUser, targetUser);
            await db.SaveChangesAsync();
        }

        var token = CreateToken(adminId, platformRole: PlatformRole.PLATFORM_ADMIN);

        await using var app = CreateTestApp();
        await app.StartAsync();
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new
        {
            status = "ACTIVE",
            reason = "Reativa"
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.PatchAsync($"/api/v1/users/{targetUserId:D}/status", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(targetUserId.ToString("D"), root.GetProperty("user_id").GetString());
        Assert.Equal("ACTIVE", root.GetProperty("status").GetString());
        Assert.Equal("GLOBAL", root.GetProperty("scope").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("group_id").ValueKind);

        // Assert DB state: status ACTIVE, failed_login_attempts 0, locked_until null, no EV-03
        await using (var db = _fixture.CreateDbContext())
        {
            var userInDb = await db.UserAccounts.FirstOrDefaultAsync(u => u.Id == targetUserId);
            Assert.NotNull(userInDb);
            Assert.Equal(UserStatus.ACTIVE, userInDb.Status);
            Assert.Equal(0, userInDb.FailedLoginAttempts);
            Assert.Null(userInDb.LockedUntil);

            var outbox = await db.OutboxMessages.Where(o => o.Topic == "usuario_bloqueado" && o.PartitionKey == targetUserId.ToString("D")).ToListAsync();
            Assert.Empty(outbox);

            var audit = await db.AuthAuditLogs.FirstOrDefaultAsync(a => a.EventType == AuditEventType.USER_STATUS_CHANGED && a.UserId == targetUserId);
            Assert.NotNull(audit);
            Assert.Equal(adminId, audit.ActorUserId);
        }
    }

    [Fact(Timeout = 60000)]
    public async Task CA_13_2_ReativacaoEmGrupoPorTenantAdminSemAlteracao()
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();

        var groupId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();

        await using (var db = _fixture.CreateDbContext())
        {
            var group = new EconomicGroup { Id = groupId, Name = "Grupo Teste", Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var tenant = new Tenant { Id = tenantId, GroupId = groupId, Cnpj = "99000001000101", LegalName = "Tenant Teste 01", BranchType = BranchType.HQ, GeoLat = 0, GeoLng = 0, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var adminUser = new UserAccount { Id = adminId, Email = "admin@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Utilizador Teste 01", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.NONE, CreatedAt = now, UpdatedAt = now };
            var targetUser = new UserAccount { Id = targetUserId, Email = "target@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Utilizador Teste 02", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.NONE, CreatedAt = now, UpdatedAt = now };

            var adminMember = new TenantMember { Id = Guid.NewGuid(), TenantId = tenantId, UserId = adminId, IsOwner = true, CanViewGroup = true, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var targetMember = new TenantMember { Id = Guid.NewGuid(), TenantId = tenantId, UserId = targetUserId, IsOwner = false, CanViewGroup = true, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };

            db.EconomicGroups.Add(group);
            db.Tenants.Add(tenant);
            db.UserAccounts.AddRange(adminUser, targetUser);
            db.TenantMembers.AddRange(adminMember, targetMember);
            await db.SaveChangesAsync();
        }

        var token = CreateToken(
            adminId,
            platformRole: PlatformRole.NONE,
            scope: OperationalScope.TENANT_ADMIN,
            groupId: groupId,
            tenantId: tenantId,
            allowedTenants: [tenantId],
            roles: ["GROUP_MANAGER"],
            permissions: ["usuario:suspender"]);

        await using var app = CreateTestApp();
        await app.StartAsync();
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new
        {
            status = "ACTIVE",
            reason = "Reativa"
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.PatchAsync($"/api/v1/users/{targetUserId:D}/status", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(targetUserId.ToString("D"), root.GetProperty("user_id").GetString());
        Assert.Equal("ACTIVE", root.GetProperty("status").GetString());
        Assert.Equal("GROUP", root.GetProperty("scope").GetString());
        Assert.Equal(groupId.ToString("D"), root.GetProperty("group_id").GetString());

        await using (var db = _fixture.CreateDbContext())
        {
            var audit = await db.AuthAuditLogs.FirstOrDefaultAsync(a => a.EventType == AuditEventType.USER_STATUS_CHANGED && a.UserId == targetUserId);
            Assert.NotNull(audit);
            Assert.Equal(adminId, audit.ActorUserId);
            Assert.Equal(groupId, audit.GroupId);
        }
    }

    [Theory(Timeout = 60000)]
    [InlineData("{\"status\":\"ACTIVE\",\"reason\":\"Reativa\"}", 200, "Nenhum")]
    [InlineData("{\"status\":\"LOCKED\",\"reason\":\"Bloqueio\"}", 400, "ERR-VAL-001")]
    [InlineData("{\"status\":\"ACTIVE\",\"reason\":\"1234\"}", 400, "ERR-VAL-001")]
    public async Task CA_13_3_ValidacaoCorpoMalformadoERepeticao(string corpo, int statusEsperado, string erroEsperado)
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();

        var adminId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();

        await using (var db = _fixture.CreateDbContext())
        {
            var adminUser = new UserAccount { Id = adminId, Email = "admin@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Utilizador Teste 01", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.PLATFORM_ADMIN, CreatedAt = now, UpdatedAt = now };
            var targetUser = new UserAccount { Id = targetUserId, Email = "target@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Utilizador Teste 02", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.NONE, CreatedAt = now, UpdatedAt = now };

            db.UserAccounts.AddRange(adminUser, targetUser);
            await db.SaveChangesAsync();
        }

        var token = CreateToken(adminId, platformRole: PlatformRole.PLATFORM_ADMIN);

        await using var app = CreateTestApp();
        await app.StartAsync();
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var content = new StringContent(corpo, Encoding.UTF8, "application/json");

        var response = await client.PatchAsync($"/api/v1/users/{targetUserId:D}/status", content);
        Assert.Equal(statusEsperado, (int)response.StatusCode);

        if (erroEsperado != "Nenhum")
        {
            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            Assert.Equal(erroEsperado, doc.RootElement.GetProperty("code").GetString());
        }
    }
}
