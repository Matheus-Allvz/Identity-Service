using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IdentityService.Modules.M00Transversal;
using IdentityService.Modules.M00Transversal.Auth;
using IdentityService.Modules.M01Login;
using IdentityService.Modules.M02Refresh;
using IdentityService.Modules.M02Refresh.Interface;
using IdentityService.Modules.M03Contexto;
using IdentityService.Modules.M06Impersonation;
using IdentityService.Modules.M07Chaves;
using IdentityService.Modules.M08Acessos;
using IdentityService.Modules.M09Logout;
using IdentityService.Modules.M11Esquema;
using IdentityService.Modules.M11Esquema.Persistence;
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
using StackExchange.Redis;
using Xunit;

namespace IdentityService.Tests;

[Trait("Fase", "F01")]
[Collection("SharedContainers")]
public class M02RefreshTests
{
    private readonly TestcontainersFixture _fixture;
    private readonly TestSigningKey _signingKey = new("key-2026-01");
    private readonly TestTimeProvider _timeProvider = new(new DateTimeOffset(2026, 11, 2, 22, 0, 0, TimeSpan.Zero));

    public M02RefreshTests(TestcontainersFixture fixture)
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

    private static (string rawToken, string hash) GenerateTestToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var raw = Base64UrlEncoder.Encode(bytes);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
        return (raw, hash);
    }

    [Fact(Timeout = 60000)]
    public async Task CA_04_1_Borda_RotacaoComSucessoERecalculoDePermissoes()
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();

        var (rawToken, tokenHash) = GenerateTestToken();
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
                TokenHash = tokenHash,
                Status = RefreshTokenStatus.ACTIVE,
                ViewMode = ViewMode.SINGLE,
                GroupId = groupId,
                ActiveTenantId = tenantId,
                AllowedTenants = [tenantId],
                ActiveScope = OperationalScope.BUYER,
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

        await using var app = CreateTestApp();
        await app.StartAsync();
        var client = app.GetTestClient();

        var payload = new { refresh_token = rawToken };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/v1/auth/refresh", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("Bearer", root.GetProperty("token_type").GetString());
        Assert.Equal(900, root.GetProperty("expires_in").GetInt32());
        Assert.Equal(604800, root.GetProperty("refresh_expires_in").GetInt32());

        var newRefreshToken = root.GetProperty("refresh_token").GetString();
        Assert.NotNull(newRefreshToken);
        Assert.NotEqual(rawToken, newRefreshToken);

        await using (var db = _fixture.CreateDbContext())
        {
            var oldRt = await db.RefreshTokens.FirstOrDefaultAsync(r => r.TokenHash == tokenHash);
            Assert.NotNull(oldRt);
            Assert.Equal(RefreshTokenStatus.CONSUMED, oldRt.Status);
            Assert.NotNull(oldRt.ReplacedById);

            var newHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(newRefreshToken)));
            var childRt = await db.RefreshTokens.FirstOrDefaultAsync(r => r.TokenHash == newHash);
            Assert.NotNull(childRt);
            Assert.Equal(RefreshTokenStatus.ACTIVE, childRt.Status);
            Assert.Equal(familyId, childRt.FamilyId);

            var audit = await db.AuthAuditLogs.FirstOrDefaultAsync(a => a.EventType == AuditEventType.TOKEN_REFRESHED && a.UserId == userId);
            Assert.NotNull(audit);
        }
    }

    [Fact(Timeout = 60000)]
    public async Task CA_04_2_Borda_TentativaComTokenInexistenteNaFronteiraDe43Caracteres()
    {
        await using var app = CreateTestApp();
        await app.StartAsync();
        var client = app.GetTestClient();

        var nonexistentToken = new string('A', 43);
        var payload = new { refresh_token = nonexistentToken };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/v1/auth/refresh", content);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("ERR-AUTH-005", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact(Timeout = 60000)]
    [Trait("Critico", "sim")]
    public async Task CA_04_3_Borda_ReusoDeTokenConsumidoRevogaFamiliaAcionaDenylistEEmiteEV04()
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();

        var (rawConsumed, consumedHash) = GenerateTestToken();
        var (rawActive, activeHash) = GenerateTestToken();
        var userId = Guid.NewGuid();
        var familyId = Guid.NewGuid();

        await using (var db = _fixture.CreateDbContext())
        {
            var user = new UserAccount { Id = userId, Email = "u1@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Utilizador Teste 01", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.NONE, CreatedAt = now, UpdatedAt = now };
            var rtActiveId = Guid.NewGuid();

            var rtConsumed = new RefreshToken
            {
                Id = Guid.NewGuid(),
                FamilyId = familyId,
                UserId = userId,
                TokenHash = consumedHash,
                Status = RefreshTokenStatus.CONSUMED,
                ReplacedById = rtActiveId,
                ViewMode = ViewMode.NONE,
                CreatedAt = now.AddMinutes(-5),
                ExpiresAt = now.AddSeconds(604800),
                ConsumedAt = now.AddMinutes(-2)
            };

            var rtActive = new RefreshToken
            {
                Id = rtActiveId,
                FamilyId = familyId,
                UserId = userId,
                TokenHash = activeHash,
                Status = RefreshTokenStatus.ACTIVE,
                ViewMode = ViewMode.NONE,
                CreatedAt = now.AddMinutes(-2),
                ExpiresAt = now.AddSeconds(604800)
            };

            db.UserAccounts.Add(user);
            db.RefreshTokens.AddRange(rtConsumed, rtActive);
            await db.SaveChangesAsync();
        }

        await using var app = CreateTestApp();
        await app.StartAsync();
        var client = app.GetTestClient();

        // Apresentar o token CONSUMED -> Reuso
        var payload = new { refresh_token = rawConsumed };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/v1/auth/refresh", content);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("ERR-AUTH-006", doc.RootElement.GetProperty("code").GetString());

        // Verificar que todos da família passaram a REVOKED
        await using (var db = _fixture.CreateDbContext())
        {
            var activeCount = await db.RefreshTokens.CountAsync(r => r.FamilyId == familyId && r.Status != RefreshTokenStatus.REVOKED);
            Assert.Equal(0, activeCount);

            var audit = await db.AuthAuditLogs.FirstOrDefaultAsync(a => a.EventType == AuditEventType.TOKEN_REUSE_DETECTED && a.UserId == userId);
            Assert.NotNull(audit);

            var outbox = await db.OutboxMessages.FirstOrDefaultAsync(o => o.Topic == "sessao_comprometida" && o.PartitionKey == userId.ToString("D"));
            Assert.NotNull(outbox);
        }

        // Verificar Redis
        var redis = ConnectionMultiplexer.Connect(_fixture.GetRedisConnectionString());
        var redisDb = redis.GetDatabase();
        var denylistVal = await redisDb.StringGetAsync($"denylist:user:{userId:D}");
        Assert.True(denylistVal.HasValue);
    }

    [Theory(Timeout = 60000)]
    [Trait("Critico", "sim")]
    [InlineData("USER_SUSPENDED", "ERR-AUTH-004")]
    [InlineData("TENANT_SUSP", "ERR-CTX-002")]
    public async Task CA_04_4_Borda_RevalidacaoDeContextoReprovadaRevogaFamiliaERetorna403(string motivo, string codigo)
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();

        var (rawToken, tokenHash) = GenerateTestToken();
        var userId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var familyId = Guid.NewGuid();

        await using (var db = _fixture.CreateDbContext())
        {
            var group = new EconomicGroup { Id = groupId, Name = "Grupo Teste 01", Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var tenantStatus = motivo == "TENANT_SUSP" ? EntityStatus.SUSPENDED : EntityStatus.ACTIVE;
            var userStatus = motivo == "USER_SUSPENDED" ? UserStatus.SUSPENDED : UserStatus.ACTIVE;

            var tenant = new Tenant { Id = tenantId, GroupId = groupId, Cnpj = "99000001000101", LegalName = "Tenant Teste 01", BranchType = BranchType.HQ, GeoLat = 0, GeoLng = 0, Status = tenantStatus, CreatedAt = now, UpdatedAt = now };
            var scope = new TenantScope { Id = Guid.NewGuid(), TenantId = tenantId, Scope = OperationalScope.BUYER, IsActive = true, CreatedAt = now };
            var user = new UserAccount { Id = userId, Email = "u1@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Utilizador Teste 01", Status = userStatus, PlatformRole = PlatformRole.NONE, CreatedAt = now, UpdatedAt = now };
            var member = new TenantMember { Id = Guid.NewGuid(), TenantId = tenantId, UserId = userId, IsOwner = true, CanViewGroup = true, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };

            var rt = new RefreshToken
            {
                Id = Guid.NewGuid(),
                FamilyId = familyId,
                UserId = userId,
                TokenHash = tokenHash,
                Status = RefreshTokenStatus.ACTIVE,
                ViewMode = ViewMode.SINGLE,
                GroupId = groupId,
                ActiveTenantId = tenantId,
                AllowedTenants = [tenantId],
                ActiveScope = OperationalScope.BUYER,
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

        await using var app = CreateTestApp();
        await app.StartAsync();
        var client = app.GetTestClient();

        var payload = new { refresh_token = rawToken };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/v1/auth/refresh", content);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(codigo, doc.RootElement.GetProperty("code").GetString());

        await using (var db = _fixture.CreateDbContext())
        {
            var activeCount = await db.RefreshTokens.CountAsync(r => r.FamilyId == familyId && r.Status != RefreshTokenStatus.REVOKED);
            Assert.Equal(0, activeCount);
        }
    }

    [Fact(Timeout = 60000)]
    public async Task CA_04_5_Borda_TokenExpiradoNaFronteiraExataDevolve401SemEfeitos()
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();

        var (rawToken, tokenHash) = GenerateTestToken();
        var userId = Guid.NewGuid();
        var familyId = Guid.NewGuid();

        await using (var db = _fixture.CreateDbContext())
        {
            var user = new UserAccount { Id = userId, Email = "u1@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Utilizador Teste 01", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.NONE, CreatedAt = now, UpdatedAt = now };
            var rt = new RefreshToken
            {
                Id = Guid.NewGuid(),
                FamilyId = familyId,
                UserId = userId,
                TokenHash = tokenHash,
                Status = RefreshTokenStatus.ACTIVE,
                ViewMode = ViewMode.NONE,
                CreatedAt = now.AddSeconds(-604800),
                ExpiresAt = now // Fronteira exata
            };

            db.UserAccounts.Add(user);
            db.RefreshTokens.Add(rt);
            await db.SaveChangesAsync();
        }

        await using var app = CreateTestApp();
        await app.StartAsync();
        var client = app.GetTestClient();

        var payload = new { refresh_token = rawToken };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/v1/auth/refresh", content);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("ERR-AUTH-005", doc.RootElement.GetProperty("code").GetString());

        await using (var db = _fixture.CreateDbContext())
        {
            var rt = await db.RefreshTokens.FirstOrDefaultAsync(r => r.TokenHash == tokenHash);
            Assert.NotNull(rt);
            Assert.Equal(RefreshTokenStatus.ACTIVE, rt.Status); // Inalterado
        }
    }

    [Fact(Timeout = 60000)]
    [Trait("Critico", "sim")]
    public async Task CA_04_6_Borda_ConcorrenciaSimultaneaTrataColisaoDeRotacaoComoReuso()
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();

        var (rawToken, tokenHash) = GenerateTestToken();
        var userId = Guid.NewGuid();
        var familyId = Guid.NewGuid();

        await using (var db = _fixture.CreateDbContext())
        {
            var user = new UserAccount { Id = userId, Email = "u1@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Utilizador Teste 01", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.NONE, CreatedAt = now, UpdatedAt = now };
            var rt = new RefreshToken
            {
                Id = Guid.NewGuid(),
                FamilyId = familyId,
                UserId = userId,
                TokenHash = tokenHash,
                Status = RefreshTokenStatus.ACTIVE,
                ViewMode = ViewMode.NONE,
                CreatedAt = now,
                ExpiresAt = now.AddSeconds(604800)
            };

            db.UserAccounts.Add(user);
            db.RefreshTokens.Add(rt);
            await db.SaveChangesAsync();
        }

        await using var app = CreateTestApp();
        await app.StartAsync();

        var client1 = app.GetTestClient();
        var client2 = app.GetTestClient();

        var payload = new { refresh_token = rawToken };
        var content1 = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var content2 = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var barrier = new Barrier(2);

        var task1 = Task.Run(async () =>
        {
            barrier.SignalAndWait();
            return await client1.PostAsync("/api/v1/auth/refresh", content1);
        });

        var task2 = Task.Run(async () =>
        {
            barrier.SignalAndWait();
            return await client2.PostAsync("/api/v1/auth/refresh", content2);
        });

        var responses = await Task.WhenAll(task1, task2);
        var statusCodes = responses.Select(r => r.StatusCode).ToList();

        // Pelo oráculo de concorrência: um obtém 200 e outro falha com 401 ERR-AUTH-006 (ou ambos falham se colisão for reuso)
        Assert.Contains(HttpStatusCode.OK, statusCodes);
        Assert.Contains(HttpStatusCode.Unauthorized, statusCodes);

        var errRes = responses.First(r => r.StatusCode == HttpStatusCode.Unauthorized);
        var errJson = await errRes.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(errJson);
        Assert.Equal("ERR-AUTH-006", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact(Timeout = 60000)]
    public async Task CA_04_7_Borda_IdempotenciaNaRevogacaoDeFamiliaEmISessionStore()
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();

        var userId = Guid.NewGuid();
        var familyId = Guid.NewGuid();

        await using (var db = _fixture.CreateDbContext())
        {
            var user = new UserAccount { Id = userId, Email = "u1@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Utilizador Teste 01", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.NONE, CreatedAt = now, UpdatedAt = now };

            var rt1 = new RefreshToken { Id = Guid.NewGuid(), FamilyId = familyId, UserId = userId, TokenHash = GenerateTestToken().hash, Status = RefreshTokenStatus.ACTIVE, CreatedAt = now, ExpiresAt = now.AddSeconds(604800) };
            var rt2 = new RefreshToken { Id = Guid.NewGuid(), FamilyId = familyId, UserId = userId, TokenHash = GenerateTestToken().hash, Status = RefreshTokenStatus.CONSUMED, ConsumedAt = now, CreatedAt = now, ExpiresAt = now.AddSeconds(604800) };
            var rt3 = new RefreshToken { Id = Guid.NewGuid(), FamilyId = familyId, UserId = userId, TokenHash = GenerateTestToken().hash, Status = RefreshTokenStatus.CONSUMED, ConsumedAt = now, CreatedAt = now, ExpiresAt = now.AddSeconds(604800) };

            db.UserAccounts.Add(user);
            db.RefreshTokens.AddRange(rt1, rt2, rt3);
            await db.SaveChangesAsync();
        }

        await using var dbContext = _fixture.CreateDbContext();
        var sessionStore = new SessionStore(dbContext, _timeProvider);

        var count1 = await sessionStore.RevokeFamilyAsync(familyId);
        Assert.Equal(3, count1);

        var count2 = await sessionStore.RevokeFamilyAsync(familyId);
        Assert.Equal(0, count2);
    }

    [Fact(Timeout = 60000)]
    public async Task CA_04_8_Borda_ValidacaoDeLimitesTemporaisRNF03()
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();

        var (rawToken, tokenHash) = GenerateTestToken();
        var userId = Guid.NewGuid();
        var familyId = Guid.NewGuid();

        await using (var db = _fixture.CreateDbContext())
        {
            var user = new UserAccount { Id = userId, Email = "u1@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Utilizador Teste 01", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.NONE, CreatedAt = now, UpdatedAt = now };
            var rt = new RefreshToken
            {
                Id = Guid.NewGuid(),
                FamilyId = familyId,
                UserId = userId,
                TokenHash = tokenHash,
                Status = RefreshTokenStatus.ACTIVE,
                ViewMode = ViewMode.NONE,
                CreatedAt = now,
                ExpiresAt = now.AddSeconds(604800)
            };

            db.UserAccounts.Add(user);
            db.RefreshTokens.Add(rt);
            await db.SaveChangesAsync();
        }

        await using var app = CreateTestApp();
        await app.StartAsync();
        var client = app.GetTestClient();

        var payload = new { refresh_token = rawToken };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/v1/auth/refresh", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var newRefreshToken = root.GetProperty("refresh_token").GetString()!;

        await using (var db = _fixture.CreateDbContext())
        {
            var newHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(newRefreshToken)));
            var childRt = await db.RefreshTokens.FirstOrDefaultAsync(r => r.TokenHash == newHash);
            Assert.NotNull(childRt);

            var diffSeconds = (childRt.ExpiresAt - childRt.CreatedAt).TotalSeconds;
            Assert.Equal(604800, diffSeconds);
        }
    }
}
