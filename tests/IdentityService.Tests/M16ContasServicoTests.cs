using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using IdentityService.Modules.M00Transversal;
using IdentityService.Modules.M00Transversal.Auth;
using IdentityService.Modules.M01Login;
using IdentityService.Modules.M02Refresh;
using IdentityService.Modules.M03Contexto;
using IdentityService.Modules.M07Chaves;
using IdentityService.Modules.M07Chaves.Interface;
using IdentityService.Modules.M08Acessos;
using IdentityService.Modules.M11Esquema;
using IdentityService.Modules.M11Esquema.Persistence.Entities;
using IdentityService.Modules.M11Esquema.Persistence.Enums;
using IdentityService.Modules.M13Isolamento;
using IdentityService.Modules.M14Eventos;
using IdentityService.Modules.M16ContasServico;
using IdentityService.Tests.Fixtures;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Xunit;

namespace IdentityService.Tests;

// NEW-06 / EP-12: POST /api/v1/auth/token (Client Credentials)
[Trait("Fase", "F06")]
[Collection("SharedContainers")]
public class M16ContasServicoTests
{
    private const string ClientId = "order-service";
    private const string ClientSecret = "segredo-de-servico-com-alta-entropia-0123456789";
    private static readonly Guid RoleBuyerOperator = Guid.Parse("01900000-0000-7000-8000-000000000001");

    private readonly TestcontainersFixture _fixture;
    private readonly TestSigningKey _signingKey = new("key-2026-01");
    private readonly TestTimeProvider _timeProvider = new(new DateTimeOffset(2026, 11, 2, 22, 0, 0, TimeSpan.Zero));
    private readonly PasswordHasher _passwordHasher = new();

    public M16ContasServicoTests(TestcontainersFixture fixture)
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
        builder.Services.AddM07Chaves();
        builder.Services.AddM08Acessos();
        builder.Services.AddM11Esquema(builder.Configuration);
        builder.Services.AddM13Isolamento();
        builder.Services.AddM14Eventos(builder.Configuration);

