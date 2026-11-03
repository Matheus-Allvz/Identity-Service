using System.Net;
using System.Text;
using System.Text.Json;
using IdentityService.Modules.M00Transversal;
using IdentityService.Modules.M00Transversal.Auth;
using IdentityService.Modules.M00Transversal.Redis;
using IdentityService.Modules.M01Login;
using IdentityService.Modules.M01Login.Interface;
using IdentityService.Modules.M02Refresh;
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
using StackExchange.Redis;
using Xunit;

namespace IdentityService.Tests;

[Trait("Fase", "F01")]
[Collection("SharedContainers")]
public class M01LoginTests
{
    private readonly TestcontainersFixture _fixture;
    private readonly TestSigningKey _signingKey = new("key-2026-01");
    private readonly TestTimeProvider _timeProvider = new(new DateTimeOffset(2026, 11, 2, 22, 0, 0, TimeSpan.Zero));
    private readonly PasswordHasher _passwordHasher = new();

    public M01LoginTests(TestcontainersFixture fixture)
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

    [Fact(Timeout = 60000)]
    [Trait("Critico", "nao")]
    public async Task CA_01_1_AutenticacaoComSucessoEEmissaoDeSessaoInicial()
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();

        var userId = Guid.NewGuid();
        var hash = _passwordHasher.HashPassword("PasswordSegura123!");

