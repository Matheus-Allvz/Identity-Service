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
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;
using Xunit;

namespace IdentityService.Tests;

[Trait("Fase", "F01")]
[Collection("SharedContainers")]
public class M09LogoutTests
{
    private readonly TestcontainersFixture _fixture;
    private readonly TestSigningKey _signingKey = new("key-2026-01");
    private readonly TestTimeProvider _timeProvider = new(new DateTimeOffset(2026, 11, 2, 22, 0, 0, TimeSpan.Zero));

    public M09LogoutTests(TestcontainersFixture fixture)
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
        builder.Services.AddM06Impersonation();
        builder.Services.AddM07Chaves();
        builder.Services.AddM08Acessos();
        builder.Services.AddM09Logout();
        builder.Services.AddM11Esquema(builder.Configuration);
        builder.Services.AddM13Isolamento();
        builder.Services.AddM14Eventos(builder.Configuration);

        configureServices?.Invoke(builder.Services);

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

    private string CreateCustomJwt(
        string jti,
        Guid sub,
        Guid? familyId,
        long iat,
        long exp,
        PlatformRole platformRole = PlatformRole.NONE,
        ViewMode viewMode = ViewMode.NONE,
        Guid? groupId = null,
        Guid? tenantId = null,
        IReadOnlyList<Guid>? allowedTenants = null,
        OperationalScope? activeScope = null,
        IReadOnlyList<string>? roles = null,
        IReadOnlyList<string>? permissions = null,
        ActorClaim? act = null,
        SecurityKey? signingKeyOverride = null)
    {
        var key = signingKeyOverride ?? _signingKey.SigningKey;
        var payloadDict = new Dictionary<string, object?>
        {
            ["jti"] = jti,
            ["sub"] = sub.ToString("D"),
            ["iat"] = iat,
            ["exp"] = exp,
            ["iss"] = "identity-service",
            ["aud"] = "b2b-platform",
            ["platform_role"] = platformRole.ToString(),
            ["view_mode"] = viewMode.ToString(),
            ["group_id"] = groupId?.ToString("D"),
            ["tenant_id"] = tenantId?.ToString("D"),
            ["allowed_tenants"] = (allowedTenants ?? []).Select(g => g.ToString("D")).ToArray(),
            ["active_scope"] = activeScope?.ToString(),
            ["roles"] = (roles ?? []).ToArray(),
            ["permissions"] = (permissions ?? []).ToArray()
        };

        if (act != null)
        {
            payloadDict["act"] = new Dictionary<string, object?>
            {
                ["sub"] = act.Sub.ToString("D"),
                ["ticket_id"] = act.TicketId
            };
        }
        else if (familyId.HasValue)
        {
            payloadDict["fid"] = familyId.Value.ToString("D");
        }

        var payloadJson = JsonSerializer.Serialize(payloadDict);
        var credentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256);
        var handler = new JsonWebTokenHandler();
        return handler.CreateToken(payloadJson, credentials);
    }

    [Fact(Timeout = 60000)]
    public async Task CA_05_1_Borda_LogoutDeSessaoNormalComCorpoDe0BytesEExpiracaoIminente()
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();

        var userId = Guid.Parse("018f0003-0000-7000-8000-000000000001");
        var groupId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var familyId = Guid.NewGuid();

        await using (var db = _fixture.CreateDbContext())
        {
            var user = new UserAccount { Id = userId, Email = "teste01@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Utilizador Teste 01", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.NONE, CreatedAt = now, UpdatedAt = now };
            var group = new EconomicGroup { Id = groupId, Name = "GRP1", Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var tenant = new Tenant { Id = tenantId, GroupId = groupId, Cnpj = "99000001000101", LegalName = "TEN1", BranchType = BranchType.HQ, GeoLat = 0, GeoLng = 0, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var member = new TenantMember { Id = Guid.NewGuid(), TenantId = tenantId, UserId = userId, IsOwner = true, CanViewGroup = true, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };

            var rtActive = new RefreshToken
            {
                Id = Guid.NewGuid(),
                FamilyId = familyId,
                UserId = userId,
                TokenHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("hash-active-01"))),
                Status = RefreshTokenStatus.ACTIVE,
                CreatedAt = now.AddMinutes(-10),
                ExpiresAt = now.AddDays(7)
            };

            var rtConsumed = new RefreshToken
            {
                Id = Guid.NewGuid(),
                FamilyId = familyId,
                UserId = userId,
                TokenHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("hash-consumed-01"))),
                Status = RefreshTokenStatus.CONSUMED,
                CreatedAt = now.AddMinutes(-20),
                ExpiresAt = now.AddDays(7),
                ConsumedAt = now.AddMinutes(-10)
            };

            db.UserAccounts.Add(user);
            db.EconomicGroups.Add(group);
            db.Tenants.Add(tenant);
            db.TenantMembers.Add(member);
            db.RefreshTokens.AddRange(rtActive, rtConsumed);
            await db.SaveChangesAsync();
        }

        var iat = now.ToUnixTimeSeconds();
        var exp = iat + 1; // 1 second ahead of now
        var jti = "JTI-01";
        var jwt = CreateCustomJwt(jti, userId, familyId, iat, exp);

        await using var app = CreateTestApp();
        await app.StartAsync();
        var client = app.GetTestClient();

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        req.Content = new ByteArrayContent([]); // 0 bytes

        var response = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.Empty(responseBody);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.False(response.Headers.Contains("Pragma"));
        Assert.True(response.Headers.Contains("X-Correlation-Id"));

        // Check Redis key denylist:jti:JTI-01
        var redisMux = ConnectionMultiplexer.Connect(_fixture.GetRedisConnectionString());
        var redisDb = redisMux.GetDatabase();
        var denylistVal = await redisDb.StringGetAsync($"denylist:jti:{jti}");
        Assert.Equal("1", denylistVal.ToString());

        var ttl = await redisDb.KeyTimeToLiveAsync($"denylist:jti:{jti}");
        Assert.NotNull(ttl);
        Assert.True(ttl.Value > TimeSpan.Zero);
        Assert.True(ttl.Value <= TimeSpan.FromSeconds(2));

        // Check refresh tokens in DB are REVOKED
        await using (var db = _fixture.CreateDbContext())
        {
            var rts = await db.RefreshTokens.Where(r => r.FamilyId == familyId).ToListAsync();
            Assert.Equal(2, rts.Count);
            foreach (var rt in rts)
            {
                Assert.Equal(RefreshTokenStatus.REVOKED, rt.Status);
                Assert.NotNull(rt.RevokedAt);
            }

            var audit = await db.AuthAuditLogs.FirstOrDefaultAsync(a => a.EventType == AuditEventType.LOGOUT && a.UserId == userId);
            Assert.NotNull(audit);
            Assert.Equal("{}", audit.Metadata);
        }
    }

    [Fact(Timeout = 60000)]
    public async Task CA_05_2_Borda_LogoutDeSessaoSobImpersonationComCorpoVazioETicketNoLimite()
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();

        var agentId = Guid.Parse("018f0003-0000-7000-8000-000000000099");
        var targetUserId = Guid.Parse("018f0003-0000-7000-8000-000000000001");
        var groupId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        await using (var db = _fixture.CreateDbContext())
        {
            var agent = new UserAccount { Id = agentId, Email = "agente99@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Agente Suporte", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.SUPPORT_AGENT, CreatedAt = now, UpdatedAt = now };
            var target = new UserAccount { Id = targetUserId, Email = "alvo01@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Utilizador Alvo", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.NONE, CreatedAt = now, UpdatedAt = now };
            var group = new EconomicGroup { Id = groupId, Name = "GRP1", Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var tenant = new Tenant { Id = tenantId, GroupId = groupId, Cnpj = "99000001000101", LegalName = "TEN1", BranchType = BranchType.HQ, GeoLat = 0, GeoLng = 0, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var member = new TenantMember { Id = Guid.NewGuid(), TenantId = tenantId, UserId = targetUserId, IsOwner = true, CanViewGroup = true, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };

            db.UserAccounts.AddRange(agent, target);
            db.EconomicGroups.Add(group);
            db.Tenants.Add(tenant);
            db.TenantMembers.Add(member);
            await db.SaveChangesAsync();
        }

        var iat = now.ToUnixTimeSeconds();
        var exp = iat + 900;
        var jti = "JTI-02";
        var act = new ActorClaim(agentId, "SUP-12345678"); // exactly 12 chars
        var jwt = CreateCustomJwt(jti, targetUserId, familyId: null, iat, exp, act: act);

        await using var app = CreateTestApp();
        await app.StartAsync();
        var client = app.GetTestClient();

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        req.Content = new StringContent("{}", Encoding.UTF8, "application/json");

        var response = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Redis key denylist:jti:JTI-02 has value 1
        var redisMux = ConnectionMultiplexer.Connect(_fixture.GetRedisConnectionString());
        var redisDb = redisMux.GetDatabase();
        var denylistVal = await redisDb.StringGetAsync($"denylist:jti:{jti}");
        Assert.Equal("1", denylistVal.ToString());

        // refresh_token untouched
        await using (var db = _fixture.CreateDbContext())
        {
            var countRt = await db.RefreshTokens.CountAsync();
            Assert.Equal(0, countRt);

            var audit = await db.AuthAuditLogs.FirstOrDefaultAsync(a => a.EventType == AuditEventType.LOGOUT && a.UserId == targetUserId);
            Assert.NotNull(audit);
            Assert.Equal(agentId, audit.ActorUserId);
            Assert.Equal("SUP-12345678", audit.TicketId);
        }
    }

    [Theory(Timeout = 60000)]
    [InlineData("{\"campo\": 1}")]
    [InlineData("{\"\": \"valor\"}")]
    [InlineData("{\"sessao\": null}")]
    [InlineData("[]")]
    [InlineData("\"logout\"")]
    [InlineData("{")]
    public async Task CA_05_3_Borda_RejeicaoDeCorpoContendoPropriedadesOuJsonInvalido(string invalidBody)
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();

        var userId = Guid.NewGuid();
        var familyId = Guid.NewGuid();

        await using (var db = _fixture.CreateDbContext())
        {
            var user = new UserAccount { Id = userId, Email = "user@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Utilizador Teste", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.NONE, CreatedAt = now, UpdatedAt = now };
            db.UserAccounts.Add(user);
            await db.SaveChangesAsync();
        }

        var iat = now.ToUnixTimeSeconds();
        var exp = iat + 900;
        var jti = "JTI-03";
        var jwt = CreateCustomJwt(jti, userId, familyId, iat, exp);

        await using var app = CreateTestApp();
        await app.StartAsync();
        var client = app.GetTestClient();

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        req.Content = new StringContent(invalidBody, Encoding.UTF8, "application/json");

        var response = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("ERR-VAL-001", doc.RootElement.GetProperty("code").GetString());

        // Verify no changes persisted
        await using (var db = _fixture.CreateDbContext())
        {
            var auditCount = await db.AuthAuditLogs.CountAsync();
            Assert.Equal(0, auditCount);
        }

        var redisMux = ConnectionMultiplexer.Connect(_fixture.GetRedisConnectionString());
        var redisDb = redisMux.GetDatabase();
        var denylistVal = await redisDb.StringGetAsync($"denylist:jti:{jti}");
        Assert.False(denylistVal.HasValue);
    }

    [Fact(Timeout = 60000)]
    public async Task CA_05_4_Borda_IdempotenciaDeLogoutQuandoRefreshTokensJaRevogadosOuAusentes()
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();

        var userId = Guid.NewGuid();
        var familyId = Guid.NewGuid();

        await using (var db = _fixture.CreateDbContext())
        {
            var user = new UserAccount { Id = userId, Email = "user@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Utilizador Teste", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.NONE, CreatedAt = now, UpdatedAt = now };
            var rtRevoked = new RefreshToken
            {
                Id = Guid.NewGuid(),
                FamilyId = familyId,
                UserId = userId,
                TokenHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("hash-revoked-fam2"))),
                Status = RefreshTokenStatus.REVOKED,
                CreatedAt = now.AddHours(-1),
                ExpiresAt = now.AddDays(7),
                RevokedAt = now.AddMinutes(-30)
            };

            db.UserAccounts.Add(user);
            db.RefreshTokens.Add(rtRevoked);
            await db.SaveChangesAsync();
        }

        var iat = now.ToUnixTimeSeconds();
        var exp = iat + 900;
        var jti = "JTI-04";
        var jwt = CreateCustomJwt(jti, userId, familyId, iat, exp);

        await using var app = CreateTestApp();
        await app.StartAsync();
        var client = app.GetTestClient();

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        req.Content = new ByteArrayContent([]);

        var response = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Redis key denylist:jti:JTI-04 has value 1
        var redisMux = ConnectionMultiplexer.Connect(_fixture.GetRedisConnectionString());
        var redisDb = redisMux.GetDatabase();
        var denylistVal = await redisDb.StringGetAsync($"denylist:jti:{jti}");
        Assert.Equal("1", denylistVal.ToString());

        // Refresh token maintains REVOKED
        await using (var db = _fixture.CreateDbContext())
        {
            var rt = await db.RefreshTokens.FirstOrDefaultAsync(r => r.FamilyId == familyId);
            Assert.NotNull(rt);
            Assert.Equal(RefreshTokenStatus.REVOKED, rt.Status);

            var audit = await db.AuthAuditLogs.FirstOrDefaultAsync(a => a.EventType == AuditEventType.LOGOUT && a.UserId == userId);
            Assert.NotNull(audit);
        }
    }

    [Theory(Timeout = 60000)]
    [InlineData("sem_auth", 401, "ERR-AUTH-002")]
    [InlineData("expirado", 401, "ERR-AUTH-002")]
    [InlineData("denylist_jti", 401, "ERR-AUTH-002")]
    [InlineData("assinatura_forjada", 401, "ERR-AUTH-002")]
    public async Task CA_05_5_Borda_RejeicaoDeTokenAusenteExpiradoOuRevogado(string condicao, int statusEsperado, string codigoEsperado)
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();
        var userId = Guid.NewGuid();
        var familyId = Guid.NewGuid();

        var iat = now.ToUnixTimeSeconds();
        var jti = $"JTI-05-{condicao}";

        string? tokenHeader = null;
        if (condicao == "expirado")
        {
            // exp = agora - 1
            tokenHeader = CreateCustomJwt(jti, userId, familyId, iat - 100, iat - 1);
        }
        else if (condicao == "denylist_jti")
        {
            tokenHeader = CreateCustomJwt(jti, userId, familyId, iat, iat + 900);
            var redisMux = ConnectionMultiplexer.Connect(_fixture.GetRedisConnectionString());
            var redisDb = redisMux.GetDatabase();
            await redisDb.StringSetAsync($"denylist:jti:{jti}", "1", TimeSpan.FromMinutes(15));
        }
        else if (condicao == "assinatura_forjada")
        {
            var forgedKey = new TestSigningKey("forged-key-99");
            tokenHeader = CreateCustomJwt(jti, userId, familyId, iat, iat + 900, signingKeyOverride: forgedKey.SigningKey);
        }

        await using var app = CreateTestApp();
        await app.StartAsync();
        var client = app.GetTestClient();

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        if (tokenHeader != null)
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenHeader);
        }
        req.Content = new ByteArrayContent([]);

        var response = await client.SendAsync(req);
        Assert.Equal((HttpStatusCode)statusEsperado, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(codigoEsperado, doc.RootElement.GetProperty("code").GetString());

        // Verify no audit records persisted
        await using (var db = _fixture.CreateDbContext())
        {
            var auditCount = await db.AuthAuditLogs.CountAsync();
            Assert.Equal(0, auditCount);
        }
    }

    [Fact(Timeout = 60000)]
    public async Task CA_05_6_Borda_ExecucaoSimultaneaDeDoisPedidosDeLogoutComMesmoToken()
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();

        var userId = Guid.NewGuid();
        var familyId = Guid.NewGuid();

        await using (var db = _fixture.CreateDbContext())
        {
            var user = new UserAccount { Id = userId, Email = "user@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Utilizador Teste", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.NONE, CreatedAt = now, UpdatedAt = now };
            var rtActive = new RefreshToken
            {
                Id = Guid.NewGuid(),
                FamilyId = familyId,
                UserId = userId,
                TokenHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("hash-active-fam3"))),
                Status = RefreshTokenStatus.ACTIVE,
                CreatedAt = now,
                ExpiresAt = now.AddDays(7)
            };

            db.UserAccounts.Add(user);
            db.RefreshTokens.Add(rtActive);
            await db.SaveChangesAsync();
        }

        var iat = now.ToUnixTimeSeconds();
        var exp = iat + 900;
        var jti = "JTI-06";
        var jwt = CreateCustomJwt(jti, userId, familyId, iat, exp);

        await using var app = CreateTestApp();
        await app.StartAsync();
        var client = app.GetTestClient();

        var req1 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        req1.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        req1.Content = new ByteArrayContent([]);

        var req2 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        req2.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        req2.Content = new ByteArrayContent([]);

        var responses = await Task.WhenAll(client.SendAsync(req1), client.SendAsync(req2));

        Assert.Equal(HttpStatusCode.NoContent, responses[0].StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, responses[1].StatusCode);

        // Redis key denylist:jti:JTI-06 has value 1
        var redisMux = ConnectionMultiplexer.Connect(_fixture.GetRedisConnectionString());
        var redisDb = redisMux.GetDatabase();
        var denylistVal = await redisDb.StringGetAsync($"denylist:jti:{jti}");
        Assert.Equal("1", denylistVal.ToString());

        // Refresh tokens of FAM3 have status REVOKED
        await using (var db = _fixture.CreateDbContext())
        {
            var rt = await db.RefreshTokens.FirstOrDefaultAsync(r => r.FamilyId == familyId);
            Assert.NotNull(rt);
            Assert.Equal(RefreshTokenStatus.REVOKED, rt.Status);
        }

        // Subsequent call with token JTI-06 returns 401 ERR-AUTH-002
        var reqSubsequent = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        reqSubsequent.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        reqSubsequent.Content = new ByteArrayContent([]);

        var resSubsequent = await client.SendAsync(reqSubsequent);
        Assert.Equal(HttpStatusCode.Unauthorized, resSubsequent.StatusCode);
        var jsonSub = await resSubsequent.Content.ReadAsStringAsync();
        using var docSub = JsonDocument.Parse(jsonSub);
        Assert.Equal("ERR-AUTH-002", docSub.RootElement.GetProperty("code").GetString());
    }

    [Fact(Timeout = 60000)]
    public async Task CA_05_7_Borda_ReversaoAtomicaDeTransacaoEmCasoDeTimeoutOuFalhaDoRedis()
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();

        var userId = Guid.NewGuid();
        var familyId = Guid.NewGuid();

        await using (var db = _fixture.CreateDbContext())
        {
            var user = new UserAccount { Id = userId, Email = "user@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Utilizador Teste", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.NONE, CreatedAt = now, UpdatedAt = now };
            var rtActive = new RefreshToken
            {
                Id = Guid.NewGuid(),
                FamilyId = familyId,
                UserId = userId,
                TokenHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("hash-active-fam4"))),
                Status = RefreshTokenStatus.ACTIVE,
                CreatedAt = now,
                ExpiresAt = now.AddDays(7)
            };

            db.UserAccounts.Add(user);
            db.RefreshTokens.Add(rtActive);
            await db.SaveChangesAsync();
        }

        var iat = now.ToUnixTimeSeconds();
        var exp = iat + 900;
        var jti = "JTI-07";
        var jwt = CreateCustomJwt(jti, userId, familyId, iat, exp);

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
                return new FaultyDenylist(real, failRevokeJti: true);
            });
        });

        await app.StartAsync();
        var client = app.GetTestClient();

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        req.Content = new ByteArrayContent([]);

        var response = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("ERR-SYS-002", doc.RootElement.GetProperty("code").GetString());

        // Verify tokens of FAM4 remain ACTIVE due to rollback
        await using (var db = _fixture.CreateDbContext())
        {
            var rt = await db.RefreshTokens.FirstOrDefaultAsync(r => r.FamilyId == familyId);
            Assert.NotNull(rt);
            Assert.Equal(RefreshTokenStatus.ACTIVE, rt.Status);

            var audit = await db.AuthAuditLogs.FirstOrDefaultAsync(a => a.EventType == AuditEventType.LOGOUT && a.UserId == userId);
            Assert.Null(audit);
        }
    }

    [Theory(Timeout = 60000)]
    [InlineData("body_grande", 413, "ERR-REQ-003")]
    [InlineData("tipo_invalido", 415, "ERR-REQ-004")]
    [InlineData("user_denylist", 403, "ERR-AUTH-004")]
    [InlineData("tenant_denylist", 403, "ERR-CTX-002")]
    public async Task CA_05_8_Borda_RejeicaoDeTransporteOuPresencaEmDenylistAdministrativa(
        string cenario, int statusEsperado, string codigoEsperado)
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();

        var userId = Guid.NewGuid();
        var familyId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var groupId = Guid.NewGuid();

        await using (var db = _fixture.CreateDbContext())
        {
            var user = new UserAccount { Id = userId, Email = "user@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "Utilizador Teste", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.NONE, CreatedAt = now, UpdatedAt = now };
            var group = new EconomicGroup { Id = groupId, Name = "GRP1", Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var tenant = new Tenant { Id = tenantId, GroupId = groupId, Cnpj = "99000001000101", LegalName = "TEN1", BranchType = BranchType.HQ, GeoLat = 0, GeoLng = 0, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var member = new TenantMember { Id = Guid.NewGuid(), TenantId = tenantId, UserId = userId, IsOwner = true, CanViewGroup = true, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };

            db.UserAccounts.Add(user);
            db.EconomicGroups.Add(group);
            db.Tenants.Add(tenant);
            db.TenantMembers.Add(member);
            await db.SaveChangesAsync();
        }

        var iat = now.ToUnixTimeSeconds();
        var exp = iat + 900;
        var jti = $"JTI-08-{cenario}";

        var jwt = CreateCustomJwt(
            jti,
            userId,
            familyId,
            iat,
            exp,
            viewMode: ViewMode.SINGLE,
            groupId: groupId,
            tenantId: tenantId,
            allowedTenants: [tenantId],
            activeScope: OperationalScope.BUYER,
            roles: ["BUYER_ADMIN"],
            permissions: ["orders:read"]);

        if (cenario == "user_denylist")
        {
            var redisMux = ConnectionMultiplexer.Connect(_fixture.GetRedisConnectionString());
            var redisDb = redisMux.GetDatabase();
            // denylist:user:{sub} with timestamp = iat
            await redisDb.StringSetAsync($"denylist:user:{userId:D}", iat.ToString(), TimeSpan.FromMinutes(15));
        }
        else if (cenario == "tenant_denylist")
        {
            var redisMux = ConnectionMultiplexer.Connect(_fixture.GetRedisConnectionString());
            var redisDb = redisMux.GetDatabase();
            await redisDb.StringSetAsync($"denylist:tenant:{tenantId:D}", "1", TimeSpan.FromMinutes(15));
        }

        await using var app = CreateTestApp();
        await app.StartAsync();
        var client = app.GetTestClient();

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        if (cenario == "body_grande")
        {
            // 65537 bytes (> 65536)
            var largeBytes = new byte[65537];
            req.Content = new ByteArrayContent(largeBytes);
            req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }
        else if (cenario == "tipo_invalido")
        {
            // 5 bytes with text/plain
            req.Content = new StringContent("hello", Encoding.UTF8, "text/plain");
        }
        else
        {
            req.Content = new ByteArrayContent([]);
        }

        var response = await client.SendAsync(req);
        Assert.Equal((HttpStatusCode)statusEsperado, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(codigoEsperado, doc.RootElement.GetProperty("code").GetString());

        // Verify no changes persisted
        await using (var db = _fixture.CreateDbContext())
        {
            var auditCount = await db.AuthAuditLogs.CountAsync();
            Assert.Equal(0, auditCount);
        }
    }

    private sealed class FaultyDenylist : IDenylist
    {
        private readonly IDenylist _inner;
        public bool FailRevokeJti { get; set; }

        public FaultyDenylist(IDenylist inner, bool failRevokeJti = true)
        {
            _inner = inner;
            FailRevokeJti = failRevokeJti;
        }

        public Task RevokeJtiAsync(string jti, long exp, CancellationToken cancellationToken = default)
        {
            if (FailRevokeJti)
            {
                throw new RedisTimeoutException("Redis command timed out after 15000ms", CommandStatus.Unknown);
            }
            return _inner.RevokeJtiAsync(jti, exp, cancellationToken);
        }

        public Task<bool> IsJtiRevokedAsync(string jti, CancellationToken cancellationToken = default)
            => _inner.IsJtiRevokedAsync(jti, cancellationToken);

        public Task<long> BlockUserAsync(Guid userId, CancellationToken cancellationToken = default)
            => _inner.BlockUserAsync(userId, cancellationToken);

        public Task BlockTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
            => _inner.BlockTenantAsync(tenantId, cancellationToken);

        public Task UnblockTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
            => _inner.UnblockTenantAsync(tenantId, cancellationToken);

        public Task<long?> GetUserRevokedAtAsync(Guid userId, CancellationToken cancellationToken = default)
            => _inner.GetUserRevokedAtAsync(userId, cancellationToken);

        public Task<IReadOnlySet<Guid>> GetBlockedTenantsAsync(IReadOnlyList<Guid> tenantIds, CancellationToken cancellationToken = default)
            => _inner.GetBlockedTenantsAsync(tenantIds, cancellationToken);
    }
}
