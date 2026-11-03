using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IdentityService.Modules.M00Transversal;
using IdentityService.Modules.M00Transversal.Audit;
using IdentityService.Modules.M00Transversal.Auth;
using IdentityService.Modules.M00Transversal.Errors;
using IdentityService.Modules.M00Transversal.Redis;
using IdentityService.Modules.M07Chaves;
using IdentityService.Modules.M07Chaves.Interface;
using IdentityService.Modules.M11Esquema.Persistence;
using IdentityService.Modules.M11Esquema.Persistence.Enums;
using IdentityService.Tests.Fixtures;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Xunit;

namespace IdentityService.Tests;

[Trait("Fase", "F00")]
[Collection("SharedContainers")]
public class M00TransversalTests
{
    private readonly TestcontainersFixture _fixture;
    private readonly TestSigningKey _signingKey = new("key-2026-01");
    private readonly TestTimeProvider _timeProvider = new(new DateTimeOffset(2026, 11, 2, 22, 0, 0, TimeSpan.Zero));

    public M00TransversalTests(TestcontainersFixture fixture)
    {
        _fixture = fixture;
    }

    private void SetupServices(WebApplicationBuilder builder)
    {
        builder.WebHost.UseTestServer();
        builder.Services.AddRouting();
        builder.Configuration["REDIS_CONNECTION_STRING"] = _fixture.GetRedisConnectionString();
        builder.Services.AddSingleton<ISigningKey>(_signingKey);
        builder.Services.AddSingleton<TimeProvider>(_timeProvider);
        builder.Services.AddM00Transversal(builder.Configuration);
    }

    [Fact(Timeout = 60000)]
    public async Task CA_15_1_Borda_ValidacaoTransporte_CorpoExcessivo_E_ContentTypeInvalido()
    {
        var builder = WebApplication.CreateBuilder();
        SetupServices(builder);

        await using var app = builder.Build();
        app.UseM00Transversal();
        app.MapPost("/test-transport", () => Results.Ok(new { success = true }));
        await app.StartAsync();

        var client = app.GetTestClient();

        // 1. Corpo > 64 KiB (65537 bytes) -> 413 ERR-REQ-003
        var largeContent = new StringContent(new string('x', 65537), Encoding.UTF8, "application/json");
        var response413 = await client.PostAsync("/test-transport", largeContent);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response413.StatusCode);
        Assert.Equal("application/problem+json", response413.Content.Headers.ContentType?.MediaType);
        var json413 = await response413.Content.ReadAsStringAsync();
        using var doc413 = JsonDocument.Parse(json413);
        Assert.Equal("ERR-REQ-003", doc413.RootElement.GetProperty("code").GetString());
        Assert.Equal(413, doc413.RootElement.GetProperty("status").GetInt32());

