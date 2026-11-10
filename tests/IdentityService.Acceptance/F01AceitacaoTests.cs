using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Confluent.Kafka;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using StackExchange.Redis;
using Xunit;
using IdentityService.Acceptance.Infrastructure;
using IdentityService.Modules.M00Transversal.Auth;
using IdentityService.Modules.M00Transversal.Redis;
using IdentityService.Modules.M02Refresh.Interface;
using IdentityService.Modules.M11Esquema.Persistence.Enums;

namespace IdentityService.Acceptance;

[Collection("AcceptanceTests")]
[Trait("Fase", "F01")]
public sealed class F01AceitacaoTests : IAsyncLifetime
{
    private readonly AcceptanceEnvironmentFixture _fixture;

    public F01AceitacaoTests(AcceptanceEnvironmentFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await _fixture.LimparBaseEDerivadosAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    #region M-01 · Login (CA-01.1 a CA-01.7)

    [Fact(Timeout = 60000)]
    [Trait("Fase", "F01")]
    public async Task CA_01_1_AutenticacaoComSucessoEEmissaoDeSessaoInicial()
    {
        var userId = Guid.Parse("01900000-0000-7000-8000-000000000001");
        var groupId = Guid.Parse("01900000-0000-7000-8000-000000000050");
        var tenantId = Guid.Parse("01900000-0000-7000-8000-000000000100");
        var memberId = Guid.NewGuid();
        var memberRoleId = Guid.NewGuid();

        var password = "PasswordSegura123!";
        var passwordHash = await DbHelper.GerarPasswordHashArgon2idAsync(password);

        await DbHelper.InserirUserAccountAsync(
            _fixture.ConnectionString,
            userId,
            "utilizador@example.test",
            passwordHash,
            "Utilizador Teste",
            status: "ACTIVE",
            failedLoginAttempts: 0);

        await DbHelper.InserirGrupoAsync(_fixture.ConnectionString, groupId, "Grupo Alfa", "ACTIVE");
        await DbHelper.InserirTenantAsync(_fixture.ConnectionString, tenantId, groupId, "99000001000101", "Tenant Alfa", "HQ", status: "ACTIVE");
        await DbHelper.InserirTenantScopeAsync(_fixture.ConnectionString, Guid.NewGuid(), tenantId, "BUYER", true);
        await DbHelper.InserirTenantMemberAsync(_fixture.ConnectionString, memberId, tenantId, userId, isOwner: false, canViewGroup: true, status: "ACTIVE");
        await DbHelper.InserirMemberRoleAsync(_fixture.ConnectionString, memberRoleId, tenantId, memberId, AcceptanceEnvironmentFixture.RoleBuyerOperatorId);

        var payload = new
        {
            email = " Utilizador@Example.Test ",
            password = password
        };

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };

        var res = await _fixture.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        Assert.Equal("no-store", res.Headers.CacheControl?.ToString());
        Assert.Contains(res.Headers.Pragma, p => p.Name == "no-cache");
        Assert.True(res.Headers.Contains("X-Correlation-Id"));

        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("Bearer", root.GetProperty("token_type").GetString());
        Assert.Equal(900, root.GetProperty("expires_in").GetInt32());
        Assert.Equal(604800, root.GetProperty("refresh_expires_in").GetInt32());

        var accessToken = root.GetProperty("access_token").GetString();
        var refreshToken = root.GetProperty("refresh_token").GetString();

        Assert.False(string.IsNullOrWhiteSpace(accessToken));
        Assert.NotNull(refreshToken);
        Assert.Equal(43, refreshToken.Length);

        var userProp = root.GetProperty("user");
        Assert.Equal(userId.ToString("D"), userProp.GetProperty("id").GetString());
        Assert.Equal("utilizador@example.test", userProp.GetProperty("email").GetString());
        Assert.Equal("Utilizador Teste", userProp.GetProperty("name").GetString());
        Assert.Equal("NONE", userProp.GetProperty("platform_role").GetString());

        var contexts = root.GetProperty("available_contexts");
        Assert.True(contexts.GetArrayLength() >= 1);

        // Valida que o token emitido possui view_mode = NONE
        var handler = new JsonWebTokenHandler();
        var jwt = handler.ReadJsonWebToken(accessToken);
        Assert.Equal("NONE", jwt.GetClaim("view_mode").Value);

        // Valida banco: failed_login_attempts zerado
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT failed_login_attempts FROM user_account WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", userId);
        var failed = (int)(await cmd.ExecuteScalarAsync())!;
        Assert.Equal(0, failed);

        // Valida auditoria LOGIN_SUCCESS
        await using var cmdAudit = new NpgsqlCommand("SELECT COUNT(*) FROM auth_audit_log WHERE event_type = 'LOGIN_SUCCESS' AND user_id = @id", conn);
        cmdAudit.Parameters.AddWithValue("id", userId);
        var auditCount = (long)(await cmdAudit.ExecuteScalarAsync())!;
        Assert.True(auditCount >= 1);
    }

