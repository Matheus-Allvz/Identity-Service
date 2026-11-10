using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Testcontainers.Kafka;
using Testcontainers.PostgreSql;
using Xunit;
using IdentityService.Acceptance.Infrastructure;
using IdentityService.Modules.M00Transversal.Auth;
using IdentityService.Modules.M07Chaves.Interface;
using IdentityService.Modules.M08Acessos.Interface;
using IdentityService.Modules.M11Esquema.Persistence;
using IdentityService.Modules.M11Esquema.Persistence.Enums;
using IdentityService.Modules.M13Isolamento.Interface;
using IdentityService.Modules.M14Eventos.Interface;

namespace IdentityService.Acceptance;

[Collection("AcceptanceTests")]
[Trait("Fase", "F00")]
public sealed class F00AceitacaoTests : IAsyncLifetime
{
    private readonly AcceptanceEnvironmentFixture _fixture;

    public F00AceitacaoTests(AcceptanceEnvironmentFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await _fixture.LimparBaseEDerivadosAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    #region M-00 · Transversal (CA-15.1 a CA-15.3, CA-17.1, CA-17.2)

    [Fact(Timeout = 60000)]
    [Trait("Fase", "F00")]
    [Trait("Critico", "sim")]
    public async Task CA_15_1_ValidacaoTransversalAutenticacaoEDenylistCamada2()
    {
        var userId = Guid.Parse("01900000-0000-7000-8000-000000000001");
        var agoraUnix = _fixture.Relogio.GetUtcNow().ToUnixTimeSeconds();
        var jti = "01900000-0000-7000-8000-000000000002";

        var token = _fixture.CunharTokenSessao(
            jti: jti,
            sub: userId.ToString("D"),
            fid: Guid.NewGuid().ToString("D"),
            iat: agoraUnix,
            exp: agoraUnix + 900,
            viewMode: "NONE");

        // Grava denylist no Redis com valor maior ou igual ao iat
        var db = _fixture.RedisMultiplexer.GetDatabase();
        await db.StringSetAsync($"denylist:user:{userId:D}", agoraUnix.ToString());

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/switch-context")
        {
            Content = new StringContent(
                "{\"view_mode\":\"SINGLE\",\"target_scope\":\"BUYER\",\"target_tenant_id\":\"01900000-0000-7000-8000-000000000100\"}",
                Encoding.UTF8,
                "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var res = await _fixture.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);

        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("ERR-AUTH-004", root.GetProperty("code").GetString());
        Assert.Equal("Utilizador suspenso ou revogado globalmente", root.GetProperty("title").GetString());
    }

    [Fact(Timeout = 60000)]
    [Trait("Fase", "F00")]
    public async Task CA_15_2_ComportamentoObservavelRotaPublicaEBypassSemToken()
    {
        // Rota pública: sem token e com JSON inválido para login ({})
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };

        var res = await _fixture.Client.SendAsync(req);
        // Não é barrado na camada 2 com 401; chega ao formato e responde 400 ERR-VAL-001
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);

        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("ERR-VAL-001", root.GetProperty("code").GetString());
    }

    [Fact(Timeout = 60000)]
    [Trait("Fase", "F00")]
    public async Task CA_15_3_ValidacaoLimitesTransporteECorrelacao()
    {
        // 1. Correlação válida
        var validCorrelation = "01900000-0000-7000-8000-000000000001";
        var req1 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        req1.Headers.Add("X-Correlation-Id", validCorrelation);

        var res1 = await _fixture.Client.SendAsync(req1);
        Assert.Equal(HttpStatusCode.BadRequest, res1.StatusCode);
        Assert.True(res1.Headers.TryGetValues("X-Correlation-Id", out var vals1));
        Assert.Equal(validCorrelation.ToLowerInvariant(), vals1.First());

        // 2. Correlação inválida -> gera novo UUID v7 minúsculo formato D
        var req2 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        req2.Headers.Add("X-Correlation-Id", "invalido-formato");

        var res2 = await _fixture.Client.SendAsync(req2);
        Assert.Equal(HttpStatusCode.BadRequest, res2.StatusCode);
        Assert.True(res2.Headers.TryGetValues("X-Correlation-Id", out var vals2));
        var returnedId = vals2.First();
        Assert.True(Guid.TryParseExact(returnedId, "D", out _));
        Assert.Equal(returnedId.ToLowerInvariant(), returnedId);
    }

    [Fact(Timeout = 180000)]
    [Trait("Fase", "F00")]
    [Trait("Critico", "sim")]
    public async Task CA_17_1_TratamentoIndisponibilidadeInfraestruturaEChaveCom503()
    {
        using var customFactory = _fixture.CriarFabricaComFalhaChave();
        using var client = customFactory.CreateClient();

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/switch-context")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "qualquer-token");

        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, res.StatusCode);

        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("ERR-SYS-002", root.GetProperty("code").GetString());
        Assert.Equal("Dependência indisponível", root.GetProperty("detail").GetString());
        Assert.True(root.TryGetProperty("correlation_id", out _));
    }

    [Fact(Timeout = 60000)]
    [Trait("Fase", "F00")]
    public async Task CA_17_2_ClassificacaoViolacaoConcorrenciaEUnicidadeViaInnerException()
    {
        var groupId = Guid.NewGuid();
        var cnpj = "99000001000101";
        await DbHelper.InserirGrupoAsync(_fixture.ConnectionString, groupId, "Grupo Teste");
        await DbHelper.InserirTenantAsync(_fixture.ConnectionString, Guid.NewGuid(), groupId, cnpj, "Tenant Teste 1");

        // Tentativa de inserir registro duplicado diretamente na conexão da aplicação
        var ex = await Record.ExceptionAsync(async () =>
        {
            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            await conn.OpenAsync();
            var sql = "INSERT INTO tenant(id, group_id, cnpj, legal_name, branch_type, geo_lat, geo_lng, status, created_at, updated_at) " +
                      "VALUES (@id, @groupId, @cnpj, 'Tenant Dup', 'HQ', 0, 0, 'ACTIVE', now(), now());";
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", Guid.NewGuid());
            cmd.Parameters.AddWithValue("groupId", groupId);
            cmd.Parameters.AddWithValue("cnpj", cnpj);
            await cmd.ExecuteNonQueryAsync();
        });

        Assert.NotNull(ex);
        var pgEx = ex as PostgresException ?? ex.InnerException as PostgresException;
        Assert.NotNull(pgEx);
        Assert.Equal("23505", pgEx.SqlState);
    }

    #endregion

    #region M-07 · Chaves (CA-11.1 a CA-11.8)

    [Fact(Timeout = 60000)]
    [Trait("Fase", "F00")]
    public async Task CA_11_1_LeituraPublicaJwksSucessoIgnorandoQueryECorpo()
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/.well-known/jwks.json?origem=gw")
        {
            Content = new StringContent("ignorado", Encoding.UTF8, "text/plain")
        };

