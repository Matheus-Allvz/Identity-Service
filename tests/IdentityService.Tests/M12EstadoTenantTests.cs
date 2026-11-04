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
public class M12EstadoTenantTests
{
    private readonly TestcontainersFixture _fixture;
    private readonly TestSigningKey _signingKey = new("key-2026-01");
    private readonly TestTimeProvider _timeProvider = new(new DateTimeOffset(2026, 11, 2, 22, 0, 0, TimeSpan.Zero));

    public M12EstadoTenantTests(TestcontainersFixture fixture)
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
    public async Task CA_09_1_SuspensaoTenantComSucessoPorPlatformAdmin()
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();

        var groupId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var familyId = Guid.NewGuid();

        await using (var db = _fixture.CreateDbContext())
        {
            var group = new EconomicGroup { Id = groupId, Name = "Grupo Teste", Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var tenant = new Tenant { Id = tenantId, GroupId = groupId, Cnpj = "99000001000101", LegalName = "Tenant Teste 01", BranchType = BranchType.HQ, GeoLat = 0, GeoLng = 0, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var adminUser = new UserAccount { Id = adminId, Email = "admin@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Utilizador Teste 01", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.PLATFORM_ADMIN, CreatedAt = now, UpdatedAt = now };
            var memberUser = new UserAccount { Id = memberId, Email = "user01@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Utilizador Teste 02", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.NONE, CreatedAt = now, UpdatedAt = now };

            var rt = new RefreshToken
            {
                Id = Guid.NewGuid(),
                FamilyId = familyId,
                UserId = memberId,
                TokenHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("refresh-token-fam-01"))),
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
            db.UserAccounts.AddRange(adminUser, memberUser);
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
            reason = "Fraude cadastral"
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.PatchAsync($"/api/v1/tenants/{tenantId:D}/status", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(tenantId.ToString("D"), root.GetProperty("tenant_id").GetString());
        Assert.Equal("SUSPENDED", root.GetProperty("status").GetString());
        Assert.True(root.TryGetProperty("updated_at", out _));

        // Assert database state: tenant status, refresh tokens revoked, outbox EV-02, audit log
        await using (var db = _fixture.CreateDbContext())
        {
            var tenantInDb = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId);
            Assert.NotNull(tenantInDb);
            Assert.Equal(EntityStatus.SUSPENDED, tenantInDb.Status);

            var rtInDb = await db.RefreshTokens.FirstOrDefaultAsync(r => r.FamilyId == familyId);
            Assert.NotNull(rtInDb);
            Assert.Equal(RefreshTokenStatus.REVOKED, rtInDb.Status);
            Assert.NotNull(rtInDb.RevokedAt);

            var outboxMsg = await db.OutboxMessages.FirstOrDefaultAsync(o => o.Topic == "tenant_bloqueado" && o.PartitionKey == tenantId.ToString("D"));
            Assert.NotNull(outboxMsg);
            Assert.Equal(OutboxStatus.PENDING, outboxMsg.Status);

            using var payloadDoc = JsonDocument.Parse(outboxMsg.Payload);
            Assert.Equal("SUSPENDED", payloadDoc.RootElement.GetProperty("status").GetString());
            Assert.Equal("Fraude cadastral", payloadDoc.RootElement.GetProperty("reason").GetString());
            Assert.Equal(adminId.ToString("D"), payloadDoc.RootElement.GetProperty("blocked_by_user_id").GetString());

            var audit = await db.AuthAuditLogs.FirstOrDefaultAsync(a => a.EventType == AuditEventType.TENANT_STATUS_CHANGED && a.TenantId == tenantId);
            Assert.NotNull(audit);
            Assert.Equal(adminId, audit.UserId);
        }

        // Assert Redis denylist:tenant
        var redisMux = ConnectionMultiplexer.Connect(_fixture.GetRedisConnectionString());
        var redisDb = redisMux.GetDatabase();
        var isBlocked = await redisDb.KeyExistsAsync($"denylist:tenant:{tenantId:D}");
        Assert.True(isBlocked);
    }

    [Fact(Timeout = 60000)]
    public async Task CA_09_2_SuspensaoRepetidaTenantJaSuspensoIdempotente()
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();

        var groupId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var adminId = Guid.NewGuid();

        await using (var db = _fixture.CreateDbContext())
        {
            var group = new EconomicGroup { Id = groupId, Name = "Grupo Teste", Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var tenant = new Tenant { Id = tenantId, GroupId = groupId, Cnpj = "99000001000101", LegalName = "Tenant Teste 01", BranchType = BranchType.HQ, GeoLat = 0, GeoLng = 0, Status = EntityStatus.SUSPENDED, CreatedAt = now, UpdatedAt = now };
            var adminUser = new UserAccount { Id = adminId, Email = "admin@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Utilizador Teste 01", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.PLATFORM_ADMIN, CreatedAt = now, UpdatedAt = now };

            db.EconomicGroups.Add(group);
            db.Tenants.Add(tenant);
            db.UserAccounts.Add(adminUser);
            await db.SaveChangesAsync();
        }

        var redisMux = ConnectionMultiplexer.Connect(_fixture.GetRedisConnectionString());
        var redisDb = redisMux.GetDatabase();
        await redisDb.StringSetAsync($"denylist:tenant:{tenantId:D}", "1", TimeSpan.FromSeconds(900));

        var token = CreateToken(adminId, platformRole: PlatformRole.PLATFORM_ADMIN);

        await using var app = CreateTestApp();
        await app.StartAsync();
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new
        {
            status = "SUSPENDED",
            reason = "Reiteração cautelar"
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.PatchAsync($"/api/v1/tenants/{tenantId:D}/status", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Denylist key remains in Redis
        var isBlocked = await redisDb.KeyExistsAsync($"denylist:tenant:{tenantId:D}");
        Assert.True(isBlocked);

        // Assert outbox message and audit log recorded
        await using (var db = _fixture.CreateDbContext())
        {
            var outboxMsgs = await db.OutboxMessages.Where(o => o.Topic == "tenant_bloqueado" && o.PartitionKey == tenantId.ToString("D")).ToListAsync();
            Assert.Single(outboxMsgs);

            var audits = await db.AuthAuditLogs.Where(a => a.EventType == AuditEventType.TENANT_STATUS_CHANGED && a.TenantId == tenantId).ToListAsync();
            Assert.Single(audits);
        }
    }

    [Theory(Timeout = 60000)]
    [InlineData("PLATFORM_ADMIN", "NONE", "1234", 400, "ERR-VAL-001")]
    [InlineData("PLATFORM_ADMIN", "NONE", "   ab   ", 400, "ERR-VAL-001")]
    [InlineData("NONE", "BUYER", "Suspensao indevida", 403, "ERR-CTX-007")]
    [InlineData("SUPPORT_AGENT", "NONE", "Tentativa suporte", 403, "ERR-CTX-007")]
    public async Task CA_09_3_ValidacaoBordasMotivoEAutorizacao(
        string rolePlataforma, string escopo, string motivo, int statusHttp, string codigoErro)
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();

        var groupId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var platRole = Enum.Parse<PlatformRole>(rolePlataforma);
        var opScope = escopo == "NONE" ? (OperationalScope?)null : Enum.Parse<OperationalScope>(escopo);

        await using (var db = _fixture.CreateDbContext())
        {
            var group = new EconomicGroup { Id = groupId, Name = "Grupo Teste", Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var tenant = new Tenant { Id = tenantId, GroupId = groupId, Cnpj = "99000001000101", LegalName = "Tenant Teste 01", BranchType = BranchType.HQ, GeoLat = 0, GeoLng = 0, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var user = new UserAccount { Id = userId, Email = "user@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Utilizador Teste 01", Status = UserStatus.ACTIVE, PlatformRole = platRole, CreatedAt = now, UpdatedAt = now };

            db.EconomicGroups.Add(group);
            db.Tenants.Add(tenant);
            db.UserAccounts.Add(user);
            await db.SaveChangesAsync();
        }

        var token = CreateToken(
            userId,
            platformRole: platRole,
            scope: opScope,
            groupId: opScope.HasValue ? groupId : null,
            tenantId: opScope.HasValue ? tenantId : null);

        await using var app = CreateTestApp();
        await app.StartAsync();
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new
        {
            status = "SUSPENDED",
            reason = motivo
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.PatchAsync($"/api/v1/tenants/{tenantId:D}/status", content);
        Assert.Equal(statusHttp, (int)response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(codigoErro, doc.RootElement.GetProperty("code").GetString());

        // Verify tenant remains ACTIVE
        await using (var db = _fixture.CreateDbContext())
        {
            var tenantInDb = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId);
            Assert.NotNull(tenantInDb);
            Assert.Equal(EntityStatus.ACTIVE, tenantInDb.Status);
        }
    }

    [Fact(Timeout = 60000)]
    public async Task CA_09_4_FalhaDependenciaRedisSuspensaoAbortaOperacao()
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();

        var groupId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var adminId = Guid.NewGuid();

        await using (var db = _fixture.CreateDbContext())
        {
            var group = new EconomicGroup { Id = groupId, Name = "Grupo Teste", Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var tenant = new Tenant { Id = tenantId, GroupId = groupId, Cnpj = "99000001000101", LegalName = "Tenant Teste 01", BranchType = BranchType.HQ, GeoLat = 0, GeoLng = 0, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var adminUser = new UserAccount { Id = adminId, Email = "admin@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Utilizador Teste 01", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.PLATFORM_ADMIN, CreatedAt = now, UpdatedAt = now };

            db.EconomicGroups.Add(group);
            db.Tenants.Add(tenant);
            db.UserAccounts.Add(adminUser);
            await db.SaveChangesAsync();
        }

        var token = CreateToken(adminId, platformRole: PlatformRole.PLATFORM_ADMIN);

        await using var app = CreateTestApp(services =>
        {
            var existing = services.FirstOrDefault(d => d.ServiceType == typeof(IDenylist));
            if (existing != null)
            {
                services.Remove(existing);
            }
            services.AddScoped<IDenylist>(sp =>
            {
                var mux = sp.GetRequiredService<IConnectionMultiplexer>();
                var tp = sp.GetRequiredService<TimeProvider>();
                var real = new DenylistService(mux, tp);
                return new FaultyDenylist(real, failBlockTenant: true);
            });
        });

        await app.StartAsync();
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new
        {
            status = "SUSPENDED",
            reason = "Bloqueio falha redis"
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.PatchAsync($"/api/v1/tenants/{tenantId:D}/status", content);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("ERR-SYS-002", doc.RootElement.GetProperty("code").GetString());

        // Verify status remains ACTIVE in DB
        await using (var db = _fixture.CreateDbContext())
        {
            var tenantInDb = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId);
            Assert.NotNull(tenantInDb);
            Assert.Equal(EntityStatus.ACTIVE, tenantInDb.Status);

            var audits = await db.AuthAuditLogs.Where(a => a.TenantId == tenantId).ToListAsync();
            Assert.Empty(audits);

            var outbox = await db.OutboxMessages.Where(o => o.Topic == "tenant_bloqueado" && o.PartitionKey == tenantId.ToString("D")).ToListAsync();
            Assert.Empty(outbox);
        }
    }

    [Fact(Timeout = 60000)]
    public async Task CA_12_1_ReativacaoTenantSuspensoComRemocaoDenylist()
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();

        var groupId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var adminId = Guid.NewGuid();

        await using (var db = _fixture.CreateDbContext())
        {
            var group = new EconomicGroup { Id = groupId, Name = "Grupo Teste", Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var tenant = new Tenant { Id = tenantId, GroupId = groupId, Cnpj = "99000001000101", LegalName = "Tenant Teste 01", BranchType = BranchType.HQ, GeoLat = 0, GeoLng = 0, Status = EntityStatus.SUSPENDED, CreatedAt = now, UpdatedAt = now };
            var adminUser = new UserAccount { Id = adminId, Email = "admin@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Utilizador Teste 01", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.PLATFORM_ADMIN, CreatedAt = now, UpdatedAt = now };

            db.EconomicGroups.Add(group);
            db.Tenants.Add(tenant);
            db.UserAccounts.Add(adminUser);
            await db.SaveChangesAsync();
        }

        var redisMux = ConnectionMultiplexer.Connect(_fixture.GetRedisConnectionString());
        var redisDb = redisMux.GetDatabase();
        await redisDb.StringSetAsync($"denylist:tenant:{tenantId:D}", "1", TimeSpan.FromSeconds(900));

        var token = CreateToken(adminId, platformRole: PlatformRole.PLATFORM_ADMIN);

        await using var app = CreateTestApp();
        await app.StartAsync();
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new
        {
            status = "ACTIVE",
            reason = "Regularização concluída"
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.PatchAsync($"/api/v1/tenants/{tenantId:D}/status", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Assert tenant ACTIVE in DB
        await using (var db = _fixture.CreateDbContext())
        {
            var tenantInDb = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId);
            Assert.NotNull(tenantInDb);
            Assert.Equal(EntityStatus.ACTIVE, tenantInDb.Status);

            var audits = await db.AuthAuditLogs.Where(a => a.EventType == AuditEventType.TENANT_STATUS_CHANGED && a.TenantId == tenantId).ToListAsync();
            Assert.Single(audits);

            var outbox = await db.OutboxMessages.Where(o => o.Topic == "tenant_bloqueado" && o.PartitionKey == tenantId.ToString("D")).ToListAsync();
            Assert.Empty(outbox);
        }

        // Assert Redis key removed
        var isBlocked = await redisDb.KeyExistsAsync($"denylist:tenant:{tenantId:D}");
        Assert.False(isBlocked);
    }

    [Fact(Timeout = 60000)]
    public async Task CA_12_2_ReativacaoRepetidaTenantJaAtivoNaoRemoveRedisNemOutbox()
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();

        var groupId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var adminId = Guid.NewGuid();

        await using (var db = _fixture.CreateDbContext())
        {
            var group = new EconomicGroup { Id = groupId, Name = "Grupo Teste", Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var tenant = new Tenant { Id = tenantId, GroupId = groupId, Cnpj = "99000001000101", LegalName = "Tenant Teste 01", BranchType = BranchType.HQ, GeoLat = 0, GeoLng = 0, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var adminUser = new UserAccount { Id = adminId, Email = "admin@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Utilizador Teste 01", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.PLATFORM_ADMIN, CreatedAt = now, UpdatedAt = now };

            db.EconomicGroups.Add(group);
            db.Tenants.Add(tenant);
            db.UserAccounts.Add(adminUser);
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
            reason = "Confirmação ativação"
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.PatchAsync($"/api/v1/tenants/{tenantId:D}/status", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using (var db = _fixture.CreateDbContext())
        {
            var tenantInDb = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId);
            Assert.NotNull(tenantInDb);
            Assert.Equal(EntityStatus.ACTIVE, tenantInDb.Status);

            var outbox = await db.OutboxMessages.Where(o => o.Topic == "tenant_bloqueado" && o.PartitionKey == tenantId.ToString("D")).ToListAsync();
            Assert.Empty(outbox);

            var audits = await db.AuthAuditLogs.Where(a => a.EventType == AuditEventType.TENANT_STATUS_CHANGED && a.TenantId == tenantId).ToListAsync();
            Assert.Single(audits);
        }
    }

    [Theory(Timeout = 60000)]
    [InlineData("invalido-uuid", 400, "ERR-VAL-001")]
    [InlineData("01900000-0000-7000-8000-999999999999", 404, "ERR-CTX-008")]
    public async Task CA_12_3_TentativaComTenantInexistenteOuUuidInvalido(
        string tenantParam, int statusHttp, string codigoErro)
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();

        var adminId = Guid.NewGuid();

        await using (var db = _fixture.CreateDbContext())
        {
            var adminUser = new UserAccount { Id = adminId, Email = "admin@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Utilizador Teste 01", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.PLATFORM_ADMIN, CreatedAt = now, UpdatedAt = now };
            db.UserAccounts.Add(adminUser);
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
            reason = "Tentativa parametro"
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.PatchAsync($"/api/v1/tenants/{tenantParam}/status", content);
        Assert.Equal(statusHttp, (int)response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(codigoErro, doc.RootElement.GetProperty("code").GetString());
    }

    [Fact(Timeout = 60000)]
    public async Task CA_09_1_Borda_SuspensaoPorTenantAdminOwnerComSucesso()
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();

        var groupId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();

        await using (var db = _fixture.CreateDbContext())
        {
            var group = new EconomicGroup { Id = groupId, Name = "Grupo Teste", Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var tenant = new Tenant { Id = tenantId, GroupId = groupId, Cnpj = "99000001000101", LegalName = "Tenant Teste 01", BranchType = BranchType.HQ, GeoLat = 0, GeoLng = 0, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var ownerUser = new UserAccount { Id = ownerUserId, Email = "owner@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Utilizador Teste 01", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.NONE, CreatedAt = now, UpdatedAt = now };
            var member = new TenantMember { Id = Guid.NewGuid(), TenantId = tenantId, UserId = ownerUserId, IsOwner = true, CanViewGroup = true, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };

            db.EconomicGroups.Add(group);
            db.Tenants.Add(tenant);
            db.UserAccounts.Add(ownerUser);
            db.TenantMembers.Add(member);
            await db.SaveChangesAsync();
        }

        var token = CreateToken(
            ownerUserId,
            platformRole: PlatformRole.NONE,
            scope: OperationalScope.TENANT_ADMIN,
            groupId: groupId,
            tenantId: tenantId,
            allowedTenants: [tenantId]);

        await using var app = CreateTestApp();
        await app.StartAsync();
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new
        {
            status = "SUSPENDED",
            reason = "Suspensão pelo próprio dono"
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.PatchAsync($"/api/v1/tenants/{tenantId:D}/status", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using (var db = _fixture.CreateDbContext())
        {
            var tenantInDb = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId);
            Assert.NotNull(tenantInDb);
            Assert.Equal(EntityStatus.SUSPENDED, tenantInDb.Status);
        }
    }

    [Fact(Timeout = 60000)]
    public async Task CA_09_3_Borda_SuspensaoPorTenantAdminNaoOwnerRejeitada()
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();

        var groupId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var nonOwnerUserId = Guid.NewGuid();

        await using (var db = _fixture.CreateDbContext())
        {
            var group = new EconomicGroup { Id = groupId, Name = "Grupo Teste", Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var tenant = new Tenant { Id = tenantId, GroupId = groupId, Cnpj = "99000001000101", LegalName = "Tenant Teste 01", BranchType = BranchType.HQ, GeoLat = 0, GeoLng = 0, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var nonOwnerUser = new UserAccount { Id = nonOwnerUserId, Email = "nonowner@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Utilizador Teste 01", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.NONE, CreatedAt = now, UpdatedAt = now };
            var member = new TenantMember { Id = Guid.NewGuid(), TenantId = tenantId, UserId = nonOwnerUserId, IsOwner = false, CanViewGroup = true, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };

            db.EconomicGroups.Add(group);
            db.Tenants.Add(tenant);
            db.UserAccounts.Add(nonOwnerUser);
            db.TenantMembers.Add(member);
            await db.SaveChangesAsync();
        }

        var token = CreateToken(
            nonOwnerUserId,
            platformRole: PlatformRole.NONE,
            scope: OperationalScope.TENANT_ADMIN,
            groupId: groupId,
            tenantId: tenantId,
            allowedTenants: [tenantId]);

        await using var app = CreateTestApp();
        await app.StartAsync();
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new
        {
            status = "SUSPENDED",
            reason = "Tentativa suspensão sem ser dono"
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.PatchAsync($"/api/v1/tenants/{tenantId:D}/status", content);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("ERR-CTX-007", doc.RootElement.GetProperty("code").GetString());
    }

    private sealed class FaultyDenylist : IDenylist
    {
        private readonly IDenylist _inner;
        public bool FailBlockTenant { get; set; }

        public FaultyDenylist(IDenylist inner, bool failBlockTenant = false)
        {
            _inner = inner;
            FailBlockTenant = failBlockTenant;
        }

        public Task RevokeJtiAsync(string jti, long exp, CancellationToken cancellationToken = default)
            => _inner.RevokeJtiAsync(jti, exp, cancellationToken);

        public Task<bool> IsJtiRevokedAsync(string jti, CancellationToken cancellationToken = default)
            => _inner.IsJtiRevokedAsync(jti, cancellationToken);

        public Task<long> BlockUserAsync(Guid userId, CancellationToken cancellationToken = default)
            => _inner.BlockUserAsync(userId, cancellationToken);

        public Task BlockTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
        {
            if (FailBlockTenant)
            {
                throw new RedisTimeoutException("Redis command timed out after 15000ms", CommandStatus.Unknown);
            }
            return _inner.BlockTenantAsync(tenantId, cancellationToken);
        }

        public Task UnblockTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
            => _inner.UnblockTenantAsync(tenantId, cancellationToken);

        public Task<long?> GetUserRevokedAtAsync(Guid userId, CancellationToken cancellationToken = default)
            => _inner.GetUserRevokedAtAsync(userId, cancellationToken);

        public Task<IReadOnlySet<Guid>> GetBlockedTenantsAsync(IReadOnlyList<Guid> tenantIds, CancellationToken cancellationToken = default)
            => _inner.GetBlockedTenantsAsync(tenantIds, cancellationToken);
    }
}