        // 2. Content-Type diferente de application/json -> 415 ERR-REQ-004
        var textContent = new StringContent("{\"test\": true}", Encoding.UTF8, "text/plain");
        var response415 = await client.PostAsync("/test-transport", textContent);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response415.StatusCode);
        Assert.Equal("application/problem+json", response415.Content.Headers.ContentType?.MediaType);
        var json415 = await response415.Content.ReadAsStringAsync();
        using var doc415 = JsonDocument.Parse(json415);
        Assert.Equal("ERR-REQ-004", doc415.RootElement.GetProperty("code").GetString());
        Assert.Equal(415, doc415.RootElement.GetProperty("status").GetInt32());

        // 3. POST válido application/json com <= 64 KiB -> 200 OK
        var validContent = new StringContent("{\"test\": true}", Encoding.UTF8, "application/json");
        var response200 = await client.PostAsync("/test-transport", validContent);
        Assert.Equal(HttpStatusCode.OK, response200.StatusCode);
    }

    [Fact(Timeout = 60000)]
    public async Task CA_15_2_Borda_InjecaoCorrelacao_E_ResolucaoIp()
    {
        var builder = WebApplication.CreateBuilder();
        SetupServices(builder);

        await using var app = builder.Build();
        app.UseM00Transversal();
        app.MapGet("/test-corr-ip", (IAuthContext auth) => Results.Ok(new
        {
            correlationId = auth.CorrelationId,
            ip = auth.IpAddress
        }));
        await app.StartAsync();

        var client = app.GetTestClient();

        // 1. Sem X-Correlation-ID -> gera novo UUID v7
        var req1 = new HttpRequestMessage(HttpMethod.Get, "/test-corr-ip");
        req1.Headers.Add("X-Forwarded-For", "198.51.100.25, 10.0.0.1");
        var res1 = await client.SendAsync(req1);

        Assert.True(res1.Headers.Contains("X-Correlation-ID"));
        var headerCorr1 = res1.Headers.GetValues("X-Correlation-ID").First();
        Assert.True(Guid.TryParse(headerCorr1, out var parsedCorr1));
        Assert.Equal(7, (parsedCorr1.ToByteArray()[7] >> 4) & 0x0F); // UUID v7 version nibble

        var json1 = await res1.Content.ReadAsStringAsync();
        using var doc1 = JsonDocument.Parse(json1);
        Assert.Equal("198.51.100.25", doc1.RootElement.GetProperty("ip").GetString());

        // 2. Com X-Correlation-ID válido -> preserva o mesmo
        var existingCorr = Guid.CreateVersion7().ToString("D");
        var req2 = new HttpRequestMessage(HttpMethod.Get, "/test-corr-ip");
        req2.Headers.Add("X-Correlation-ID", existingCorr);
        var res2 = await client.SendAsync(req2);

        Assert.True(res2.Headers.Contains("X-Correlation-ID"));
        Assert.Equal(existingCorr, res2.Headers.GetValues("X-Correlation-ID").First());
    }

    [Fact(Timeout = 60000)]
    public async Task CA_15_3_Borda_TratamentoGlobalExcecoes_RFC9457()
    {
        var builder = WebApplication.CreateBuilder();
        SetupServices(builder);

        await using var app = builder.Build();
        app.UseM00Transversal();
        app.MapPost("/test-method", () => Results.Ok());
        app.MapGet("/test-crypto-error", () =>
        {
            throw new CryptographicException("Falha criptográfica de teste.");
#pragma warning disable CS0162
            return Results.Ok();
#pragma warning restore CS0162
        });
        app.MapGet("/test-unhandled", () =>
        {
            throw new InvalidOperationException("Erro inesperado.");
#pragma warning disable CS0162
            return Results.Ok();
#pragma warning restore CS0162
        });
        await app.StartAsync();

        var client = app.GetTestClient();

        // 1. Rota inexistente -> 404 ERR-REQ-001
        var res404 = await client.GetAsync("/api/v1/rota-totalmente-inexistente");
        Assert.Equal(HttpStatusCode.NotFound, res404.StatusCode);
        var json404 = await res404.Content.ReadAsStringAsync();
        using var doc404 = JsonDocument.Parse(json404);
        Assert.Equal("ERR-REQ-001", doc404.RootElement.GetProperty("code").GetString());
        Assert.Equal(404, doc404.RootElement.GetProperty("status").GetInt32());

        // 2. Método não permitido -> 405 ERR-REQ-002
        var res405 = await client.GetAsync("/test-method");
        Assert.Equal(HttpStatusCode.MethodNotAllowed, res405.StatusCode);
        var json405 = await res405.Content.ReadAsStringAsync();
        using var doc405 = JsonDocument.Parse(json405);
        Assert.Equal("ERR-REQ-002", doc405.RootElement.GetProperty("code").GetString());
        Assert.Equal(405, doc405.RootElement.GetProperty("status").GetInt32());

        // 3. Exceção de dependência (Crypto) -> 503 ERR-SYS-002
        var res503 = await client.GetAsync("/test-crypto-error");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, res503.StatusCode);
        var json503 = await res503.Content.ReadAsStringAsync();
        using var doc503 = JsonDocument.Parse(json503);
        Assert.Equal("ERR-SYS-002", doc503.RootElement.GetProperty("code").GetString());
        Assert.Equal(503, doc503.RootElement.GetProperty("status").GetInt32());

        // 4. Exceção não tratada geral -> 500 ERR-SYS-001
        var res500 = await client.GetAsync("/test-unhandled");
        Assert.Equal(HttpStatusCode.InternalServerError, res500.StatusCode);
        var json500 = await res500.Content.ReadAsStringAsync();
        using var doc500 = JsonDocument.Parse(json500);
        Assert.Equal("ERR-SYS-001", doc500.RootElement.GetProperty("code").GetString());
        Assert.Equal(500, doc500.RootElement.GetProperty("status").GetInt32());
    }

    [Fact(Timeout = 60000)]
    public async Task CA_17_1_Borda_InspecaoDenylistRedis_SemTocarBaseDados()
    {
        var redisMux = ConnectionMultiplexer.Connect(_fixture.GetRedisConnectionString());
        var redisDb = redisMux.GetDatabase();

        var builder = WebApplication.CreateBuilder();
        SetupServices(builder);
        builder.Services.AddM07Chaves();

        await using var app = builder.Build();
        app.UseM00Transversal();
        app.MapGet("/api/v1/auth/protected-endpoint", () => Results.Ok(new { protectedData = true }));
        await app.StartAsync();

        var issuer = new TokenIssuer(_signingKey, _timeProvider, builder.Configuration);
        var tenantId = Guid.NewGuid();
        var sub = Guid.NewGuid();
        var familyId = Guid.NewGuid();

        var claims = new AccessTokenClaims(
            Sub: sub,
            FamilyId: familyId,
            PlatformRole: PlatformRole.NONE,
            ViewMode: ViewMode.SINGLE,
            GroupId: Guid.NewGuid(),
            TenantId: tenantId,
            AllowedTenants: new[] { tenantId },
            ActiveScope: OperationalScope.BUYER,
            Roles: new[] { "BUYER_OPERATOR" },
            Permissions: new[] { "orders:read" }
        );

        var token = issuer.Issue(claims);
        var client = app.GetTestClient();

        // 1. Token válido sem denylist -> 200 OK
        var reqValid = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/protected-endpoint");
        reqValid.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        var resValid = await client.SendAsync(reqValid);
        Assert.True(resValid.IsSuccessStatusCode, await resValid.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, resValid.StatusCode);

        // 2. Colocar jti na denylist no Redis
        await redisDb.StringSetAsync($"denylist:jti:{token.Jti}", "1", TimeSpan.FromMinutes(15));

        var reqRevokedToken = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/protected-endpoint");
        reqRevokedToken.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        var resRevokedToken = await client.SendAsync(reqRevokedToken);

        Assert.Equal(HttpStatusCode.Unauthorized, resRevokedToken.StatusCode);
        var jsonRevoked = await resRevokedToken.Content.ReadAsStringAsync();
        using var docRevoked = JsonDocument.Parse(jsonRevoked);
        Assert.Equal("ERR-AUTH-002", docRevoked.RootElement.GetProperty("code").GetString());

        // 3. Colocar user na denylist no Redis com timestamp posterior ao iat
        await redisDb.KeyDeleteAsync($"denylist:jti:{token.Jti}");
        var nextToken = issuer.Issue(claims);
        await redisDb.StringSetAsync($"denylist:user:{sub:D}", (nextToken.Iat + 10).ToString(), TimeSpan.FromMinutes(15));

        var reqRevokedUser = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/protected-endpoint");
        reqRevokedUser.Headers.Authorization = new AuthenticationHeaderValue("Bearer", nextToken.AccessToken);
        var resRevokedUser = await client.SendAsync(reqRevokedUser);

        Assert.Equal(HttpStatusCode.Forbidden, resRevokedUser.StatusCode);
        var jsonRevokedUser = await resRevokedUser.Content.ReadAsStringAsync();
        using var docRevokedUser = JsonDocument.Parse(jsonRevokedUser);
        Assert.Equal("ERR-AUTH-004", docRevokedUser.RootElement.GetProperty("code").GetString());
    }

    [Fact(Timeout = 60000)]
    public async Task CA_17_2_EmissaoEPersistenciaEventosAuditoria()
    {
        await _fixture.CleanDatabaseAsync();

        var authContext = new AuthContext
        {
            UserId = null,
            ActorUserId = null,
            ActiveTenantId = null,
            IpAddress = "198.51.100.42",
            CorrelationId = Guid.CreateVersion7()
        };

        await using var dbContext = _fixture.CreateDbContext(authContext);
        var auditLog = new AuditLog(dbContext, authContext, _timeProvider);

        var details = new Dictionary<string, object?>
        {
            ["motivo"] = "Teste de auditoria estruturada",
            ["tentativas"] = 3
        };

        auditLog.Append(
            AuditEventType.LOGIN_SUCCESS,
            authContext.UserId,
            authContext.ActorUserId,
            authContext.ActiveTenantId,
            authContext.GroupId,
            "TICKET-9999",
            details);

        await dbContext.SaveChangesAsync();

        // Verificar persistência direta no banco
        var savedLog = await dbContext.AuthAuditLogs
            .FirstOrDefaultAsync(l => l.CorrelationId == authContext.CorrelationId);

        Assert.NotNull(savedLog);
        Assert.Equal(AuditEventType.LOGIN_SUCCESS, savedLog.EventType);
        Assert.Null(savedLog.UserId);
        Assert.Null(savedLog.ActorUserId);
        Assert.Null(savedLog.TenantId);
        Assert.Equal("198.51.100.42", savedLog.IpAddress);
        Assert.Equal(authContext.CorrelationId, savedLog.CorrelationId);
        Assert.Contains("Teste de auditoria estruturada", savedLog.Metadata);
    }
}