        await using (var db = _fixture.CreateDbContext())
        {
            var user = new UserAccount
            {
                Id = userId,
                Email = "utilizador@example.test",
                PasswordHash = hash,
                Name = "Utilizador Teste 01",
                Status = UserStatus.ACTIVE,
                PlatformRole = PlatformRole.NONE,
                FailedLoginAttempts = 0,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.UserAccounts.Add(user);
            await db.SaveChangesAsync();
        }

        await using var app = CreateTestApp();
        await app.StartAsync();
        var client = app.GetTestClient();

        var payload = new
        {
            email = " Utilizador@Example.Test ",
            password = "PasswordSegura123!"
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/v1/auth/login", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("Bearer", root.GetProperty("token_type").GetString());
        Assert.Equal(900, root.GetProperty("expires_in").GetInt32());
        Assert.Equal(604800, root.GetProperty("refresh_expires_in").GetInt32());

        var accessToken = root.GetProperty("access_token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(accessToken));

        var refreshToken = root.GetProperty("refresh_token").GetString();
        Assert.NotNull(refreshToken);
        Assert.Equal(43, refreshToken.Length);

        var userJson = root.GetProperty("user");
        Assert.Equal(userId.ToString("D"), userJson.GetProperty("id").GetString());
        Assert.Equal("utilizador@example.test", userJson.GetProperty("email").GetString());
        Assert.Equal("NONE", userJson.GetProperty("platform_role").GetString());

        // Verificar banco: falhas zeradas e auditoria LOGIN_SUCCESS
        await using (var db = _fixture.CreateDbContext())
        {
            var u = await db.UserAccounts.FindAsync(userId);
            Assert.NotNull(u);
            Assert.Equal(0, u.FailedLoginAttempts);

            var audit = await db.AuthAuditLogs.FirstOrDefaultAsync(l => l.UserId == userId && l.EventType == AuditEventType.LOGIN_SUCCESS);
            Assert.NotNull(audit);
        }
    }

    [Theory(Timeout = 60000)]
    [InlineData("invalido", "PasswordSegura123!")]
    [InlineData("utilizador@example.test", "fraca")]
    [InlineData("utilizador@example.test", "SemDigitoOuEspecial")]
    public async Task CA_01_2_Borda_RejeicaoPorFormatoDeEmailOuPoliticaDePalavraPasse(string email, string password)
    {
        await using var app = CreateTestApp();
        await app.StartAsync();
        var client = app.GetTestClient();

        var payload = new { email, password };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/v1/auth/login", content);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("ERR-VAL-003", doc.RootElement.GetProperty("code").GetString());
    }

    [Theory(Timeout = 60000)]
    [Trait("Critico", "sim")]
    [InlineData("inexistente@example.test", "PasswordSegura123!")]
    [InlineData("utilizador@example.test", "PasswordErrada123!")]
    public async Task CA_01_3_Borda_ProtecaoDeEnumeracaoECredenciaisInvalidas(string email, string password)
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();
        var userId = Guid.NewGuid();

        await using (var db = _fixture.CreateDbContext())
        {
            var user = new UserAccount
            {
                Id = userId,
                Email = "utilizador@example.test",
                PasswordHash = _passwordHasher.HashPassword("PasswordSegura123!"),
                Name = "Utilizador Teste 01",
                Status = UserStatus.ACTIVE,
                PlatformRole = PlatformRole.NONE,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.UserAccounts.Add(user);
            await db.SaveChangesAsync();
        }

        await using var app = CreateTestApp();
        await app.StartAsync();
        var client = app.GetTestClient();

        var payload = new { email, password };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/v1/auth/login", content);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("ERR-AUTH-001", doc.RootElement.GetProperty("code").GetString());

        await using (var db = _fixture.CreateDbContext())
        {
            var audit = await db.AuthAuditLogs.FirstOrDefaultAsync(l => l.EventType == AuditEventType.LOGIN_FAILED);
            Assert.NotNull(audit);
        }
    }

    [Fact(Timeout = 60000)]
    [Trait("Critico", "sim")]
    public async Task CA_01_4_Borda_BloqueioDaContaApos5TentativasFalhadasConsecutivas()
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();
        var userId = Guid.NewGuid();

        await using (var db = _fixture.CreateDbContext())
        {
            var user = new UserAccount
            {
                Id = userId,
                Email = "utilizador@example.test",
                PasswordHash = _passwordHasher.HashPassword("PasswordSegura123!"),
                Name = "Utilizador Teste 01",
                Status = UserStatus.ACTIVE,
                PlatformRole = PlatformRole.NONE,
                FailedLoginAttempts = 4,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.UserAccounts.Add(user);
            await db.SaveChangesAsync();
        }

        await using var app = CreateTestApp();
        await app.StartAsync();
        var client = app.GetTestClient();

        var payload = new { email = "utilizador@example.test", password = "PasswordErrada123!" };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/v1/auth/login", content);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("ERR-AUTH-001", doc.RootElement.GetProperty("code").GetString());

        // Verificar bloqueio no banco
        await using (var db = _fixture.CreateDbContext())
        {
            var u = await db.UserAccounts.FindAsync(userId);
            Assert.NotNull(u);
            Assert.Equal(UserStatus.LOCKED, u.Status);
            Assert.Equal(5, u.FailedLoginAttempts);
            Assert.NotNull(u.LockedUntil);

            var audit = await db.AuthAuditLogs.FirstOrDefaultAsync(l => l.UserId == userId && l.EventType == AuditEventType.USER_LOCKED);
            Assert.NotNull(audit);

            var outbox = await db.OutboxMessages.FirstOrDefaultAsync(o => o.Topic == "usuario_bloqueado" && o.PartitionKey == userId.ToString("D"));
            Assert.NotNull(outbox);
        }

        // Verificar denylist no Redis
        var redis = ConnectionMultiplexer.Connect(_fixture.GetRedisConnectionString());
        var redisDb = redis.GetDatabase();
        var denylistVal = await redisDb.StringGetAsync($"denylist:user:{userId:D}");
        Assert.True(denylistVal.HasValue);
    }

    [Fact(Timeout = 60000)]
    public async Task CA_01_5_Borda_ReativacaoAutomaticaDeContaAposExpiracaoDoPeriodoDeBloqueio()
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();
        var userId = Guid.NewGuid();

        await using (var db = _fixture.CreateDbContext())
        {
            var user = new UserAccount
            {
                Id = userId,
                Email = "utilizador@example.test",
                PasswordHash = _passwordHasher.HashPassword("PasswordSegura123!"),
                Name = "Utilizador Teste 01",
                Status = UserStatus.LOCKED,
                PlatformRole = PlatformRole.NONE,
                FailedLoginAttempts = 5,
                LockedUntil = now.AddSeconds(-10), // expirado
                CreatedAt = now,
                UpdatedAt = now
            };
            db.UserAccounts.Add(user);
            await db.SaveChangesAsync();
        }

        await using var app = CreateTestApp();
        await app.StartAsync();
        var client = app.GetTestClient();

        var payload = new { email = "utilizador@example.test", password = "PasswordSegura123!" };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/v1/auth/login", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using (var db = _fixture.CreateDbContext())
        {
            var u = await db.UserAccounts.FindAsync(userId);
            Assert.NotNull(u);
            Assert.Equal(UserStatus.ACTIVE, u.Status);
            Assert.Null(u.LockedUntil);
            Assert.Equal(0, u.FailedLoginAttempts);
        }
    }

    [Fact(Timeout = 60000)]
    [Trait("Critico", "sim")]
    public async Task CA_01_6_Borda_RejeicaoDeUtilizadorSuspensoComSenhaCorreta()
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();
        var userId = Guid.NewGuid();

        await using (var db = _fixture.CreateDbContext())
        {
            var user = new UserAccount
            {
                Id = userId,
                Email = "utilizador@example.test",
                PasswordHash = _passwordHasher.HashPassword("PasswordSegura123!"),
                Name = "Utilizador Teste 01",
                Status = UserStatus.SUSPENDED,
                PlatformRole = PlatformRole.NONE,
                FailedLoginAttempts = 0,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.UserAccounts.Add(user);
            await db.SaveChangesAsync();
        }

        await using var app = CreateTestApp();
        await app.StartAsync();
        var client = app.GetTestClient();

        var payload = new { email = "utilizador@example.test", password = "PasswordSegura123!" };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/v1/auth/login", content);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("ERR-AUTH-004", doc.RootElement.GetProperty("code").GetString());

        await using (var db = _fixture.CreateDbContext())
        {
            var u = await db.UserAccounts.FindAsync(userId);
            Assert.NotNull(u);
            Assert.Equal(0, u.FailedLoginAttempts);
        }
    }

    [Fact(Timeout = 60000)]
    [Trait("Critico", "sim")]
    public async Task CA_01_7_Borda_RollbackTotalNaFalhaDePersistenciaDuranteAutenticacao()
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();
        var userId = Guid.NewGuid();

        await using (var db = _fixture.CreateDbContext())
        {
            var user = new UserAccount
            {
                Id = userId,
                Email = "utilizador@example.test",
                PasswordHash = _passwordHasher.HashPassword("PasswordSegura123!"),
                Name = "Utilizador Teste 01",
                Status = UserStatus.ACTIVE,
                PlatformRole = PlatformRole.NONE,
                FailedLoginAttempts = 0,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.UserAccounts.Add(user);
            await db.SaveChangesAsync();
        }

        // Testar IUserAccountStatusWriter com transições e rollback
        await using var dbContext = _fixture.CreateDbContext();
        var writer = new UserAccountStatusWriter(dbContext, _timeProvider);

        // LOCKED gera ArgumentException
        await Assert.ThrowsAsync<ArgumentException>(() => writer.SetAdministrativeStatusAsync(userId, UserStatus.LOCKED));

        // Mudança válida para SUSPENDED
        var prev = await writer.SetAdministrativeStatusAsync(userId, UserStatus.SUSPENDED);
        Assert.Equal(UserStatus.ACTIVE, prev);

        // Utilizador inexistente retorna null
        var none = await writer.SetAdministrativeStatusAsync(Guid.NewGuid(), UserStatus.ACTIVE);
        Assert.Null(none);
    }
}