        var app = builder.Build();
        app.UseM00Transversal();
        app.MapM01Login();
        app.MapM03Contexto();
        app.MapM16ContasServico();
        return app;
    }

    private async Task<(Guid AccountId, Guid GroupId, Guid TenantId)> SeedAsync(
        EntityStatus accountStatus = EntityStatus.ACTIVE,
        EntityStatus tenantStatus = EntityStatus.ACTIVE,
        bool scopeActive = true)
    {
        await _fixture.CleanDatabaseAsync();
        var now = _timeProvider.GetUtcNow();
        var accountId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        await using var db = _fixture.CreateDbContext();
        db.EconomicGroups.Add(new EconomicGroup { Id = groupId, Name = "Grupo NEW06", Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now });
        db.Tenants.Add(new Tenant
        {
            Id = tenantId, GroupId = groupId, Cnpj = "99000001000101", LegalName = "Tenant NEW06", BranchType = BranchType.HQ,
            GeoLat = 0, GeoLng = 0, Status = tenantStatus, CreatedAt = now, UpdatedAt = now
        });
        db.TenantScopes.Add(new TenantScope { Id = Guid.NewGuid(), TenantId = tenantId, Scope = OperationalScope.BUYER, IsActive = scopeActive, CreatedAt = now });
        db.ServiceAccounts.Add(new ServiceAccount
        {
            Id = accountId, ClientId = ClientId, ClientSecretHash = _passwordHasher.HashPassword(ClientSecret),
            TenantId = tenantId, RoleId = RoleBuyerOperator, Status = accountStatus, CreatedAt = now, UpdatedAt = now
        });
        await db.SaveChangesAsync();
        return (accountId, groupId, tenantId);
    }

    private static FormUrlEncodedContent Form(string grantType = "client_credentials", string? clientId = ClientId, string? clientSecret = ClientSecret)
    {
        var fields = new Dictionary<string, string> { ["grant_type"] = grantType };
        if (clientId != null) fields["client_id"] = clientId;
        if (clientSecret != null) fields["client_secret"] = clientSecret;
        return new FormUrlEncodedContent(fields);
    }

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    [Fact(Timeout = 60000)]
    public async Task CA_NEW06_1_FormUrlEncoded_EmiteJwtDeServicoValidavelPeloJwks()
    {
        var (accountId, groupId, tenantId) = await SeedAsync();
        await using var app = CreateTestApp();
        await app.StartAsync();

        var response = await app.GetTestClient().PostAsync("/api/v1/auth/token", Form());
        var body = await BodyAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.Equal(["access_token", "token_type", "expires_in"], body.EnumerateObject().Select(p => p.Name));
        Assert.Equal("Bearer", body.GetProperty("token_type").GetString());
        Assert.Equal(900, body.GetProperty("expires_in").GetInt32());

        // Validação como um consumidor faria: RS256 com a chave pública, iss e aud.
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(body.GetProperty("access_token").GetString(), new TokenValidationParameters
        {
            ValidIssuer = "identity-service",
            ValidAudience = "b2b-platform",
            IssuerSigningKey = _signingKey.SigningKey,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            ValidateLifetime = false
        });
        Assert.True(result.IsValid);

        var claims = JsonDocument.Parse(Base64UrlEncoder.Decode(((JsonWebToken)result.SecurityToken).EncodedPayload)).RootElement;
        Assert.Equal(accountId.ToString("D"), claims.GetProperty("sub").GetString());
        Assert.Equal("service", claims.GetProperty("token_use").GetString());
        Assert.Equal(ClientId, claims.GetProperty("client_id").GetString());
        Assert.False(claims.TryGetProperty("fid", out _));
        Assert.False(claims.TryGetProperty("act", out _));
        Assert.Equal("SINGLE", claims.GetProperty("view_mode").GetString());
        Assert.Equal("NONE", claims.GetProperty("platform_role").GetString());
        Assert.Equal(groupId.ToString("D"), claims.GetProperty("group_id").GetString());
        Assert.Equal(tenantId.ToString("D"), claims.GetProperty("tenant_id").GetString());
        Assert.Equal(tenantId.ToString("D"), claims.GetProperty("allowed_tenants")[0].GetString());
        Assert.Equal("BUYER", claims.GetProperty("active_scope").GetString());
        Assert.Equal("BUYER_OPERATOR", claims.GetProperty("roles")[0].GetString());
        Assert.Equal(
            ["demanda:criar", "demanda:listar", "leilao:participar", "pedido:visualizar"],
            claims.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()));
        Assert.Equal(claims.GetProperty("iat").GetInt64() + 900, claims.GetProperty("exp").GetInt64());
    }

    [Theory(Timeout = 60000)]
    [InlineData("basic")]
    [InlineData("json")]
    public async Task CA_NEW06_2_BasicEJson_EmitemToken(string mode)
    {
        await SeedAsync();
        await using var app = CreateTestApp();
        await app.StartAsync();

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/token");
        if (mode == "basic")
        {
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{ClientId}:{ClientSecret}")));
            request.Content = Form(clientId: null, clientSecret: null);
        }
        else
        {
            request.Content = new StringContent(
                JsonSerializer.Serialize(new { grant_type = "client_credentials", client_id = ClientId, client_secret = ClientSecret }),
                Encoding.UTF8, "application/json");
        }

        var response = await app.GetTestClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(string.IsNullOrEmpty((await BodyAsync(response)).GetProperty("access_token").GetString()));
    }

    [Theory(Timeout = 60000)]
    [InlineData("segredo-errado", ClientId, EntityStatus.ACTIVE)]
    [InlineData(ClientSecret, "cliente-inexistente", EntityStatus.ACTIVE)]
    [InlineData(ClientSecret, ClientId, EntityStatus.SUSPENDED)]
    public async Task CA_NEW06_3_CredenciaisInvalidasOuContaSuspensa_Devolvem401Identico(string secret, string clientId, EntityStatus status)
    {
        await SeedAsync(accountStatus: status);
        await using var app = CreateTestApp();
        await app.StartAsync();

        var response = await app.GetTestClient().PostAsync("/api/v1/auth/token", Form(clientId: clientId, clientSecret: secret));
        var body = await BodyAsync(response);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("ERR-AUTH-001", body.GetProperty("code").GetString());
        Assert.Equal("Credenciais inválidas", body.GetProperty("detail").GetString());
    }

    [Theory(Timeout = 60000)]
    [InlineData("password", ClientId, ClientSecret)]
    [InlineData("", ClientId, ClientSecret)]
    [InlineData("client_credentials", null, ClientSecret)]
    [InlineData("client_credentials", ClientId, null)]
    public async Task CA_NEW06_4_GrantOuCredenciaisAusentes_Devolvem400(string grantType, string? clientId, string? secret)
    {
        await SeedAsync();
        await using var app = CreateTestApp();
        await app.StartAsync();

        var response = await app.GetTestClient().PostAsync("/api/v1/auth/token", Form(grantType, clientId, secret));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("ERR-VAL-001", (await BodyAsync(response)).GetProperty("code").GetString());
    }

    [Theory(Timeout = 60000)]
    [InlineData(EntityStatus.SUSPENDED, true, "ERR-CTX-002")]
    [InlineData(EntityStatus.ACTIVE, false, "ERR-CTX-003")]
    public async Task CA_NEW06_5_TenantSuspensoOuEscopoInativo_Devolvem403(EntityStatus tenantStatus, bool scopeActive, string code)
    {
        await SeedAsync(tenantStatus: tenantStatus, scopeActive: scopeActive);
        await using var app = CreateTestApp();
        await app.StartAsync();

        var response = await app.GetTestClient().PostAsync("/api/v1/auth/token", Form());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(code, (await BodyAsync(response)).GetProperty("code").GetString());
    }

    [Fact(Timeout = 60000)]
    public async Task CA_NEW06_6_7_TokenDeServico_RecusadoNoIdentityEAtivoNoIntrospect()
    {
        var (_, _, tenantId) = await SeedAsync();
        await using var app = CreateTestApp();
        await app.StartAsync();
        var client = app.GetTestClient();

        var token = (await BodyAsync(await client.PostAsync("/api/v1/auth/token", Form()))).GetProperty("access_token").GetString()!;

        // RN-NEW06.3: sem fid nem act, os endpoints do próprio Identity recusam o token.
        var switchRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/switch-context")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { view_mode = "SINGLE", target_scope = "BUYER", target_tenant_id = tenantId.ToString("D") }),
                Encoding.UTF8, "application/json")
        };
        switchRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var switchResponse = await client.SendAsync(switchRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, switchResponse.StatusCode);
        Assert.Equal("ERR-AUTH-002", (await BodyAsync(switchResponse)).GetProperty("code").GetString());

        var introspect = await client.PostAsync("/api/v1/auth/introspect", new StringContent(
            JsonSerializer.Serialize(new { token }), Encoding.UTF8, "application/json"));
        var introspected = await BodyAsync(introspect);
        Assert.True(introspected.GetProperty("active").GetBoolean());
        Assert.Equal(tenantId.ToString("D"), introspected.GetProperty("tenant_id").GetString());
    }

    [Fact(Timeout = 60000)]
    public async Task CA_NEW06_8_Emissao_GravaAuditoriaSemUserId()
    {
        var (_, groupId, tenantId) = await SeedAsync();
        await using var app = CreateTestApp();
        await app.StartAsync();

        var response = await app.GetTestClient().PostAsync("/api/v1/auth/token", Form());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var db = _fixture.CreateDbContext();
        var audit = await db.AuthAuditLogs.SingleAsync(l => l.EventType == AuditEventType.SERVICE_TOKEN_ISSUED);
        Assert.Null(audit.UserId);
        Assert.Equal(tenantId, audit.TenantId);
        Assert.Equal(groupId, audit.GroupId);
        Assert.Equal(ClientId, JsonDocument.Parse(audit.Metadata).RootElement.GetProperty("client_id").GetString());
    }

    [Fact(Timeout = 60000)]
    public async Task CA_NEW06_9_Rls_EscondeContasForaDoFluxoDeAutenticacao()
    {
        var (_, _, tenantId) = await SeedAsync();

        await using var conn = new NpgsqlConnection(_fixture.GetConnectionString());
        await conn.OpenAsync();

        async Task<long> CountAsync(string authFlow, string allowedTenants)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "SET ROLE identity_app; " +
                "SELECT set_config('app.auth_flow', @f, false), set_config('app.allowed_tenants', @a, false), set_config('app.write_tenants', '', false); " +
                "SELECT count(*) FROM service_account;";
            cmd.Parameters.AddWithValue("f", authFlow);
            cmd.Parameters.AddWithValue("a", allowedTenants);
            await using var reader = await cmd.ExecuteReaderAsync();
            await reader.NextResultAsync();
            await reader.ReadAsync();
            return reader.GetInt64(0);
        }

        Assert.Equal(0, await CountAsync("false", ""));
        Assert.Equal(0, await CountAsync("false", Guid.NewGuid().ToString("D")));
        Assert.Equal(1, await CountAsync("false", tenantId.ToString("D")));
        Assert.Equal(1, await CountAsync("true", ""));
    }

    // O seed de demonstração tem de aplicar no esquema migrado e o segredo documentado tem de bater com o hash.
    // Corre numa transação revertida: o seed altera papéis partilhados por outros testes.
    [Fact(Timeout = 60000)]
    public async Task SeedDemo_ContasDeServico_AplicamNoEsquemaEConferemComOSegredoDocumentado()
    {
        await _fixture.CleanDatabaseAsync();
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "seed-demo.sql")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        var seed = await File.ReadAllTextAsync(Path.Combine(dir.FullName, "seed-demo.sql"));

        await using var conn = new NpgsqlConnection(_fixture.GetConnectionString());
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await using (var apply = new NpgsqlCommand(seed, conn, tx))
        {
            await apply.ExecuteNonQueryAsync();
        }

        var hashes = new List<string>();
        await using (var query = new NpgsqlCommand(
            "SELECT client_secret_hash FROM service_account WHERE client_id IN ('order-service','catalog-service')", conn, tx))
        await using (var reader = await query.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                hashes.Add(reader.GetString(0));
            }
        }

        await tx.RollbackAsync();

        Assert.Equal(2, hashes.Count);
        Assert.All(hashes, h => Assert.True(_passwordHasher.VerifyPassword("SenhaForte@2026!", h)));
    }
    [Fact]
    public void TokenIssuer_TokenDeServico_NaoAceitaFamiliaNemImpersonation()
    {
        var issuer = new TokenIssuer(_signingKey, _timeProvider, new ConfigurationBuilder().Build());
        var tenantId = Guid.NewGuid();
        AccessTokenClaims Build(Guid? familyId, ActorClaim? act, ViewMode viewMode) => new(
            Guid.NewGuid(), familyId, PlatformRole.NONE, viewMode,
            viewMode == ViewMode.NONE ? null : Guid.NewGuid(),
            viewMode == ViewMode.NONE ? null : tenantId,
            viewMode == ViewMode.NONE ? [] : [tenantId],
            viewMode == ViewMode.NONE ? null : OperationalScope.BUYER,
            [], [], act, ServiceClientId: "order-service");

        Assert.Throws<ArgumentException>(() => issuer.Issue(Build(Guid.NewGuid(), null, ViewMode.SINGLE)));
        Assert.Throws<ArgumentException>(() => issuer.Issue(Build(null, new ActorClaim(Guid.NewGuid(), "SUP-1"), ViewMode.SINGLE)));
        Assert.Throws<ArgumentException>(() => issuer.Issue(Build(null, null, ViewMode.NONE)));
        Assert.NotNull(issuer.Issue(Build(null, null, ViewMode.SINGLE)));
    }
}