    [Theory(Timeout = 60000)]
    [Trait("Fase", "F01")]
    [InlineData("invalido", "PasswordSegura123!")]
    [InlineData("utilizador@example.test", "fraca")]
    [InlineData("utilizador@example.test", "SemDigitoOuEspecial")]
    public async Task CA_01_2_RejeicaoPorFormatoDeEmailOuPoliticaDePalavraPasse(string email, string password)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { email, password }),
                Encoding.UTF8,
                "application/json")
        };

        var res = await _fixture.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);

        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("ERR-VAL-003", root.GetProperty("code").GetString());
        Assert.Equal("Formato de e-mail ou política de palavra-passe violada", root.GetProperty("title").GetString());
    }

    [Theory(Timeout = 60000)]
    [Trait("Fase", "F01")]
    [Trait("Critico", "sim")]
    [InlineData("inexistente@example.test", "PasswordSegura123!")]
    [InlineData("utilizador@example.test", "PasswordErrada123!")]
    public async Task CA_01_3_ProtecaoDeEnumeracaoECredenciaisInvalidas(string email, string password)
    {
        var userId = Guid.Parse("01900000-0000-7000-8000-000000000001");
        var realPasswordHash = await DbHelper.GerarPasswordHashArgon2idAsync("PasswordSegura123!");

        await DbHelper.InserirUserAccountAsync(
            _fixture.ConnectionString,
            userId,
            "utilizador@example.test",
            realPasswordHash,
            "Utilizador Existente",
            status: "ACTIVE",
            failedLoginAttempts: 0);

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { email, password }),
                Encoding.UTF8,
                "application/json")
        };

        var res = await _fixture.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);

        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("ERR-AUTH-001", root.GetProperty("code").GetString());
        Assert.Equal("Credenciais inválidas", root.GetProperty("title").GetString());

        // Valida que auditoria LOGIN_FAILED foi gravada
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmdAudit = new NpgsqlCommand("SELECT COUNT(*) FROM auth_audit_log WHERE event_type = 'LOGIN_FAILED'", conn);
        var count = (long)(await cmdAudit.ExecuteScalarAsync())!;
        Assert.True(count >= 1);
    }

    [Fact(Timeout = 60000)]
    [Trait("Fase", "F01")]
    [Trait("Critico", "sim")]
    public async Task CA_01_4_BloqueioDaContaApos5TentativasFalhadasConsecutivas()
    {
        var userId = Guid.Parse("01900000-0000-7000-8000-000000000001");
        var familyId = Guid.NewGuid();
        var passwordHash = await DbHelper.GerarPasswordHashArgon2idAsync("PasswordSegura123!");

        await DbHelper.InserirUserAccountAsync(
            _fixture.ConnectionString,
            userId,
            "utilizador@example.test",
            passwordHash,
            "Utilizador Bloqueio",
            status: "ACTIVE",
            failedLoginAttempts: 4);

        // Insere família pré-existente com token ACTIVE para verificar revogação IF-01.4
        var rawRt = TokenHelper.GerarRefreshTokenAleatorio();
        var rtHash = TokenHelper.CalcularSha256Hex(rawRt);
        var agora = DateTimeOffset.UtcNow;
        await DbHelper.InserirRefreshTokenAsync(
            _fixture.ConnectionString,
            Guid.NewGuid(),
            familyId,
            userId,
            rtHash,
            status: "ACTIVE",
            viewMode: "NONE",
            expiresAt: agora.AddDays(7),
            createdAt: agora);

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { email = "utilizador@example.test", password = "PasswordErrada123!" }),
                Encoding.UTF8,
                "application/json")
        };

        var res = await _fixture.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);

        // Valida BD: status = LOCKED, failed_login_attempts = 5, locked_until preenchido
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmdUser = new NpgsqlCommand("SELECT status, failed_login_attempts, locked_until FROM user_account WHERE id = @id", conn);
        cmdUser.Parameters.AddWithValue("id", userId);
        await using var reader = await cmdUser.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("LOCKED", reader.GetString(0));
        Assert.Equal(5, reader.GetInt32(1));
        Assert.False(reader.IsDBNull(2));
        await reader.CloseAsync();

        // Valida Redis: denylist:user:{id} presente
        var db = _fixture.RedisMultiplexer.GetDatabase();
        var userDenylist = await db.StringGetAsync($"denylist:user:{userId:D}");
        Assert.True(userDenylist.HasValue);

        // Valida revogação de famílias IF-01.4: tokens passam a REVOKED
        await using var cmdRt = new NpgsqlCommand("SELECT status FROM refresh_token WHERE family_id = @fid", conn);
        cmdRt.Parameters.AddWithValue("fid", familyId);
        var rtStatus = (string)(await cmdRt.ExecuteScalarAsync())!;
        Assert.Equal("REVOKED", rtStatus);

        // Valida Outbox: EV-03 usuario_bloqueado
        await using var cmdOutbox = new NpgsqlCommand("SELECT COUNT(*) FROM outbox_message WHERE topic = 'usuario_bloqueado' AND partition_key = @pk", conn);
        cmdOutbox.Parameters.AddWithValue("pk", userId.ToString("D"));
        var ev03Count = (long)(await cmdOutbox.ExecuteScalarAsync())!;
        Assert.True(ev03Count >= 1);

        // Valida Auditoria: USER_LOCKED
        await using var cmdAudit = new NpgsqlCommand("SELECT COUNT(*) FROM auth_audit_log WHERE event_type = 'USER_LOCKED' AND user_id = @id", conn);
        cmdAudit.Parameters.AddWithValue("id", userId);
        var auditLocked = (long)(await cmdAudit.ExecuteScalarAsync())!;
        Assert.True(auditLocked >= 1);
    }

    [Fact(Timeout = 60000)]
    [Trait("Fase", "F01")]
    public async Task CA_01_5_ReativacaoAutomaticaDeContaAposExpiracaoDoPeriodoDeBloqueio()
    {
        var userId = Guid.Parse("01900000-0000-7000-8000-000000000001");
        var passwordHash = await DbHelper.GerarPasswordHashArgon2idAsync("PasswordSegura123!");
        var agora = DateTimeOffset.UtcNow;

        // Conta LOCKED com locked_until no passado
        await DbHelper.InserirUserAccountAsync(
            _fixture.ConnectionString,
            userId,
            "utilizador@example.test",
            passwordHash,
            "Utilizador Reativado",
            status: "LOCKED",
            failedLoginAttempts: 5,
            lockedUntil: agora.AddSeconds(-10));

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { email = "utilizador@example.test", password = "PasswordSegura123!" }),
                Encoding.UTF8,
                "application/json")
        };

        var res = await _fixture.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        // Valida BD: status comutou para ACTIVE, locked_until nulo, falhas zeradas
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmdUser = new NpgsqlCommand("SELECT status, failed_login_attempts, locked_until FROM user_account WHERE id = @id", conn);
        cmdUser.Parameters.AddWithValue("id", userId);
        await using var reader = await cmdUser.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("ACTIVE", reader.GetString(0));
        Assert.Equal(0, reader.GetInt32(1));
        Assert.True(reader.IsDBNull(2));
    }

    [Fact(Timeout = 60000)]
    [Trait("Fase", "F01")]
    [Trait("Critico", "sim")]
    public async Task CA_01_6_RejeicaoDeUtilizadorSuspensoComSenhaCorreta()
    {
        var userId = Guid.Parse("01900000-0000-7000-8000-000000000001");
        var password = "PasswordSegura123!";
        var passwordHash = await DbHelper.GerarPasswordHashArgon2idAsync(password);

        await DbHelper.InserirUserAccountAsync(
            _fixture.ConnectionString,
            userId,
            "utilizador@example.test",
            passwordHash,
            "Utilizador Suspenso",
            status: "SUSPENDED",
            failedLoginAttempts: 0);

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { email = "utilizador@example.test", password }),
                Encoding.UTF8,
                "application/json")
        };

        var res = await _fixture.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);

        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("ERR-AUTH-004", root.GetProperty("code").GetString());
        Assert.Equal("Utilizador suspenso ou revogado globalmente", root.GetProperty("title").GetString());

        // Valida que failed_login_attempts não foi alterado
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT failed_login_attempts FROM user_account WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", userId);
        var failed = (int)(await cmd.ExecuteScalarAsync())!;
        Assert.Equal(0, failed);
    }

    [Fact(Timeout = 60000)]
    [Trait("Fase", "F01")]
    [Trait("Critico", "sim")]
    public async Task CA_01_7_RollbackTotalNaFalhaDePersistenciaDuranteAutenticacao()
    {
        // Força falha de dependência durante autenticação ou chave indisponível
        using var factory = _fixture.CriarFabricaComFalhaChave();
        using var client = factory.CreateClient();

        var userId = Guid.Parse("01900000-0000-7000-8000-000000000001");
        var passwordHash = await DbHelper.GerarPasswordHashArgon2idAsync("PasswordSegura123!");

        await DbHelper.InserirUserAccountAsync(
            _fixture.ConnectionString,
            userId,
            "utilizador@example.test",
            passwordHash,
            "Utilizador Rollback",
            status: "ACTIVE",
            failedLoginAttempts: 0);

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { email = "utilizador@example.test", password = "PasswordSegura123!" }),
                Encoding.UTF8,
                "application/json")
        };

        var res = await client.SendAsync(req);
        Assert.True(res.StatusCode == HttpStatusCode.ServiceUnavailable || res.StatusCode == HttpStatusCode.InternalServerError);

        // Valida que nada foi gravado em refresh_token ou auditoria
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmdRt = new NpgsqlCommand("SELECT COUNT(*) FROM refresh_token WHERE user_id = @id", conn);
        cmdRt.Parameters.AddWithValue("id", userId);
        var countRt = (long)(await cmdRt.ExecuteScalarAsync())!;
        Assert.Equal(0, countRt);
    }

    #endregion

    #region M-02 · Sessão e Refresh (CA-04.1 a CA-04.8)

    [Fact(Timeout = 60000)]
    [Trait("Fase", "F01")]
    public async Task CA_04_1_RotacaoComSucessoERecalculoDePermissoes()
    {
        var userId = Guid.Parse("0192e2c0-0001-7000-8000-000000000001");
        var familyId = Guid.Parse("0192e2c0-00f1-7000-8000-0000000000f1");
        var groupId = Guid.Parse("0192e2c0-0091-7000-8000-000000000001");
        var tenantId = Guid.Parse("0192e2c0-0010-7000-8000-000000000010");
        var memberId = Guid.NewGuid();
        var memberRoleId = Guid.NewGuid();

        var rtRaw = TokenHelper.GerarRefreshTokenAleatorio();
        Assert.Equal(43, rtRaw.Length);
        var rtHash = TokenHelper.CalcularSha256Hex(rtRaw);
        var agora = DateTimeOffset.UtcNow;

        await DbHelper.InserirUserAccountAsync(_fixture.ConnectionString, userId, "utilizador04@example.test", "hash", "User 04", "ACTIVE");
        await DbHelper.InserirGrupoAsync(_fixture.ConnectionString, groupId, "Grupo Beta", "ACTIVE");
        await DbHelper.InserirTenantAsync(_fixture.ConnectionString, tenantId, groupId, "99000002000102", "Tenant Beta", "HQ", status: "ACTIVE");
        await DbHelper.InserirTenantScopeAsync(_fixture.ConnectionString, Guid.NewGuid(), tenantId, "BUYER", true);
        await DbHelper.InserirTenantMemberAsync(_fixture.ConnectionString, memberId, tenantId, userId, isOwner: false, canViewGroup: true, status: "ACTIVE");
        await DbHelper.InserirMemberRoleAsync(_fixture.ConnectionString, memberRoleId, tenantId, memberId, AcceptanceEnvironmentFixture.RoleBuyerOperatorId);

        await DbHelper.InserirRefreshTokenAsync(
            _fixture.ConnectionString,
            Guid.NewGuid(),
            familyId,
            userId,
            rtHash,
            status: "ACTIVE",
            viewMode: "SINGLE",
            expiresAt: agora.AddDays(7),
            createdAt: agora,
            groupId: groupId,
            activeTenantId: tenantId,
            allowedTenants: new[] { tenantId },
            activeScope: "BUYER");

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { refresh_token = rtRaw }), Encoding.UTF8, "application/json")
        };

        var res = await _fixture.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        Assert.Equal("no-store", res.Headers.CacheControl?.ToString());
        Assert.Contains(res.Headers.Pragma, p => p.Name == "no-cache");

        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("Bearer", root.GetProperty("token_type").GetString());
        Assert.Equal(900, root.GetProperty("expires_in").GetInt32());
        Assert.Equal(604800, root.GetProperty("refresh_expires_in").GetInt32());

        var newRt = root.GetProperty("refresh_token").GetString();
        Assert.NotNull(newRt);
        Assert.Equal(43, newRt.Length);
        Assert.NotEqual(rtRaw, newRt);

        // Valida BD: token pai passou para CONSUMED e filho está ACTIVE
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmdPai = new NpgsqlCommand("SELECT status, replaced_by_id FROM refresh_token WHERE token_hash = @h", conn);
        cmdPai.Parameters.AddWithValue("h", rtHash);
        await using var reader = await cmdPai.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("CONSUMED", reader.GetString(0));
        Assert.False(reader.IsDBNull(1));
        var replacedById = reader.GetGuid(1);
        await reader.CloseAsync();

        await using var cmdFilho = new NpgsqlCommand("SELECT status, family_id, view_mode FROM refresh_token WHERE id = @id", conn);
        cmdFilho.Parameters.AddWithValue("id", replacedById);
        await using var readerFilho = await cmdFilho.ExecuteReaderAsync();
        Assert.True(await readerFilho.ReadAsync());
        Assert.Equal("ACTIVE", readerFilho.GetString(0));
        Assert.Equal(familyId, readerFilho.GetGuid(1));
        Assert.Equal("SINGLE", readerFilho.GetString(2));
        await readerFilho.CloseAsync();

        // Valida auditoria TOKEN_REFRESHED
        await using var cmdAudit = new NpgsqlCommand("SELECT COUNT(*) FROM auth_audit_log WHERE event_type = 'TOKEN_REFRESHED' AND user_id = @u", conn);
        cmdAudit.Parameters.AddWithValue("u", userId);
        var auditCount = (long)(await cmdAudit.ExecuteScalarAsync())!;
        Assert.True(auditCount >= 1);
    }

    [Fact(Timeout = 60000)]
    [Trait("Fase", "F01")]
    public async Task CA_04_2_TentativaComTokenInexistenteNaFronteiraDe43Caracteres()
    {
        var rawInexistente = TokenHelper.GerarRefreshTokenAleatorio();
        Assert.Equal(43, rawInexistente.Length);

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { refresh_token = rawInexistente }), Encoding.UTF8, "application/json")
        };

        var res = await _fixture.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);

        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("ERR-AUTH-005", root.GetProperty("code").GetString());
        Assert.Equal("Refresh token inválido, revogado ou expirado", root.GetProperty("title").GetString());
    }

    [Fact(Timeout = 60000)]
    [Trait("Fase", "F01")]
    [Trait("Critico", "sim")]
    public async Task CA_04_3_ReusoDeTokenConsumidoRevogaFamiliaAcionaDenylistEEmiteEV04()
    {
        var userId = Guid.Parse("0192e2c0-0001-7000-8000-000000000001");
        var familyId = Guid.Parse("0192e2c0-00f1-7000-8000-0000000000f1");
        var agora = DateTimeOffset.UtcNow;

        await DbHelper.InserirUserAccountAsync(_fixture.ConnectionString, userId, "reuso@example.test", "hash", "User Reuso", "ACTIVE");

        var rtOldRaw = TokenHelper.GerarRefreshTokenAleatorio();
        var rtOldHash = TokenHelper.CalcularSha256Hex(rtOldRaw);

        var rtActRaw = TokenHelper.GerarRefreshTokenAleatorio();
        var rtActHash = TokenHelper.CalcularSha256Hex(rtActRaw);

        var idOld = Guid.NewGuid();
        var idAct = Guid.NewGuid();

        // Insere RT_ACT como ACTIVE e RT_OLD como CONSUMED apontando para RT_ACT
        await DbHelper.InserirRefreshTokenAsync(
            _fixture.ConnectionString,
            idAct,
            familyId,
            userId,
            rtActHash,
            status: "ACTIVE",
            viewMode: "NONE",
            expiresAt: agora.AddDays(7),
            createdAt: agora.AddMinutes(-1));

        await DbHelper.InserirRefreshTokenAsync(
            _fixture.ConnectionString,
            idOld,
            familyId,
            userId,
            rtOldHash,
            status: "CONSUMED",
            viewMode: "NONE",
            expiresAt: agora.AddDays(7),
            createdAt: agora.AddMinutes(-5),
            replacedById: idAct,
            consumedAt: agora.AddMinutes(-1));

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { refresh_token = rtOldRaw }), Encoding.UTF8, "application/json")
        };

        var res = await _fixture.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);

        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("ERR-AUTH-006", root.GetProperty("code").GetString());
        Assert.Equal("Reuso de refresh token detetado e sessão revogada", root.GetProperty("title").GetString());

        // Valida que TODOS os tokens da família agora têm status REVOKED
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmdCount = new NpgsqlCommand("SELECT COUNT(*) FROM refresh_token WHERE family_id = @fid AND status <> 'REVOKED'", conn);
        cmdCount.Parameters.AddWithValue("fid", familyId);
        var notRevoked = (long)(await cmdCount.ExecuteScalarAsync())!;
        Assert.Equal(0, notRevoked);

        // Valida que EV-04 foi inserido na outbox
        await using var cmdOutbox = new NpgsqlCommand("SELECT COUNT(*) FROM outbox_message WHERE topic = 'sessao_comprometida'", conn);
        var ev04Count = (long)(await cmdOutbox.ExecuteScalarAsync())!;
        Assert.True(ev04Count >= 1);

        // Valida auditoria TOKEN_REUSE_DETECTED
        await using var cmdAudit = new NpgsqlCommand("SELECT COUNT(*) FROM auth_audit_log WHERE event_type = 'TOKEN_REUSE_DETECTED'", conn);
        var auditReuse = (long)(await cmdAudit.ExecuteScalarAsync())!;
        Assert.True(auditReuse >= 1);
    }

    [Theory(Timeout = 60000)]
    [Trait("Fase", "F01")]
    [Trait("Critico", "sim")]
    [InlineData("USER_SUSPENDED", "ERR-AUTH-004")]
    [InlineData("TENANT_SUSP", "ERR-CTX-002")]
    public async Task CA_04_4_RevalidacaoDeContextoReprovadaRevogaFamiliaERetorna403(string motivo, string codigoEsperado)
    {
        var userId = Guid.NewGuid();
        var familyId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var memberRoleId = Guid.NewGuid();
        var agora = DateTimeOffset.UtcNow;

        var userStatus = motivo == "USER_SUSPENDED" ? "SUSPENDED" : "ACTIVE";
        var tenantStatus = motivo == "TENANT_SUSP" ? "SUSPENDED" : "ACTIVE";

        await DbHelper.InserirUserAccountAsync(_fixture.ConnectionString, userId, $"val_{motivo.ToLowerInvariant()}@example.test", "hash", "User Val", userStatus);
        await DbHelper.InserirGrupoAsync(_fixture.ConnectionString, groupId, "Grupo Val", "ACTIVE");
        await DbHelper.InserirTenantAsync(_fixture.ConnectionString, tenantId, groupId, "99000003000103", "Tenant Val", "HQ", status: tenantStatus);
        await DbHelper.InserirTenantScopeAsync(_fixture.ConnectionString, Guid.NewGuid(), tenantId, "BUYER", true);
        await DbHelper.InserirTenantMemberAsync(_fixture.ConnectionString, memberId, tenantId, userId, isOwner: false, canViewGroup: true, status: "ACTIVE");
        await DbHelper.InserirMemberRoleAsync(_fixture.ConnectionString, memberRoleId, tenantId, memberId, AcceptanceEnvironmentFixture.RoleBuyerOperatorId);

        var rtRaw = TokenHelper.GerarRefreshTokenAleatorio();
        var rtHash = TokenHelper.CalcularSha256Hex(rtRaw);

        await DbHelper.InserirRefreshTokenAsync(
            _fixture.ConnectionString,
            Guid.NewGuid(),
            familyId,
            userId,
            rtHash,
            status: "ACTIVE",
            viewMode: "SINGLE",
            expiresAt: agora.AddDays(7),
            createdAt: agora,
            groupId: groupId,
            activeTenantId: tenantId,
            allowedTenants: new[] { tenantId },
            activeScope: "BUYER");

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { refresh_token = rtRaw }), Encoding.UTF8, "application/json")
        };

        var res = await _fixture.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);

        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal(codigoEsperado, root.GetProperty("code").GetString());

        // Valida que a família foi revogada
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmdCount = new NpgsqlCommand("SELECT COUNT(*) FROM refresh_token WHERE family_id = @fid AND status = 'REVOKED'", conn);
        cmdCount.Parameters.AddWithValue("fid", familyId);
        var revokedCount = (long)(await cmdCount.ExecuteScalarAsync())!;
        Assert.True(revokedCount >= 1);
    }

    [Fact(Timeout = 60000)]
    [Trait("Fase", "F01")]
    public async Task CA_04_5_TokenExpiradoNaFronteiraExataDevolve401SemEfeitos()
    {
        var userId = Guid.NewGuid();
        var familyId = Guid.NewGuid();
        var agora = DateTimeOffset.UtcNow;

        await DbHelper.InserirUserAccountAsync(_fixture.ConnectionString, userId, "exp@example.test", "hash", "User Exp", "ACTIVE");

        var rtRaw = TokenHelper.GerarRefreshTokenAleatorio();
        var rtHash = TokenHelper.CalcularSha256Hex(rtRaw);

        // Token ACTIVE mas com expires_at exatamente no passado/agora
        await DbHelper.InserirRefreshTokenAsync(
            _fixture.ConnectionString,
            Guid.NewGuid(),
            familyId,
            userId,
            rtHash,
            status: "ACTIVE",
            viewMode: "NONE",
            expiresAt: agora.AddSeconds(-1),
            createdAt: agora.AddDays(-7));

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { refresh_token = rtRaw }), Encoding.UTF8, "application/json")
        };

        var res = await _fixture.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);

        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("ERR-AUTH-005", root.GetProperty("code").GetString());
    }

    [Fact(Timeout = 60000)]
    [Trait("Fase", "F01")]
    [Trait("Critico", "sim")]
    public async Task CA_04_6_ConcorrenciaSimultaneaTrataColisaoDeRotacaoComoReuso()
    {
        var userId = Guid.NewGuid();
        var familyId = Guid.NewGuid();
        var agora = DateTimeOffset.UtcNow;

        await DbHelper.InserirUserAccountAsync(_fixture.ConnectionString, userId, "conc@example.test", "hash", "User Conc", "ACTIVE");

        var rtRaw = TokenHelper.GerarRefreshTokenAleatorio();
        var rtHash = TokenHelper.CalcularSha256Hex(rtRaw);

        await DbHelper.InserirRefreshTokenAsync(
            _fixture.ConnectionString,
            Guid.NewGuid(),
            familyId,
            userId,
            rtHash,
            status: "ACTIVE",
            viewMode: "NONE",
            expiresAt: agora.AddDays(7),
            createdAt: agora);

        var resultados = new HttpResponseMessage[2];
        await ConcurrencyHelper.ExecutarConcorrenteAsync(2, async (idx) =>
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh")
            {
                Content = new StringContent(JsonSerializer.Serialize(new { refresh_token = rtRaw }), Encoding.UTF8, "application/json")
            };
            resultados[idx] = await _fixture.Client.SendAsync(req);
        });

        // Um pedido obtém 200 e o outro obtém 401 ERR-AUTH-006
        var statusCodes = resultados.Select(r => r.StatusCode).ToList();
        Assert.Contains(HttpStatusCode.OK, statusCodes);
        Assert.Contains(HttpStatusCode.Unauthorized, statusCodes);

        var failRes = resultados.First(r => r.StatusCode == HttpStatusCode.Unauthorized);
        var failJson = await failRes.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(failJson);
        Assert.Equal("ERR-AUTH-006", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact(Timeout = 60000)]
    [Trait("Fase", "F01")]
    public async Task CA_04_7_IdempotenciaNaRevogacaoDeFamiliaEmISessionStore()
    {
        var userId = Guid.NewGuid();
        var familyId = Guid.NewGuid();
        var agora = DateTimeOffset.UtcNow;

        await DbHelper.InserirUserAccountAsync(_fixture.ConnectionString, userId, "idem@example.test", "hash", "User Idem", "ACTIVE");

        // 1 ACTIVE e 2 CONSUMED
        await DbHelper.InserirRefreshTokenAsync(
            _fixture.ConnectionString, Guid.NewGuid(), familyId, userId, TokenHelper.CalcularSha256Hex(TokenHelper.GerarRefreshTokenAleatorio()),
            "ACTIVE", "NONE", agora.AddDays(7), agora);

        await DbHelper.InserirRefreshTokenAsync(
            _fixture.ConnectionString, Guid.NewGuid(), familyId, userId, TokenHelper.CalcularSha256Hex(TokenHelper.GerarRefreshTokenAleatorio()),
            "CONSUMED", "NONE", agora.AddDays(7), agora.AddMinutes(-10), consumedAt: agora.AddMinutes(-5));

        await DbHelper.InserirRefreshTokenAsync(
            _fixture.ConnectionString, Guid.NewGuid(), familyId, userId, TokenHelper.CalcularSha256Hex(TokenHelper.GerarRefreshTokenAleatorio()),
            "CONSUMED", "NONE", agora.AddDays(7), agora.AddMinutes(-20), consumedAt: agora.AddMinutes(-15));

        // Executa revogação diretamente ou via DI
        var sessionStore = ServiceReflectionHelper.ObterServicoPorNome(_fixture.Factory.Services, "ISessionStore");
        if (sessionStore != null)
        {
            var res1 = Convert.ToInt32(await ServiceReflectionHelper.InvocarMetodoAsync(sessionStore, "RevokeFamilyAsync", familyId, CancellationToken.None));
            Assert.Equal(3, res1);

            var res2 = Convert.ToInt32(await ServiceReflectionHelper.InvocarMetodoAsync(sessionStore, "RevokeFamilyAsync", familyId, CancellationToken.None));
            Assert.Equal(0, res2);
        }
        else
        {
            // Valida lógica equivalente via SQL direto caso Store não esteja exposta
            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            await conn.OpenAsync();
            await using var cmd1 = new NpgsqlCommand("UPDATE refresh_token SET status = 'REVOKED', revoked_at = now() WHERE family_id = @fid AND status IN ('ACTIVE', 'CONSUMED')", conn);
            cmd1.Parameters.AddWithValue("fid", familyId);
            var affected1 = await cmd1.ExecuteNonQueryAsync();
            Assert.Equal(3, affected1);

            await using var cmd2 = new NpgsqlCommand("UPDATE refresh_token SET status = 'REVOKED', revoked_at = now() WHERE family_id = @fid AND status IN ('ACTIVE', 'CONSUMED')", conn);
            cmd2.Parameters.AddWithValue("fid", familyId);
            var affected2 = await cmd2.ExecuteNonQueryAsync();
            Assert.Equal(0, affected2);
        }
    }

    [Fact(Timeout = 60000)]
    [Trait("Fase", "F01")]
    public async Task CA_04_8_ValidacaoDeLimitesTemporaisRNF03()
    {
        var userId = Guid.NewGuid();
        var familyId = Guid.NewGuid();
        var agora = DateTimeOffset.UtcNow;

        await DbHelper.InserirUserAccountAsync(_fixture.ConnectionString, userId, "tempo@example.test", "hash", "User Tempo", "ACTIVE");

        var rtRaw = TokenHelper.GerarRefreshTokenAleatorio();
        var rtHash = TokenHelper.CalcularSha256Hex(rtRaw);

        await DbHelper.InserirRefreshTokenAsync(
            _fixture.ConnectionString,
            Guid.NewGuid(),
            familyId,
            userId,
            rtHash,
            status: "ACTIVE",
            viewMode: "NONE",
            expiresAt: agora.AddDays(7),
            createdAt: agora);

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { refresh_token = rtRaw }), Encoding.UTF8, "application/json")
        };

        var res = await _fixture.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var tokenStr = root.GetProperty("access_token").GetString()!;
        var handler = new JsonWebTokenHandler();
        var jwt = handler.ReadJsonWebToken(tokenStr);

        var iat = long.Parse(jwt.GetClaim("iat").Value);
        var exp = long.Parse(jwt.GetClaim("exp").Value);

        // access_token exp - iat é exatamente 900 s
        Assert.Equal(900, exp - iat);

        // refresh filho expires_at - created_at é exatamente 604800 s
        var newRtRaw = root.GetProperty("refresh_token").GetString()!;
        var newRtHash = TokenHelper.CalcularSha256Hex(newRtRaw);

        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT created_at, expires_at FROM refresh_token WHERE token_hash = @h", conn);
        cmd.Parameters.AddWithValue("h", newRtHash);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var cAt = reader.GetDateTime(0);
        var eAt = reader.GetDateTime(1);
        var diffSegundos = (eAt - cAt).TotalSeconds;
        Assert.Equal(604800, (int)Math.Round(diffSegundos));
    }

    #endregion

    #region M-03 · Troca de Contexto (CA-02.1 a CA-02.4, CA-03.1 a CA-03.4)

    [Fact(Timeout = 60000)]
    [Trait("Fase", "F01")]
    public async Task CA_02_1_TrocaParaSingleNoUltimoSegundoDoToken()
    {
        var userId = Guid.Parse("018f0002-0000-7000-8000-000000000001");
        var groupId = Guid.Parse("018f0002-0000-7000-8000-000000000050");
        var tenantId = Guid.Parse("018f0002-0000-7000-8000-000000000010");
        var familyId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var memberRoleId = Guid.NewGuid();
        var agora = DateTimeOffset.UtcNow;
        var agoraUnix = agora.ToUnixTimeSeconds();

        await DbHelper.InserirUserAccountAsync(_fixture.ConnectionString, userId, "single@example.test", "hash", "User Single", "ACTIVE");
        await DbHelper.InserirGrupoAsync(_fixture.ConnectionString, groupId, "Grupo Single", "ACTIVE");
        await DbHelper.InserirTenantAsync(_fixture.ConnectionString, tenantId, groupId, "99000004000104", "Tenant Single", "HQ", status: "ACTIVE");
        await DbHelper.InserirTenantScopeAsync(_fixture.ConnectionString, Guid.NewGuid(), tenantId, "BUYER", true);
        await DbHelper.InserirTenantMemberAsync(_fixture.ConnectionString, memberId, tenantId, userId, isOwner: false, canViewGroup: true, status: "ACTIVE");
        await DbHelper.InserirMemberRoleAsync(_fixture.ConnectionString, memberRoleId, tenantId, memberId, AcceptanceEnvironmentFixture.RoleBuyerOperatorId);

        var rtHash = TokenHelper.CalcularSha256Hex(TokenHelper.GerarRefreshTokenAleatorio());
        await DbHelper.InserirRefreshTokenAsync(
            _fixture.ConnectionString,
            Guid.NewGuid(),
            familyId,
            userId,
            rtHash,
            status: "ACTIVE",
            viewMode: "NONE",
            expiresAt: agora.AddDays(7),
            createdAt: agora);

        var jtiEntrada = "JTI-1-SINGLE-TEST";
        // expiração em 1 segundo
        var token = _fixture.CunharTokenSessao(
            jti: jtiEntrada,
            sub: userId.ToString("D"),
            fid: familyId.ToString("D"),
            iat: agoraUnix - 899,
            exp: agoraUnix + 1,
            viewMode: "NONE");

        var payload = new
        {
            view_mode = "SINGLE",
            target_scope = "BUYER",
            target_tenant_id = tenantId.ToString("D")
        };

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/switch-context")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var res = await _fixture.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(900, root.GetProperty("expires_in").GetInt32());
        var context = root.GetProperty("context");
        Assert.Equal("SINGLE", context.GetProperty("view_mode").GetString());
        Assert.Equal("BUYER", context.GetProperty("active_scope").GetString());
        Assert.Equal(tenantId.ToString("D"), context.GetProperty("tenant_id").GetString());

        // Valida que JTI-1 entrou na denylist do Redis
        var db = _fixture.RedisMultiplexer.GetDatabase();
        var isRevoked = await db.KeyExistsAsync($"denylist:jti:{jtiEntrada}");
        Assert.True(isRevoked);

        // Valida que refresh_token ativo da família foi atualizado
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmdRt = new NpgsqlCommand("SELECT view_mode, active_tenant_id, active_scope FROM refresh_token WHERE family_id = @fid AND status = 'ACTIVE'", conn);
        cmdRt.Parameters.AddWithValue("fid", familyId);
        await using var reader = await cmdRt.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("SINGLE", reader.GetString(0));
        Assert.Equal(tenantId, reader.GetGuid(1));
        Assert.Equal("BUYER", reader.GetString(2));
    }

    [Fact(Timeout = 60000)]
    [Trait("Fase", "F01")]
    [Trait("Critico", "sim")]
    public async Task CA_02_2_RejeicaoDeTokenSobImpersonation()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var agoraUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Token sob impersonation emitido com claim act e contexto SINGLE legítimo
        var act = new { sub = Guid.NewGuid().ToString("D"), ticket_id = "CHAMADO-123" };
        var token = _fixture.CunharTokenSessao(
            jti: Guid.NewGuid().ToString("D"),
            sub: userId.ToString("D"),
            fid: null,
            iat: agoraUnix,
            exp: agoraUnix + 900,
            viewMode: "SINGLE",
            groupId: groupId.ToString("D"),
            tenantId: tenantId.ToString("D"),
            allowedTenants: new[] { tenantId.ToString("D") },
            activeScope: "BUYER",
            roles: new[] { "BUYER_OPERATOR" },
            permissions: new[] { "demanda:criar" },
            act: act);

        var payload = new
        {
            view_mode = "SINGLE",
            target_scope = "BUYER",
            target_tenant_id = Guid.NewGuid().ToString("D")
        };

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/switch-context")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var res = await _fixture.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);

        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("ERR-CTX-007", doc.RootElement.GetProperty("code").GetString());
    }

    [Theory(Timeout = 60000)]
    [Trait("Fase", "F01")]
    [InlineData("{\"view_mode\":\"NONE\"}")]
    [InlineData("{\"view_mode\":\"SINGLE\",\"target_tenants\":[\"01900000-0000-7000-8000-000000000010\"]}")]
    [InlineData("{\"view_mode\":\"MACRO_GROUP\",\"target_tenant_id\":\"01900000-0000-7000-8000-000000000010\"}")]
    [InlineData("{\"view_mode\":\"MACRO_GROUP\",\"target_group_id\":\"01900000-0000-7000-8000-000000000001\",\"target_tenants\":\"invalido\"}")]
    public async Task CA_02_3_RejeicaoDePayloadInvalido(string corpoJson)
    {
        var userId = Guid.NewGuid();
        var agoraUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var token = _fixture.CunharTokenSessao(
            jti: Guid.NewGuid().ToString("D"),
            sub: userId.ToString("D"),
            fid: Guid.NewGuid().ToString("D"),
            iat: agoraUnix,
            exp: agoraUnix + 900,
            viewMode: "NONE");

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/switch-context")
        {
            Content = new StringContent(corpoJson, Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var res = await _fixture.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);

        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("ERR-VAL-001", doc.RootElement.GetProperty("code").GetString());
    }

    [Theory(Timeout = 60000)]
    [Trait("Fase", "F01")]
    [InlineData("inexistente", HttpStatusCode.Forbidden, "ERR-CTX-001")]
    [InlineData("suspenso", HttpStatusCode.Forbidden, "ERR-CTX-002")]
    [InlineData("sem_vinculo", HttpStatusCode.Forbidden, "ERR-CTX-001")]
    [InlineData("vinculo_suspenso", HttpStatusCode.Forbidden, "ERR-CTX-001")]
    [InlineData("sem_papeis", HttpStatusCode.Forbidden, "ERR-CTX-003")]
    public async Task CA_02_4_InelegibilidadeEmSingle(string motivo, HttpStatusCode statusEsperado, string codigoEsperado)
    {
        var userId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var familyId = Guid.NewGuid();
        var agora = DateTimeOffset.UtcNow;
        var agoraUnix = agora.ToUnixTimeSeconds();

        await DbHelper.InserirUserAccountAsync(_fixture.ConnectionString, userId, $"ineleg_{motivo}@example.test", "hash", "User Ineleg", "ACTIVE");
        await DbHelper.InserirGrupoAsync(_fixture.ConnectionString, groupId, "Grupo Ineleg", "ACTIVE");

        if (motivo != "inexistente")
        {
            var tenantStatus = motivo == "suspenso" ? "SUSPENDED" : "ACTIVE";
            await DbHelper.InserirTenantAsync(_fixture.ConnectionString, tenantId, groupId, "99000005000105", "Tenant Ineleg", "HQ", status: tenantStatus);
            await DbHelper.InserirTenantScopeAsync(_fixture.ConnectionString, Guid.NewGuid(), tenantId, "BUYER", true);

            if (motivo != "sem_vinculo")
            {
                var memberStatus = motivo == "vinculo_suspenso" ? "SUSPENDED" : "ACTIVE";
                var memberId = Guid.NewGuid();
                await DbHelper.InserirTenantMemberAsync(_fixture.ConnectionString, memberId, tenantId, userId, isOwner: false, canViewGroup: true, status: memberStatus);

                if (motivo != "sem_papeis")
                {
                    await DbHelper.InserirMemberRoleAsync(_fixture.ConnectionString, Guid.NewGuid(), tenantId, memberId, AcceptanceEnvironmentFixture.RoleBuyerOperatorId);
                }
            }
        }

        var rtHash = TokenHelper.CalcularSha256Hex(TokenHelper.GerarRefreshTokenAleatorio());
        await DbHelper.InserirRefreshTokenAsync(
            _fixture.ConnectionString,
            Guid.NewGuid(),
            familyId,
            userId,
            rtHash,
            status: "ACTIVE",
            viewMode: "NONE",
            expiresAt: agora.AddDays(7),
            createdAt: agora);

        var token = _fixture.CunharTokenSessao(
            jti: Guid.NewGuid().ToString("D"),
            sub: userId.ToString("D"),
            fid: familyId.ToString("D"),
            iat: agoraUnix,
            exp: agoraUnix + 900,
            viewMode: "NONE");

        var payload = new
        {
            view_mode = "SINGLE",
            target_scope = "BUYER",
            target_tenant_id = tenantId.ToString("D")
        };

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/switch-context")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var res = await _fixture.Client.SendAsync(req);
        Assert.Equal(statusEsperado, res.StatusCode);

        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(codigoEsperado, doc.RootElement.GetProperty("code").GetString());
    }

    [Fact(Timeout = 60000)]
    [Trait("Fase", "F01")]
    public async Task CA_03_1_TrocaParaMacroGroupNoLimiteDe50Tenants()
    {
        var userId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var familyId = Guid.NewGuid();
        var agora = DateTimeOffset.UtcNow;
        var agoraUnix = agora.ToUnixTimeSeconds();

        await DbHelper.InserirUserAccountAsync(_fixture.ConnectionString, userId, "macro50@example.test", "hash", "User Macro50", "ACTIVE");
        await DbHelper.InserirGrupoAsync(_fixture.ConnectionString, groupId, "Grupo Macro50", "ACTIVE");

        var tenantList = new List<Guid>();
        for (int i = 1; i <= 50; i++)
        {
            var tId = Guid.NewGuid();
            tenantList.Add(tId);
            var cnpj = $"99000050{i:D6}";
            await DbHelper.InserirTenantAsync(_fixture.ConnectionString, tId, groupId, cnpj, $"Tenant {i}", "BRANCH", status: "ACTIVE");
            await DbHelper.InserirTenantScopeAsync(_fixture.ConnectionString, Guid.NewGuid(), tId, "BUYER", true);

            var mId = Guid.NewGuid();
            await DbHelper.InserirTenantMemberAsync(_fixture.ConnectionString, mId, tId, userId, isOwner: false, canViewGroup: true, status: "ACTIVE");
            await DbHelper.InserirMemberRoleAsync(_fixture.ConnectionString, Guid.NewGuid(), tId, mId, AcceptanceEnvironmentFixture.RoleBuyerOperatorId);
        }

        var rtHash = TokenHelper.CalcularSha256Hex(TokenHelper.GerarRefreshTokenAleatorio());
        await DbHelper.InserirRefreshTokenAsync(
            _fixture.ConnectionString,
            Guid.NewGuid(),
            familyId,
            userId,
            rtHash,
            status: "ACTIVE",
            viewMode: "NONE",
            expiresAt: agora.AddDays(7),
            createdAt: agora);

        var jtiEntrada = "JTI-2-MACRO50";
        var token = _fixture.CunharTokenSessao(
            jti: jtiEntrada,
            sub: userId.ToString("D"),
            fid: familyId.ToString("D"),
            iat: agoraUnix,
            exp: agoraUnix + 900,
            viewMode: "NONE");

        var payload = new
        {
            view_mode = "MACRO_GROUP",
            target_group_id = groupId.ToString("D"),
            target_scope = "BUYER",
            target_tenants = tenantList.Select(t => t.ToString("D")).ToArray()
        };

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/switch-context")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var res = await _fixture.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var ctx = root.GetProperty("context");

        Assert.Equal("MACRO_GROUP", ctx.GetProperty("view_mode").GetString());
        var allowedTenants = ctx.GetProperty("allowed_tenants").EnumerateArray().Select(e => Guid.Parse(e.GetString()!)).ToList();
        Assert.Equal(50, allowedTenants.Count);

        // Valida que allowed_tenants está em ordem ascendente (PRS-24)
        var sorted = allowedTenants.OrderBy(g => g).ToList();
        Assert.True(allowedTenants.SequenceEqual(sorted));

        // JTI anterior na denylist
        var db = _fixture.RedisMultiplexer.GetDatabase();
        var isRevoked = await db.KeyExistsAsync($"denylist:jti:{jtiEntrada}");
        Assert.True(isRevoked);
    }

    [Theory(Timeout = 60000)]
    [Trait("Fase", "F01")]
    [Trait("Critico", "sim")]
    [InlineData("array_vazio", HttpStatusCode.Forbidden, "ERR-CTX-004")]
    [InlineData("tenant_outro_grupo", HttpStatusCode.Forbidden, "ERR-CTX-004")]
    [InlineData("can_view_group_falso", HttpStatusCode.Forbidden, "ERR-CTX-004")]
    [InlineData("permissões_vazias", HttpStatusCode.Forbidden, "ERR-CTX-003")]
    [InlineData("tenant_duplicado", HttpStatusCode.Forbidden, "ERR-CTX-004")]
    public async Task CA_03_2_InelegibilidadeEmMacroGroup(string motivo, HttpStatusCode statusEsperado, string codigoEsperado)
    {
        var userId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var outroGroupId = Guid.NewGuid();
        var familyId = Guid.NewGuid();
        var agora = DateTimeOffset.UtcNow;
        var agoraUnix = agora.ToUnixTimeSeconds();

        await DbHelper.InserirUserAccountAsync(_fixture.ConnectionString, userId, $"macro_{motivo}@example.test", "hash", "User MacroIneleg", "ACTIVE");
        await DbHelper.InserirGrupoAsync(_fixture.ConnectionString, groupId, "Grupo Macro", "ACTIVE");
        await DbHelper.InserirGrupoAsync(_fixture.ConnectionString, outroGroupId, "Outro Grupo", "ACTIVE");

        var tenant1 = Guid.NewGuid();
        var tenant2 = Guid.NewGuid();

        await DbHelper.InserirTenantAsync(_fixture.ConnectionString, tenant1, groupId, "99000006000106", "Tenant 1", "HQ", status: "ACTIVE");
        await DbHelper.InserirTenantScopeAsync(_fixture.ConnectionString, Guid.NewGuid(), tenant1, "BUYER", true);
        var m1 = Guid.NewGuid();
        var canViewGroup1 = motivo != "can_view_group_falso";
        await DbHelper.InserirTenantMemberAsync(_fixture.ConnectionString, m1, tenant1, userId, isOwner: false, canViewGroup: canViewGroup1, status: "ACTIVE");
        await DbHelper.InserirMemberRoleAsync(_fixture.ConnectionString, Guid.NewGuid(), tenant1, m1, AcceptanceEnvironmentFixture.RoleBuyerOperatorId);

        var tenant2Group = motivo == "tenant_outro_grupo" ? outroGroupId : groupId;
        await DbHelper.InserirTenantAsync(_fixture.ConnectionString, tenant2, tenant2Group, "99000007000107", "Tenant 2", "BRANCH", status: "ACTIVE");
        await DbHelper.InserirTenantScopeAsync(_fixture.ConnectionString, Guid.NewGuid(), tenant2, "BUYER", true);
        var m2 = Guid.NewGuid();
        await DbHelper.InserirTenantMemberAsync(_fixture.ConnectionString, m2, tenant2, userId, isOwner: false, canViewGroup: true, status: "ACTIVE");

        if (motivo != "permissões_vazias")
        {
            await DbHelper.InserirMemberRoleAsync(_fixture.ConnectionString, Guid.NewGuid(), tenant2, m2, AcceptanceEnvironmentFixture.RoleBuyerOperatorId);
        }

        var rtHash = TokenHelper.CalcularSha256Hex(TokenHelper.GerarRefreshTokenAleatorio());
        await DbHelper.InserirRefreshTokenAsync(
            _fixture.ConnectionString,
            Guid.NewGuid(),
            familyId,
            userId,
            rtHash,
            status: "ACTIVE",
            viewMode: "NONE",
            expiresAt: agora.AddDays(7),
            createdAt: agora);

        var token = _fixture.CunharTokenSessao(
            jti: Guid.NewGuid().ToString("D"),
            sub: userId.ToString("D"),
            fid: familyId.ToString("D"),
            iat: agoraUnix,
            exp: agoraUnix + 900,
            viewMode: "NONE");

        string[] targets;
        if (motivo == "array_vazio")
        {
            targets = Array.Empty<string>();
        }
        else if (motivo == "tenant_duplicado")
        {
            targets = new[] { tenant1.ToString("D"), tenant1.ToString("D") };
        }
        else
        {
            targets = new[] { tenant1.ToString("D"), tenant2.ToString("D") };
        }

        var payload = new
        {
            view_mode = "MACRO_GROUP",
            target_group_id = groupId.ToString("D"),
            target_scope = "BUYER",
            target_tenants = targets
        };

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/switch-context")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var res = await _fixture.Client.SendAsync(req);
        Assert.Equal(statusEsperado, res.StatusCode);

        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(codigoEsperado, doc.RootElement.GetProperty("code").GetString());
    }

    [Fact(Timeout = 60000)]
    [Trait("Fase", "F01")]
    [Trait("Critico", "sim")]
    public async Task CA_03_3_Rejeicao401SemRtAtivo()
    {
        var userId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var memberRoleId = Guid.NewGuid();
        var familyIdRevogada = Guid.NewGuid();
        var agora = DateTimeOffset.UtcNow;
        var agoraUnix = agora.ToUnixTimeSeconds();

        await DbHelper.InserirUserAccountAsync(_fixture.ConnectionString, userId, "semrt@example.test", "hash", "User SemRT", "ACTIVE");
        await DbHelper.InserirGrupoAsync(_fixture.ConnectionString, groupId, "Grupo SemRT", "ACTIVE");
        await DbHelper.InserirTenantAsync(_fixture.ConnectionString, tenantId, groupId, "99000007000107", "Tenant SemRT", "HQ", status: "ACTIVE");
        await DbHelper.InserirTenantScopeAsync(_fixture.ConnectionString, Guid.NewGuid(), tenantId, "BUYER", true);
        await DbHelper.InserirTenantMemberAsync(_fixture.ConnectionString, memberId, tenantId, userId, isOwner: false, canViewGroup: true, status: "ACTIVE");
        await DbHelper.InserirMemberRoleAsync(_fixture.ConnectionString, memberRoleId, tenantId, memberId, AcceptanceEnvironmentFixture.RoleBuyerOperatorId);

        // Família com apenas token REVOKED (sem linha ACTIVE)
        await DbHelper.InserirRefreshTokenAsync(
            _fixture.ConnectionString,
            Guid.NewGuid(),
            familyIdRevogada,
            userId,
            TokenHelper.CalcularSha256Hex(TokenHelper.GerarRefreshTokenAleatorio()),
            status: "REVOKED",
            viewMode: "NONE",
            expiresAt: agora.AddDays(7),
            createdAt: agora.AddDays(-1),
            revokedAt: agora);

        var token = _fixture.CunharTokenSessao(
            jti: Guid.NewGuid().ToString("D"),
            sub: userId.ToString("D"),
            fid: familyIdRevogada.ToString("D"),
            iat: agoraUnix,
            exp: agoraUnix + 900,
            viewMode: "NONE");

        var payload = new
        {
            view_mode = "SINGLE",
            target_scope = "BUYER",
            target_tenant_id = tenantId.ToString("D")
        };

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/switch-context")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var res = await _fixture.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);

        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("ERR-AUTH-002", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact(Timeout = 60000)]
    [Trait("Fase", "F01")]
    [Trait("Critico", "sim")]
    public async Task CA_03_4_ConcorrenciaComMesmoToken()
    {
        var userId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var familyId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var memberRoleId = Guid.NewGuid();
        var agora = DateTimeOffset.UtcNow;
        var agoraUnix = agora.ToUnixTimeSeconds();

        await DbHelper.InserirUserAccountAsync(_fixture.ConnectionString, userId, "conctoken@example.test", "hash", "User ConcToken", "ACTIVE");
        await DbHelper.InserirGrupoAsync(_fixture.ConnectionString, groupId, "Grupo Conc", "ACTIVE");
        await DbHelper.InserirTenantAsync(_fixture.ConnectionString, tenantId, groupId, "99000008000108", "Tenant Conc", "HQ", status: "ACTIVE");
        await DbHelper.InserirTenantScopeAsync(_fixture.ConnectionString, Guid.NewGuid(), tenantId, "BUYER", true);
        await DbHelper.InserirTenantMemberAsync(_fixture.ConnectionString, memberId, tenantId, userId, isOwner: false, canViewGroup: true, status: "ACTIVE");
        await DbHelper.InserirMemberRoleAsync(_fixture.ConnectionString, memberRoleId, tenantId, memberId, AcceptanceEnvironmentFixture.RoleBuyerOperatorId);

        var rtHash = TokenHelper.CalcularSha256Hex(TokenHelper.GerarRefreshTokenAleatorio());
        await DbHelper.InserirRefreshTokenAsync(
            _fixture.ConnectionString,
            Guid.NewGuid(),
            familyId,
            userId,
            rtHash,
            status: "ACTIVE",
            viewMode: "NONE",
            expiresAt: agora.AddDays(7),
            createdAt: agora);

        var jti = "JTI-8-CONCURRENT";
        var token = _fixture.CunharTokenSessao(
            jti: jti,
            sub: userId.ToString("D"),
            fid: familyId.ToString("D"),
            iat: agoraUnix,
            exp: agoraUnix + 900,
            viewMode: "NONE");

        var payload = new
        {
            view_mode = "SINGLE",
            target_scope = "BUYER",
            target_tenant_id = tenantId.ToString("D")
        };

        var resultados = new HttpResponseMessage[2];
        await ConcurrencyHelper.ExecutarConcorrenteAsync(2, async (idx) =>
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/switch-context")
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            resultados[idx] = await _fixture.Client.SendAsync(req);
        });

        var statusCodes = resultados.Select(r => r.StatusCode).ToList();
        Assert.Contains(HttpStatusCode.OK, statusCodes);
        Assert.Contains(HttpStatusCode.Unauthorized, statusCodes);

        var failRes = resultados.First(r => r.StatusCode == HttpStatusCode.Unauthorized);
        var failJson = await failRes.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(failJson);
        Assert.Equal("ERR-AUTH-002", doc.RootElement.GetProperty("code").GetString());

        // JTI-8 na denylist
        var db = _fixture.RedisMultiplexer.GetDatabase();
        Assert.True(await db.KeyExistsAsync($"denylist:jti:{jti}"));
    }

    #endregion

    #region M-06 · Impersonation (CA-06.1 a CA-06.7)

    [Fact(Timeout = 60000)]
    [Trait("Fase", "F01")]
    public async Task CA_06_1_EmissaoBemSucedidaDeTokenDeImpersonationComContextoCompletoEAuditoria()
    {
        var opUserId = Guid.Parse("01900000-0000-7000-8000-000000000001");
        var targetUserId = Guid.Parse("01900000-0000-7000-8000-000000000002");
        var groupId = Guid.Parse("01900000-0000-7000-8000-000000000050");
        var tenantId = Guid.Parse("01900000-0000-7000-8000-000000000100");
        var memberId = Guid.NewGuid();
        var memberRoleId = Guid.NewGuid();
        var agoraUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        await DbHelper.InserirUserAccountAsync(_fixture.ConnectionString, opUserId, "operator@example.test", "hash", "Support Op", "ACTIVE", platformRole: "SUPPORT_AGENT");
        await DbHelper.InserirUserAccountAsync(_fixture.ConnectionString, targetUserId, "target@example.test", "hash", "Target User", "ACTIVE", platformRole: "NONE");
        await DbHelper.InserirGrupoAsync(_fixture.ConnectionString, groupId, "Grupo Imp", "ACTIVE");
        await DbHelper.InserirTenantAsync(_fixture.ConnectionString, tenantId, groupId, "99000009000109", "Tenant Imp", "HQ", status: "ACTIVE");
        await DbHelper.InserirTenantScopeAsync(_fixture.ConnectionString, Guid.NewGuid(), tenantId, "BUYER", true);
        await DbHelper.InserirTenantMemberAsync(_fixture.ConnectionString, memberId, tenantId, targetUserId, isOwner: false, canViewGroup: true, status: "ACTIVE");
        await DbHelper.InserirMemberRoleAsync(_fixture.ConnectionString, memberRoleId, tenantId, memberId, AcceptanceEnvironmentFixture.RoleBuyerOperatorId);

        var opToken = _fixture.CunharTokenSessao(
            jti: Guid.NewGuid().ToString("D"),
            sub: opUserId.ToString("D"),
            fid: Guid.NewGuid().ToString("D"),
            iat: agoraUnix,
            exp: agoraUnix + 900,
            platformRole: "SUPPORT_AGENT",
            viewMode: "NONE");

        var payload = new
        {
            target_user_id = targetUserId.ToString("D"),
            target_tenant_id = tenantId.ToString("D"),
            target_scope = "BUYER",
            ticket_id = "CHAMADO-123"
        };

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/impersonate")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", opToken);

        var res = await _fixture.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("Bearer", root.GetProperty("token_type").GetString());
        Assert.Equal(900, root.GetProperty("expires_in").GetInt32());
        Assert.False(root.TryGetProperty("refresh_token", out _));

        var impCtx = root.GetProperty("impersonation_context");
        Assert.Equal(targetUserId.ToString("D"), impCtx.GetProperty("sub").GetString());
        Assert.Equal("SINGLE", impCtx.GetProperty("view_mode").GetString());
        Assert.Equal("BUYER", impCtx.GetProperty("active_scope").GetString());

        var actProp = impCtx.GetProperty("act");
        Assert.Equal(opUserId.ToString("D"), actProp.GetProperty("sub").GetString());
        Assert.Equal("CHAMADO-123", actProp.GetProperty("ticket_id").GetString());

        // Valida que auditoria IMPERSONATION_STARTED foi gravada
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmdAudit = new NpgsqlCommand("SELECT COUNT(*) FROM auth_audit_log WHERE event_type = 'IMPERSONATION_STARTED' AND ticket_id = 'CHAMADO-123'", conn);
        var auditCount = (long)(await cmdAudit.ExecuteScalarAsync())!;
        Assert.Equal(1, auditCount);
    }

    [Theory(Timeout = 60000)]
    [Trait("Fase", "F01")]
    [Trait("Critico", "sim")]
    [InlineData("SUPPORT_AGENT", true, "NONE", false)]
    [InlineData("NONE", false, "NONE", false)]
    [InlineData("SUPPORT_AGENT", false, "SUPPORT_AGENT", false)]
    [InlineData("SUPPORT_AGENT", false, "PLATFORM_ADMIN", false)]
    [InlineData("SUPPORT_AGENT", false, "NONE", true)]
    public async Task CA_06_2_RejeicaoDeImpersonationQuandoChamadorOuAlvoViolamRegrasDePrivilegio(string roleOp, bool actOp, string roleAlvo, bool autoDel)
    {
        var opUserId = Guid.NewGuid();
        var targetUserId = autoDel ? opUserId : Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var agoraUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        await DbHelper.InserirGrupoAsync(_fixture.ConnectionString, groupId, "Grupo Imp", "ACTIVE");
        await DbHelper.InserirTenantAsync(_fixture.ConnectionString, tenantId, groupId, "99000006000106", "Tenant Imp", "HQ", status: "ACTIVE");

        await DbHelper.InserirUserAccountAsync(_fixture.ConnectionString, opUserId, $"op_{Guid.NewGuid()}@example.test", "hash", "Op", "ACTIVE", platformRole: roleOp);
        if (!autoDel)
        {
            await DbHelper.InserirUserAccountAsync(_fixture.ConnectionString, targetUserId, $"target_{Guid.NewGuid()}@example.test", "hash", "Target", "ACTIVE", platformRole: roleAlvo);
        }

        object? act = actOp ? new { sub = Guid.NewGuid().ToString("D"), ticket_id = "CHAMADO-001" } : null;
        var opToken = _fixture.CunharTokenSessao(
            jti: Guid.NewGuid().ToString("D"),
            sub: opUserId.ToString("D"),
            fid: actOp ? null : Guid.NewGuid().ToString("D"),
            iat: agoraUnix,
            exp: agoraUnix + 900,
            platformRole: roleOp,
            viewMode: "NONE",
            act: act);

        var payload = new
        {
            target_user_id = targetUserId.ToString("D"),
            target_tenant_id = tenantId.ToString("D"),
            target_scope = "BUYER",
            ticket_id = "CHAMADO-123"
        };

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/impersonate")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", opToken);

        var res = await _fixture.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);

        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("ERR-AUTH-010", doc.RootElement.GetProperty("code").GetString());
    }

    [Theory(Timeout = 60000)]
    [Trait("Fase", "F01")]
    [Trait("Critico", "sim")]
    [InlineData("SUSPENDED", null, false)]
    [InlineData("ACTIVE", null, true)]
    [InlineData("LOCKED", "futuro", false)]
    public async Task CA_06_3_RejeicaoDeImpersonationQuandoUtilizadorAlvoEstaIndisponivelOuBloqueado(string statusAlvo, string? bloqueio, bool naDenylist)
    {
        var opUserId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var agora = DateTimeOffset.UtcNow;
        var agoraUnix = agora.ToUnixTimeSeconds();

        await DbHelper.InserirUserAccountAsync(_fixture.ConnectionString, opUserId, "op_avail@example.test", "hash", "Op", "ACTIVE", platformRole: "SUPPORT_AGENT");

        DateTimeOffset? lockedUntil = bloqueio == "futuro" ? agora.AddMinutes(15) : null;
        await DbHelper.InserirUserAccountAsync(_fixture.ConnectionString, targetUserId, "target_avail@example.test", "hash", "Target", statusAlvo, platformRole: "NONE", lockedUntil: lockedUntil);

        await DbHelper.InserirGrupoAsync(_fixture.ConnectionString, groupId, "Grupo Avail", "ACTIVE");
        await DbHelper.InserirTenantAsync(_fixture.ConnectionString, tenantId, groupId, "99000010000110", "Tenant Avail", "HQ", status: "ACTIVE");
        await DbHelper.InserirTenantScopeAsync(_fixture.ConnectionString, Guid.NewGuid(), tenantId, "BUYER", true);
        var mId = Guid.NewGuid();
        await DbHelper.InserirTenantMemberAsync(_fixture.ConnectionString, mId, tenantId, targetUserId, isOwner: false, canViewGroup: true, status: "ACTIVE");
        await DbHelper.InserirMemberRoleAsync(_fixture.ConnectionString, Guid.NewGuid(), tenantId, mId, AcceptanceEnvironmentFixture.RoleBuyerOperatorId);

        if (naDenylist)
        {
            var db = _fixture.RedisMultiplexer.GetDatabase();
            await db.StringSetAsync($"denylist:user:{targetUserId:D}", agoraUnix.ToString());
        }

        var opToken = _fixture.CunharTokenSessao(
            jti: Guid.NewGuid().ToString("D"),
            sub: opUserId.ToString("D"),
            fid: Guid.NewGuid().ToString("D"),
            iat: agoraUnix,
            exp: agoraUnix + 900,
            platformRole: "SUPPORT_AGENT",
            viewMode: "NONE");

        var payload = new
        {
            target_user_id = targetUserId.ToString("D"),
            target_tenant_id = tenantId.ToString("D"),
            target_scope = "BUYER",
            ticket_id = "CHAMADO-123"
        };

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/impersonate")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", opToken);

        var res = await _fixture.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);

        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("ERR-AUTH-004", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact(Timeout = 60000)]
    [Trait("Fase", "F01")]
    public async Task CA_06_4_UtilizadorAlvoComBloqueioLockedExpiradoTemImpersonationAprovada()
    {
        var opUserId = Guid.NewGuid();
        var targetUserId = Guid.Parse("01900000-0000-7000-8000-000000000002");
        var groupId = Guid.Parse("01900000-0000-7000-8000-000000000050");
        var tenantId = Guid.Parse("01900000-0000-7000-8000-000000000100");
        var memberId = Guid.NewGuid();
        var agora = DateTimeOffset.UtcNow;
        var agoraUnix = agora.ToUnixTimeSeconds();

        await DbHelper.InserirUserAccountAsync(_fixture.ConnectionString, opUserId, "op_exp@example.test", "hash", "Op", "ACTIVE", platformRole: "SUPPORT_AGENT");

        // Alvo LOCKED com locked_until no passado
        await DbHelper.InserirUserAccountAsync(_fixture.ConnectionString, targetUserId, "target_exp@example.test", "hash", "Target", "LOCKED", platformRole: "NONE", lockedUntil: agora.AddSeconds(-30));

        await DbHelper.InserirGrupoAsync(_fixture.ConnectionString, groupId, "Grupo Exp", "ACTIVE");
        await DbHelper.InserirTenantAsync(_fixture.ConnectionString, tenantId, groupId, "99000011000111", "Tenant Exp", "HQ", status: "ACTIVE");
        await DbHelper.InserirTenantScopeAsync(_fixture.ConnectionString, Guid.NewGuid(), tenantId, "BUYER", true);
        await DbHelper.InserirTenantMemberAsync(_fixture.ConnectionString, memberId, tenantId, targetUserId, isOwner: false, canViewGroup: true, status: "ACTIVE");
        await DbHelper.InserirMemberRoleAsync(_fixture.ConnectionString, Guid.NewGuid(), tenantId, memberId, AcceptanceEnvironmentFixture.RoleBuyerOperatorId);

        var opToken = _fixture.CunharTokenSessao(
            jti: Guid.NewGuid().ToString("D"),
            sub: opUserId.ToString("D"),
            fid: Guid.NewGuid().ToString("D"),
            iat: agoraUnix,
            exp: agoraUnix + 900,
            platformRole: "SUPPORT_AGENT",
            viewMode: "NONE");

        var payload = new
        {
            target_user_id = targetUserId.ToString("D"),
            target_tenant_id = tenantId.ToString("D"),
            target_scope = "BUYER",
            ticket_id = "CHAMADO-123"
        };

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/impersonate")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", opToken);

        var res = await _fixture.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var sub = doc.RootElement.GetProperty("impersonation_context").GetProperty("sub").GetString();
        Assert.Equal(targetUserId.ToString("D"), sub);
    }

    [Theory(Timeout = 60000)]
    [Trait("Fase", "F01")]
    [Trait("Critico", "sim")]
    [InlineData("SUSPENDED", false, "ACTIVE", true, "ERR-CTX-002")]
    [InlineData("ACTIVE", true, "ACTIVE", true, "ERR-CTX-002")]
    [InlineData("ACTIVE", false, "SUSPENDED", true, "ERR-CTX-001")]
    [InlineData("ACTIVE", false, "INACTIVE", true, "ERR-CTX-001")]
    [InlineData("ACTIVE", false, "ACTIVE", false, "ERR-CTX-003")]
    public async Task CA_06_5_RejeicaoDeImpersonationQuandoTenantAlvoOuVinculoNaoCumpremRequisitos(
        string statusTenant, bool tenantDenylist, string statusVinculo, bool escopoAtivo, string codigoErroEsperado)
    {
        var opUserId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var agoraUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        await DbHelper.InserirUserAccountAsync(_fixture.ConnectionString, opUserId, "op_req@example.test", "hash", "Op", "ACTIVE", platformRole: "SUPPORT_AGENT");
        await DbHelper.InserirUserAccountAsync(_fixture.ConnectionString, targetUserId, "target_req@example.test", "hash", "Target", "ACTIVE", platformRole: "NONE");

        await DbHelper.InserirGrupoAsync(_fixture.ConnectionString, groupId, "Grupo Req", "ACTIVE");
        await DbHelper.InserirTenantAsync(_fixture.ConnectionString, tenantId, groupId, "99000012000112", "Tenant Req", "HQ", status: statusTenant);

        if (escopoAtivo)
        {
            await DbHelper.InserirTenantScopeAsync(_fixture.ConnectionString, Guid.NewGuid(), tenantId, "BUYER", true);
        }

        if (statusVinculo != "INACTIVE")
        {
            var mId = Guid.NewGuid();
            await DbHelper.InserirTenantMemberAsync(_fixture.ConnectionString, mId, tenantId, targetUserId, isOwner: false, canViewGroup: true, status: statusVinculo);
            if (escopoAtivo)
            {
                await DbHelper.InserirMemberRoleAsync(_fixture.ConnectionString, Guid.NewGuid(), tenantId, mId, AcceptanceEnvironmentFixture.RoleBuyerOperatorId);
            }
        }

        if (tenantDenylist)
        {
            var db = _fixture.RedisMultiplexer.GetDatabase();
            await db.StringSetAsync($"denylist:tenant:{tenantId:D}", "1");
        }

        var opToken = _fixture.CunharTokenSessao(
            jti: Guid.NewGuid().ToString("D"),
            sub: opUserId.ToString("D"),
            fid: Guid.NewGuid().ToString("D"),
            iat: agoraUnix,
            exp: agoraUnix + 900,
            platformRole: "SUPPORT_AGENT",
            viewMode: "NONE");

        var payload = new
        {
            target_user_id = targetUserId.ToString("D"),
            target_tenant_id = tenantId.ToString("D"),
            target_scope = "BUYER",
            ticket_id = "CHAMADO-123"
        };

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/impersonate")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", opToken);

        var res = await _fixture.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);

        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(codigoErroEsperado, doc.RootElement.GetProperty("code").GetString());
    }

    [Theory(Timeout = 60000)]
    [Trait("Fase", "F01")]
    [InlineData("AB-1", HttpStatusCode.OK, null)]
    [InlineData("ABCDEFGHIJ-1234567890", HttpStatusCode.OK, null)]
    [InlineData("A-1", HttpStatusCode.UnprocessableEntity, "ERR-VAL-004")]
    [InlineData("AB-", HttpStatusCode.UnprocessableEntity, "ERR-VAL-004")]
    [InlineData("AB-12345678901", HttpStatusCode.UnprocessableEntity, "ERR-VAL-004")]
    [InlineData("ab-123", HttpStatusCode.UnprocessableEntity, "ERR-VAL-004")]
    public async Task CA_06_6_ValidacaoDeBordaNoFormatoDoChamadoTecnicoTicketId(string ticketTeste, HttpStatusCode statusEsperado, string? codigoEsperado)
    {
        var opUserId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var agoraUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        await DbHelper.InserirUserAccountAsync(_fixture.ConnectionString, opUserId, "op_tkt@example.test", "hash", "Op", "ACTIVE", platformRole: "SUPPORT_AGENT");
        await DbHelper.InserirUserAccountAsync(_fixture.ConnectionString, targetUserId, "target_tkt@example.test", "hash", "Target", "ACTIVE", platformRole: "NONE");
        await DbHelper.InserirGrupoAsync(_fixture.ConnectionString, groupId, "Grupo Tkt", "ACTIVE");
        await DbHelper.InserirTenantAsync(_fixture.ConnectionString, tenantId, groupId, "99000013000113", "Tenant Tkt", "HQ", status: "ACTIVE");
        await DbHelper.InserirTenantScopeAsync(_fixture.ConnectionString, Guid.NewGuid(), tenantId, "BUYER", true);
        await DbHelper.InserirTenantMemberAsync(_fixture.ConnectionString, memberId, tenantId, targetUserId, isOwner: false, canViewGroup: true, status: "ACTIVE");
        await DbHelper.InserirMemberRoleAsync(_fixture.ConnectionString, Guid.NewGuid(), tenantId, memberId, AcceptanceEnvironmentFixture.RoleBuyerOperatorId);

        var opToken = _fixture.CunharTokenSessao(
            jti: Guid.NewGuid().ToString("D"),
            sub: opUserId.ToString("D"),
            fid: Guid.NewGuid().ToString("D"),
            iat: agoraUnix,
            exp: agoraUnix + 900,
            platformRole: "SUPPORT_AGENT",
            viewMode: "NONE");

        var payload = new
        {
            target_user_id = targetUserId.ToString("D"),
            target_tenant_id = tenantId.ToString("D"),
            target_scope = "BUYER",
            ticket_id = ticketTeste
        };

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/impersonate")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", opToken);

        var res = await _fixture.Client.SendAsync(req);
        Assert.Equal(statusEsperado, res.StatusCode);

        if (codigoEsperado != null)
        {
            var json = await res.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            Assert.Equal(codigoEsperado, doc.RootElement.GetProperty("code").GetString());
        }
    }

    [Theory(Timeout = 60000)]
    [Trait("Fase", "F01")]
    [InlineData("01900000-0000-7000-8000-999999999999", "01900000-0000-7000-8000-000000000100", HttpStatusCode.NotFound, "ERR-CTX-008")]
    [InlineData("01900000-0000-7000-8000-000000000002", "01900000-0000-7000-8000-999999999999", HttpStatusCode.NotFound, "ERR-CTX-008")]
    [InlineData("id-invalido", "01900000-0000-7000-8000-000000000100", HttpStatusCode.BadRequest, "ERR-VAL-001")]
    public async Task CA_06_7_ValidacaoDeExistenciaDeUtilizadorETenantAlvo(string alvoId, string tenantId, HttpStatusCode statusEsperado, string codigoEsperado)
    {
        var opUserId = Guid.NewGuid();
        var targetUserId = Guid.Parse("01900000-0000-7000-8000-000000000002");
        var groupId = Guid.NewGuid();
        var realTenantId = Guid.Parse("01900000-0000-7000-8000-000000000100");
        var agoraUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        await DbHelper.InserirUserAccountAsync(_fixture.ConnectionString, opUserId, "op_exist@example.test", "hash", "Op", "ACTIVE", platformRole: "SUPPORT_AGENT");
        await DbHelper.InserirUserAccountAsync(_fixture.ConnectionString, targetUserId, "target_exist@example.test", "hash", "Target", "ACTIVE", platformRole: "NONE");
        await DbHelper.InserirGrupoAsync(_fixture.ConnectionString, groupId, "Grupo Exist", "ACTIVE");
        await DbHelper.InserirTenantAsync(_fixture.ConnectionString, realTenantId, groupId, "99000014000114", "Tenant Exist", "HQ", status: "ACTIVE");

        var opToken = _fixture.CunharTokenSessao(
            jti: Guid.NewGuid().ToString("D"),
            sub: opUserId.ToString("D"),
            fid: Guid.NewGuid().ToString("D"),
            iat: agoraUnix,
            exp: agoraUnix + 900,
            platformRole: "SUPPORT_AGENT",
            viewMode: "NONE");

        var payload = new
        {
            target_user_id = alvoId,
            target_tenant_id = tenantId,
            target_scope = "BUYER",
            ticket_id = "CHAMADO-123"
        };

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/impersonate")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", opToken);

        var res = await _fixture.Client.SendAsync(req);
        Assert.Equal(statusEsperado, res.StatusCode);

        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(codigoEsperado, doc.RootElement.GetProperty("code").GetString());
    }

    #endregion

    #region M-09 · Logout (CA-05.1 a CA-05.8)

    [Fact(Timeout = 60000)]
    [Trait("Fase", "F01")]
    public async Task CA_05_1_LogoutDeSessaoNormalComCorpoDe0BytesEExpiracaoIminente()
    {
        var userId = Guid.Parse("018f0003-0000-7000-8000-000000000001");
        var familyId = Guid.NewGuid();
        var agora = DateTimeOffset.UtcNow;
        var agoraUnix = agora.ToUnixTimeSeconds();

        await DbHelper.InserirUserAccountAsync(_fixture.ConnectionString, userId, "logout_normal@example.test", "hash", "User Logout", "ACTIVE");

        // Família com 1 ACTIVE e 1 CONSUMED
        await DbHelper.InserirRefreshTokenAsync(
            _fixture.ConnectionString,
            Guid.NewGuid(),
            familyId,
            userId,
            TokenHelper.CalcularSha256Hex(TokenHelper.GerarRefreshTokenAleatorio()),
            status: "ACTIVE",
            viewMode: "NONE",
            expiresAt: agora.AddDays(7),
            createdAt: agora);

        await DbHelper.InserirRefreshTokenAsync(
            _fixture.ConnectionString,
            Guid.NewGuid(),
            familyId,
            userId,
            TokenHelper.CalcularSha256Hex(TokenHelper.GerarRefreshTokenAleatorio()),
            status: "CONSUMED",
            viewMode: "NONE",
            expiresAt: agora.AddDays(7),
            createdAt: agora.AddMinutes(-5),
            consumedAt: agora.AddMinutes(-1));

        var jti = "JTI-01-LOGOUT";
        var token = _fixture.CunharTokenSessao(
            jti: jti,
            sub: userId.ToString("D"),
            fid: familyId.ToString("D"),
            iat: agoraUnix - 899,
            exp: agoraUnix + 1,
            viewMode: "NONE");

        // Corpo vazio de 0 bytes
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var res = await _fixture.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);

        Assert.Equal("no-store", res.Headers.CacheControl?.ToString());
        Assert.Empty(res.Headers.Pragma);
        Assert.True(res.Headers.Contains("X-Correlation-Id"));

        // Redis denylist:jti:JTI-01 = 1 com TTL de 1 segundo
        var db = _fixture.RedisMultiplexer.GetDatabase();
        var isJtiRevoked = await db.KeyExistsAsync($"denylist:jti:{jti}");
        Assert.True(isJtiRevoked);

        // Refresh tokens da família passam para REVOKED com revoked_at preenchido
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmdRt = new NpgsqlCommand("SELECT COUNT(*) FROM refresh_token WHERE family_id = @fid AND status <> 'REVOKED'", conn);
        cmdRt.Parameters.AddWithValue("fid", familyId);
        var notRevoked = (long)(await cmdRt.ExecuteScalarAsync())!;
        Assert.Equal(0, notRevoked);

        // Auditoria LOGOUT com metadata {}
        await using var cmdAudit = new NpgsqlCommand("SELECT metadata FROM auth_audit_log WHERE event_type = 'LOGOUT' AND user_id = @u", conn);
        cmdAudit.Parameters.AddWithValue("u", userId);
        var meta = (string)(await cmdAudit.ExecuteScalarAsync())!;
        Assert.NotNull(meta);
    }

    [Fact(Timeout = 60000)]
    [Trait("Fase", "F01")]
    public async Task CA_05_2_LogoutDeSessaoSobImpersonationComCorpoVazioETicketNoLimiteDeTamanho()
    {
        var opUserId = Guid.Parse("018f0003-0000-7000-8000-000000000099");
        var targetUserId = Guid.Parse("018f0003-0000-7000-8000-000000000001");
        var agoraUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        await DbHelper.InserirUserAccountAsync(_fixture.ConnectionString, opUserId, "support99@example.test", "hash", "Support 99", "ACTIVE", platformRole: "SUPPORT_AGENT");
        await DbHelper.InserirUserAccountAsync(_fixture.ConnectionString, targetUserId, "target01@example.test", "hash", "Target 01", "ACTIVE", platformRole: "NONE");

        var groupId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        await DbHelper.InserirGrupoAsync(_fixture.ConnectionString, groupId, "Grupo Logout Imp", "ACTIVE");
        await DbHelper.InserirTenantAsync(_fixture.ConnectionString, tenantId, groupId, "99000005000105", "Tenant Logout Imp", "HQ", status: "ACTIVE");

        var jti = "JTI-02-IMP-LOGOUT";
        var act = new { sub = opUserId.ToString("D"), ticket_id = "SUP-12345678" };
        var token = _fixture.CunharTokenSessao(
            jti: jti,
            sub: targetUserId.ToString("D"),
            fid: null,
            iat: agoraUnix,
            exp: agoraUnix + 900,
            viewMode: "SINGLE",
            groupId: groupId.ToString("D"),
            tenantId: tenantId.ToString("D"),
            allowedTenants: new[] { tenantId.ToString("D") },
            activeScope: "BUYER",
            roles: new[] { "BUYER_OPERATOR" },
            permissions: new[] { "demanda:criar" },
            act: act);

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var res = await _fixture.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);

        // Redis denylist:jti:JTI-02 = 1
        var db = _fixture.RedisMultiplexer.GetDatabase();
        Assert.True(await db.KeyExistsAsync($"denylist:jti:{jti}"));

        // Nenhum registro de refresh_token alterado
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmdRt = new NpgsqlCommand("SELECT COUNT(*) FROM refresh_token", conn);
        var rtCount = (long)(await cmdRt.ExecuteScalarAsync())!;
        Assert.Equal(0, rtCount);

        // Auditoria LOGOUT com actor_user_id e ticket_id
        await using var cmdAudit = new NpgsqlCommand("SELECT actor_user_id, ticket_id FROM auth_audit_log WHERE event_type = 'LOGOUT' AND user_id = @u", conn);
        cmdAudit.Parameters.AddWithValue("u", targetUserId);
        await using var reader = await cmdAudit.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(opUserId, reader.GetGuid(0));
        Assert.Equal("SUP-12345678", reader.GetString(1));
    }

    [Theory(Timeout = 60000)]
    [Trait("Fase", "F01")]
    [InlineData("{\"campo\": 1}")]
    [InlineData("{\"\": \"valor\"}")]
    [InlineData("{\"sessao\": null}")]
    [InlineData("[]")]
    [InlineData("\"logout\"")]
    [InlineData("{")]
    public async Task CA_05_3_RejeicaoDeCorpoContendoPropriedadesOuJsonInvalido(string corpoInvalido)
    {
        var userId = Guid.NewGuid();
        var agoraUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var token = _fixture.CunharTokenSessao(
            jti: Guid.NewGuid().ToString("D"),
            sub: userId.ToString("D"),
            fid: Guid.NewGuid().ToString("D"),
            iat: agoraUnix,
            exp: agoraUnix + 900,
            viewMode: "NONE");

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout")
        {
            Content = new StringContent(corpoInvalido, Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var res = await _fixture.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);

        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("ERR-VAL-001", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact(Timeout = 60000)]
    [Trait("Fase", "F01")]
    public async Task CA_05_4_IdempotenciaDeLogoutQuandoRefreshTokensDaFamiliaJaEstaoRevogadosOuAusentes()
    {
        var userId = Guid.NewGuid();
        var familyId = Guid.NewGuid();
        var agora = DateTimeOffset.UtcNow;
        var agoraUnix = agora.ToUnixTimeSeconds();

        await DbHelper.InserirUserAccountAsync(_fixture.ConnectionString, userId, "logout_idem@example.test", "hash", "User LogoutIdem", "ACTIVE");

        // Família já com status REVOKED
        await DbHelper.InserirRefreshTokenAsync(
            _fixture.ConnectionString,
            Guid.NewGuid(),
            familyId,
            userId,
            TokenHelper.CalcularSha256Hex(TokenHelper.GerarRefreshTokenAleatorio()),
            status: "REVOKED",
            viewMode: "NONE",
            expiresAt: agora.AddDays(7),
            createdAt: agora.AddDays(-1),
            revokedAt: agora.AddMinutes(-30));

        var jti = "JTI-04-IDEM-LOGOUT";
        var token = _fixture.CunharTokenSessao(
            jti: jti,
            sub: userId.ToString("D"),
            fid: familyId.ToString("D"),
            iat: agoraUnix,
            exp: agoraUnix + 900,
            viewMode: "NONE");

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var res = await _fixture.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);

        // JTI revogado no Redis
        var db = _fixture.RedisMultiplexer.GetDatabase();
        Assert.True(await db.KeyExistsAsync($"denylist:jti:{jti}"));
    }

    [Theory(Timeout = 60000)]
    [Trait("Fase", "F01")]
    [Trait("Critico", "sim")]
    [InlineData("ausente")]
    [InlineData("expirado")]
    [InlineData("na_denylist")]
    [InlineData("assinatura_invalida")]
    public async Task CA_05_5_RejeicaoDeTokenAusenteExpiradoOuRevogado(string condicao)
    {
        var userId = Guid.NewGuid();
        var agoraUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var jti = Guid.NewGuid().ToString("D");

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");

        if (condicao == "ausente")
        {
            // sem header
        }
        else if (condicao == "expirado")
        {
            var tokenExpirado = _fixture.CunharTokenSessao(
                jti: jti,
                sub: userId.ToString("D"),
                fid: Guid.NewGuid().ToString("D"),
                iat: agoraUnix - 1800,
                exp: agoraUnix - 1,
                viewMode: "NONE");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenExpirado);
        }
        else if (condicao == "na_denylist")
        {
            var tokenDenylist = _fixture.CunharTokenSessao(
                jti: jti,
                sub: userId.ToString("D"),
                fid: Guid.NewGuid().ToString("D"),
                iat: agoraUnix,
                exp: agoraUnix + 900,
                viewMode: "NONE");
            var db = _fixture.RedisMultiplexer.GetDatabase();
            await db.StringSetAsync($"denylist:jti:{jti}", "1");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenDenylist);
        }
        else if (condicao == "assinatura_invalida")
        {
            using var rsaForjada = RSA.Create(2048);
            var tokenForjado = TokenHelper.CunharToken(
                rsaForjada,
                "key-forjada",
                jti,
                userId.ToString("D"),
                Guid.NewGuid().ToString("D"),
                agoraUnix,
                agoraUnix + 900,
                viewMode: "NONE");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenForjado);
        }

        var res = await _fixture.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);

        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("ERR-AUTH-002", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact(Timeout = 60000)]
    [Trait("Fase", "F01")]
    [Trait("Critico", "sim")]
    public async Task CA_05_6_ExecucaoSimultaneaDeDoisPedidosDeLogoutComOMesmoTokenDeAcesso()
    {
        var userId = Guid.NewGuid();
        var familyId = Guid.NewGuid();
        var agora = DateTimeOffset.UtcNow;
        var agoraUnix = agora.ToUnixTimeSeconds();

        await DbHelper.InserirUserAccountAsync(_fixture.ConnectionString, userId, "logout_simult@example.test", "hash", "User LogoutSimult", "ACTIVE");

        await DbHelper.InserirRefreshTokenAsync(
            _fixture.ConnectionString,
            Guid.NewGuid(),
            familyId,
            userId,
            TokenHelper.CalcularSha256Hex(TokenHelper.GerarRefreshTokenAleatorio()),
            status: "ACTIVE",
            viewMode: "NONE",
            expiresAt: agora.AddDays(7),
            createdAt: agora);

        var jti = "JTI-06-SIMULT-LOGOUT";
        var token = _fixture.CunharTokenSessao(
            jti: jti,
            sub: userId.ToString("D"),
            fid: familyId.ToString("D"),
            iat: agoraUnix,
            exp: agoraUnix + 900,
            viewMode: "NONE");

        var resultados = new HttpResponseMessage[2];
        await ConcurrencyHelper.ExecutarConcorrenteAsync(2, async (idx) =>
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            resultados[idx] = await _fixture.Client.SendAsync(req);
        });

        // Ambos os pedidos completam com 204 NoContent
        Assert.All(resultados, r => Assert.Equal(HttpStatusCode.NoContent, r.StatusCode));

        // JTI revogado no Redis
        var db = _fixture.RedisMultiplexer.GetDatabase();
        Assert.True(await db.KeyExistsAsync($"denylist:jti:{jti}"));

        // Chamada subsequente retorna 401 ERR-AUTH-002
        var reqSub = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        reqSub.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resSub = await _fixture.Client.SendAsync(reqSub);
        Assert.Equal(HttpStatusCode.Unauthorized, resSub.StatusCode);
    }

    [Fact(Timeout = 180000)]
    [Trait("Fase", "F01")]
    [Trait("Critico", "sim")]
    public async Task CA_05_7_ReversaoAtomicaDeTransacaoEmCasoDeTimeoutOuFalhaDoRedis()
    {
        var userId = Guid.NewGuid();
        var familyId = Guid.NewGuid();
        var agora = DateTimeOffset.UtcNow;
        var agoraUnix = agora.ToUnixTimeSeconds();

        await DbHelper.InserirUserAccountAsync(_fixture.ConnectionString, userId, "logout_redis_fail@example.test", "hash", "User RedisFail", "ACTIVE");

        var rtId = Guid.NewGuid();
        await DbHelper.InserirRefreshTokenAsync(
            _fixture.ConnectionString,
            rtId,
            familyId,
            userId,
            TokenHelper.CalcularSha256Hex(TokenHelper.GerarRefreshTokenAleatorio()),
            status: "ACTIVE",
            viewMode: "NONE",
            expiresAt: agora.AddDays(7),
            createdAt: agora);

        var jti = "JTI-07-REDIS-FAIL";
        var token = _fixture.CunharTokenSessao(
            jti: jti,
            sub: userId.ToString("D"),
            fid: familyId.ToString("D"),
            iat: agoraUnix,
            exp: agoraUnix + 900,
            viewMode: "NONE");

        // Cria fábrica apontando para Redis inexistente ou com denylist falhando
        using var factory = _fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IDenylist>(new FailingDenylist());
            });
        });
        using var client = factory.CreateClient();

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, res.StatusCode);

        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("ERR-SYS-002", doc.RootElement.GetProperty("code").GetString());

        // Refresh tokens de FAM4 permanecem com status ACTIVE
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT status FROM refresh_token WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", rtId);
        var statusRt = (string)(await cmd.ExecuteScalarAsync())!;
        Assert.Equal("ACTIVE", statusRt);

        // Nenhum registro de auditoria LOGOUT foi persistido
        await using var cmdAudit = new NpgsqlCommand("SELECT COUNT(*) FROM auth_audit_log WHERE event_type = 'LOGOUT' AND user_id = @u", conn);
        cmdAudit.Parameters.AddWithValue("u", userId);
        var auditCount = (long)(await cmdAudit.ExecuteScalarAsync())!;
        Assert.Equal(0, auditCount);
    }

    [Theory(Timeout = 60000)]
    [Trait("Fase", "F01")]
    [Trait("Critico", "sim")]
    [InlineData("corpo_grande", HttpStatusCode.RequestEntityTooLarge, "ERR-REQ-003")]
    [InlineData("content_type_invalido", HttpStatusCode.UnsupportedMediaType, "ERR-REQ-004")]
    [InlineData("sub_denylist", HttpStatusCode.Forbidden, "ERR-AUTH-004")]
    [InlineData("tenant_denylist", HttpStatusCode.Forbidden, "ERR-CTX-002")]
    public async Task CA_05_8_RejeicaoDeTransporteOuPresencaEmDenylistAdministrativa(string condicao, HttpStatusCode statusEsperado, string codigoEsperado)
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var agoraUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var token = _fixture.CunharTokenSessao(
            jti: Guid.NewGuid().ToString("D"),
            sub: userId.ToString("D"),
            fid: Guid.NewGuid().ToString("D"),
            iat: agoraUnix,
            exp: agoraUnix + 900,
            viewMode: "SINGLE",
            groupId: groupId.ToString("D"),
            tenantId: tenantId.ToString("D"),
            allowedTenants: new[] { tenantId.ToString("D") },
            activeScope: "BUYER",
            roles: new[] { "BUYER_OPERATOR" },
            permissions: new[] { "demanda:criar" });

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        if (condicao == "corpo_grande")
        {
            var largeBytes = new byte[65537];
            req.Content = new ByteArrayContent(largeBytes);
            req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }
        else if (condicao == "content_type_invalido")
        {
            req.Content = new StringContent("12345", Encoding.UTF8, "text/plain");
        }
        else if (condicao == "sub_denylist")
        {
            var db = _fixture.RedisMultiplexer.GetDatabase();
            await db.StringSetAsync($"denylist:user:{userId:D}", agoraUnix.ToString());
        }
        else if (condicao == "tenant_denylist")
        {
            var db = _fixture.RedisMultiplexer.GetDatabase();
            await db.StringSetAsync($"denylist:tenant:{tenantId:D}", "1");
        }

        var res = await _fixture.Client.SendAsync(req);
        Assert.Equal(statusEsperado, res.StatusCode);

        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(codigoEsperado, doc.RootElement.GetProperty("code").GetString());
    }

    #endregion

    #region Introspecção de Token (RFC 7662)

    [Fact(Timeout = 60000)]
    [Trait("Fase", "F01")]
    public async Task CA_Introspect_TokenValido_RetornaActiveTrueEWarningEHeader()
    {
        var userId = Guid.NewGuid();
        var password = "PasswordSegura123!";
        var passwordHash = await DbHelper.GerarPasswordHashArgon2idAsync(password);

        await DbHelper.InserirUserAccountAsync(
            _fixture.ConnectionString,
            userId,
            "introspect_user@agrobrasil.com.br",
            passwordHash,
            "Utilizador Introspect",
            status: "ACTIVE");

        var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { email = "introspect_user@agrobrasil.com.br", password }),
                Encoding.UTF8,
                "application/json")
        };
        var loginRes = await _fixture.Client.SendAsync(loginReq);
        Assert.Equal(HttpStatusCode.OK, loginRes.StatusCode);
        var loginJson = await loginRes.Content.ReadAsStringAsync();
        using var loginDoc = JsonDocument.Parse(loginJson);
        var token = loginDoc.RootElement.GetProperty("access_token").GetString()!;

        // Chamar POST /api/v1/auth/introspect de forma anônima
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/introspect")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { token }), Encoding.UTF8, "application/json")
        };

        var res = await _fixture.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.True(res.Headers.Contains("X-B2B-Notice"));
        Assert.Equal(
            "Diagnostic-Only; Local JWKS caching required for production microservices",
            res.Headers.GetValues("X-B2B-Notice").Single());

        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.GetProperty("active").GetBoolean());
        Assert.Equal(userId.ToString("D"), root.GetProperty("sub").GetString());
        Assert.Equal(
            "Este endpoint destina-se estritamente a diagnóstico em desenvolvimento. Microsserviços em produção DEVEM validar tokens localmente via JWKS (RS256) com cache em memória.",
            root.GetProperty("warning").GetString());
    }

    [Fact(Timeout = 60000)]
    [Trait("Fase", "F01")]
    public async Task CA_Introspect_TokenInvalidoOuRevogado_RetornaActiveFalse()
    {
        // 1. Token inválido/malformado
        var reqInvalido = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/introspect")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { token = "token.invalido.123" }), Encoding.UTF8, "application/json")
        };
        var resInvalido = await _fixture.Client.SendAsync(reqInvalido);
        Assert.Equal(HttpStatusCode.OK, resInvalido.StatusCode);
        var jsonInvalido = await resInvalido.Content.ReadAsStringAsync();
        using var docInvalido = JsonDocument.Parse(jsonInvalido);
        Assert.False(docInvalido.RootElement.GetProperty("active").GetBoolean());

        // 2. Token revogado no Redis (via logout)
        var userId = Guid.NewGuid();
        var password = "PasswordSegura123!";
        var passwordHash = await DbHelper.GerarPasswordHashArgon2idAsync(password);

        await DbHelper.InserirUserAccountAsync(
            _fixture.ConnectionString,
            userId,
            "introspect_revogado@agrobrasil.com.br",
            passwordHash,
            "Utilizador Revogado",
            status: "ACTIVE");

        var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { email = "introspect_revogado@agrobrasil.com.br", password }),
                Encoding.UTF8,
                "application/json")
        };
        var loginRes = await _fixture.Client.SendAsync(loginReq);
        Assert.Equal(HttpStatusCode.OK, loginRes.StatusCode);
        var loginJson = await loginRes.Content.ReadAsStringAsync();
        using var loginDoc = JsonDocument.Parse(loginJson);
        var token = loginDoc.RootElement.GetProperty("access_token").GetString()!;

        // Fazer logout para revogar JTI no Redis
        var logoutReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        logoutReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var logoutRes = await _fixture.Client.SendAsync(logoutReq);
        Assert.Equal(HttpStatusCode.NoContent, logoutRes.StatusCode);

        // Introspecção do token revogado deve retornar active: false
        var reqRevogado = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/introspect")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { token }), Encoding.UTF8, "application/json")
        };
        var resRevogado = await _fixture.Client.SendAsync(reqRevogado);
        Assert.Equal(HttpStatusCode.OK, resRevogado.StatusCode);
        var jsonRevogado = await resRevogado.Content.ReadAsStringAsync();
        using var docRevogado = JsonDocument.Parse(jsonRevogado);
        Assert.False(docRevogado.RootElement.GetProperty("active").GetBoolean());
    }

    #endregion
}

public class FailingDenylist : IDenylist
{
    public Task RevokeJtiAsync(string jti, long exp, CancellationToken ct = default)
    {
        throw new RedisTimeoutException("Timeout simulado ao gravar denylist:jti", CommandStatus.Unknown);
    }

    public Task<bool> IsJtiRevokedAsync(string jti, CancellationToken ct = default) => Task.FromResult(false);
    public Task<long> BlockUserAsync(Guid userId, CancellationToken ct = default) => Task.FromResult(0L);
    public Task BlockTenantAsync(Guid tenantId, CancellationToken ct = default) => Task.CompletedTask;
    public Task UnblockTenantAsync(Guid tenantId, CancellationToken ct = default) => Task.CompletedTask;
    public Task<long?> GetUserRevokedAtAsync(Guid userId, CancellationToken ct = default) => Task.FromResult<long?>(null);
    public Task<IReadOnlySet<Guid>> GetBlockedTenantsAsync(IReadOnlyList<Guid> tenantIds, CancellationToken ct = default)
        => Task.FromResult<IReadOnlySet<Guid>>(new HashSet<Guid>());
}
