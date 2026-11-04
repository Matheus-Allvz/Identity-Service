using System.Data;
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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using StackExchange.Redis;
using Testcontainers.Kafka;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Xunit;

namespace IdentityService.Acceptance;

[Trait("Fase", "F03")]
public sealed class F03AceitacaoTests : IClassFixture<F03AceitacaoTests.F03Fixture>, IAsyncLifetime
{
    private readonly F03Fixture _fixture;

    public F03AceitacaoTests(F03Fixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await _fixture.ResetDatabaseAndRedisAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    #region CA-10.1

    [Fact(Timeout = 60000)]
    [Trait("Critico", "sim")]
    public async Task CA_10_1_SuspensaoGlobalPorPlatformAdmin()
    {
        var adminId = Guid.Parse("0192e2c0-0000-7000-8000-000000000001");
        var targetUserId = Guid.Parse("0192e2c0-0001-7000-8000-000000000001");
        var familyId = Guid.NewGuid();

        await _fixture.InsertUserAccountAsync(adminId, "platadmin@example.test", "Utilizador Teste 99", "ACTIVE", "PLATFORM_ADMIN");
        await _fixture.InsertUserAccountAsync(targetUserId, "alvo01@example.test", "Utilizador Teste 01", "ACTIVE", "NONE");

        var refreshHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        await _fixture.InsertRefreshTokenAsync(Guid.NewGuid(), familyId, targetUserId, refreshHash, "ACTIVE", "NONE");

        var token = _fixture.CunharToken(sub: adminId, platformRole: "PLATFORM_ADMIN", viewMode: "NONE");

        using var client = _fixture.CreateHttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new
        {
            status = "SUSPENDED",
            reason = "12345"
        };

        var response = await client.PatchAsJsonAsync($"/api/v1/users/{targetUserId:D}/status", payload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        Assert.Equal(targetUserId.ToString("D"), root.GetProperty("user_id").GetString());
        Assert.Equal("SUSPENDED", root.GetProperty("status").GetString());
        Assert.Equal("GLOBAL", root.GetProperty("scope").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("group_id").ValueKind);

        var user = await _fixture.GetUserAsync(targetUserId);
        Assert.NotNull(user);
        Assert.Equal("SUSPENDED", user.Status);

        var familyActiveCount = await _fixture.GetActiveRefreshTokensForUserAsync(targetUserId);
        Assert.Equal(0, familyActiveCount);

        var denylistVal = await _fixture.GetRedisStringAsync($"denylist:user:{targetUserId:D}");
        Assert.NotNull(denylistVal);

        var outboxTopic = await _fixture.GetLatestOutboxTopicAsync(targetUserId.ToString("D"));
        Assert.Equal("usuario_bloqueado", outboxTopic);

        var auditCount = await _fixture.GetAuditCountAsync("USER_STATUS_CHANGED", userId: targetUserId);
        Assert.True(auditCount >= 1);
    }

    [Fact(Timeout = 60000)]
    [Trait("Critico", "sim")]
    public async Task CA_10_1_Borda_SuspensaoGlobalPorPlatformAdmin()
    {
        var adminId = Guid.Parse("0192e2c0-0000-7000-8000-000000000001");
        var targetUserId = Guid.Parse("0192e2c0-0001-7000-8000-000000000002");

        await _fixture.InsertUserAccountAsync(adminId, "platadmin@example.test", "Utilizador Teste 99", "ACTIVE", "PLATFORM_ADMIN");
        await _fixture.InsertUserAccountAsync(targetUserId, "alvo02@example.test", "Utilizador Teste 02", "ACTIVE", "NONE");

        var token = _fixture.CunharToken(sub: adminId, platformRole: "PLATFORM_ADMIN", viewMode: "NONE");

        using var client = _fixture.CreateHttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        string reason255 = new string('x', 255);
        var payload = new
        {
            status = "SUSPENDED",
            reason = reason255
        };

        var response = await client.PatchAsJsonAsync($"/api/v1/users/{targetUserId:D}/status", payload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("SUSPENDED", doc.RootElement.GetProperty("status").GetString());
    }

    #endregion

    #region CA-10.2

    [Fact(Timeout = 60000)]
    [Trait("Critico", "sim")]
    public async Task CA_10_2_SuspensaoEmGrupoPorTenantAdminAlteraNaoDonos()
    {
        var groupId = Guid.Parse("0192e2c0-00a1-7000-8000-000000000001");
        var tenantId = Guid.Parse("0192e2c0-00b1-7000-8000-000000000001");
        var adminId = Guid.Parse("0192e2c0-0000-7000-8000-000000000002");
        var targetUserId = Guid.Parse("0192e2c0-0001-7000-8000-000000000003");
        var familyId = Guid.NewGuid();

        await _fixture.InsertEconomicGroupAsync(groupId, "Grupo Suspensao Local", "ACTIVE");
        await _fixture.InsertTenantAsync(tenantId, groupId, "99000001000101", "Tenant Membro Local LTDA", "HQ", 0, 0, "ACTIVE");
        await _fixture.InsertTenantScopeAsync(Guid.NewGuid(), tenantId, "BUYER");

        await _fixture.InsertUserAccountAsync(adminId, "adminlocal@example.test", "Utilizador Teste 04", "ACTIVE", "NONE");
        await _fixture.InsertUserAccountAsync(targetUserId, "alvolocal@example.test", "Utilizador Teste 05", "ACTIVE", "NONE");

        var memberId = Guid.NewGuid();
        await _fixture.InsertTenantMemberAsync(memberId, tenantId, targetUserId, isOwner: false, canViewGroup: true, "ACTIVE");

        var refreshHash = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        await _fixture.InsertRefreshTokenAsync(Guid.NewGuid(), familyId, targetUserId, refreshHash, "ACTIVE", "SINGLE", groupId, tenantId, new[] { tenantId }, "BUYER");

        var token = _fixture.CunharToken(
            sub: adminId,
            platformRole: "NONE",
            viewMode: "SINGLE",
            groupId: groupId,
            tenantId: tenantId,
            allowedTenants: new[] { tenantId },
            activeScope: "TENANT_ADMIN",
            permissions: new[] { "usuario:suspender" });

        using var client = _fixture.CreateHttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new
        {
            status = "SUSPENDED",
            reason = "Suspenso"
        };

        var response = await client.PatchAsJsonAsync($"/api/v1/users/{targetUserId:D}/status", payload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        Assert.Equal("SUSPENDED", root.GetProperty("status").GetString());
        Assert.Equal("GROUP", root.GetProperty("scope").GetString());
        Assert.Equal(groupId.ToString("D"), root.GetProperty("group_id").GetString());

        var memberStatus = await _fixture.GetMemberStatusAsync(memberId);
        Assert.Equal("SUSPENDED", memberStatus);

        var user = await _fixture.GetUserAsync(targetUserId);
        Assert.NotNull(user);
        Assert.Equal("ACTIVE", user.Status);

        var activeRefreshCount = await _fixture.GetActiveRefreshTokensForUserAsync(targetUserId);
        Assert.Equal(0, activeRefreshCount);

        var denylistVal = await _fixture.GetRedisStringAsync($"denylist:user:{targetUserId:D}");
        Assert.Null(denylistVal);

        var outboxCount = await _fixture.GetOutboxCountByTopicAsync("usuario_bloqueado", targetUserId.ToString("D"));
        Assert.Equal(0, outboxCount);

        var auditCount = await _fixture.GetAuditCountAsync("USER_STATUS_CHANGED", userId: targetUserId, groupId: groupId);
        Assert.True(auditCount >= 1);
    }

    [Fact(Timeout = 60000)]
    [Trait("Critico", "sim")]
    public async Task CA_10_2_Borda_SuspensaoEmGrupoPorTenantAdminAlteraNaoDonos()
    {
        var groupId = Guid.Parse("0192e2c0-00a1-7000-8000-000000000001");
        var tenant1 = Guid.Parse("0192e2c0-00b1-7000-8000-000000000001");
        var tenant2 = Guid.Parse("0192e2c0-00b2-7000-8000-000000000002");
        var adminId = Guid.Parse("0192e2c0-0000-7000-8000-000000000002");
        var targetUserId = Guid.Parse("0192e2c0-0001-7000-8000-000000000004");

        await _fixture.InsertEconomicGroupAsync(groupId, "Grupo Multiplos Vinculos", "ACTIVE");
        await _fixture.InsertTenantAsync(tenant1, groupId, "99000001000101", "Tenant 1", "HQ", 0, 0, "ACTIVE");
        await _fixture.InsertTenantAsync(tenant2, groupId, "99000002000148", "Tenant 2", "BRANCH", 0, 0, "ACTIVE");

        await _fixture.InsertTenantScopeAsync(Guid.NewGuid(), tenant1, "BUYER");
        await _fixture.InsertTenantScopeAsync(Guid.NewGuid(), tenant2, "BUYER");

        await _fixture.InsertUserAccountAsync(adminId, "adminlocal@example.test", "Utilizador Teste 04", "ACTIVE", "NONE");
        await _fixture.InsertUserAccountAsync(targetUserId, "alvomulti@example.test", "Utilizador Teste 06", "ACTIVE", "NONE");

        var memberId1 = Guid.NewGuid();
        var memberId2 = Guid.NewGuid();

        await _fixture.InsertTenantMemberAsync(memberId1, tenant1, targetUserId, isOwner: false, canViewGroup: true, "ACTIVE");
        await _fixture.InsertTenantMemberAsync(memberId2, tenant2, targetUserId, isOwner: true, canViewGroup: true, "ACTIVE");

        var token = _fixture.CunharToken(
            sub: adminId,
            platformRole: "NONE",
            viewMode: "SINGLE",
            groupId: groupId,
            tenantId: tenant1,
            allowedTenants: new[] { tenant1 },
            activeScope: "TENANT_ADMIN",
            permissions: new[] { "usuario:suspender" });

        using var client = _fixture.CreateHttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new
        {
            status = "SUSPENDED",
            reason = "Suspensao seletiva"
        };

        var response = await client.PatchAsJsonAsync($"/api/v1/users/{targetUserId:D}/status", payload);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("ERR-CTX-007", doc.RootElement.GetProperty("code").GetString());
    }

    #endregion

    #region CA-10.3

    [Theory(Timeout = 60000)]
    [InlineData("proprio_usuario")]
    [InlineData("proprietario")]
    [InlineData("sem_vinculo")]
    public async Task CA_10_3_RejeicaoSeC9ForViolada(string tipo)
    {
        var groupId = Guid.Parse("0192e2c0-00a1-7000-8000-000000000001");
        var tenantId = Guid.Parse("0192e2c0-00b1-7000-8000-000000000001");
        var adminId = Guid.Parse("0192e2c0-0000-7000-8000-000000000002");
        var targetUserId = (tipo == "proprio_usuario") ? adminId : Guid.NewGuid();

        await _fixture.InsertEconomicGroupAsync(groupId, "Grupo Violacao C9", "ACTIVE");
        await _fixture.InsertTenantAsync(tenantId, groupId, "99000001000101", "Tenant C9", "HQ", 0, 0, "ACTIVE");
        await _fixture.InsertTenantScopeAsync(Guid.NewGuid(), tenantId, "BUYER");

        await _fixture.InsertUserAccountAsync(adminId, "admin@example.test", "Utilizador Teste 04", "ACTIVE", "NONE");

        if (tipo != "proprio_usuario")
        {
            await _fixture.InsertUserAccountAsync(targetUserId, "alvo@example.test", "Utilizador Teste 07", "ACTIVE", "NONE");
            if (tipo == "proprietario")
            {
                await _fixture.InsertTenantMemberAsync(Guid.NewGuid(), tenantId, targetUserId, isOwner: true, canViewGroup: true, "ACTIVE");
            }
        }

        var token = _fixture.CunharToken(
            sub: adminId,
            platformRole: "NONE",
            viewMode: "SINGLE",
            groupId: groupId,
            tenantId: tenantId,
            allowedTenants: new[] { tenantId },
            activeScope: "TENANT_ADMIN",
            permissions: new[] { "usuario:suspender" });

        using var client = _fixture.CreateHttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new
        {
            status = "SUSPENDED",
            reason = "Tentativa violacao C9"
        };

        var response = await client.PatchAsJsonAsync($"/api/v1/users/{targetUserId:D}/status", payload);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("ERR-CTX-007", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact(Timeout = 60000)]
    public async Task CA_10_3_Borda_RejeicaoSeC9ForViolada()
    {
        var groupId = Guid.Parse("0192e2c0-00a1-7000-8000-000000000001");
        var tenantId = Guid.Parse("0192e2c0-00b1-7000-8000-000000000001");
        var adminId = Guid.Parse("0192e2c0-0000-7000-8000-000000000002");
        var targetUserId = Guid.NewGuid();

        await _fixture.InsertEconomicGroupAsync(groupId, "Grupo Sem Permissao Usuario", "ACTIVE");
        await _fixture.InsertTenantAsync(tenantId, groupId, "99000001000101", "Tenant C9 Borda", "HQ", 0, 0, "ACTIVE");
        await _fixture.InsertTenantScopeAsync(Guid.NewGuid(), tenantId, "BUYER");

        await _fixture.InsertUserAccountAsync(adminId, "admin@example.test", "Utilizador Teste 04", "ACTIVE", "NONE");
        await _fixture.InsertUserAccountAsync(targetUserId, "alvo@example.test", "Utilizador Teste 07", "ACTIVE", "NONE");
        await _fixture.InsertTenantMemberAsync(Guid.NewGuid(), tenantId, targetUserId, isOwner: false, canViewGroup: true, "ACTIVE");

        var token = _fixture.CunharToken(
            sub: adminId,
            platformRole: "NONE",
            viewMode: "SINGLE",
            groupId: groupId,
            tenantId: tenantId,
            allowedTenants: new[] { tenantId },
            activeScope: "TENANT_ADMIN",
            permissions: new[] { "membro:vincular" });

        using var client = _fixture.CreateHttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new
        {
            status = "SUSPENDED",
            reason = "Tentativa sem permissao"
        };

        var response = await client.PatchAsJsonAsync($"/api/v1/users/{targetUserId:D}/status", payload);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("ERR-CTX-007", doc.RootElement.GetProperty("code").GetString());
    }

    #endregion

    #region CA-10.4

    [Theory(Timeout = 60000)]
    [InlineData("PLATFORM_ADMIN", HttpStatusCode.NotFound, "ERR-CTX-008")]
    [InlineData("TENANT_ADMIN", HttpStatusCode.Forbidden, "ERR-CTX-007")]
    public async Task CA_10_4_AlvoInexistenteDevolve404Ou403(string ator, HttpStatusCode statusEsperado, string erroEsperado)
    {
        var callerId = Guid.NewGuid();
        var inexistenteUserId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        await _fixture.InsertUserAccountAsync(callerId, "caller@example.test", "Utilizador Teste 10", "ACTIVE", ator == "PLATFORM_ADMIN" ? "PLATFORM_ADMIN" : "NONE");
        if (ator != "PLATFORM_ADMIN")
        {
            await _fixture.InsertEconomicGroupAsync(groupId, "Grupo Teste", "ACTIVE");
            await _fixture.InsertTenantAsync(tenantId, groupId, "99000001000101", "Tenant Teste", "HQ", 0, 0, "ACTIVE");
            await _fixture.InsertTenantScopeAsync(Guid.NewGuid(), tenantId, "BUYER");
            await _fixture.InsertTenantMemberAsync(Guid.NewGuid(), tenantId, callerId, isOwner: true, canViewGroup: true, "ACTIVE");
        }

        var token = ator == "PLATFORM_ADMIN"
            ? _fixture.CunharToken(sub: callerId, platformRole: "PLATFORM_ADMIN", viewMode: "NONE")
            : _fixture.CunharToken(
                sub: callerId,
                platformRole: "NONE",
                viewMode: "SINGLE",
                groupId: groupId,
                tenantId: tenantId,
                allowedTenants: new[] { tenantId },
                activeScope: "TENANT_ADMIN",
                permissions: new[] { "usuario:suspender" });

        using var client = _fixture.CreateHttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new
        {
            status = "SUSPENDED",
            reason = "Alvo Inexistente"
        };

        var response = await client.PatchAsJsonAsync($"/api/v1/users/{inexistenteUserId:D}/status", payload);
        Assert.Equal(statusEsperado, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(erroEsperado, doc.RootElement.GetProperty("code").GetString());
    }

    [Fact(Timeout = 60000)]
    public async Task CA_10_4_Borda_AlvoInexistenteDevolve404Ou403()
    {
        var adminId = Guid.NewGuid();
        await _fixture.InsertUserAccountAsync(adminId, "platadmin@example.test", "Utilizador Teste 99", "ACTIVE", "PLATFORM_ADMIN");

        var token = _fixture.CunharToken(sub: adminId, platformRole: "PLATFORM_ADMIN", viewMode: "NONE");

        using var client = _fixture.CreateHttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new
        {
            status = "SUSPENDED",
            reason = "Uuid Invalido"
        };

        var response = await client.PatchAsJsonAsync("/api/v1/users/uuid-invalido-formato/status", payload);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("ERR-VAL-001", doc.RootElement.GetProperty("code").GetString());
    }

    #endregion

    #region CA-10.5

    [Fact(Timeout = 60000)]
    [Trait("Critico", "sim")]
    public async Task CA_10_5_RepeticaoDeSuspendedEmGlobalReexecutaEfeitos()
    {
        var adminId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();
        var familyId = Guid.NewGuid();

        await _fixture.InsertUserAccountAsync(adminId, "platadmin@example.test", "Utilizador Teste 99", "ACTIVE", "PLATFORM_ADMIN");
        await _fixture.InsertUserAccountAsync(targetUserId, "alvojapedido@example.test", "Utilizador Teste 11", "SUSPENDED", "NONE");

        var refreshHash = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";
        await _fixture.InsertRefreshTokenAsync(Guid.NewGuid(), familyId, targetUserId, refreshHash, "ACTIVE", "NONE");

        var token = _fixture.CunharToken(sub: adminId, platformRole: "PLATFORM_ADMIN", viewMode: "NONE");

        using var client = _fixture.CreateHttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new
        {
            status = "SUSPENDED",
            reason = "Repetir"
        };

        var response = await client.PatchAsJsonAsync($"/api/v1/users/{targetUserId:D}/status", payload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("SUSPENDED", doc.RootElement.GetProperty("status").GetString());

        var activeRefreshCount = await _fixture.GetActiveRefreshTokensForUserAsync(targetUserId);
        Assert.Equal(0, activeRefreshCount);

        var denylistVal = await _fixture.GetRedisStringAsync($"denylist:user:{targetUserId:D}");
        Assert.NotNull(denylistVal);

        var outboxTopic = await _fixture.GetLatestOutboxTopicAsync(targetUserId.ToString("D"));
        Assert.Equal("usuario_bloqueado", outboxTopic);

        var auditCount = await _fixture.GetAuditCountAsync("USER_STATUS_CHANGED", userId: targetUserId);
        Assert.True(auditCount >= 1);
    }

    [Fact(Timeout = 60000)]
    [Trait("Critico", "sim")]
    public async Task CA_10_5_Borda_RepeticaoDeSuspendedEmGlobalReexecutaEfeitos()
    {
        var adminId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();

        await _fixture.InsertUserAccountAsync(adminId, "platadmin@example.test", "Utilizador Teste 99", "ACTIVE", "PLATFORM_ADMIN");
        await _fixture.InsertUserAccountAsync(targetUserId, "alvoconc@example.test", "Utilizador Teste 12", "ACTIVE", "NONE");

        var token = _fixture.CunharToken(sub: adminId, platformRole: "PLATFORM_ADMIN", viewMode: "NONE");

        for (int rep = 0; rep < 20; rep++)
        {
            using var barrier = new Barrier(2);
            var tasks = new Task<HttpResponseMessage>[2];

            for (int i = 0; i < 2; i++)
            {
                tasks[i] = Task.Run(async () =>
                {
                    using var c = _fixture.CreateHttpClient();
                    c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    var p = new
                    {
                        status = "SUSPENDED",
                        reason = "Concorrente"
                    };
                    barrier.SignalAndWait(TimeSpan.FromSeconds(10));
                    return await c.PatchAsJsonAsync($"/api/v1/users/{targetUserId:D}/status", p);
                });
            }

            var responses = await Task.WhenAll(tasks);
            Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        }
    }

    #endregion

    #region CA-13.1

    [Fact(Timeout = 60000)]
    public async Task CA_13_1_ReativacaoGlobalPorPlatformAdminDesbloqueiaLocked()
    {
        var adminId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();

        await _fixture.InsertUserAccountAsync(adminId, "platadmin@example.test", "Utilizador Teste 99", "ACTIVE", "PLATFORM_ADMIN");
        await _fixture.InsertUserAccountAsync(targetUserId, "alvolocked@example.test", "Utilizador Teste 13", "LOCKED", "NONE", failedAttempts: 5, lockedUntil: DateTimeOffset.UtcNow.AddMinutes(15));

        var token = _fixture.CunharToken(sub: adminId, platformRole: "PLATFORM_ADMIN", viewMode: "NONE");

        using var client = _fixture.CreateHttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new
        {
            status = "ACTIVE",
            reason = "Reativa"
        };

        var response = await client.PatchAsJsonAsync($"/api/v1/users/{targetUserId:D}/status", payload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        Assert.Equal("ACTIVE", root.GetProperty("status").GetString());
        Assert.Equal("GLOBAL", root.GetProperty("scope").GetString());

        var user = await _fixture.GetUserAsync(targetUserId);
        Assert.NotNull(user);
        Assert.Equal("ACTIVE", user.Status);
        Assert.Equal(0, user.FailedLoginAttempts);
        Assert.Null(user.LockedUntil);

        var outboxCount = await _fixture.GetOutboxCountByTopicAsync("usuario_bloqueado", targetUserId.ToString("D"));
        Assert.Equal(0, outboxCount);

        var auditCount = await _fixture.GetAuditCountAsync("USER_STATUS_CHANGED", userId: targetUserId);
        Assert.True(auditCount >= 1);
    }

    [Fact(Timeout = 60000)]
    public async Task CA_13_1_Borda_ReativacaoGlobalPorPlatformAdminDesbloqueiaLocked()
    {
        var adminId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();

        await _fixture.InsertUserAccountAsync(adminId, "platadmin@example.test", "Utilizador Teste 99", "ACTIVE", "PLATFORM_ADMIN");
        await _fixture.InsertUserAccountAsync(targetUserId, "alvojaativo@example.test", "Utilizador Teste 14", "ACTIVE", "NONE");

        var token = _fixture.CunharToken(sub: adminId, platformRole: "PLATFORM_ADMIN", viewMode: "NONE");

        using var client = _fixture.CreateHttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new
        {
            status = "ACTIVE",
            reason = "Reativa Novamente"
        };

        var response = await client.PatchAsJsonAsync($"/api/v1/users/{targetUserId:D}/status", payload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("ACTIVE", doc.RootElement.GetProperty("status").GetString());
    }

    #endregion

    #region CA-13.2

    [Fact(Timeout = 60000)]
    public async Task CA_13_2_ReativacaoEmGrupoPorTenantAdminSemAlteracao()
    {
        var groupId = Guid.Parse("0192e2c0-00a1-7000-8000-000000000001");
        var tenantId = Guid.Parse("0192e2c0-00b1-7000-8000-000000000001");
        var adminId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();

        await _fixture.InsertEconomicGroupAsync(groupId, "Grupo Reativacao", "ACTIVE");
        await _fixture.InsertTenantAsync(tenantId, groupId, "99000001000101", "Tenant Reativacao", "HQ", 0, 0, "ACTIVE");
        await _fixture.InsertTenantScopeAsync(Guid.NewGuid(), tenantId, "BUYER");

        await _fixture.InsertUserAccountAsync(adminId, "admin@example.test", "Utilizador Teste 04", "ACTIVE", "NONE");
        await _fixture.InsertUserAccountAsync(targetUserId, "alvo@example.test", "Utilizador Teste 15", "ACTIVE", "NONE");

        await _fixture.InsertTenantMemberAsync(Guid.NewGuid(), tenantId, targetUserId, isOwner: false, canViewGroup: true, "ACTIVE");

        var token = _fixture.CunharToken(
            sub: adminId,
            platformRole: "NONE",
            viewMode: "SINGLE",
            groupId: groupId,
            tenantId: tenantId,
            allowedTenants: new[] { tenantId },
            activeScope: "TENANT_ADMIN",
            permissions: new[] { "usuario:suspender" });

        using var client = _fixture.CreateHttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new
        {
            status = "ACTIVE",
            reason = "Reativa"
        };

        var response = await client.PatchAsJsonAsync($"/api/v1/users/{targetUserId:D}/status", payload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        Assert.Equal("ACTIVE", root.GetProperty("status").GetString());
        Assert.Equal("GROUP", root.GetProperty("scope").GetString());
        Assert.NotNull(root.GetProperty("updated_at").GetString());

        var auditCount = await _fixture.GetAuditCountAsync("USER_STATUS_CHANGED", userId: targetUserId, groupId: groupId);
        Assert.True(auditCount >= 1);
    }

    [Fact(Timeout = 60000)]
    public async Task CA_13_2_Borda_ReativacaoEmGrupoPorTenantAdminSemAlteracao()
    {
        var groupId = Guid.Parse("0192e2c0-00a1-7000-8000-000000000001");
        var tenantId = Guid.Parse("0192e2c0-00b1-7000-8000-000000000001");
        var adminId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();

        await _fixture.InsertEconomicGroupAsync(groupId, "Grupo Reativacao Suspensa", "ACTIVE");
        await _fixture.InsertTenantAsync(tenantId, groupId, "99000001000101", "Tenant Reativacao Suspensa", "HQ", 0, 0, "ACTIVE");
        await _fixture.InsertTenantScopeAsync(Guid.NewGuid(), tenantId, "BUYER");

        await _fixture.InsertUserAccountAsync(adminId, "admin@example.test", "Utilizador Teste 04", "ACTIVE", "NONE");
        await _fixture.InsertUserAccountAsync(targetUserId, "alvo@example.test", "Utilizador Teste 15", "ACTIVE", "NONE");

        var memberId = Guid.NewGuid();
        await _fixture.InsertTenantMemberAsync(memberId, tenantId, targetUserId, isOwner: false, canViewGroup: true, "SUSPENDED");

        var token = _fixture.CunharToken(
            sub: adminId,
            platformRole: "NONE",
            viewMode: "SINGLE",
            groupId: groupId,
            tenantId: tenantId,
            allowedTenants: new[] { tenantId },
            activeScope: "TENANT_ADMIN",
            permissions: new[] { "usuario:suspender" });

        using var client = _fixture.CreateHttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new
        {
            status = "ACTIVE",
            reason = "Reativar Membro"
        };

        var response = await client.PatchAsJsonAsync($"/api/v1/users/{targetUserId:D}/status", payload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var memberStatus = await _fixture.GetMemberStatusAsync(memberId);
        Assert.Equal("ACTIVE", memberStatus);
    }

    #endregion

    #region CA-13.3

    [Theory(Timeout = 60000)]
    [InlineData("{\"status\":\"ACTIVE\",\"reason\":\"Reativa\"}", HttpStatusCode.OK, "Nenhum")]
    [InlineData("{\"status\":\"LOCKED\",\"reason\":\"Bloqueio\"}", HttpStatusCode.BadRequest, "ERR-VAL-001")]
    [InlineData("{\"status\":\"ACTIVE\",\"reason\":\"1234\"}", HttpStatusCode.BadRequest, "ERR-VAL-001")]
    public async Task CA_13_3_ValidacaoDeCorpoMalformadoERepeticao(string corpo, HttpStatusCode statusEsperado, string erroEsperado)
    {
        var adminId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();

        await _fixture.InsertUserAccountAsync(adminId, "platadmin@example.test", "Utilizador Teste 99", "ACTIVE", "PLATFORM_ADMIN");
        await _fixture.InsertUserAccountAsync(targetUserId, "alvo@example.test", "Utilizador Teste 16", "ACTIVE", "NONE");

        var token = _fixture.CunharToken(sub: adminId, platformRole: "PLATFORM_ADMIN", viewMode: "NONE");

        using var client = _fixture.CreateHttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var content = new StringContent(corpo, Encoding.UTF8, "application/json");
        var response = await client.PatchAsync($"/api/v1/users/{targetUserId:D}/status", content);

        Assert.Equal(statusEsperado, response.StatusCode);
        if (erroEsperado != "Nenhum")
        {
            var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(erroEsperado, doc.RootElement.GetProperty("code").GetString());
        }
    }

    [Fact(Timeout = 60000)]
    public async Task CA_13_3_Borda_ValidacaoDeCorpoMalformadoERepeticao()
    {
        var adminId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();

        await _fixture.InsertUserAccountAsync(adminId, "platadmin@example.test", "Utilizador Teste 99", "ACTIVE", "PLATFORM_ADMIN");
        await _fixture.InsertUserAccountAsync(targetUserId, "alvo@example.test", "Utilizador Teste 16", "ACTIVE", "NONE");

        var token = _fixture.CunharToken(sub: adminId, platformRole: "PLATFORM_ADMIN", viewMode: "NONE");

        using var client = _fixture.CreateHttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        string reason256 = new string('z', 256);
        var payloadLong = new { status = "ACTIVE", reason = reason256 };
        var resLong = await client.PatchAsJsonAsync($"/api/v1/users/{targetUserId:D}/status", payloadLong);
        Assert.Equal(HttpStatusCode.BadRequest, resLong.StatusCode);

        var payloadBlank = new { status = "ACTIVE", reason = "       " };
        var resBlank = await client.PatchAsJsonAsync($"/api/v1/users/{targetUserId:D}/status", payloadBlank);
        Assert.Equal(HttpStatusCode.BadRequest, resBlank.StatusCode);
    }

    #endregion

    #region CA-09.1

    [Fact(Timeout = 60000)]
    [Trait("Critico", "sim")]
    public async Task CA_09_1_SuspensaoDeTenantComSucessoPorPlatformAdmin()
    {
        var groupId = Guid.NewGuid();
        var tenantId = Guid.Parse("01900000-0000-7000-8000-000000000100");
        var adminId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var familyId = Guid.NewGuid();

        await _fixture.InsertEconomicGroupAsync(groupId, "Grupo Tenant Suspensao", "ACTIVE");
        await _fixture.InsertTenantAsync(tenantId, groupId, "99000001000101", "Tenant Para Suspender", "HQ", 0, 0, "ACTIVE");
        await _fixture.InsertTenantScopeAsync(Guid.NewGuid(), tenantId, "BUYER");

        await _fixture.InsertUserAccountAsync(adminId, "platadmin@example.test", "Utilizador Teste 99", "ACTIVE", "PLATFORM_ADMIN");
        await _fixture.InsertUserAccountAsync(userId, "usuario@example.test", "Utilizador Teste 17", "ACTIVE", "NONE");

        var refreshHash = "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd";
        await _fixture.InsertRefreshTokenAsync(Guid.NewGuid(), familyId, userId, refreshHash, "ACTIVE", "SINGLE", groupId, tenantId, new[] { tenantId }, "BUYER");

        var token = _fixture.CunharToken(sub: adminId, platformRole: "PLATFORM_ADMIN", viewMode: "NONE");

        using var client = _fixture.CreateHttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new
        {
            status = "SUSPENDED",
            reason = "Fraude cadastral"
        };

        var response = await client.PatchAsJsonAsync($"/api/v1/tenants/{tenantId:D}/status", payload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("SUSPENDED", doc.RootElement.GetProperty("status").GetString());

        var tenant = await _fixture.GetTenantAsync(tenantId);
        Assert.NotNull(tenant);
        Assert.Equal("SUSPENDED", tenant.Status);

        var denylistVal = await _fixture.GetRedisStringAsync($"denylist:tenant:{tenantId:D}");
        Assert.Equal("1", denylistVal);

        var activeRefreshCount = await _fixture.GetActiveRefreshTokensForTenantAsync(tenantId);
        Assert.Equal(0, activeRefreshCount);

        var outboxTopic = await _fixture.GetLatestOutboxTopicAsync(tenantId.ToString("D"));
        Assert.Equal("tenant_bloqueado", outboxTopic);

        var auditCount = await _fixture.GetAuditCountAsync("TENANT_STATUS_CHANGED", tenantId: tenantId);
        Assert.True(auditCount >= 1);
    }

    [Fact(Timeout = 60000)]
    [Trait("Critico", "sim")]
    public async Task CA_09_1_Borda_SuspensaoDeTenantComSucessoPorPlatformAdmin()
    {
        var groupId = Guid.NewGuid();
        var tenantId = Guid.Parse("01900000-0000-7000-8000-000000000101");
        var ownerId = Guid.NewGuid();

        await _fixture.InsertEconomicGroupAsync(groupId, "Grupo Tenant Owner Suspensao", "ACTIVE");
        await _fixture.InsertTenantAsync(tenantId, groupId, "99000002000148", "Tenant Owner Suspensao", "HQ", 0, 0, "ACTIVE");
        await _fixture.InsertTenantScopeAsync(Guid.NewGuid(), tenantId, "TENANT_ADMIN");

        await _fixture.InsertUserAccountAsync(ownerId, "owner@example.test", "Utilizador Teste 18", "ACTIVE", "NONE");
        await _fixture.InsertTenantMemberAsync(Guid.NewGuid(), tenantId, ownerId, isOwner: true, canViewGroup: true, "ACTIVE");

        var token = _fixture.CunharToken(
            sub: ownerId,
            platformRole: "NONE",
            viewMode: "SINGLE",
            groupId: groupId,
            tenantId: tenantId,
            allowedTenants: new[] { tenantId },
            activeScope: "TENANT_ADMIN",
            permissions: new[] { "tenant:listar" });

        using var client = _fixture.CreateHttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new
        {
            status = "SUSPENDED",
            reason = "Auto suspensao pelo proprietario"
        };

        var response = await client.PatchAsJsonAsync($"/api/v1/tenants/{tenantId:D}/status", payload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var tenant = await _fixture.GetTenantAsync(tenantId);
        Assert.NotNull(tenant);
        Assert.Equal("SUSPENDED", tenant.Status);
    }

    #endregion

    #region CA-09.2

    [Fact(Timeout = 60000)]
    public async Task CA_09_2_SuspensaoRepetidaDeTenantJaSuspensoEIdempotenteERepeteEfeitos()
    {
        var groupId = Guid.NewGuid();
        var tenantId = Guid.Parse("01900000-0000-7000-8000-000000000100");
        var adminId = Guid.NewGuid();

        await _fixture.InsertEconomicGroupAsync(groupId, "Grupo Tenant Ja Suspenso", "ACTIVE");
        await _fixture.InsertTenantAsync(tenantId, groupId, "99000001000101", "Tenant Ja Suspenso", "HQ", 0, 0, "SUSPENDED");

        await _fixture.InsertUserAccountAsync(adminId, "platadmin@example.test", "Utilizador Teste 99", "ACTIVE", "PLATFORM_ADMIN");

        var token = _fixture.CunharToken(sub: adminId, platformRole: "PLATFORM_ADMIN", viewMode: "NONE");

        using var client = _fixture.CreateHttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new
        {
            status = "SUSPENDED",
            reason = "Reiteração cautelar"
        };

        var response = await client.PatchAsJsonAsync($"/api/v1/tenants/{tenantId:D}/status", payload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var denylistVal = await _fixture.GetRedisStringAsync($"denylist:tenant:{tenantId:D}");
        Assert.Equal("1", denylistVal);

        var outboxTopic = await _fixture.GetLatestOutboxTopicAsync(tenantId.ToString("D"));
        Assert.Equal("tenant_bloqueado", outboxTopic);

        var auditCount = await _fixture.GetAuditCountAsync("TENANT_STATUS_CHANGED", tenantId: tenantId);
        Assert.True(auditCount >= 1);
    }

    [Fact(Timeout = 60000)]
    public async Task CA_09_2_Borda_SuspensaoRepetidaDeTenantJaSuspensoEIdempotenteERepeteEfeitos()
    {
        var groupId = Guid.NewGuid();
        var tenantId = Guid.Parse("01900000-0000-7000-8000-000000000100");
        var adminId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var familyId = Guid.NewGuid();

        await _fixture.InsertEconomicGroupAsync(groupId, "Grupo Repetido Com Sessoes", "ACTIVE");
        await _fixture.InsertTenantAsync(tenantId, groupId, "99000001000101", "Tenant Com Sessao Nova", "HQ", 0, 0, "SUSPENDED");

        await _fixture.InsertUserAccountAsync(adminId, "platadmin@example.test", "Utilizador Teste 99", "ACTIVE", "PLATFORM_ADMIN");
        await _fixture.InsertUserAccountAsync(userId, "usuario@example.test", "Utilizador Teste 19", "ACTIVE", "NONE");

        var refreshHash = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
        await _fixture.InsertRefreshTokenAsync(Guid.NewGuid(), familyId, userId, refreshHash, "ACTIVE", "SINGLE", groupId, tenantId, new[] { tenantId }, "BUYER");

        var token = _fixture.CunharToken(sub: adminId, platformRole: "PLATFORM_ADMIN", viewMode: "NONE");

        using var client = _fixture.CreateHttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new
        {
            status = "SUSPENDED",
            reason = "Segunda suspensao"
        };

        var response = await client.PatchAsJsonAsync($"/api/v1/tenants/{tenantId:D}/status", payload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var activeRefreshCount = await _fixture.GetActiveRefreshTokensForTenantAsync(tenantId);
        Assert.Equal(0, activeRefreshCount);
    }

    #endregion

    #region CA-09.3

    [Theory(Timeout = 60000)]
    [InlineData("PLATFORM_ADMIN", "NONE", "1234", HttpStatusCode.BadRequest, "ERR-VAL-001")]
    [InlineData("PLATFORM_ADMIN", "NONE", "   ab   ", HttpStatusCode.BadRequest, "ERR-VAL-001")]
    [InlineData("NONE", "BUYER", "Suspensao indevida", HttpStatusCode.Forbidden, "ERR-CTX-007")]
    [InlineData("SUPPORT_AGENT", "NONE", "Tentativa suporte", HttpStatusCode.Forbidden, "ERR-CTX-007")]
    [Trait("Critico", "sim")]
    public async Task CA_09_3_ValidacaoDeBordasDoMotivoEAutorizacaoNaSuspensao(
        string rolePlataforma, string escopo, string motivo, HttpStatusCode statusHttp, string codigoErro)
    {
        var groupId = Guid.NewGuid();
        var tenantId = Guid.Parse("01900000-0000-7000-8000-000000000100");
        var userId = Guid.NewGuid();

        await _fixture.InsertEconomicGroupAsync(groupId, "Grupo Validacao Bordas", "ACTIVE");
        await _fixture.InsertTenantAsync(tenantId, groupId, "99000001000101", "Tenant Bordas", "HQ", 0, 0, "ACTIVE");
        await _fixture.InsertTenantScopeAsync(Guid.NewGuid(), tenantId, "BUYER");

        await _fixture.InsertUserAccountAsync(userId, "user@example.test", "Utilizador Teste 20", "ACTIVE", rolePlataforma);

        var token = _fixture.CunharToken(
            sub: userId,
            platformRole: rolePlataforma,
            viewMode: escopo == "NONE" ? "NONE" : "SINGLE",
            groupId: groupId,
            tenantId: tenantId,
            allowedTenants: new[] { tenantId },
            activeScope: escopo == "NONE" ? null : escopo);

        using var client = _fixture.CreateHttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new
        {
            status = "SUSPENDED",
            reason = motivo
        };

        var response = await client.PatchAsJsonAsync($"/api/v1/tenants/{tenantId:D}/status", payload);
        Assert.Equal(statusHttp, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(codigoErro, doc.RootElement.GetProperty("code").GetString());

        var tenant = await _fixture.GetTenantAsync(tenantId);
        Assert.NotNull(tenant);
        Assert.Equal("ACTIVE", tenant.Status);
    }

    [Fact(Timeout = 60000)]
    [Trait("Critico", "sim")]
    public async Task CA_09_3_Borda_ValidacaoDeBordasDoMotivoEAutorizacaoNaSuspensao()
    {
        var groupId = Guid.NewGuid();
        var tenantId = Guid.Parse("01900000-0000-7000-8000-000000000100");
        var nonOwnerAdminId = Guid.NewGuid();

        await _fixture.InsertEconomicGroupAsync(groupId, "Grupo Nao Dono", "ACTIVE");
        await _fixture.InsertTenantAsync(tenantId, groupId, "99000001000101", "Tenant Nao Dono", "HQ", 0, 0, "ACTIVE");
        await _fixture.InsertTenantScopeAsync(Guid.NewGuid(), tenantId, "TENANT_ADMIN");

        await _fixture.InsertUserAccountAsync(nonOwnerAdminId, "naodono@example.test", "Utilizador Teste 21", "ACTIVE", "NONE");
        await _fixture.InsertTenantMemberAsync(Guid.NewGuid(), tenantId, nonOwnerAdminId, isOwner: false, canViewGroup: true, "ACTIVE");

        var token = _fixture.CunharToken(
            sub: nonOwnerAdminId,
            platformRole: "NONE",
            viewMode: "SINGLE",
            groupId: groupId,
            tenantId: tenantId,
            allowedTenants: new[] { tenantId },
            activeScope: "TENANT_ADMIN");

        using var client = _fixture.CreateHttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new
        {
            status = "SUSPENDED",
            reason = "Tentativa nao dono"
        };

        var response = await client.PatchAsJsonAsync($"/api/v1/tenants/{tenantId:D}/status", payload);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("ERR-CTX-007", doc.RootElement.GetProperty("code").GetString());
    }

    #endregion

    #region CA-09.4

    [Fact(Timeout = 180000)]
    public async Task CA_09_4_FalhaDeDependenciaDoRedisNaSuspensaoAbortaOperacao()
    {
        var dedicatedRedis = new RedisBuilder("redis:7.4.6").Build();
        await dedicatedRedis.StartAsync();

        var adminId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var tenantId = Guid.Parse("01900000-0000-7000-8000-000000000100");

        await _fixture.InsertEconomicGroupAsync(groupId, "Grupo Falha Redis", "ACTIVE");
        await _fixture.InsertTenantAsync(tenantId, groupId, "99000001000101", "Tenant Falha Redis", "HQ", 0, 0, "ACTIVE");
        await _fixture.InsertUserAccountAsync(adminId, "platadmin@example.test", "Utilizador Teste 99", "ACTIVE", "PLATFORM_ADMIN");

        var dedicatedFactory = _fixture.CreateFactoryWithRedis(dedicatedRedis.GetConnectionString());
        var client = dedicatedFactory.CreateClient();

        await dedicatedRedis.StopAsync();

        try
        {
            var token = _fixture.CunharToken(sub: adminId, platformRole: "PLATFORM_ADMIN", viewMode: "NONE");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var payload = new
            {
                status = "SUSPENDED",
                reason = "Bloqueio falha redis"
            };

            var response = await client.PatchAsJsonAsync($"/api/v1/tenants/{tenantId:D}/status", payload);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

            var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("ERR-SYS-002", doc.RootElement.GetProperty("code").GetString());

            var tenant = await _fixture.GetTenantAsync(tenantId);
            Assert.NotNull(tenant);
            Assert.Equal("ACTIVE", tenant.Status);
        }
        finally
        {
            await dedicatedRedis.DisposeAsync();
            await dedicatedFactory.DisposeAsync();
        }
    }

    [Fact(Timeout = 60000)]
    public async Task CA_09_4_Borda_FalhaDeDependenciaDoRedisNaSuspensaoAbortaOperacao()
    {
        var adminId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var tenantId = Guid.Parse("01900000-0000-7000-8000-000000000100");

        await _fixture.InsertEconomicGroupAsync(groupId, "Grupo Concorrencia Tenant", "ACTIVE");
        await _fixture.InsertTenantAsync(tenantId, groupId, "99000001000101", "Tenant Concorrencia Suspensao", "HQ", 0, 0, "ACTIVE");
        await _fixture.InsertUserAccountAsync(adminId, "platadmin@example.test", "Utilizador Teste 99", "ACTIVE", "PLATFORM_ADMIN");

        var token = _fixture.CunharToken(sub: adminId, platformRole: "PLATFORM_ADMIN", viewMode: "NONE");

        for (int rep = 0; rep < 20; rep++)
        {
            using var barrier = new Barrier(2);
            var tasks = new Task<HttpResponseMessage>[2];

            for (int i = 0; i < 2; i++)
            {
                tasks[i] = Task.Run(async () =>
                {
                    using var c = _fixture.CreateHttpClient();
                    c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    var p = new
                    {
                        status = "SUSPENDED",
                        reason = "Suspensao concorrente"
                    };
                    barrier.SignalAndWait(TimeSpan.FromSeconds(10));
                    return await c.PatchAsJsonAsync($"/api/v1/tenants/{tenantId:D}/status", p);
                });
            }

            var responses = await Task.WhenAll(tasks);
            Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        }

        var tenant = await _fixture.GetTenantAsync(tenantId);
        Assert.NotNull(tenant);
        Assert.Equal("SUSPENDED", tenant.Status);
    }

    #endregion

    #region CA-12.1

    [Fact(Timeout = 60000)]
    [Trait("Critico", "sim")]
    public async Task CA_12_1_ReativacaoDeTenantSuspensoComRemocaoDaDenylist()
    {
        var groupId = Guid.NewGuid();
        var tenantId = Guid.Parse("01900000-0000-7000-8000-000000000100");
        var adminId = Guid.NewGuid();

        await _fixture.InsertEconomicGroupAsync(groupId, "Grupo Reativacao Tenant", "ACTIVE");
        await _fixture.InsertTenantAsync(tenantId, groupId, "99000001000101", "Tenant Suspenso Reativar", "HQ", 0, 0, "SUSPENDED");
        await _fixture.InsertUserAccountAsync(adminId, "platadmin@example.test", "Utilizador Teste 99", "ACTIVE", "PLATFORM_ADMIN");

        await _fixture.SetRedisStringAsync($"denylist:tenant:{tenantId:D}", "1", TimeSpan.FromMinutes(15));

        var token = _fixture.CunharToken(sub: adminId, platformRole: "PLATFORM_ADMIN", viewMode: "NONE");

        using var client = _fixture.CreateHttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new
        {
            status = "ACTIVE",
            reason = "Regularização concluída"
        };

        var response = await client.PatchAsJsonAsync($"/api/v1/tenants/{tenantId:D}/status", payload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("ACTIVE", doc.RootElement.GetProperty("status").GetString());

        var tenant = await _fixture.GetTenantAsync(tenantId);
        Assert.NotNull(tenant);
        Assert.Equal("ACTIVE", tenant.Status);

        var denylistVal = await _fixture.GetRedisStringAsync($"denylist:tenant:{tenantId:D}");
        Assert.Null(denylistVal);

        var outboxCount = await _fixture.GetOutboxCountByTopicAsync("tenant_bloqueado", tenantId.ToString("D"));
        Assert.Equal(0, outboxCount);

        var auditCount = await _fixture.GetAuditCountAsync("TENANT_STATUS_CHANGED", tenantId: tenantId);
        Assert.True(auditCount >= 1);
    }

    [Fact(Timeout = 60000)]
    [Trait("Critico", "sim")]
    public async Task CA_12_1_Borda_ReativacaoDeTenantSuspensoComRemocaoDaDenylist()
    {
        var groupId = Guid.NewGuid();
        var tenantId = Guid.Parse("01900000-0000-7000-8000-000000000100");
        var adminId = Guid.NewGuid();

        await _fixture.InsertEconomicGroupAsync(groupId, "Grupo Reativacao Sem Redis", "ACTIVE");
        await _fixture.InsertTenantAsync(tenantId, groupId, "99000001000101", "Tenant Suspenso Sem Chave Redis", "HQ", 0, 0, "SUSPENDED");
        await _fixture.InsertUserAccountAsync(adminId, "platadmin@example.test", "Utilizador Teste 99", "ACTIVE", "PLATFORM_ADMIN");

        var token = _fixture.CunharToken(sub: adminId, platformRole: "PLATFORM_ADMIN", viewMode: "NONE");

        using var client = _fixture.CreateHttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new
        {
            status = "ACTIVE",
            reason = "Chave Ja Expirada"
        };

        var response = await client.PatchAsJsonAsync($"/api/v1/tenants/{tenantId:D}/status", payload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var tenant = await _fixture.GetTenantAsync(tenantId);
        Assert.NotNull(tenant);
        Assert.Equal("ACTIVE", tenant.Status);
    }

    #endregion

    #region CA-12.2

    [Fact(Timeout = 60000)]
    public async Task CA_12_2_ReativacaoRepetidaDeTenantJaAtivoNaoRemoveNoRedisNemOutbox()
    {
        var groupId = Guid.NewGuid();
        var tenantId = Guid.Parse("01900000-0000-7000-8000-000000000100");
        var adminId = Guid.NewGuid();

        await _fixture.InsertEconomicGroupAsync(groupId, "Grupo Reativacao Repetida", "ACTIVE");
        await _fixture.InsertTenantAsync(tenantId, groupId, "99000001000101", "Tenant Ja Ativo", "HQ", 0, 0, "ACTIVE");
        await _fixture.InsertUserAccountAsync(adminId, "platadmin@example.test", "Utilizador Teste 99", "ACTIVE", "PLATFORM_ADMIN");

        var token = _fixture.CunharToken(sub: adminId, platformRole: "PLATFORM_ADMIN", viewMode: "NONE");

        using var client = _fixture.CreateHttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new
        {
            status = "ACTIVE",
            reason = "Confirmação ativação"
        };

        var response = await client.PatchAsJsonAsync($"/api/v1/tenants/{tenantId:D}/status", payload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var outboxCount = await _fixture.GetOutboxCountByTopicAsync("tenant_bloqueado", tenantId.ToString("D"));
        Assert.Equal(0, outboxCount);

        var auditCount = await _fixture.GetAuditCountAsync("TENANT_STATUS_CHANGED", tenantId: tenantId);
        Assert.True(auditCount >= 1);
    }

    [Fact(Timeout = 60000)]
    public async Task CA_12_2_Borda_ReativacaoRepetidaDeTenantJaAtivoNaoRemoveNoRedisNemOutbox()
    {
        var groupId = Guid.NewGuid();
        var tenantId = Guid.Parse("01900000-0000-7000-8000-000000000100");
        var adminId = Guid.NewGuid();

        await _fixture.InsertEconomicGroupAsync(groupId, "Grupo Reativacao Multiplas", "ACTIVE");
        await _fixture.InsertTenantAsync(tenantId, groupId, "99000001000101", "Tenant Repetidas Chamadas", "HQ", 0, 0, "ACTIVE");
        await _fixture.InsertUserAccountAsync(adminId, "platadmin@example.test", "Utilizador Teste 99", "ACTIVE", "PLATFORM_ADMIN");

        var token = _fixture.CunharToken(sub: adminId, platformRole: "PLATFORM_ADMIN", viewMode: "NONE");

        using var client = _fixture.CreateHttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        for (int i = 0; i < 3; i++)
        {
            var payload = new { status = "ACTIVE", reason = $"Reativacao Chamada {i}" };
            var response = await client.PatchAsJsonAsync($"/api/v1/tenants/{tenantId:D}/status", payload);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var tenant = await _fixture.GetTenantAsync(tenantId);
        Assert.NotNull(tenant);
        Assert.Equal("ACTIVE", tenant.Status);
    }

    #endregion

    #region CA-12.3

    [Theory(Timeout = 60000)]
    [InlineData("invalido-uuid", HttpStatusCode.BadRequest, "ERR-VAL-001")]
    [InlineData("01900000-0000-7000-8000-999999999999", HttpStatusCode.NotFound, "ERR-CTX-008")]
    public async Task CA_12_3_TentativaComTenantInexistenteOuUuidInvalido(string tenantParam, HttpStatusCode statusHttp, string codigoErro)
    {
        var adminId = Guid.NewGuid();
        await _fixture.InsertUserAccountAsync(adminId, "platadmin@example.test", "Utilizador Teste 99", "ACTIVE", "PLATFORM_ADMIN");

        var token = _fixture.CunharToken(sub: adminId, platformRole: "PLATFORM_ADMIN", viewMode: "NONE");

        using var client = _fixture.CreateHttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new
        {
            status = "ACTIVE",
            reason = "Tentativa parametro"
        };

        var response = await client.PatchAsJsonAsync($"/api/v1/tenants/{tenantParam}/status", payload);
        Assert.Equal(statusHttp, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(codigoErro, doc.RootElement.GetProperty("code").GetString());
    }

    [Fact(Timeout = 60000)]
    public async Task CA_12_3_Borda_TentativaComTenantInexistenteOuUuidInvalido()
    {
        var callerId = Guid.NewGuid();
        var inexistenteTenantId = Guid.Parse("01900000-0000-7000-8000-999999999999");
        var tokenTenantId = Guid.NewGuid();
        var groupId = Guid.NewGuid();

        await _fixture.InsertEconomicGroupAsync(groupId, "Grupo Borda Tenant Inexistente", "ACTIVE");
        await _fixture.InsertTenantAsync(tokenTenantId, groupId, "99000001000101", "Tenant Token", "HQ", 0, 0, "ACTIVE");
        await _fixture.InsertTenantScopeAsync(Guid.NewGuid(), tokenTenantId, "TENANT_ADMIN");

        await _fixture.InsertUserAccountAsync(callerId, "caller@example.test", "Utilizador Teste 22", "ACTIVE", "NONE");
        await _fixture.InsertTenantMemberAsync(Guid.NewGuid(), tokenTenantId, callerId, isOwner: true, canViewGroup: true, "ACTIVE");

        var token = _fixture.CunharToken(
            sub: callerId,
            platformRole: "NONE",
            viewMode: "SINGLE",
            groupId: groupId,
            tenantId: tokenTenantId,
            allowedTenants: new[] { tokenTenantId },
            activeScope: "TENANT_ADMIN");

        using var client = _fixture.CreateHttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new
        {
            status = "ACTIVE",
            reason = "Tentativa Tenant Fora de Allowed"
        };

        var response = await client.PatchAsJsonAsync($"/api/v1/tenants/{inexistenteTenantId:D}/status", payload);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("ERR-CTX-007", doc.RootElement.GetProperty("code").GetString());
    }

    #endregion

    #region Fixture e Infraestrutura

    public sealed class F03Fixture : IAsyncLifetime
    {
        private readonly PostgreSqlContainer _postgres;
        private readonly RedisContainer _redis;
        private readonly KafkaContainer _kafka;
        private WebApplicationFactory<Program>? _factory;
        private RSA _rsa = null!;
        private TestClock _clock = null!;
        private string _connectionString = null!;
        private string _redisConn = null!;
        private IConnectionMultiplexer _redisMultiplexer = null!;

        public F03Fixture()
        {
            _postgres = new PostgreSqlBuilder("postgres:17.6")
                .WithDatabase("identity")
                .WithUsername("postgres")
                .WithPassword("postgres-test")
                .Build();

            _redis = new RedisBuilder("redis:7.4.6").Build();
            _kafka = new KafkaBuilder("apache/kafka:3.9.1").Build();
        }

        public async Task InitializeAsync()
        {
            _rsa = RSA.Create(2048);
            var pem = _rsa.ExportPkcs8PrivateKeyPem();
            _clock = new TestClock(DateTimeOffset.Parse("2026-10-19T18:00:00Z"));

            await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync(), _kafka.StartAsync());

            _connectionString = _postgres.GetConnectionString();
            _redisConn = _redis.GetConnectionString();
            var kafkaBootstrap = _kafka.GetBootstrapAddress().Replace("plaintext://", "").TrimEnd('/');

            Environment.SetEnvironmentVariable("DB_CONNECTION_STRING", _connectionString);
            Environment.SetEnvironmentVariable("REDIS_CONNECTION_STRING", _redisConn);
            Environment.SetEnvironmentVariable("KAFKA_BOOTSTRAP_SERVERS", kafkaBootstrap);
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
            Environment.SetEnvironmentVariable("JWT_RSA_PRIVATE_KEY_PEM", pem);

            _redisMultiplexer = await ConnectionMultiplexer.ConnectAsync($"{_redisConn},allowAdmin=true");

            _factory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.UseEnvironment("Testing");
                    builder.ConfigureServices(services =>
                    {
                        services.AddSingleton<TimeProvider>(_clock);
                    });
                });

            // Inicializar WebApplicationFactory para executar migrações de banco
            _ = _factory.CreateClient();
        }

        public async Task DisposeAsync()
        {
            _redisMultiplexer.Dispose();
            if (_factory != null) await _factory.DisposeAsync();
            await Task.WhenAll(_postgres.DisposeAsync().AsTask(), _redis.DisposeAsync().AsTask(), _kafka.DisposeAsync().AsTask());
            _rsa.Dispose();
        }

        public HttpClient CreateHttpClient() => _factory!.CreateClient();

        public WebApplicationFactory<Program> CreateFactoryWithRedis(string redisConn)
        {
            return new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.UseEnvironment("Testing");
                    builder.ConfigureServices(services =>
                    {
                        services.AddSingleton<TimeProvider>(_clock);
                        var existing = services.FirstOrDefault(d => d.ServiceType == typeof(IConnectionMultiplexer));
                        if (existing != null)
                        {
                            services.Remove(existing);
                        }
                        var mux = ConnectionMultiplexer.Connect(redisConn + ",abortConnect=false,connectTimeout=1000,syncTimeout=1000");
                        services.AddSingleton<IConnectionMultiplexer>(mux);
                    });
                });
        }

        public async Task ResetDatabaseAndRedisAsync()
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "TRUNCATE economic_group, tenant, tenant_scope, user_account, tenant_member, member_role, refresh_token, auth_audit_log, outbox_message CASCADE;";
            await cmd.ExecuteNonQueryAsync();

            var server = _redisMultiplexer.GetServer(_redisMultiplexer.GetEndPoints()[0]);
            await server.FlushDatabaseAsync();
        }

        public string CunharToken(
            Guid sub,
            string platformRole = "NONE",
            string viewMode = "NONE",
            Guid? groupId = null,
            Guid? tenantId = null,
            IEnumerable<Guid>? allowedTenants = null,
            string? activeScope = null,
            IEnumerable<string>? roles = null,
            IEnumerable<string>? permissions = null,
            Guid? fid = null,
            (Guid sub, string ticketId)? act = null,
            long? iat = null,
            long? exp = null)
        {
            long nowSec = iat ?? _clock.GetUtcNow().ToUnixTimeSeconds();
            long expSec = exp ?? (nowSec + 900);

            var effectiveFid = fid ?? (act.HasValue ? (Guid?)null : Guid.CreateVersion7());

            Guid? effectiveTenantId = tenantId;
            var allowedList = allowedTenants?.ToList();
            if (viewMode == "NONE")
            {
                effectiveTenantId = null;
                groupId = null;
                allowedList = null;
                activeScope = null;
                roles = null;
                permissions = null;
            }
            else if (viewMode == "SINGLE")
            {
                if (!effectiveTenantId.HasValue && allowedList != null && allowedList.Count > 0)
                {
                    effectiveTenantId = allowedList[0];
                }
                else if (effectiveTenantId.HasValue && (allowedList == null || allowedList.Count == 0))
                {
                    allowedList = new List<Guid> { effectiveTenantId.Value };
                }
            }

            var payloadObj = new JsonObject
            {
                ["jti"] = Guid.CreateVersion7().ToString("D"),
                ["sub"] = sub.ToString("D"),
                ["iat"] = nowSec,
                ["exp"] = expSec,
                ["iss"] = "identity-service",
                ["aud"] = "b2b-platform",
                ["platform_role"] = platformRole,
                ["view_mode"] = viewMode,
                ["group_id"] = groupId.HasValue ? groupId.Value.ToString("D") : null,
                ["tenant_id"] = effectiveTenantId.HasValue ? effectiveTenantId.Value.ToString("D") : null,
                ["active_scope"] = activeScope,
                ["allowed_tenants"] = allowedList != null
                    ? new JsonArray(allowedList.Select(t => (JsonNode)JsonValue.Create(t.ToString("D"))).ToArray())
                    : new JsonArray(),
                ["roles"] = roles != null
                    ? new JsonArray(roles.Select(r => (JsonNode)JsonValue.Create(r)).ToArray())
                    : new JsonArray(),
                ["permissions"] = permissions != null
                    ? new JsonArray(permissions.Select(p => (JsonNode)JsonValue.Create(p)).ToArray())
                    : new JsonArray()
            };

            if (effectiveFid.HasValue)
            {
                payloadObj["fid"] = effectiveFid.Value.ToString("D");
            }

            if (act.HasValue)
            {
                payloadObj["act"] = new JsonObject
                {
                    ["sub"] = act.Value.sub.ToString("D"),
                    ["ticket_id"] = act.Value.ticketId
                };
            }

            var signingCredentials = new SigningCredentials(
                new RsaSecurityKey(_rsa) { KeyId = "key-2026-01" },
                SecurityAlgorithms.RsaSha256);

            var handler = new JsonWebTokenHandler();
            return handler.CreateToken(payloadObj.ToJsonString(), signingCredentials);
        }

        public async Task InsertEconomicGroupAsync(Guid id, string name, string status)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO economic_group (id, name, status, created_at, updated_at) VALUES (@id, @name, @status, @now, @now) ON CONFLICT (id) DO NOTHING;";
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("name", name);
            cmd.Parameters.AddWithValue("status", status);
            cmd.Parameters.AddWithValue("now", _clock.GetUtcNow());
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task InsertTenantAsync(Guid id, Guid groupId, string cnpj, string legalName, string branchType, decimal lat, decimal lng, string status)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO tenant (id, group_id, cnpj, legal_name, branch_type, geo_lat, geo_lng, status, created_at, updated_at) VALUES (@id, @groupId, @cnpj, @legalName, @branchType, @lat, @lng, @status, @now, @now) ON CONFLICT (id) DO NOTHING;";
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("groupId", groupId);
            cmd.Parameters.AddWithValue("cnpj", cnpj);
            cmd.Parameters.AddWithValue("legalName", legalName);
            cmd.Parameters.AddWithValue("branchType", branchType);
            cmd.Parameters.AddWithValue("lat", lat);
            cmd.Parameters.AddWithValue("lng", lng);
            cmd.Parameters.AddWithValue("status", status);
            cmd.Parameters.AddWithValue("now", _clock.GetUtcNow());
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task InsertTenantScopeAsync(Guid id, Guid tenantId, string scope)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO tenant_scope (id, tenant_id, scope, is_active, created_at) VALUES (@id, @tenantId, @scope, true, @now) ON CONFLICT (id) DO NOTHING;";
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("scope", scope);
            cmd.Parameters.AddWithValue("now", _clock.GetUtcNow());
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task InsertUserAccountAsync(Guid id, string email, string name, string status, string platformRole, int failedAttempts = 0, DateTimeOffset? lockedUntil = null)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO user_account (id, email, password_hash, name, status, platform_role, failed_login_attempts, locked_until, created_at, updated_at) VALUES (@id, @email, @pwd, @name, @status, @role, @failed, @locked, @now, @now) ON CONFLICT (id) DO NOTHING;";
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("email", email);
            cmd.Parameters.AddWithValue("pwd", "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHRzb21lc2FsdA$abcdefghijklmnopqrstuvwxyz0123456789ABCDEFG");
            cmd.Parameters.AddWithValue("name", name);
            cmd.Parameters.AddWithValue("status", status);
            cmd.Parameters.AddWithValue("role", platformRole);
            cmd.Parameters.AddWithValue("failed", failedAttempts);
            cmd.Parameters.AddWithValue("locked", (object?)lockedUntil ?? DBNull.Value);
            cmd.Parameters.AddWithValue("now", _clock.GetUtcNow());
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task InsertTenantMemberAsync(Guid id, Guid tenantId, Guid userId, bool isOwner, bool canViewGroup, string status)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO tenant_member (id, tenant_id, user_id, is_owner, can_view_group, status, created_at, updated_at) VALUES (@id, @tenantId, @userId, @isOwner, @canViewGroup, @status, @now, @now) ON CONFLICT (id) DO NOTHING;";
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("userId", userId);
            cmd.Parameters.AddWithValue("isOwner", isOwner);
            cmd.Parameters.AddWithValue("canViewGroup", canViewGroup);
            cmd.Parameters.AddWithValue("status", status);
            cmd.Parameters.AddWithValue("now", _clock.GetUtcNow());
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task InsertRefreshTokenAsync(
            Guid id, Guid familyId, Guid userId, string tokenHash, string status, string viewMode,
            Guid? groupId = null, Guid? activeTenantId = null, IEnumerable<Guid>? allowedTenants = null, string? activeScope = null)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO refresh_token (
                    id, family_id, user_id, token_hash, status, replaced_by_id, view_mode,
                    group_id, active_tenant_id, allowed_tenants, active_scope, expires_at,
                    consumed_at, revoked_at, created_at
                ) VALUES (
                    @id, @familyId, @userId, @hash, @status, NULL, @viewMode,
                    @groupId, @tenantId, @allowed, @scope, @expires,
                    NULL, NULL, @now
                );";
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("familyId", familyId);
            cmd.Parameters.AddWithValue("userId", userId);
            cmd.Parameters.AddWithValue("hash", tokenHash);
            cmd.Parameters.AddWithValue("status", status);
            cmd.Parameters.AddWithValue("viewMode", viewMode);
            cmd.Parameters.AddWithValue("groupId", (object?)groupId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("tenantId", (object?)activeTenantId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("allowed", allowedTenants?.ToArray() ?? Array.Empty<Guid>());
            cmd.Parameters.AddWithValue("scope", (object?)activeScope ?? DBNull.Value);
            cmd.Parameters.AddWithValue("expires", _clock.GetUtcNow().AddDays(7));
            cmd.Parameters.AddWithValue("now", _clock.GetUtcNow());
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<int> GetActiveRefreshTokensForUserAsync(Guid userId)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM refresh_token WHERE user_id = @id AND status = 'ACTIVE';";
            cmd.Parameters.AddWithValue("id", userId);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync() ?? 0);
        }

        public async Task<int> GetActiveRefreshTokensForTenantAsync(Guid tenantId)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM refresh_token WHERE (active_tenant_id = @t OR @t = ANY(allowed_tenants)) AND status = 'ACTIVE';";
            cmd.Parameters.AddWithValue("t", tenantId);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync() ?? 0);
        }

        public sealed record UserRecord(string Status, int FailedLoginAttempts, DateTimeOffset? LockedUntil);
        public sealed record TenantRecord(string Status, string Cnpj, string LegalName);

        public async Task<UserRecord?> GetUserAsync(Guid userId)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT status, failed_login_attempts, locked_until FROM user_account WHERE id = @id;";
            cmd.Parameters.AddWithValue("id", userId);
            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new UserRecord(
                    reader.GetString(0),
                    reader.GetInt32(1),
                    reader.IsDBNull(2) ? null : reader.GetDateTime(2)
                );
            }
            return null;
        }

        public async Task<string?> GetMemberStatusAsync(Guid memberId)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT status FROM tenant_member WHERE id = @id;";
            cmd.Parameters.AddWithValue("id", memberId);
            return (string?)await cmd.ExecuteScalarAsync();
        }

        public async Task<TenantRecord?> GetTenantAsync(Guid tenantId)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT status, cnpj, legal_name FROM tenant WHERE id = @id;";
            cmd.Parameters.AddWithValue("id", tenantId);
            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new TenantRecord(reader.GetString(0), reader.GetString(1), reader.GetString(2));
            }
            return null;
        }

        public async Task<string?> GetLatestOutboxTopicAsync(string partitionKey)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT topic FROM outbox_message WHERE partition_key = @key ORDER BY created_at DESC LIMIT 1;";
            cmd.Parameters.AddWithValue("key", partitionKey);
            return (string?)await cmd.ExecuteScalarAsync();
        }

        public async Task<int> GetOutboxCountByTopicAsync(string topic, string partitionKey)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM outbox_message WHERE topic = @topic AND partition_key = @key;";
            cmd.Parameters.AddWithValue("topic", topic);
            cmd.Parameters.AddWithValue("key", partitionKey);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync() ?? 0);
        }

        public async Task<int> GetAuditCountAsync(string eventType, Guid? userId = null, Guid? tenantId = null, Guid? groupId = null)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            var sql = "SELECT COUNT(*) FROM auth_audit_log WHERE event_type = @eventType";
            cmd.Parameters.AddWithValue("eventType", eventType);
            if (userId.HasValue)
            {
                sql += " AND user_id = @userId";
                cmd.Parameters.AddWithValue("userId", userId.Value);
            }
            if (tenantId.HasValue)
            {
                sql += " AND tenant_id = @tenantId";
                cmd.Parameters.AddWithValue("tenantId", tenantId.Value);
            }
            if (groupId.HasValue)
            {
                sql += " AND group_id = @groupId";
                cmd.Parameters.AddWithValue("groupId", groupId.Value);
            }
            cmd.CommandText = sql;
            return Convert.ToInt32(await cmd.ExecuteScalarAsync() ?? 0);
        }

        public async Task<string?> GetRedisStringAsync(string key)
        {
            var db = _redisMultiplexer.GetDatabase();
            var val = await db.StringGetAsync(key);
            return val.IsNullOrEmpty ? null : val.ToString();
        }

        public async Task SetRedisStringAsync(string key, string value, TimeSpan? ttl = null)
        {
            var db = _redisMultiplexer.GetDatabase();
            await db.StringSetAsync(key, value);
            if (ttl.HasValue)
            {
                await db.KeyExpireAsync(key, ttl.Value);
            }
        }

        private sealed class TestClock : TimeProvider
        {
            private DateTimeOffset _utcNow;

            public TestClock(DateTimeOffset initial)
            {
                _utcNow = new DateTimeOffset(initial.Year, initial.Month, initial.Day, initial.Hour, initial.Minute, initial.Second, TimeSpan.Zero);
            }

            public override DateTimeOffset GetUtcNow() => _utcNow;
            public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);
            public void Set(DateTimeOffset time) =>
                _utcNow = new DateTimeOffset(time.Year, time.Month, time.Day, time.Hour, time.Minute, time.Second, TimeSpan.Zero);
        }
    }

    #endregion
}