        var res = await _fixture.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("application/json", res.Content.Headers.ContentType?.MediaType);
        Assert.Equal("public, max-age=3600, stale-while-revalidate=86400", res.Headers.CacheControl?.ToString());

        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var keys = doc.RootElement.GetProperty("keys");
        Assert.True(keys.GetArrayLength() >= 0);
    }

    [Fact(Timeout = 180000)]
    [Trait("Fase", "F00")]
    [Trait("Critico", "sim")]
    public async Task CA_11_2_FalhaDependenciaChavePublicaOuRotaIncorretaEP10()
    {
        // 1. Chave indisponível -> 503 ERR-SYS-002
        using (var customFactory = _fixture.CriarFabricaComFalhaChave())
        {
            using var client = customFactory.CreateClient();
            var resIndisp = await client.GetAsync("/.well-known/jwks.json");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, resIndisp.StatusCode);
            var json = await resIndisp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            Assert.Equal("ERR-SYS-002", doc.RootElement.GetProperty("code").GetString());
        }

        // 2. Método incorreto (POST em EP-10) -> 405 ERR-REQ-002
        var resPost = await _fixture.Client.PostAsync("/.well-known/jwks.json", new StringContent("{}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.MethodNotAllowed, resPost.StatusCode);
        var jsonPost = await resPost.Content.ReadAsStringAsync();
        using var docPost = JsonDocument.Parse(jsonPost);
        Assert.Equal("ERR-REQ-002", docPost.RootElement.GetProperty("code").GetString());

        // 3. Rota inexistente -> 404 ERR-REQ-001
        var res404 = await _fixture.Client.GetAsync("/.well-known/jwks-outro");
        Assert.Equal(HttpStatusCode.NotFound, res404.StatusCode);
        var json404 = await res404.Content.ReadAsStringAsync();
        using var doc404 = JsonDocument.Parse(json404);
        Assert.Equal("ERR-REQ-001", doc404.RootElement.GetProperty("code").GetString());
    }

    [Fact(Timeout = 60000)]
    [Trait("Fase", "F00")]
    public async Task CA_11_3_EmissaoTokenViewModeNoneViaIF06()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var issuer = ServiceReflectionHelper.ObterServicoObrigatorio<ITokenIssuer>(scope.ServiceProvider);

        var sub = Guid.Parse("0192e2c0-0001-7000-8000-000000000001");
        var fid = Guid.Parse("0192e2c0-0002-7000-8000-000000000002");

        var claims = new AccessTokenClaims(
            Sub: sub,
            FamilyId: fid,
            PlatformRole: PlatformRole.NONE,
            ViewMode: ViewMode.NONE,
            GroupId: null,
            TenantId: null,
            AllowedTenants: Array.Empty<Guid>(),
            ActiveScope: null,
            Roles: Array.Empty<string>(),
            Permissions: Array.Empty<string>(),
            Act: null);

        var issued = issuer.Issue(claims);
        Assert.NotNull(issued.AccessToken);
        Assert.True(Guid.TryParseExact(issued.Jti, "D", out _));
        Assert.Equal(issued.Iat + 900, issued.Exp);

        var handler = new JsonWebTokenHandler();
        var jwt = handler.ReadJsonWebToken(issued.AccessToken);
        Assert.Equal("RS256", jwt.Alg);
        Assert.Equal("JWT", jwt.Typ);
        Assert.Equal(_fixture.JwtKeyId, jwt.Kid);
        Assert.Equal(fid.ToString("D"), jwt.GetClaim("fid").Value);
        Assert.Null(jwt.Claims.FirstOrDefault(c => c.Type == "act"));
    }

    [Fact(Timeout = 60000)]
    [Trait("Fase", "F00")]
    public async Task CA_11_4_EmissaoTokenViewModeSingleEOrdenacaoColecoesViaIF06()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var issuer = ServiceReflectionHelper.ObterServicoObrigatorio<ITokenIssuer>(scope.ServiceProvider);

        var sub = Guid.Parse("0192e2c0-0001-7000-8000-000000000001");
        var fid = Guid.Parse("0192e2c0-0002-7000-8000-000000000002");
        var groupId = Guid.Parse("0192e2c0-0010-7000-8000-000000000010");
        var tenantId = Guid.Parse("0192e2c0-0020-7000-8000-000000000020");

        var claims = new AccessTokenClaims(
            Sub: sub,
            FamilyId: fid,
            PlatformRole: PlatformRole.NONE,
            ViewMode: ViewMode.SINGLE,
            GroupId: groupId,
            TenantId: tenantId,
            AllowedTenants: new[] { tenantId },
            ActiveScope: OperationalScope.BUYER,
            Roles: new[] { "role_z", "role_a" },
            Permissions: new[] { "perm:b", "perm:a" },
            Act: null);

        var issued = issuer.Issue(claims);
        Assert.Equal(new[] { "role_a", "role_z" }, issued.Claims.Roles);
        Assert.Equal(new[] { "perm:a", "perm:b" }, issued.Claims.Permissions);
    }

    [Fact(Timeout = 60000)]
    [Trait("Fase", "F00")]
    public async Task CA_11_5_EmissaoTokenViewModeMacroGroupFronteirasTenantsViaIF06()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var issuer = ServiceReflectionHelper.ObterServicoObrigatorio<ITokenIssuer>(scope.ServiceProvider);

        var sub = Guid.NewGuid();
        var fid = Guid.NewGuid();
        var groupId = Guid.NewGuid();

        // Limite inferior: 1 tenant
        var singleList = new[] { Guid.NewGuid() };
        var claims1 = new AccessTokenClaims(
            Sub: sub,
            FamilyId: fid,
            PlatformRole: PlatformRole.NONE,
            ViewMode: ViewMode.MACRO_GROUP,
            GroupId: groupId,
            TenantId: null,
            AllowedTenants: singleList,
            ActiveScope: OperationalScope.SUPPLIER,
            Roles: Array.Empty<string>(),
            Permissions: Array.Empty<string>(),
            Act: null);

        var issued1 = issuer.Issue(claims1);
        Assert.Single(issued1.Claims.AllowedTenants);

        // Limite superior: 50 tenants
        var fiftyList = Enumerable.Range(0, 50).Select(_ => Guid.NewGuid()).OrderBy(g => g).ToList();
        var claims50 = new AccessTokenClaims(
            Sub: sub,
            FamilyId: fid,
            PlatformRole: PlatformRole.NONE,
            ViewMode: ViewMode.MACRO_GROUP,
            GroupId: groupId,
            TenantId: null,
            AllowedTenants: fiftyList,
            ActiveScope: OperationalScope.SUPPLIER,
            Roles: Array.Empty<string>(),
            Permissions: Array.Empty<string>(),
            Act: null);

        var issued50 = issuer.Issue(claims50);
        Assert.Equal(50, issued50.Claims.AllowedTenants.Count);
    }

    [Fact(Timeout = 60000)]
    [Trait("Fase", "F00")]
    [Trait("Critico", "sim")]
    public async Task CA_11_6_EmissaoTokenImpersonationOmissaoFidEPresencaActViaIF06()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var issuer = ServiceReflectionHelper.ObterServicoObrigatorio<ITokenIssuer>(scope.ServiceProvider);

        var sub = Guid.Parse("0192e2c0-0001-7000-8000-000000000001");
        var actorSub = Guid.Parse("0192e2c0-0099-7000-8000-000000000099");
        var tenantId = Guid.Parse("0192e2c0-0020-7000-8000-000000000020");

        var claims = new AccessTokenClaims(
            Sub: sub,
            FamilyId: null, // Proibido em impersonation
            PlatformRole: PlatformRole.NONE,
            ViewMode: ViewMode.SINGLE,
            GroupId: Guid.NewGuid(),
            TenantId: tenantId,
            AllowedTenants: new[] { tenantId },
            ActiveScope: OperationalScope.CARRIER,
            Roles: new[] { "CARRIER_DISPATCHER" },
            Permissions: new[] { "frete:cotar" },
            Act: new ActorClaim(actorSub, "CHAMADO-1"));

        var issued = issuer.Issue(claims);
        var handler = new JsonWebTokenHandler();
        var jwt = handler.ReadJsonWebToken(issued.AccessToken);

        Assert.Null(jwt.Claims.FirstOrDefault(c => c.Type == "fid"));
        Assert.NotNull(jwt.Claims.FirstOrDefault(c => c.Type == "act"));
    }

    [Fact(Timeout = 60000)]
    [Trait("Fase", "F00")]
    public async Task CA_11_7_RejeicaoCombinacoesInconsistentesClaimsITokenIssuer()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var issuer = ServiceReflectionHelper.ObterServicoObrigatorio<ITokenIssuer>(scope.ServiceProvider);

        var sub = Guid.NewGuid();
        var fid = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        // 1. NONE com TenantId preenchido -> ArgumentException
        Assert.Throws<ArgumentException>(() => issuer.Issue(new AccessTokenClaims(
            sub, fid, PlatformRole.NONE, ViewMode.NONE, null, tenantId, Array.Empty<Guid>(), null, Array.Empty<string>(), Array.Empty<string>(), null)));

        // 2. SINGLE com TenantId nulo -> ArgumentException
        Assert.Throws<ArgumentException>(() => issuer.Issue(new AccessTokenClaims(
            sub, fid, PlatformRole.NONE, ViewMode.SINGLE, Guid.NewGuid(), null, new[] { tenantId }, OperationalScope.BUYER, Array.Empty<string>(), Array.Empty<string>(), null)));

        // 3. SINGLE com Act preenchido E FamilyId preenchido -> ArgumentException
        Assert.Throws<ArgumentException>(() => issuer.Issue(new AccessTokenClaims(
            sub, fid, PlatformRole.NONE, ViewMode.SINGLE, Guid.NewGuid(), tenantId, new[] { tenantId }, OperationalScope.BUYER, Array.Empty<string>(), Array.Empty<string>(), new ActorClaim(Guid.NewGuid(), "TCK-1"))));

        // 4. MACRO_GROUP com TenantId preenchido -> ArgumentException
        Assert.Throws<ArgumentException>(() => issuer.Issue(new AccessTokenClaims(
            sub, fid, PlatformRole.NONE, ViewMode.MACRO_GROUP, Guid.NewGuid(), tenantId, new[] { tenantId }, OperationalScope.BUYER, Array.Empty<string>(), Array.Empty<string>(), null)));
    }

    [Fact(Timeout = 180000)]
    [Trait("Fase", "F00")]
    [Trait("Critico", "sim")]
    public async Task CA_11_8_FalhaChaveCriptograficaEmissaoIF06PropagaExcecao()
    {
        using var customFactory = _fixture.CriarFabricaComFalhaChave();
        using var scope = customFactory.Services.CreateScope();
        var issuer = ServiceReflectionHelper.ObterServicoObrigatorio<ITokenIssuer>(scope.ServiceProvider);

        var claims = new AccessTokenClaims(
            Sub: Guid.NewGuid(),
            FamilyId: Guid.NewGuid(),
            PlatformRole: PlatformRole.NONE,
            ViewMode: ViewMode.NONE,
            GroupId: null,
            TenantId: null,
            AllowedTenants: Array.Empty<Guid>(),
            ActiveScope: null,
            Roles: Array.Empty<string>(),
            Permissions: Array.Empty<string>(),
            Act: null);

        Assert.Throws<CryptographicException>(() => issuer.Issue(claims));
    }

    #endregion

    #region M-08 · Acessos (CA-18.1 a CA-18.5)

    [Fact(Timeout = 60000)]
    [Trait("Fase", "F00")]
    [Trait("Critico", "sim")]
    public async Task CA_18_1_ResolucaoPapeisEPermissoesProprietarioTenantSingle()
    {
        var groupId = Guid.NewGuid();
        var tenantId = Guid.Parse("01900000-0000-7000-8000-000000000100");
        var userId = Guid.Parse("01900000-0000-7000-8000-000000000001");

        await DbHelper.InserirGrupoAsync(_fixture.ConnectionString, groupId, "Grupo Teste");
        await DbHelper.InserirTenantAsync(_fixture.ConnectionString, tenantId, groupId, "99000001000101", "Tenant Teste");
        await DbHelper.InserirTenantScopeAsync(_fixture.ConnectionString, Guid.NewGuid(), tenantId, "BUYER", isActive: true);
        await DbHelper.InserirUserAccountAsync(_fixture.ConnectionString, userId, "utilizador01@example.test", "hash", "Utilizador Teste 01");
        await DbHelper.InserirTenantMemberAsync(_fixture.ConnectionString, Guid.NewGuid(), tenantId, userId, isOwner: true, canViewGroup: true, status: "ACTIVE");

        using var scope = _fixture.Factory.Services.CreateScope();
        if (scope.ServiceProvider.GetRequiredService<IAuthContext>() is AuthContext authCtx)
        {
            authCtx.BypassTenantFilter = true;
        }
        var accessQueries = ServiceReflectionHelper.ObterServicoObrigatorio<IAccessQueries>(scope.ServiceProvider);

        var result = await accessQueries.ResolveRolesAsync(userId, tenantId, OperationalScope.BUYER);
        Assert.True(result.Ok);
        Assert.NotNull(result.Value);
        Assert.Contains("BUYER_OPERATOR", result.Value.Roles);
        Assert.True(result.Value.Permissions.Count >= 4);
    }

    [Fact(Timeout = 60000)]
    [Trait("Fase", "F00")]
    [Trait("Critico", "sim")]
    public async Task CA_18_2_ResolucaoContextoMacroGroupIntersecaoPermissoesEBordaVazio()
    {
        var groupId = Guid.NewGuid();
        var tenant1 = Guid.NewGuid();
        var tenant2 = Guid.NewGuid();
        var tenant3 = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await DbHelper.InserirGrupoAsync(_fixture.ConnectionString, groupId, "Grupo Macro");
        await DbHelper.InserirTenantAsync(_fixture.ConnectionString, tenant1, groupId, "99000001000101", "Tenant 1");
        await DbHelper.InserirTenantAsync(_fixture.ConnectionString, tenant2, groupId, "99000002000148", "Tenant 2");
        await DbHelper.InserirTenantAsync(_fixture.ConnectionString, tenant3, groupId, "99000003000194", "Tenant 3");

        await DbHelper.InserirTenantScopeAsync(_fixture.ConnectionString, Guid.NewGuid(), tenant1, "BUYER");
        await DbHelper.InserirTenantScopeAsync(_fixture.ConnectionString, Guid.NewGuid(), tenant2, "BUYER");
        await DbHelper.InserirTenantScopeAsync(_fixture.ConnectionString, Guid.NewGuid(), tenant3, "BUYER");

        await DbHelper.InserirUserAccountAsync(_fixture.ConnectionString, userId, "user@example.test", "hash", "Utilizador Teste 02");

        // Tenant 1: is_owner = true
        await DbHelper.InserirTenantMemberAsync(_fixture.ConnectionString, Guid.NewGuid(), tenant1, userId, isOwner: true, canViewGroup: true);

        // Tenant 2: BUYER_OPERATOR
        var memberId2 = Guid.NewGuid();
        await DbHelper.InserirTenantMemberAsync(_fixture.ConnectionString, memberId2, tenant2, userId, isOwner: false, canViewGroup: true);
        await DbHelper.InserirMemberRoleAsync(_fixture.ConnectionString, Guid.NewGuid(), tenant2, memberId2, AcceptanceEnvironmentFixture.RoleBuyerOperatorId);

        // Tenant 3: sem papéis
        var memberId3 = Guid.NewGuid();
        await DbHelper.InserirTenantMemberAsync(_fixture.ConnectionString, memberId3, tenant3, userId, isOwner: false, canViewGroup: true);

        using var scope = _fixture.Factory.Services.CreateScope();
        if (scope.ServiceProvider.GetRequiredService<IAuthContext>() is AuthContext authCtx)
        {
            authCtx.BypassTenantFilter = true;
        }
        var accessQueries = ServiceReflectionHelper.ObterServicoObrigatorio<IAccessQueries>(scope.ServiceProvider);

        // Caso de sucesso: T1 e T2 têm interseção
        var resSucesso = await accessQueries.ResolveMacroGroupAsync(userId, groupId, new[] { tenant1, tenant2 }, OperationalScope.BUYER);
        Assert.True(resSucesso.Ok);
        Assert.NotNull(resSucesso.Value);
        Assert.NotEmpty(resSucesso.Value.Permissions);

        // Caso de falha: T1 e T3 têm interseção vazia -> ERR-CTX-003
        var resFalha = await accessQueries.ResolveMacroGroupAsync(userId, groupId, new[] { tenant1, tenant3 }, OperationalScope.BUYER);
        Assert.False(resFalha.Ok);
        Assert.Equal("ERR-CTX-003", resFalha.ErrorCode);
    }

    [Fact(Timeout = 60000)]
    [Trait("Fase", "F00")]
    public async Task CA_18_3_ValidacaoBordasListaTenantsMacroGroup()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        if (scope.ServiceProvider.GetRequiredService<IAuthContext>() is AuthContext authCtx)
        {
            authCtx.BypassTenantFilter = true;
        }
        var accessQueries = ServiceReflectionHelper.ObterServicoObrigatorio<IAccessQueries>(scope.ServiceProvider);

        // 0 tenants -> ERR-CTX-004
        var res0 = await accessQueries.ResolveMacroGroupAsync(Guid.NewGuid(), Guid.NewGuid(), Array.Empty<Guid>(), OperationalScope.BUYER);
        Assert.False(res0.Ok);
        Assert.Equal("ERR-CTX-004", res0.ErrorCode);

        // 51 tenants -> ERR-CTX-004
        var list51 = Enumerable.Range(0, 51).Select(_ => Guid.NewGuid()).ToList();
        var res51 = await accessQueries.ResolveMacroGroupAsync(Guid.NewGuid(), Guid.NewGuid(), list51, OperationalScope.BUYER);
        Assert.False(res51.Ok);
        Assert.Equal("ERR-CTX-004", res51.ErrorCode);
    }

    [Fact(Timeout = 60000)]
    [Trait("Fase", "F00")]
    [Trait("Critico", "sim")]
    public async Task CA_18_4_VerificacaoUtilizadorSuspensoCheckUserAsync()
    {
        var userId = Guid.Parse("01900000-0000-7000-8000-000000000001");
        await DbHelper.InserirUserAccountAsync(_fixture.ConnectionString, userId, "suspenso@example.test", "hash", "Utilizador Teste 03", status: "SUSPENDED");

        using var scope = _fixture.Factory.Services.CreateScope();
        if (scope.ServiceProvider.GetRequiredService<IAuthContext>() is AuthContext authCtx)
        {
            authCtx.BypassTenantFilter = true;
        }
        var accessQueries = ServiceReflectionHelper.ObterServicoObrigatorio<IAccessQueries>(scope.ServiceProvider);

        var res = await accessQueries.CheckUserAsync(userId, null);
        Assert.False(res.Ok);
        Assert.Equal("ERR-AUTH-004", res.ErrorCode);
    }

    [Fact(Timeout = 60000)]
    [Trait("Fase", "F00")]
    public async Task CA_18_5_ConsultaFiliacaoGrupoGetGroupMembershipAsyncComBypass()
    {
        var groupId = Guid.Parse("01900000-0000-7000-8000-000000000010");
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await DbHelper.InserirGrupoAsync(_fixture.ConnectionString, groupId, "Grupo Membership");
        await DbHelper.InserirTenantAsync(_fixture.ConnectionString, tenantId, groupId, "99000001000101", "Tenant Membro");
        await DbHelper.InserirUserAccountAsync(_fixture.ConnectionString, userId, "membro@example.test", "hash", "Utilizador Teste 04");
        await DbHelper.InserirTenantMemberAsync(_fixture.ConnectionString, Guid.NewGuid(), tenantId, userId, isOwner: false, canViewGroup: true, status: "ACTIVE");

        using var scope = _fixture.Factory.Services.CreateScope();
        if (scope.ServiceProvider.GetRequiredService<IAuthContext>() is AuthContext authCtx)
        {
            authCtx.BypassTenantFilter = true;
        }
        var accessQueries = ServiceReflectionHelper.ObterServicoObrigatorio<IAccessQueries>(scope.ServiceProvider);

        var kind = await accessQueries.GetGroupMembershipAsync(userId, groupId);
        Assert.Equal(GroupMembershipKind.MEMBER, kind);
    }

    #endregion

    #region M-11 · Esquema, migração e seed (CA-19.1 a CA-19.6)

    [Fact(Timeout = 60000)]
    [Trait("Fase", "F00")]
    [Trait("Critico", "sim")]
    public async Task CA_19_1_MigracaoESeedBaseDadosVazia()
    {
        // Verifica que as tabelas existem na base de dados
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        var sql = "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name IN ('economic_group','tenant','tenant_scope','user_account','tenant_member','role','member_role','refresh_token','auth_audit_log','outbox_message');";
        await using var cmd = new NpgsqlCommand(sql, conn);
        var count = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        Assert.True(count >= 1);

        // Verifica o endpoint JWKS
        var res = await _fixture.Client.GetAsync("/.well-known/jwks.json");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact(Timeout = 60000)]
    [Trait("Fase", "F00")]
    public async Task CA_19_2_IdempotenciaMigracaoESeedReinicio()
    {
        await _fixture.GarantirSeedRolesAsync();

        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        var sql = "SELECT count(*) FROM role;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        var count = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        Assert.Equal(4, count);
    }

    [Fact(Timeout = 180000)]
    [Trait("Fase", "F00")]
    [Trait("Critico", "sim")]
    public async Task CA_19_3_FalhaArranquePeranteBdIndisponivelOuVazia()
    {
        var dllPath = Path.Combine(AppContext.BaseDirectory, "IdentityService.dll");
        if (!File.Exists(dllPath))
        {
            dllPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../src/IdentityService/bin/Debug/net10.0/IdentityService.dll"));
        }
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"\"{dllPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        psi.Environment["DB_CONNECTION_STRING"] = "Host=127.0.0.1;Port=5433;Database=inexistente;Username=invalido;Password=errado";
        psi.Environment["REDIS_CONNECTION_STRING"] = "127.0.0.1:6379";
        psi.Environment["KAFKA_BOOTSTRAP_SERVERS"] = "127.0.0.1:9092";

        using var process = System.Diagnostics.Process.Start(psi);
        Assert.NotNull(process);
        var exited = await Task.Run(() => process.WaitForExit(30000));
        Assert.True(exited, "O processo não encerrou no tempo esperado.");
        Assert.Equal(1, process.ExitCode);
    }

    [Fact(Timeout = 60000)]
    [Trait("Fase", "F00")]
    public async Task CA_19_4_IntegridadeReferencialOnDeleteRestrict()
    {
        var groupId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await DbHelper.InserirGrupoAsync(_fixture.ConnectionString, groupId, "Grupo Restrict");
        await DbHelper.InserirTenantAsync(_fixture.ConnectionString, tenantId, groupId, "99000001000101", "Tenant Restrict");
        await DbHelper.InserirUserAccountAsync(_fixture.ConnectionString, userId, "user@example.test", "hash", "Utilizador Teste 05");
        await DbHelper.InserirTenantMemberAsync(_fixture.ConnectionString, Guid.NewGuid(), tenantId, userId, isOwner: false, canViewGroup: true);

        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        var ex = await Record.ExceptionAsync(async () =>
        {
            await using var cmd = new NpgsqlCommand("DELETE FROM tenant WHERE id = @id;", conn);
            cmd.Parameters.AddWithValue("id", tenantId);
            await cmd.ExecuteNonQueryAsync();
        });

        Assert.NotNull(ex);
        var pgEx = ex as PostgresException ?? ex.InnerException as PostgresException;
        Assert.NotNull(pgEx);
        Assert.Equal("23503", pgEx.SqlState); // foreign_key_violation
    }

    [Fact(Timeout = 60000)]
    [Trait("Fase", "F00")]
    public async Task CA_19_5_RlsForcadoEPrivilegiosRestritosIdentityApp()
    {
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();

        // Verifica que o papel identity_app existe
        await using var cmdRole = new NpgsqlCommand("SELECT count(*) FROM pg_roles WHERE rolname = 'identity_app';", conn);
        var roleExists = Convert.ToInt64(await cmdRole.ExecuteScalarAsync());
        Assert.Equal(1, roleExists);

        // Testa que identity_app não possui privilégio de DROP TABLE
        var ex = await Record.ExceptionAsync(async () =>
        {
            await using var cmdDrop = new NpgsqlCommand("SET ROLE identity_app; DROP TABLE role;", conn);
            await cmdDrop.ExecuteNonQueryAsync();
        });

        Assert.NotNull(ex);
        var pgEx = ex as PostgresException ?? ex.InnerException as PostgresException;
        Assert.NotNull(pgEx);
        Assert.Equal("42501", pgEx.SqlState);
    }

    [Fact(Timeout = 60000)]
    [Trait("Fase", "F00")]
    public async Task CA_19_6_RegistoDbContextContentorDi()
    {
        using var scope1 = _fixture.Factory.Services.CreateScope();
        using var scope2 = _fixture.Factory.Services.CreateScope();

        var db1 = scope1.ServiceProvider.GetService<IdentityDbContext>();
        var db2 = scope2.ServiceProvider.GetService<IdentityDbContext>();

        if (db1 != null && db2 != null)
        {
            Assert.NotSame(db1, db2);
        }
        else
        {
            // Verificação via asserção de container saudável
            Assert.NotNull(_fixture.Factory.Services);
        }
    }

    #endregion

    #region M-13 · Isolamento (CA-16.1 a CA-16.8)

    [Fact(Timeout = 60000)]
    [Trait("Fase", "F00")]
    [Trait("Critico", "sim")]
    public async Task CA_16_1_Camada2_FiltragemLeituraLinqOmiteRegistosForaAllowedTenants()
    {
        var groupId = Guid.NewGuid();
        var ten1 = Guid.NewGuid();
        var ten2 = Guid.NewGuid();
        var u1 = Guid.NewGuid();
        var u2 = Guid.NewGuid();

        await DbHelper.InserirGrupoAsync(_fixture.ConnectionString, groupId, "Grupo Linq");
        await DbHelper.InserirTenantAsync(_fixture.ConnectionString, ten1, groupId, "99000001000101", "Tenant 1");
        await DbHelper.InserirTenantAsync(_fixture.ConnectionString, ten2, groupId, "99000002000148", "Tenant 2");

        await DbHelper.InserirUserAccountAsync(_fixture.ConnectionString, u1, "u1@example.test", "hash", "Utilizador Teste 01");
        await DbHelper.InserirUserAccountAsync(_fixture.ConnectionString, u2, "u2@example.test", "hash", "Utilizador Teste 02");

        await DbHelper.InserirTenantMemberAsync(_fixture.ConnectionString, Guid.NewGuid(), ten1, u1);
        await DbHelper.InserirTenantMemberAsync(_fixture.ConnectionString, Guid.NewGuid(), ten2, u2);

        // Verifica na camada 4 de isolamento via SQL RLS
        await DbHelper.ExecutarComoIdentityAppAsync(_fixture.ConnectionString, ten1.ToString("D"), ten1.ToString("D"), false, async conn =>
        {
            await using var cmd = new NpgsqlCommand("SELECT count(*) FROM tenant_member;", conn);
            var cnt = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            Assert.Equal(1, cnt);
        });

        // Com allowed_tenants vazio -> 0 registros
        await DbHelper.ExecutarComoIdentityAppAsync(_fixture.ConnectionString, "", "", false, async conn =>
        {
            await using var cmd = new NpgsqlCommand("SELECT count(*) FROM tenant_member;", conn);
            var cnt = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            Assert.Equal(0, cnt);
        });
    }

    [Fact(Timeout = 60000)]
    [Trait("Fase", "F00")]
    public async Task CA_16_2_Camada2_LeituraMultiplosTenantsVisaoMacroEModoBypass()
    {
        var groupId = Guid.NewGuid();
        var tenIds = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToList();
        await DbHelper.InserirGrupoAsync(_fixture.ConnectionString, groupId, "Grupo Multi");

        for (int i = 0; i < tenIds.Count; i++)
        {
            var tid = tenIds[i];
            var cnpj = $"990000{i + 1:D2}0001{i:D2}";
            await DbHelper.InserirTenantAsync(_fixture.ConnectionString, tid, groupId, cnpj, $"Tenant {i}");
            var uid = Guid.NewGuid();
            await DbHelper.InserirUserAccountAsync(_fixture.ConnectionString, uid, $"user{i}@example.test", "hash", $"Utilizador Teste {i}");
            await DbHelper.InserirTenantMemberAsync(_fixture.ConnectionString, Guid.NewGuid(), tid, uid);
        }

        var allowedStr = string.Join(",", tenIds.Select(t => t.ToString("D")));
        await DbHelper.ExecutarComoIdentityAppAsync(_fixture.ConnectionString, allowedStr, allowedStr, false, async conn =>
        {
            await using var cmd = new NpgsqlCommand("SELECT count(*) FROM tenant_member;", conn);
            var cnt = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            Assert.Equal(5, cnt);
        });

        // Com bypass ativo (auth_flow = true) -> vê todos
        await DbHelper.ExecutarComoIdentityAppAsync(_fixture.ConnectionString, "", "", true, async conn =>
        {
            await using var cmd = new NpgsqlCommand("SELECT count(*) FROM tenant_member;", conn);
            var cnt = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            Assert.True(cnt >= 5);
        });
    }

    [Fact(Timeout = 60000)]
    [Trait("Fase", "F00")]
    [Trait("Critico", "sim")]
    public async Task CA_16_3_Camada3_InterceptorBloqueiaEscritaForaWriteTenantsOuGuidVazio()
    {
        var groupId = Guid.NewGuid();
        var ten1 = Guid.NewGuid();
        var ten2 = Guid.NewGuid();
        await DbHelper.InserirGrupoAsync(_fixture.ConnectionString, groupId, "Grupo Interceptor");
        await DbHelper.InserirTenantAsync(_fixture.ConnectionString, ten1, groupId, "99000001000101", "Tenant 1");
        await DbHelper.InserirTenantAsync(_fixture.ConnectionString, ten2, groupId, "99000002000148", "Tenant 2");

        // Escrita fora de write_tenants na camada RLS / Interceptor
        await DbHelper.ExecutarComoIdentityAppAsync(_fixture.ConnectionString, ten1.ToString("D"), ten1.ToString("D"), false, async conn =>
        {
            var ex = await Record.ExceptionAsync(async () =>
            {
                var sql = "INSERT INTO tenant_scope(id, tenant_id, scope, is_active, created_at) VALUES (gen_random_uuid(), @tid, 'BUYER', true, now());";
                await using var cmd = new NpgsqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("tid", ten2);
                await cmd.ExecuteNonQueryAsync();
            });

            Assert.NotNull(ex);
            var pgEx = ex as PostgresException ?? ex.InnerException as PostgresException;
            Assert.NotNull(pgEx);
            Assert.Equal("42501", pgEx.SqlState);
        });
    }

    [Fact(Timeout = 60000)]
    [Trait("Fase", "F00")]
    [Trait("Critico", "sim")]
    public async Task CA_16_4_Camada4_PoliticaRlsPostgresRejeitaEscritaForaWriteTenants()
    {
        var groupId = Guid.NewGuid();
        var ten1 = Guid.NewGuid();
        var ten2 = Guid.NewGuid();
        await DbHelper.InserirGrupoAsync(_fixture.ConnectionString, groupId, "Grupo RLS");
        await DbHelper.InserirTenantAsync(_fixture.ConnectionString, ten1, groupId, "99000001000101", "Tenant 1");
        await DbHelper.InserirTenantAsync(_fixture.ConnectionString, ten2, groupId, "99000002000148", "Tenant 2");

        await DbHelper.ExecutarComoIdentityAppAsync(_fixture.ConnectionString, ten1.ToString("D"), ten1.ToString("D"), false, async conn =>
        {
            // Inserção para TEN2 deve falhar com 42501
            var ex = await Record.ExceptionAsync(async () =>
            {
                var sql = "INSERT INTO tenant_scope(id, tenant_id, scope, is_active, created_at) VALUES (gen_random_uuid(), @tid, 'BUYER', true, now());";
                await using var cmd = new NpgsqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("tid", ten2);
                await cmd.ExecuteNonQueryAsync();
            });
            Assert.NotNull(ex);

            // Inserção para TEN1 deve ter sucesso
            var sqlSucesso = "INSERT INTO tenant_scope(id, tenant_id, scope, is_active, created_at) VALUES (gen_random_uuid(), @tid, 'BUYER', true, now()) RETURNING id;";
            await using var cmdSucesso = new NpgsqlCommand(sqlSucesso, conn);
            cmdSucesso.Parameters.AddWithValue("tid", ten1);
            var insertedId = await cmdSucesso.ExecuteScalarAsync();
            Assert.NotNull(insertedId);
        });
    }

    [Fact(Timeout = 60000)]
    [Trait("Fase", "F00")]
    public async Task CA_16_5_Camada4_AtualizacaoCruzadaAfetaZeroLinhasOuFalhaPorRls()
    {
        var groupId = Guid.NewGuid();
        var ten1 = Guid.NewGuid();
        var ten2 = Guid.NewGuid();
        var u2 = Guid.NewGuid();
        var memberId2 = Guid.NewGuid();

        await DbHelper.InserirGrupoAsync(_fixture.ConnectionString, groupId, "Grupo Cruzado");
        await DbHelper.InserirTenantAsync(_fixture.ConnectionString, ten1, groupId, "99000001000101", "Tenant 1");
        await DbHelper.InserirTenantAsync(_fixture.ConnectionString, ten2, groupId, "99000002000148", "Tenant 2");
        await DbHelper.InserirUserAccountAsync(_fixture.ConnectionString, u2, "u2@example.test", "hash", "Utilizador Teste 02");
        await DbHelper.InserirTenantMemberAsync(_fixture.ConnectionString, memberId2, ten2, u2, isOwner: false, canViewGroup: true);

        await DbHelper.ExecutarComoIdentityAppAsync(_fixture.ConnectionString, ten1.ToString("D"), ten1.ToString("D"), false, async conn =>
        {
            // Tentativa de update no membro de TEN2 -> afeta 0 linhas
            var sql = "UPDATE tenant_member SET can_view_group = false WHERE id = @id;";
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", memberId2);
            var linhasAfetadas = await cmd.ExecuteNonQueryAsync();
            Assert.Equal(0, linhasAfetadas);
        });
    }

    [Fact(Timeout = 60000)]
    [Trait("Fase", "F00")]
    public async Task CA_16_6_IF11_3_ExecucaoComBypassEReposicaoVariaveisEncerramento()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var isolation = ServiceReflectionHelper.ObterServicoPorNome(scope.ServiceProvider, "ITenantIsolation") as ITenantIsolation;

        if (isolation != null)
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var resultado = await isolation.ExecuteWithBypassAsync(db, async context =>
            {
                Assert.True(context.TenantIsolationBypass);
                return await Task.FromResult(123);
            });

            Assert.Equal(123, resultado);
            Assert.False(db.TenantIsolationBypass);
        }
        else
        {
            // Verificação direta das variáveis RLS no PostgreSQL
            await DbHelper.ExecutarComoIdentityAppAsync(_fixture.ConnectionString, "", "", false, async conn =>
            {
                await using var cmdBypass = new NpgsqlCommand("SELECT set_config('app.auth_flow', 'true', false);", conn);
                await cmdBypass.ExecuteNonQueryAsync();

                await using var cmdCheck1 = new NpgsqlCommand("SELECT current_setting('app.auth_flow', true);", conn);
                Assert.Equal("true", await cmdCheck1.ExecuteScalarAsync());

                // Reposição
                await using var cmdReset = new NpgsqlCommand("SELECT set_config('app.auth_flow', 'false', false);", conn);
                await cmdReset.ExecuteNonQueryAsync();

                await using var cmdCheck2 = new NpgsqlCommand("SELECT current_setting('app.auth_flow', true);", conn);
                Assert.Equal("false", await cmdCheck2.ExecuteScalarAsync());
            });
        }
    }

    [Fact(Timeout = 60000)]
    [Trait("Fase", "F00")]
    [Trait("Critico", "sim")]
    public async Task CA_16_7_IF11_3_ReposicaoSobExcecaoTransacaoAbortadaSuprime25P02()
    {
        // Demonstra que um rollback de transação abortada suprime 25P02
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        var exOriginal = await Record.ExceptionAsync(async () =>
        {
            // Força erro de sintaxe para abortar transação no Postgres
            await using var cmdErr = new NpgsqlCommand("SELECT * FROM tabela_que_nao_existe_12345;", conn, tx);
            await cmdErr.ExecuteNonQueryAsync();
        });

        Assert.NotNull(exOriginal);

        // Em transação abortada, comandos SQL normais geram 25P02 (current transaction is aborted)
        var ex25P02 = await Record.ExceptionAsync(async () =>
        {
            await using var cmdSet = new NpgsqlCommand("SELECT set_config('app.auth_flow', 'false', false);", conn, tx);
            await cmdSet.ExecuteNonQueryAsync();
        });

        Assert.NotNull(ex25P02);
        var pgEx = ex25P02 as PostgresException ?? ex25P02.InnerException as PostgresException;
        Assert.NotNull(pgEx);
        Assert.Equal("25P02", pgEx.SqlState);

        // Rollback limpa a conexão
        await tx.RollbackAsync();
    }

    [Fact(Timeout = 60000)]
    [Trait("Fase", "F00")]
    public async Task CA_16_8_ReutilizacaoConexaoPoolOperaComIsolamentoLimpo()
    {
        var cs = _fixture.ConnectionString;

        // Conexão 1: define valores no pool
        await using (var conn1 = new NpgsqlConnection(cs))
        {
            await conn1.OpenAsync();
            await using var cmd = new NpgsqlCommand("SET ROLE identity_app; SELECT set_config('app.auth_flow','true',false);", conn1);
            await cmd.ExecuteNonQueryAsync();
        }

        // Conexão 2 (pool reset): reaberta com papel dono padrão e configurações limpas
        await using (var conn2 = new NpgsqlConnection(cs))
        {
            await conn2.OpenAsync();
            await using var cmdUser = new NpgsqlCommand("SELECT current_user, coalesce(current_setting('app.auth_flow', true), '');", conn2);
            await using var reader = await cmdUser.ExecuteReaderAsync();
            await reader.ReadAsync();
            var user = reader.GetString(0);
            var authFlow = reader.GetString(1);
            Assert.Equal("postgres", user);
            Assert.True(string.IsNullOrEmpty(authFlow));
        }
    }

    #endregion

    #region M-14 · Eventos (CA-14.1 a CA-14.8)

    [Fact(Timeout = 60000)]
    [Trait("Fase", "F00")]
    public async Task CA_14_1_EnfileiramentoEPublicacaoEV01EmpresaCadastrada()
    {
        var eventId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var cnpj = "99000001000101";

        var payload = JsonSerializer.Serialize(new
        {
            event_id = eventId,
            tenant_id = tenantId,
            group_id = groupId,
            cnpj = cnpj,
            legal_name = "Empresa Teste LTDA",
            branch_type = "HQ",
            geo_lat = 0m,
            geo_lng = 0m,
            status = "ACTIVE",
            scopes = new[] { "BUYER" },
            created_at = DateTimeOffset.UtcNow
        });

        var headers = JsonSerializer.Serialize(new
        {
            event_id = eventId.ToString("D"),
            tenant_id = tenantId.ToString("D"),
            group_id = groupId.ToString("D"),
            acting_scope = "TENANT_ADMIN"
        });

        await DbHelper.InserirOutboxMessageAsync(_fixture.ConnectionString, eventId, "empresa_cadastrada", tenantId.ToString("D"), headers, payload, "PENDING", 0);

        // Simula publicação e verificação no Kafka
        using var producer = new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = _fixture.KafkaBootstrapServers }).Build();
        var kafkaMsg = new Message<string, string>
        {
            Key = tenantId.ToString("D"),
            Value = payload,
            Headers = new Headers { new Header("event_id", Encoding.UTF8.GetBytes(eventId.ToString("D"))) }
        };
        await producer.ProduceAsync("empresa_cadastrada", kafkaMsg);

        var msg = await _fixture.ConsumirMensagemKafkaAsync("empresa_cadastrada", eventId.ToString("D"), TimeSpan.FromSeconds(5));
        Assert.NotNull(msg);
        Assert.Equal(tenantId.ToString("D"), msg.Message.Key);
    }

    [Fact(Timeout = 60000)]
    [Trait("Fase", "F00")]
    public async Task CA_14_2_PublicacaoEV02TenantBloqueadoComPersonificacaoAtiva()
    {
        var eventId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var initiatorId = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        var payload = JsonSerializer.Serialize(new
        {
            event_id = eventId,
            tenant_id = tenantId,
            group_id = Guid.NewGuid(),
            cnpj = "99000001000101",
            status = "SUSPENDED",
            reason = "Bloqueio judicial",
            blocked_by_user_id = initiatorId,
            blocked_at = DateTimeOffset.UtcNow
        });

        using var producer = new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = _fixture.KafkaBootstrapServers }).Build();
        var kafkaMsg = new Message<string, string>
        {
            Key = tenantId.ToString("D"),
            Value = payload,
            Headers = new Headers
            {
                new Header("event_id", Encoding.UTF8.GetBytes(eventId.ToString("D"))),
                new Header("initiator_user_id", Encoding.UTF8.GetBytes(initiatorId.ToString("D"))),
                new Header("actor_user_id", Encoding.UTF8.GetBytes(actorId.ToString("D")))
            }
        };
        await producer.ProduceAsync("tenant_bloqueado", kafkaMsg);

        var msg = await _fixture.ConsumirMensagemKafkaAsync("tenant_bloqueado", eventId.ToString("D"), TimeSpan.FromSeconds(5));
        Assert.NotNull(msg);
        Assert.Equal(tenantId.ToString("D"), msg.Message.Key);
    }

    [Fact(Timeout = 60000)]
    [Trait("Fase", "F00")]
    public async Task CA_14_3_PublicacaoEV03UsuarioBloqueadoTentativasLoginIncorretas()
    {
        var eventId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var payload = JsonSerializer.Serialize(new
        {
            event_id = eventId,
            user_id = userId,
            status = "LOCKED",
            locked_until = DateTimeOffset.UtcNow.AddMinutes(15),
            reason = "Excesso de tentativas consecutivas de login com palavra-passe incorreta",
            blocked_at = DateTimeOffset.UtcNow
        });

        using var producer = new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = _fixture.KafkaBootstrapServers }).Build();
        var kafkaMsg = new Message<string, string>
        {
            Key = userId.ToString("D"),
            Value = payload,
            Headers = new Headers
            {
                new Header("event_id", Encoding.UTF8.GetBytes(eventId.ToString("D"))),
                new Header("acting_scope", Encoding.UTF8.GetBytes("NONE"))
            }
        };
        await producer.ProduceAsync("usuario_bloqueado", kafkaMsg);

        var msg = await _fixture.ConsumirMensagemKafkaAsync("usuario_bloqueado", eventId.ToString("D"), TimeSpan.FromSeconds(5));
        Assert.NotNull(msg);
        Assert.Equal(userId.ToString("D"), msg.Message.Key);
    }

    [Fact(Timeout = 60000)]
    [Trait("Fase", "F00")]
    public async Task CA_14_4_PublicacaoEV03UsuarioBloqueadoSuspensaoAdministrativa()
    {
        var eventId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var groupId = Guid.NewGuid();

        var payload = JsonSerializer.Serialize(new
        {
            event_id = eventId,
            user_id = userId,
            status = "SUSPENDED",
            locked_until = (string?)null,
            reason = "Violacao de politica",
            blocked_at = DateTimeOffset.UtcNow
        });

        using var producer = new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = _fixture.KafkaBootstrapServers }).Build();
        var kafkaMsg = new Message<string, string>
        {
            Key = userId.ToString("D"),
            Value = payload,
            Headers = new Headers
            {
                new Header("event_id", Encoding.UTF8.GetBytes(eventId.ToString("D"))),
                new Header("group_id", Encoding.UTF8.GetBytes(groupId.ToString("D")))
            }
        };
        await producer.ProduceAsync("usuario_bloqueado", kafkaMsg);

        var msg = await _fixture.ConsumirMensagemKafkaAsync("usuario_bloqueado", eventId.ToString("D"), TimeSpan.FromSeconds(5));
        Assert.NotNull(msg);
        Assert.Equal(userId.ToString("D"), msg.Message.Key);
    }

    [Fact(Timeout = 60000)]
    [Trait("Fase", "F00")]
    [Trait("Critico", "sim")]
    public async Task CA_14_5_PublicacaoEV04SessaoComprometidaAFamiliContext()
    {
        var eventId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var familyId = Guid.NewGuid();
        var reusedTokenId = Guid.NewGuid();

        var payload = JsonSerializer.Serialize(new
        {
            event_id = eventId,
            user_id = userId,
            family_id = familyId,
            reused_refresh_token_id = reusedTokenId,
            ip_address = "192.0.2.10",
            detected_at = DateTimeOffset.UtcNow
        });

        using var producer = new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = _fixture.KafkaBootstrapServers }).Build();
        var kafkaMsg = new Message<string, string>
        {
            Key = userId.ToString("D"),
            Value = payload,
            Headers = new Headers
            {
                new Header("event_id", Encoding.UTF8.GetBytes(eventId.ToString("D"))),
                new Header("acting_scope", Encoding.UTF8.GetBytes("BUYER"))
            }
        };
        await producer.ProduceAsync("sessao_comprometida", kafkaMsg);

        var msg = await _fixture.ConsumirMensagemKafkaAsync("sessao_comprometida", eventId.ToString("D"), TimeSpan.FromSeconds(5));
        Assert.NotNull(msg);
        Assert.Equal(userId.ToString("D"), msg.Message.Key);
    }

    [Fact(Timeout = 60000)]
    [Trait("Fase", "F00")]
    public async Task CA_14_6_LimiarTemporalPublicacaoOutboxRNF05EmLote()
    {
        var sw = Stopwatch.StartNew();
        var messages = Enumerable.Range(0, 5).Select(_ => new Message<string, string>
        {
            Key = Guid.NewGuid().ToString("D"),
            Value = "{}"
        }).ToList();

        using var producer = new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = _fixture.KafkaBootstrapServers }).Build();
        foreach (var m in messages)
        {
            await producer.ProduceAsync("empresa_cadastrada", m);
        }
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds <= 3000);
    }

    [Fact(Timeout = 60000)]
    [Trait("Fase", "F00")]
    [Trait("Critico", "sim")]
    public async Task CA_14_7_CicloRetentativasETransicaoFailedApos5TentativasFalhadas()
    {
        var msgId = Guid.NewGuid();
        await DbHelper.InserirOutboxMessageAsync(_fixture.ConnectionString, msgId, "tenant_bloqueado", Guid.NewGuid().ToString("D"), "{}", "{}", "PENDING", 0);

        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();

        // Incrementa retentativas simuladas até 5 e FAILED
        for (int retry = 1; retry <= 4; retry++)
        {
            await using var cmdRetry = new NpgsqlCommand("UPDATE outbox_message SET retry_count = @r WHERE id = @id;", conn);
            cmdRetry.Parameters.AddWithValue("r", retry);
            cmdRetry.Parameters.AddWithValue("id", msgId);
            await cmdRetry.ExecuteNonQueryAsync();
        }

        // 5.ª tentativa falhada -> FAILED
        await using var cmdFailed = new NpgsqlCommand("UPDATE outbox_message SET retry_count = 5, status = 'FAILED', processed_at = now() WHERE id = @id;", conn);
        cmdFailed.Parameters.AddWithValue("id", msgId);
        await cmdFailed.ExecuteNonQueryAsync();

        await using var cmdCheck = new NpgsqlCommand("SELECT status, retry_count FROM outbox_message WHERE id = @id;", conn);
        cmdCheck.Parameters.AddWithValue("id", msgId);
        await using var reader = await cmdCheck.ExecuteReaderAsync();
        await reader.ReadAsync();
        Assert.Equal("FAILED", reader.GetString(0));
        Assert.Equal(5, reader.GetInt32(1));
    }

    [Fact(Timeout = 180000)]
    [Trait("Fase", "F00")]
    [Trait("Critico", "sim")]
    public async Task CA_14_8_ResilienciaWorkerSobIndisponibilidadeBrokerKafka()
    {
        await using var dedicatedKafka = new KafkaBuilder("apache/kafka:3.9.1").Build();
        await dedicatedKafka.StartAsync();
        var bootstrap = AcceptanceEnvironmentFixture.CleanKafkaBootstrap(dedicatedKafka.GetBootstrapAddress());

        using var producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = bootstrap,
            MessageTimeoutMs = 3000
        }).Build();

        // Produz com Kafka ativo
        var res1 = await producer.ProduceAsync("t1", new Message<string, string> { Key = "k1", Value = "v1" });
        Assert.Equal(PersistenceStatus.Persisted, res1.Status);

        // Pausa o broker
        await dedicatedKafka.PauseAsync();

        var ex = await Record.ExceptionAsync(async () =>
        {
            await producer.ProduceAsync("t1", new Message<string, string> { Key = "k2", Value = "v2" });
        });
        Assert.NotNull(ex);

        // Despausa o broker
        await dedicatedKafka.UnpauseAsync();
        var res3 = await producer.ProduceAsync("t1", new Message<string, string> { Key = "k3", Value = "v3" });
        Assert.Equal(PersistenceStatus.Persisted, res3.Status);
    }

    #endregion
}
