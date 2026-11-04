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

[Trait("Fase", "F02")]
public sealed class F02AceitacaoTests : IClassFixture<F02AceitacaoTests.F02Fixture>, IAsyncLifetime
{
    private readonly F02Fixture _fixture;

    public F02AceitacaoTests(F02Fixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await _fixture.ResetDatabaseAndRedisAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    #region CA-07.1

    [Fact(Timeout = 60000)]
    public async Task CA_07_1_SucessoPorTenantAdminAcrescentandoEscopoAdministrativo()
    {
        var groupId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcde");
        var userId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcd0");
        var cnpj = "99000001000101";

        await _fixture.InsertEconomicGroupAsync(groupId, "Grupo Teste Alvorada", "ACTIVE");
        await _fixture.InsertUserAccountAsync(userId, "admin01@example.test", "Utilizador Teste 01", "ACTIVE", "NONE");

        var token = _fixture.CunharToken(
            sub: userId,
            platformRole: "NONE",
            viewMode: "SINGLE",
            groupId: groupId,
            activeScope: "TENANT_ADMIN",
            permissions: new[] { "tenant:criar" },
            allowedTenants: new[] { Guid.NewGuid() });

        using var client = _fixture.CreateHttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new
        {
            group_id = groupId.ToString("D"),
            cnpj = cnpj,
            legal_name = "Logística Alvorada LTDA",
            branch_type = "HQ",
            geo_lat = -23.550520m,
            geo_lng = -46.633308m,
            scopes = new[] { "BUYER" }
        };

        var response = await client.PostAsJsonAsync("/api/v1/tenants", payload);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        var tenantIdStr = root.GetProperty("id").GetString();
        Assert.NotNull(tenantIdStr);
        var tenantId = Guid.Parse(tenantIdStr);

        Assert.Equal("ACTIVE", root.GetProperty("status").GetString());

        var scopes = root.GetProperty("scopes").EnumerateArray().Select(s => s.GetString()).ToList();
        Assert.Contains("BUYER", scopes);
        Assert.Contains("TENANT_ADMIN", scopes);

        var tenantInDb = await _fixture.GetTenantAsync(tenantId);
        Assert.NotNull(tenantInDb);
        Assert.Equal("ACTIVE", tenantInDb.Status);
        Assert.Equal(cnpj, tenantInDb.Cnpj);

        var hasMember = await _fixture.HasTenantMemberWithRoleAsync(tenantId, userId, "GROUP_MANAGER");
        Assert.True(hasMember);

        var outboxTopic = await _fixture.GetLatestOutboxTopicAsync(tenantId.ToString("D"));
        Assert.Equal("empresa_cadastrada", outboxTopic);

        var auditCount = await _fixture.GetAuditCountAsync("TENANT_CREATED", tenantId: tenantId);
        Assert.True(auditCount >= 1);
        var memberAuditCount = await _fixture.GetAuditCountAsync("MEMBER_ADDED", tenantId: tenantId, userId: userId);
        Assert.True(memberAuditCount >= 1);
    }

    [Fact(Timeout = 60000)]
    public async Task CA_07_1_Borda_SucessoPorTenantAdminAcrescentandoEscopoAdministrativo()
    {
        var groupId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcde");
        var userId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcd0");
        var cnpj = "99000002000148";

        await _fixture.InsertEconomicGroupAsync(groupId, "Grupo Teste Alvorada", "ACTIVE");
        await _fixture.InsertUserAccountAsync(userId, "admin01@example.test", "Utilizador Teste 01", "ACTIVE", "NONE");

        var token = _fixture.CunharToken(
            sub: userId,
            platformRole: "NONE",
            viewMode: "SINGLE",
            groupId: groupId,
            activeScope: "TENANT_ADMIN",
            permissions: new[] { "tenant:criar" },
            allowedTenants: new[] { Guid.NewGuid() });

        using var client = _fixture.CreateHttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new
        {
            group_id = groupId.ToString("D"),
            cnpj = cnpj,
            legal_name = "AB",
            branch_type = "BRANCH",
            geo_lat = 0.000000m,
            geo_lng = 0.000000m,
            scopes = new[] { "BUYER", "TENANT_ADMIN" }
        };

        var response = await client.PostAsJsonAsync("/api/v1/tenants", payload);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var scopes = doc.RootElement.GetProperty("scopes").EnumerateArray().Select(s => s.GetString()).ToList();
        Assert.Equal(2, scopes.Count);
        Assert.Contains("BUYER", scopes);
        Assert.Contains("TENANT_ADMIN", scopes);
    }

    #endregion

    #region CA-07.2

    [Fact(Timeout = 60000)]
    public async Task CA_07_2_SucessoPorPlatformAdminSemVinculoDeMembro()
    {
        var groupId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcde");
        var adminId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcd9");
        var cnpj = "99000003000192";

        await _fixture.InsertEconomicGroupAsync(groupId, "Grupo Teste Central", "ACTIVE");
        await _fixture.InsertUserAccountAsync(adminId, "platadmin@example.test", "Utilizador Teste 99", "ACTIVE", "PLATFORM_ADMIN");

        var token = _fixture.CunharToken(
            sub: adminId,
            platformRole: "PLATFORM_ADMIN",
            viewMode: "NONE");

        using var client = _fixture.CreateHttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new
        {
            group_id = groupId.ToString("D"),
            cnpj = cnpj,
            legal_name = "Transportes Expressos Filial",
            branch_type = "BRANCH",
            geo_lat = 90.000000m,
            geo_lng = 180.000000m,
            scopes = new[] { "CARRIER" }
        };

        var response = await client.PostAsJsonAsync("/api/v1/tenants", payload);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var tenantId = Guid.Parse(doc.RootElement.GetProperty("id").GetString()!);

        Assert.Equal("ACTIVE", doc.RootElement.GetProperty("status").GetString());
        var scopes = doc.RootElement.GetProperty("scopes").EnumerateArray().Select(s => s.GetString()).ToList();
        Assert.Single(scopes);
        Assert.Equal("CARRIER", scopes[0]);

        var memberCount = await _fixture.GetMemberCountAsync(tenantId);
        Assert.Equal(0, memberCount);

        var outboxTopic = await _fixture.GetLatestOutboxTopicAsync(tenantId.ToString("D"));
        Assert.Equal("empresa_cadastrada", outboxTopic);
    }

    [Fact(Timeout = 60000)]
    public async Task CA_07_2_Borda_SucessoPorPlatformAdminSemVinculoDeMembro()
    {
        var groupId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcde");
        var adminId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcd9");
        var cnpj = "99000004000137";

        await _fixture.InsertEconomicGroupAsync(groupId, "Grupo Teste Sul", "ACTIVE");
        await _fixture.InsertUserAccountAsync(adminId, "platadmin@example.test", "Utilizador Teste 99", "ACTIVE", "PLATFORM_ADMIN");

        var token = _fixture.CunharToken(
            sub: adminId,
            platformRole: "PLATFORM_ADMIN",
            viewMode: "NONE");

        using var client = _fixture.CreateHttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new
        {
            group_id = groupId.ToString("D"),
            cnpj = cnpj,
            legal_name = "   Logística de Fronteira Sul   ",
            branch_type = "HQ",
            geo_lat = -90.000000m,
            geo_lng = -180.000000m,
            scopes = new[] { "BUYER", "SUPPLIER", "CARRIER", "TENANT_ADMIN" }
        };

        var response = await client.PostAsJsonAsync("/api/v1/tenants", payload);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var tenantId = Guid.Parse(doc.RootElement.GetProperty("id").GetString()!);

        var tenant = await _fixture.GetTenantAsync(tenantId);
        Assert.NotNull(tenant);
        Assert.Equal("Logística de Fronteira Sul", tenant.LegalName);
    }

    #endregion

    #region CA-07.3

    [Fact(Timeout = 60000)]
    [Trait("Critico", "sim")]
    public async Task CA_07_3_ConflitoDeUnicidadeAoCadastrarCnpjExistenteOuReenvio()
    {
        var groupId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcde");
        var adminId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcd9");
        var cnpj = "99000001000101";

        await _fixture.InsertEconomicGroupAsync(groupId, "Grupo Teste Unicidade", "ACTIVE");
        await _fixture.InsertTenantAsync(Guid.NewGuid(), groupId, cnpj, "Empresa Existente LTDA", "HQ", 0, 0, "ACTIVE");
        await _fixture.InsertUserAccountAsync(adminId, "platadmin@example.test", "Utilizador Teste 99", "ACTIVE", "PLATFORM_ADMIN");

        var token = _fixture.CunharToken(sub: adminId, platformRole: "PLATFORM_ADMIN", viewMode: "NONE");

        using var client = _fixture.CreateHttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new
        {
            group_id = groupId.ToString("D"),
            cnpj = cnpj,
            legal_name = "Tentativa Duplicada LTDA",
            branch_type = "HQ",
            geo_lat = -23.550520m,
            geo_lng = -46.633308m,
            scopes = new[] { "BUYER" }
        };

        var response = await client.PostAsJsonAsync("/api/v1/tenants", payload);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("ERR-CTX-005", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact(Timeout = 60000)]
    [Trait("Critico", "sim")]
    public async Task CA_07_3_Borda_ConflitoDeUnicidadeAoCadastrarCnpjExistenteOuReenvio()
    {
        var groupId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcde");
        var adminId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcd9");
        var cnpj = "99000005000181";

        await _fixture.InsertEconomicGroupAsync(groupId, "Grupo Teste Concorrencia", "ACTIVE");
        await _fixture.InsertUserAccountAsync(adminId, "platadmin@example.test", "Utilizador Teste 99", "ACTIVE", "PLATFORM_ADMIN");

        var token = _fixture.CunharToken(sub: adminId, platformRole: "PLATFORM_ADMIN", viewMode: "NONE");

        for (int rep = 0; rep < 20; rep++)
        {
            await _fixture.DeleteTenantByCnpjAsync(cnpj);

            using var barrier = new Barrier(2);
            var tasks = new Task<HttpResponseMessage>[2];

            for (int i = 0; i < 2; i++)
            {
                int idx = i;
                tasks[i] = Task.Run(async () =>
                {
                    using var c = _fixture.CreateHttpClient();
                    c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    var p = new
                    {
                        group_id = groupId.ToString("D"),
                        cnpj = cnpj,
                        legal_name = $"Empresa Concorrente {idx}",
                        branch_type = "HQ",
                        geo_lat = 0.0m,
                        geo_lng = 0.0m,
                        scopes = new[] { "BUYER" }
                    };
                    barrier.SignalAndWait(TimeSpan.FromSeconds(10));
                    return await c.PostAsJsonAsync("/api/v1/tenants", p);
                });
            }

            var responses = await Task.WhenAll(tasks);
            var statuses = responses.Select(r => r.StatusCode).ToList();

            Assert.Contains(HttpStatusCode.Created, statuses);
            Assert.Contains(HttpStatusCode.Conflict, statuses);
        }
    }

    #endregion

    #region CA-07.4

    [Theory(Timeout = 60000)]
    [InlineData("1111111" + "1111111", 0.0, 0.0)]
    [InlineData("99000001000199", 0.0, 0.0)]
    [InlineData("99000001000101", -90.000001, 0.0)]
    [InlineData("99000001000101", 90.000001, 0.0)]
    [InlineData("99000001000101", 0.0, -180.000001)]
    [InlineData("99000001000101", 0.0, 180.000001)]
    public async Task CA_07_4_RejeicaoPorDadosInvalidosDeCnpjOuGeolocalizacao(string cnpjInv, decimal lat, decimal lng)
    {
        var groupId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcde");
        var adminId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcd9");

        await _fixture.InsertEconomicGroupAsync(groupId, "Grupo Teste Validação", "ACTIVE");
        await _fixture.InsertUserAccountAsync(adminId, "platadmin@example.test", "Utilizador Teste 99", "ACTIVE", "PLATFORM_ADMIN");

        var token = _fixture.CunharToken(sub: adminId, platformRole: "PLATFORM_ADMIN", viewMode: "NONE");

        using var client = _fixture.CreateHttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new
        {
            group_id = groupId.ToString("D"),
            cnpj = cnpjInv,
            legal_name = "Empresa Invalida LTDA",
            branch_type = "HQ",
            geo_lat = lat,
            geo_lng = lng,
            scopes = new[] { "BUYER" }
        };

        var response = await client.PostAsJsonAsync("/api/v1/tenants", payload);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("ERR-VAL-002", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact(Timeout = 60000)]
    public async Task CA_07_4_Borda_RejeicaoPorDadosInvalidosDeCnpjOuGeolocalizacao()
    {
        var groupId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcde");
        var adminId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcd9");

        await _fixture.InsertEconomicGroupAsync(groupId, "Grupo Teste Borda CNPJ", "ACTIVE");
        await _fixture.InsertUserAccountAsync(adminId, "platadmin@example.test", "Utilizador Teste 99", "ACTIVE", "PLATFORM_ADMIN");

        var token = _fixture.CunharToken(sub: adminId, platformRole: "PLATFORM_ADMIN", viewMode: "NONE");

        using var client = _fixture.CreateHttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payloadLetras = new
        {
            group_id = groupId.ToString("D"),
            cnpj = "9900000100010A",
            legal_name = "Empresa Letras LTDA",
            branch_type = "HQ",
            geo_lat = 0.0m,
            geo_lng = 0.0m,
            scopes = new[] { "BUYER" }
        };
        var resLetras = await client.PostAsJsonAsync("/api/v1/tenants", payloadLetras);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resLetras.StatusCode);

        var payload13Digitos = new
        {
            group_id = groupId.ToString("D"),
            cnpj = "9900000100010",
            legal_name = "Empresa 13 Digitos LTDA",
            branch_type = "HQ",
            geo_lat = 0.0m,
            geo_lng = 0.0m,
            scopes = new[] { "BUYER" }
        };
        var res13 = await client.PostAsJsonAsync("/api/v1/tenants", payload13Digitos);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, res13.StatusCode);
    }

    #endregion

    #region CA-07.5

    [Fact(Timeout = 180000)]
    [Trait("Critico", "sim")]
    public async Task CA_07_5_RollbackTotalNaCriacaoDeTenantAposFalhaSimulada()
    {
        var groupId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcde");
        var adminId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcd9");
        var cnpj = "99000006000126";

        await _fixture.InsertEconomicGroupAsync(groupId, "Grupo Teste Falha", "ACTIVE");
        await _fixture.InsertUserAccountAsync(adminId, "platadmin@example.test", "Utilizador Teste 99", "ACTIVE", "PLATFORM_ADMIN");

        await _fixture.CreateScopeTriggerToFailAsync();

        try
        {
            var token = _fixture.CunharToken(sub: adminId, platformRole: "PLATFORM_ADMIN", viewMode: "NONE");
            using var client = _fixture.CreateHttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var payload = new
            {
                group_id = groupId.ToString("D"),
                cnpj = cnpj,
                legal_name = "Empresa Teste Rollback",
                branch_type = "HQ",
                geo_lat = 0.0m,
                geo_lng = 0.0m,
                scopes = new[] { "BUYER" }
            };

            var response = await client.PostAsJsonAsync("/api/v1/tenants", payload);
            Assert.True(response.StatusCode == HttpStatusCode.InternalServerError || response.StatusCode == HttpStatusCode.ServiceUnavailable);

            var exists = await _fixture.TenantExistsByCnpjAsync(cnpj);
            Assert.False(exists);
        }
        finally
        {
            await _fixture.DropScopeTriggerToFailAsync();
        }
    }

    [Fact(Timeout = 180000)]
    [Trait("Critico", "sim")]
    public async Task CA_07_5_Borda_RollbackTotalNaCriacaoDeTenantAposFalhaSimulada()
    {
        var groupId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcde");
        var adminId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcd9");
        var cnpj = "99000007000170";

        await _fixture.InsertEconomicGroupAsync(groupId, "Grupo Teste Borda Falha", "ACTIVE");
        await _fixture.InsertUserAccountAsync(adminId, "platadmin@example.test", "Utilizador Teste 99", "ACTIVE", "PLATFORM_ADMIN");

        await _fixture.CreateScopeTriggerToFailAsync();

        try
        {
            var token = _fixture.CunharToken(sub: adminId, platformRole: "PLATFORM_ADMIN", viewMode: "NONE");
            using var client = _fixture.CreateHttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var payload = new
            {
                group_id = groupId.ToString("D"),
                cnpj = cnpj,
                legal_name = "Empresa Borda Rollback",
                branch_type = "BRANCH",
                geo_lat = 10.0m,
                geo_lng = 10.0m,
                scopes = new[] { "CARRIER" }
            };

            var response = await client.PostAsJsonAsync("/api/v1/tenants", payload);
            Assert.True(response.StatusCode == HttpStatusCode.InternalServerError || response.StatusCode == HttpStatusCode.ServiceUnavailable);

            var outboxCount = await _fixture.GetOutboxCountByCnpjAsync(cnpj);
            Assert.Equal(0, outboxCount);

            var auditCount = await _fixture.GetAuditCountAsync("TENANT_CREATED");
            Assert.Equal(0, auditCount);
        }
        finally
        {
            await _fixture.DropScopeTriggerToFailAsync();
        }
    }

    #endregion

    #region CA-07.6

    [Theory(Timeout = 60000)]
    [InlineData("BUYER", "01923456-789a-7bc8-9012-3456789abcde")]
    [InlineData("TENANT_ADMIN_OUTRO_GRUPO", "01923456-789a-7bc8-9012-3456789abce0")]
    [Trait("Critico", "sim")]
    public async Task CA_07_6_NegacaoDeAutorizacaoParaChamadoresSemPrivilegio(string perfil, string grupoIdStr)
    {
        var targetGroupId = Guid.Parse(grupoIdStr);
        var tokenGroupId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcde");
        var userId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcd0");
        var cnpj = "99000008000115";

        await _fixture.InsertEconomicGroupAsync(tokenGroupId, "Grupo Token", "ACTIVE");
        if (targetGroupId != tokenGroupId)
        {
            await _fixture.InsertEconomicGroupAsync(targetGroupId, "Grupo Alvo", "ACTIVE");
        }
        await _fixture.InsertUserAccountAsync(userId, "user01@example.test", "Utilizador Teste 01", "ACTIVE", "NONE");

        string activeScope = perfil.StartsWith("TENANT_ADMIN") ? "TENANT_ADMIN" : "BUYER";
        var perms = perfil.StartsWith("TENANT_ADMIN") ? new[] { "tenant:criar" } : Array.Empty<string>();

        var token = _fixture.CunharToken(
            sub: userId,
            platformRole: "NONE",
            viewMode: "SINGLE",
            groupId: tokenGroupId,
            activeScope: activeScope,
            permissions: perms,
            allowedTenants: new[] { Guid.NewGuid() });

        using var client = _fixture.CreateHttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new
        {
            group_id = targetGroupId.ToString("D"),
            cnpj = cnpj,
            legal_name = "Empresa Bloqueada LTDA",
            branch_type = "HQ",
            geo_lat = 0.0m,
            geo_lng = 0.0m,
            scopes = new[] { "BUYER" }
        };

        var response = await client.PostAsJsonAsync("/api/v1/tenants", payload);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("ERR-CTX-007", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact(Timeout = 60000)]
    [Trait("Critico", "sim")]
    public async Task CA_07_6_Borda_NegacaoDeAutorizacaoParaChamadoresSemPrivilegio()
    {
        var groupId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcde");
        var userId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcd0");
        var cnpj = "99000009000160";

        await _fixture.InsertEconomicGroupAsync(groupId, "Grupo Mesmo Grupo", "ACTIVE");
        await _fixture.InsertUserAccountAsync(userId, "user01@example.test", "Utilizador Teste 01", "ACTIVE", "NONE");

        var token = _fixture.CunharToken(
            sub: userId,
            platformRole: "NONE",
            viewMode: "SINGLE",
            groupId: groupId,
            activeScope: "TENANT_ADMIN",
            permissions: new[] { "membro:vincular" },
            allowedTenants: new[] { Guid.NewGuid() });

        using var client = _fixture.CreateHttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new
        {
            group_id = groupId.ToString("D"),
            cnpj = cnpj,
            legal_name = "Empresa Sem Permissao LTDA",
            branch_type = "HQ",
            geo_lat = 0.0m,
            geo_lng = 0.0m,
            scopes = new[] { "BUYER" }
        };

        var response = await client.PostAsJsonAsync("/api/v1/tenants", payload);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("ERR-CTX-007", doc.RootElement.GetProperty("code").GetString());
    }

    #endregion

    #region CA-07.7

    [Theory(Timeout = 60000)]
    [InlineData("01923456-789a-7bc8-9012-000000000000", HttpStatusCode.NotFound, "ERR-CTX-008")]
    [InlineData("01923456-789a-7bc8-9012-3456789abcdf", HttpStatusCode.Forbidden, "ERR-CTX-002")]
    [Trait("Critico", "sim")]
    public async Task CA_07_7_RejeicaoDeGrupoEconomicoInexistenteOuSuspenso(string grupoStr, HttpStatusCode statusEsperado, string codigoEsperado)
    {
        var targetGroupId = Guid.Parse(grupoStr);
        var adminId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcd9");
        var cnpj = "99000010000194";

        if (statusEsperado == HttpStatusCode.Forbidden)
        {
            await _fixture.InsertEconomicGroupAsync(targetGroupId, "Grupo Suspenso", "SUSPENDED");
        }
        await _fixture.InsertUserAccountAsync(adminId, "platadmin@example.test", "Utilizador Teste 99", "ACTIVE", "PLATFORM_ADMIN");

        var token = _fixture.CunharToken(sub: adminId, platformRole: "PLATFORM_ADMIN", viewMode: "NONE");

        using var client = _fixture.CreateHttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new
        {
            group_id = targetGroupId.ToString("D"),
            cnpj = cnpj,
            legal_name = "Empresa Teste Grupo Invalido",
            branch_type = "HQ",
            geo_lat = 0.0m,
            geo_lng = 0.0m,
            scopes = new[] { "BUYER" }
        };

        var response = await client.PostAsJsonAsync("/api/v1/tenants", payload);
        Assert.Equal(statusEsperado, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(codigoEsperado, doc.RootElement.GetProperty("code").GetString());
    }

    [Fact(Timeout = 60000)]
    [Trait("Critico", "sim")]
    public async Task CA_07_7_Borda_RejeicaoDeGrupoEconomicoInexistenteOuSuspenso()
    {
        var tokenGroupId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcde");
        var inexistenteGroupId = Guid.Parse("01923456-789a-7bc8-9012-000000000000");
        var userId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcd0");
        var cnpj = "99000001000101";

        await _fixture.InsertEconomicGroupAsync(tokenGroupId, "Grupo Valido", "ACTIVE");
        await _fixture.InsertUserAccountAsync(userId, "user01@example.test", "Utilizador Teste 01", "ACTIVE", "NONE");

        var token = _fixture.CunharToken(
            sub: userId,
            platformRole: "NONE",
            viewMode: "SINGLE",
            groupId: tokenGroupId,
            activeScope: "TENANT_ADMIN",
            permissions: new[] { "tenant:criar" },
            allowedTenants: new[] { Guid.NewGuid() });

        using var client = _fixture.CreateHttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new
        {
            group_id = inexistenteGroupId.ToString("D"),
            cnpj = cnpj,
            legal_name = "Empresa Precedencia Grupo",
            branch_type = "HQ",
            geo_lat = 0.0m,
            geo_lng = 0.0m,
            scopes = new[] { "BUYER" }
        };

        var response = await client.PostAsJsonAsync("/api/v1/tenants", payload);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("ERR-CTX-007", doc.RootElement.GetProperty("code").GetString());
    }

    #endregion

    #region CA-07.8

    [Theory(Timeout = 60000)]
    [InlineData("{\"legal_name\":\"A\"}")]
    [InlineData("{\"legal_name\":\" A \"}")]
    [InlineData("{\"scopes\":[]}")]
    [InlineData("{\"scopes\":[\"BUYER\",\"BUYER\"]}")]
    [InlineData("{\"branch_type\":\"INVALID\"}")]
    [InlineData("{\"group_id\":\"nao-uuid\"}")]
    public async Task CA_07_8_RejeicaoDeFormatoDeEntradaMalformada(string caso)
    {
        var adminId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcd9");
        await _fixture.InsertUserAccountAsync(adminId, "platadmin@example.test", "Utilizador Teste 99", "ACTIVE", "PLATFORM_ADMIN");

        var token = _fixture.CunharToken(sub: adminId, platformRole: "PLATFORM_ADMIN", viewMode: "NONE");

        using var client = _fixture.CreateHttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var baseJson = new JsonObject
        {
            ["group_id"] = "01923456-789a-7bc8-9012-3456789abcde",
            ["cnpj"] = "99000001000101",
            ["legal_name"] = "Nome Valido da Empresa LTDA",
            ["branch_type"] = "HQ",
            ["geo_lat"] = 0.0m,
            ["geo_lng"] = 0.0m,
            ["scopes"] = new JsonArray("BUYER")
        };

        var patchDoc = JsonDocument.Parse(caso);
        foreach (var prop in patchDoc.RootElement.EnumerateObject())
        {
            baseJson[prop.Name] = JsonNode.Parse(prop.Value.GetRawText());
        }

        var content = new StringContent(baseJson.ToJsonString(), Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/api/v1/tenants", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("ERR-VAL-001", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact(Timeout = 60000)]
    public async Task CA_07_8_Borda_RejeicaoDeFormatoDeEntradaMalformada()
    {
        var adminId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcd9");
        await _fixture.InsertUserAccountAsync(adminId, "platadmin@example.test", "Utilizador Teste 99", "ACTIVE", "PLATFORM_ADMIN");

        var token = _fixture.CunharToken(sub: adminId, platformRole: "PLATFORM_ADMIN", viewMode: "NONE");

        using var client = _fixture.CreateHttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var jsonNul = "{\"group_id\":\"01923456-789a-7bc8-9012-3456789abcde\",\"cnpj\":\"99000001000101\",\"legal_name\":\"Empresa\\u0000LTDA\",\"branch_type\":\"HQ\",\"geo_lat\":0.0,\"geo_lng\":0.0,\"scopes\":[\"BUYER\"]}";
        var contentNul = new StringContent(jsonNul, Encoding.UTF8, "application/json");
        var resNul = await client.PostAsync("/api/v1/tenants", contentNul);
        Assert.Equal(HttpStatusCode.BadRequest, resNul.StatusCode);

        var contentEmpty = new StringContent("", Encoding.UTF8, "application/json");
        var resEmpty = await client.PostAsync("/api/v1/tenants", contentEmpty);
        Assert.Equal(HttpStatusCode.BadRequest, resEmpty.StatusCode);

        var contentArray = new StringContent("[]", Encoding.UTF8, "application/json");
        var resArray = await client.PostAsync("/api/v1/tenants", contentArray);
        Assert.Equal(HttpStatusCode.BadRequest, resArray.StatusCode);
    }

    #endregion

    #region CA-08.1

    [Fact(Timeout = 60000)]
    public async Task CA_08_1_VinculacaoDeMembroComSucessoPorTenantAdmin()
    {
        var groupId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcde");
        var tenantId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcd1");
        var callerId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcd0");
        var targetUserId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcd2");
        var roleBuyer = Guid.Parse("01900000-0000-7000-8000-000000000001");

        await _fixture.InsertEconomicGroupAsync(groupId, "Grupo Membros", "ACTIVE");
        await _fixture.InsertTenantAsync(tenantId, groupId, "99000001000101", "Tenant Comprador LTDA", "HQ", 0, 0, "ACTIVE");
        await _fixture.InsertTenantScopeAsync(Guid.NewGuid(), tenantId, "BUYER");

        await _fixture.InsertUserAccountAsync(callerId, "admin@example.test", "Utilizador Teste 01", "ACTIVE", "NONE");
        await _fixture.InsertUserAccountAsync(targetUserId, "membro@example.test", "Utilizador Teste 02", "ACTIVE", "NONE");

        var token = _fixture.CunharToken(
            sub: callerId,
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
            user_id = targetUserId.ToString("D"),
            is_owner = false,
            can_view_group = true,
            role_ids = new[] { roleBuyer.ToString("D") }
        };

        var response = await client.PostAsJsonAsync($"/api/v1/tenants/{tenantId:D}/members", payload);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        Assert.Equal("ACTIVE", root.GetProperty("status").GetString());
        Assert.False(root.GetProperty("is_owner").GetBoolean());
        Assert.True(root.GetProperty("can_view_group").GetBoolean());

        var roles = root.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).ToList();
        Assert.Single(roles);
        Assert.Equal("BUYER_OPERATOR", roles[0]);

        var hasMember = await _fixture.HasTenantMemberWithRoleAsync(tenantId, targetUserId, "BUYER_OPERATOR");
        Assert.True(hasMember);

        var auditCount = await _fixture.GetAuditCountAsync("MEMBER_ADDED", tenantId: tenantId, userId: targetUserId);
        Assert.True(auditCount >= 1);
    }

    [Fact(Timeout = 60000)]
    public async Task CA_08_1_Borda_VinculacaoDeMembroComSucessoPorTenantAdmin()
    {
        var groupId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcde");
        var tenantId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcd1");
        var callerId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcd0");
        var targetUserId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcd2");
        var roleBuyer = Guid.Parse("01900000-0000-7000-8000-000000000001");
        var roleAdmin = Guid.Parse("01900000-0000-7000-8000-000000000004");

        await _fixture.InsertEconomicGroupAsync(groupId, "Grupo Multiplos Papeis", "ACTIVE");
        await _fixture.InsertTenantAsync(tenantId, groupId, "99000001000101", "Tenant MultiEscopo LTDA", "HQ", 0, 0, "ACTIVE");
        await _fixture.InsertTenantScopeAsync(Guid.NewGuid(), tenantId, "BUYER");
        await _fixture.InsertTenantScopeAsync(Guid.NewGuid(), tenantId, "TENANT_ADMIN");

        await _fixture.InsertUserAccountAsync(callerId, "admin@example.test", "Utilizador Teste 01", "ACTIVE", "NONE");
        await _fixture.InsertUserAccountAsync(targetUserId, "membro@example.test", "Utilizador Teste 02", "ACTIVE", "NONE");

        var token = _fixture.CunharToken(
            sub: callerId,
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
            user_id = targetUserId.ToString("D"),
            is_owner = false,
            can_view_group = true,
            role_ids = new[] { roleAdmin.ToString("D"), roleBuyer.ToString("D") }
        };

        var response = await client.PostAsJsonAsync($"/api/v1/tenants/{tenantId:D}/members", payload);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var roles = doc.RootElement.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).ToList();

        Assert.Equal(2, roles.Count);
        Assert.Equal("BUYER_OPERATOR", roles[0]);
        Assert.Equal("GROUP_MANAGER", roles[1]);
    }

    #endregion

    #region CA-08.2

    [Fact(Timeout = 60000)]
    public async Task CA_08_2_VinculacaoComSucessoPorPlatformAdmin()
    {
        var groupId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcde");
        var tenantId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcd1");
        var adminId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcd9");
        var targetUserId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcd3");
        var roleBuyer = Guid.Parse("01900000-0000-7000-8000-000000000001");

        await _fixture.InsertEconomicGroupAsync(groupId, "Grupo Plat Admin Membros", "ACTIVE");
        await _fixture.InsertTenantAsync(tenantId, groupId, "99000001000101", "Tenant PlatAdmin LTDA", "HQ", 0, 0, "ACTIVE");
        await _fixture.InsertTenantScopeAsync(Guid.NewGuid(), tenantId, "BUYER");

        await _fixture.InsertUserAccountAsync(adminId, "platadmin@example.test", "Utilizador Teste 99", "ACTIVE", "PLATFORM_ADMIN");
        await _fixture.InsertUserAccountAsync(targetUserId, "dono@example.test", "Utilizador Teste 03", "ACTIVE", "NONE");

        var token = _fixture.CunharToken(sub: adminId, platformRole: "PLATFORM_ADMIN", viewMode: "NONE");

        using var client = _fixture.CreateHttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new
        {
            user_id = targetUserId.ToString("D"),
            is_owner = true,
            can_view_group = false,
            role_ids = new[] { roleBuyer.ToString("D") }
        };

        var response = await client.PostAsJsonAsync($"/api/v1/tenants/{tenantId:D}/members", payload);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        Assert.Equal("ACTIVE", root.GetProperty("status").GetString());
        Assert.True(root.GetProperty("is_owner").GetBoolean());
        Assert.False(root.GetProperty("can_view_group").GetBoolean());
    }

    [Fact(Timeout = 60000)]
    public async Task CA_08_2_Borda_VinculacaoComSucessoPorPlatformAdmin()
    {
        var groupId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcde");
        var tenantId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcd1");
        var adminId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcd9");
        var targetUserId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcd3");
        var roleBuyer = Guid.Parse("01900000-0000-7000-8000-000000000001");

        await _fixture.InsertEconomicGroupAsync(groupId, "Grupo Borda PlatAdmin", "ACTIVE");
        await _fixture.InsertTenantAsync(tenantId, groupId, "99000001000101", "Tenant Borda LTDA", "HQ", 0, 0, "ACTIVE");
        await _fixture.InsertTenantScopeAsync(Guid.NewGuid(), tenantId, "BUYER");

        await _fixture.InsertUserAccountAsync(adminId, "platadmin@example.test", "Utilizador Teste 99", "ACTIVE", "PLATFORM_ADMIN");
        await _fixture.InsertUserAccountAsync(targetUserId, "alvo@example.test", "Utilizador Teste 03", "ACTIVE", "NONE");

        var token = _fixture.CunharToken(sub: adminId, platformRole: "PLATFORM_ADMIN", viewMode: "NONE");

        using var client = _fixture.CreateHttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new
        {
            user_id = targetUserId.ToString("D"),
            is_owner = true,
            can_view_group = true,
            role_ids = new[] { roleBuyer.ToString("D") }
        };

        var response = await client.PostAsJsonAsync($"/api/v1/tenants/{tenantId:D}/members", payload);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.GetProperty("is_owner").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("can_view_group").GetBoolean());
    }

    #endregion

    #region CA-08.3

    [Fact(Timeout = 60000)]
    [Trait("Critico", "sim")]
    public async Task CA_08_3_ConflitoDeUnicidadeAoVincularMembroJaExistente()
    {
        var groupId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcde");
        var tenantId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcd1");
        var adminId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcd9");
        var targetUserId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcd3");
        var roleBuyer = Guid.Parse("01900000-0000-7000-8000-000000000001");

        await _fixture.InsertEconomicGroupAsync(groupId, "Grupo Unicidade Membro", "ACTIVE");
        await _fixture.InsertTenantAsync(tenantId, groupId, "99000001000101", "Tenant Unicidade LTDA", "HQ", 0, 0, "ACTIVE");
        await _fixture.InsertTenantScopeAsync(Guid.NewGuid(), tenantId, "BUYER");

        await _fixture.InsertUserAccountAsync(adminId, "platadmin@example.test", "Utilizador Teste 99", "ACTIVE", "PLATFORM_ADMIN");
        await _fixture.InsertUserAccountAsync(targetUserId, "existente@example.test", "Utilizador Teste 03", "ACTIVE", "NONE");

        var memberId = Guid.NewGuid();
        await _fixture.InsertTenantMemberAsync(memberId, tenantId, targetUserId, isOwner: false, canViewGroup: false, "ACTIVE");
        await _fixture.InsertMemberRoleAsync(Guid.NewGuid(), tenantId, memberId, roleBuyer);

        var token = _fixture.CunharToken(sub: adminId, platformRole: "PLATFORM_ADMIN", viewMode: "NONE");

        using var client = _fixture.CreateHttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new
        {
            user_id = targetUserId.ToString("D"),
            is_owner = false,
            can_view_group = true,
            role_ids = new[] { roleBuyer.ToString("D") }
        };

        var response = await client.PostAsJsonAsync($"/api/v1/tenants/{tenantId:D}/members", payload);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("ERR-CTX-005", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact(Timeout = 60000)]
    [Trait("Critico", "sim")]
    public async Task CA_08_3_Borda_ConflitoDeUnicidadeAoVincularMembroJaExistente()
    {
        var groupId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcde");
        var tenantId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcd1");
        var adminId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcd9");
        var targetUserId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcd3");
        var roleBuyer = Guid.Parse("01900000-0000-7000-8000-000000000001");

        await _fixture.InsertEconomicGroupAsync(groupId, "Grupo Concorrencia Membro", "ACTIVE");
        await _fixture.InsertTenantAsync(tenantId, groupId, "99000001000101", "Tenant Concorrente LTDA", "HQ", 0, 0, "ACTIVE");
        await _fixture.InsertTenantScopeAsync(Guid.NewGuid(), tenantId, "BUYER");

        await _fixture.InsertUserAccountAsync(adminId, "platadmin@example.test", "Utilizador Teste 99", "ACTIVE", "PLATFORM_ADMIN");
        await _fixture.InsertUserAccountAsync(targetUserId, "concorrente@example.test", "Utilizador Teste 03", "ACTIVE", "NONE");

        var token = _fixture.CunharToken(sub: adminId, platformRole: "PLATFORM_ADMIN", viewMode: "NONE");

        for (int rep = 0; rep < 20; rep++)
        {
            await _fixture.DeleteMemberByTenantAndUserAsync(tenantId, targetUserId);

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
                        user_id = targetUserId.ToString("D"),
                        is_owner = false,
                        can_view_group = false,
                        role_ids = new[] { roleBuyer.ToString("D") }
                    };
                    barrier.SignalAndWait(TimeSpan.FromSeconds(10));
                    return await c.PostAsJsonAsync($"/api/v1/tenants/{tenantId:D}/members", p);
                });
            }

            var responses = await Task.WhenAll(tasks);
            var statuses = responses.Select(r => r.StatusCode).ToList();

            Assert.Contains(HttpStatusCode.Created, statuses);
            Assert.Contains(HttpStatusCode.Conflict, statuses);
        }
    }

    #endregion

    #region CA-08.4

    [Fact(Timeout = 60000)]
    [Trait("Critico", "sim")]
    public async Task CA_08_4_RejeicaoAoAtribuirPapelComEscopoInativoNoTenant()
    {
        var groupId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcde");
        var tenantId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcd1");
        var adminId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcd9");
        var targetUserId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcd3");
        var roleCarrier = Guid.Parse("01900000-0000-7000-8000-000000000003");

        await _fixture.InsertEconomicGroupAsync(groupId, "Grupo Escopo Inativo", "ACTIVE");
        await _fixture.InsertTenantAsync(tenantId, groupId, "99000001000101", "Tenant So Buyer LTDA", "HQ", 0, 0, "ACTIVE");
        await _fixture.InsertTenantScopeAsync(Guid.NewGuid(), tenantId, "BUYER");

        await _fixture.InsertUserAccountAsync(adminId, "platadmin@example.test", "Utilizador Teste 99", "ACTIVE", "PLATFORM_ADMIN");
        await _fixture.InsertUserAccountAsync(targetUserId, "inativo@example.test", "Utilizador Teste 03", "ACTIVE", "NONE");

        var token = _fixture.CunharToken(sub: adminId, platformRole: "PLATFORM_ADMIN", viewMode: "NONE");

        using var client = _fixture.CreateHttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new
        {
            user_id = targetUserId.ToString("D"),
            is_owner = false,
            can_view_group = true,
            role_ids = new[] { roleCarrier.ToString("D") }
        };

        var response = await client.PostAsJsonAsync($"/api/v1/tenants/{tenantId:D}/members", payload);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("ERR-CTX-006", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact(Timeout = 60000)]
    [Trait("Critico", "sim")]
    public async Task CA_08_4_Borda_RejeicaoAoAtribuirPapelComEscopoInativoNoTenant()
    {
        var groupId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcde");
        var tenantId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcd1");
        var adminId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcd9");
        var targetUserId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcd3");
        var roleBuyer = Guid.Parse("01900000-0000-7000-8000-000000000001");
        var roleCarrier = Guid.Parse("01900000-0000-7000-8000-000000000003");

        await _fixture.InsertEconomicGroupAsync(groupId, "Grupo Borda Misto", "ACTIVE");
        await _fixture.InsertTenantAsync(tenantId, groupId, "99000001000101", "Tenant Misto LTDA", "HQ", 0, 0, "ACTIVE");
        await _fixture.InsertTenantScopeAsync(Guid.NewGuid(), tenantId, "BUYER");

        await _fixture.InsertUserAccountAsync(adminId, "platadmin@example.test", "Utilizador Teste 99", "ACTIVE", "PLATFORM_ADMIN");
        await _fixture.InsertUserAccountAsync(targetUserId, "misto@example.test", "Utilizador Teste 03", "ACTIVE", "NONE");

        var token = _fixture.CunharToken(sub: adminId, platformRole: "PLATFORM_ADMIN", viewMode: "NONE");

        using var client = _fixture.CreateHttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new
        {
            user_id = targetUserId.ToString("D"),
            is_owner = false,
            can_view_group = true,
            role_ids = new[] { roleBuyer.ToString("D"), roleCarrier.ToString("D") }
        };

        var response = await client.PostAsJsonAsync($"/api/v1/tenants/{tenantId:D}/members", payload);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("ERR-CTX-006", doc.RootElement.GetProperty("code").GetString());

        var memberCount = await _fixture.GetMemberCountAsync(tenantId);
        Assert.Equal(0, memberCount);
    }

    #endregion

    #region CA-08.5

    [Fact(Timeout = 180000)]
    [Trait("Critico", "sim")]
    public async Task CA_08_5_RollbackTotalNaVinculacaoDeMembroAposFalhaSimulada()
    {
        var groupId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcde");
        var tenantId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcd1");
        var adminId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcd9");
        var targetUserId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcd3");
        var roleBuyer = Guid.Parse("01900000-0000-7000-8000-000000000001");

        await _fixture.InsertEconomicGroupAsync(groupId, "Grupo Falha Membro", "ACTIVE");
        await _fixture.InsertTenantAsync(tenantId, groupId, "99000001000101", "Tenant Falha Membro LTDA", "HQ", 0, 0, "ACTIVE");
        await _fixture.InsertTenantScopeAsync(Guid.NewGuid(), tenantId, "BUYER");

        await _fixture.InsertUserAccountAsync(adminId, "platadmin@example.test", "Utilizador Teste 99", "ACTIVE", "PLATFORM_ADMIN");
        await _fixture.InsertUserAccountAsync(targetUserId, "alvo@example.test", "Utilizador Teste 03", "ACTIVE", "NONE");

        await _fixture.CreateMemberRoleTriggerToFailAsync();

        try
        {
            var token = _fixture.CunharToken(sub: adminId, platformRole: "PLATFORM_ADMIN", viewMode: "NONE");
            using var client = _fixture.CreateHttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var payload = new
            {
                user_id = targetUserId.ToString("D"),
                is_owner = false,
                can_view_group = true,
                role_ids = new[] { roleBuyer.ToString("D") }
            };

            var response = await client.PostAsJsonAsync($"/api/v1/tenants/{tenantId:D}/members", payload);
            Assert.True(response.StatusCode == HttpStatusCode.InternalServerError || response.StatusCode == HttpStatusCode.ServiceUnavailable);

            var memberCount = await _fixture.GetMemberCountAsync(tenantId);
            Assert.Equal(0, memberCount);
        }
        finally
        {
            await _fixture.DropMemberRoleTriggerToFailAsync();
        }
    }

    [Fact(Timeout = 180000)]
    [Trait("Critico", "sim")]
    public async Task CA_08_5_Borda_RollbackTotalNaVinculacaoDeMembroAposFalhaSimulada()
    {
        var groupId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcde");
        var tenantId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcd1");
        var adminId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcd9");
        var targetUserId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcd3");
        var roleBuyer = Guid.Parse("01900000-0000-7000-8000-000000000001");

        await _fixture.InsertEconomicGroupAsync(groupId, "Grupo Borda Falha Membro", "ACTIVE");
        await _fixture.InsertTenantAsync(tenantId, groupId, "99000001000101", "Tenant Borda Falha Membro LTDA", "HQ", 0, 0, "ACTIVE");
        await _fixture.InsertTenantScopeAsync(Guid.NewGuid(), tenantId, "BUYER");

        await _fixture.InsertUserAccountAsync(adminId, "platadmin@example.test", "Utilizador Teste 99", "ACTIVE", "PLATFORM_ADMIN");
        await _fixture.InsertUserAccountAsync(targetUserId, "alvo@example.test", "Utilizador Teste 03", "ACTIVE", "NONE");

        await _fixture.CreateMemberRoleTriggerToFailAsync();

        try
        {
            var token = _fixture.CunharToken(sub: adminId, platformRole: "PLATFORM_ADMIN", viewMode: "NONE");
            using var client = _fixture.CreateHttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var payload = new
            {
                user_id = targetUserId.ToString("D"),
                is_owner = true,
                can_view_group = false,
                role_ids = new[] { roleBuyer.ToString("D") }
            };

            var response = await client.PostAsJsonAsync($"/api/v1/tenants/{tenantId:D}/members", payload);
            Assert.True(response.StatusCode == HttpStatusCode.InternalServerError || response.StatusCode == HttpStatusCode.ServiceUnavailable);

            var auditCount = await _fixture.GetAuditCountAsync("MEMBER_ADDED", tenantId: tenantId);
            Assert.Equal(0, auditCount);
        }
        finally
        {
            await _fixture.DropMemberRoleTriggerToFailAsync();
        }
    }

    #endregion

    #region CA-08.6

    [Theory(Timeout = 60000)]
    [InlineData("BUYER")]
    [InlineData("TENANT_ADMIN_OUTRO_TENANT")]
    [Trait("Critico", "sim")]
    public async Task CA_08_6_NegacaoDeAutorizacaoParaChamadoresSemPermissao(string perfil)
    {
        var groupId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcde");
        var tenantId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcd1");
        var outroTenantId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcd2");
        var callerId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcd0");
        var targetUserId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcd3");
        var roleBuyer = Guid.Parse("01900000-0000-7000-8000-000000000001");

        await _fixture.InsertEconomicGroupAsync(groupId, "Grupo Sem Permissao Membro", "ACTIVE");
        await _fixture.InsertTenantAsync(tenantId, groupId, "99000001000101", "Tenant Alvo LTDA", "HQ", 0, 0, "ACTIVE");
        await _fixture.InsertTenantScopeAsync(Guid.NewGuid(), tenantId, "BUYER");

        await _fixture.InsertUserAccountAsync(callerId, "caller@example.test", "Utilizador Teste 01", "ACTIVE", "NONE");
        await _fixture.InsertUserAccountAsync(targetUserId, "alvo@example.test", "Utilizador Teste 03", "ACTIVE", "NONE");

        string activeScope = perfil.StartsWith("TENANT_ADMIN") ? "TENANT_ADMIN" : "BUYER";
        var perms = perfil.StartsWith("TENANT_ADMIN") ? new[] { "membro:vincular" } : Array.Empty<string>();
        var allowed = perfil.StartsWith("TENANT_ADMIN") ? new[] { outroTenantId } : new[] { tenantId };

        var token = _fixture.CunharToken(
            sub: callerId,
            platformRole: "NONE",
            viewMode: "SINGLE",
            groupId: groupId,
            tenantId: allowed[0],
            allowedTenants: allowed,
            activeScope: activeScope,
            permissions: perms);

        using var client = _fixture.CreateHttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new
        {
            user_id = targetUserId.ToString("D"),
            is_owner = false,
            can_view_group = true,
            role_ids = new[] { roleBuyer.ToString("D") }
        };

        var response = await client.PostAsJsonAsync($"/api/v1/tenants/{tenantId:D}/members", payload);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("ERR-CTX-007", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact(Timeout = 60000)]
    [Trait("Critico", "sim")]
    public async Task CA_08_6_Borda_NegacaoDeAutorizacaoParaChamadoresSemPermissao()
    {
        var groupId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcde");
        var tenantId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcd1");
        var callerId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcd0");
        var targetUserId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcd3");
        var roleBuyer = Guid.Parse("01900000-0000-7000-8000-000000000001");

        await _fixture.InsertEconomicGroupAsync(groupId, "Grupo Sem Permissao Exata", "ACTIVE");
        await _fixture.InsertTenantAsync(tenantId, groupId, "99000001000101", "Tenant Alvo LTDA", "HQ", 0, 0, "ACTIVE");
        await _fixture.InsertTenantScopeAsync(Guid.NewGuid(), tenantId, "BUYER");

        await _fixture.InsertUserAccountAsync(callerId, "caller@example.test", "Utilizador Teste 01", "ACTIVE", "NONE");
        await _fixture.InsertUserAccountAsync(targetUserId, "alvo@example.test", "Utilizador Teste 03", "ACTIVE", "NONE");

        var token = _fixture.CunharToken(
            sub: callerId,
            platformRole: "NONE",
            viewMode: "SINGLE",
            groupId: groupId,
            tenantId: tenantId,
            allowedTenants: new[] { tenantId },
            activeScope: "TENANT_ADMIN",
            permissions: new[] { "tenant:criar" });

        using var client = _fixture.CreateHttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new
        {
            user_id = targetUserId.ToString("D"),
            is_owner = false,
            can_view_group = true,
            role_ids = new[] { roleBuyer.ToString("D") }
        };

        var response = await client.PostAsJsonAsync($"/api/v1/tenants/{tenantId:D}/members", payload);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("ERR-CTX-007", doc.RootElement.GetProperty("code").GetString());
    }

    #endregion

    #region CA-08.7

    [Theory(Timeout = 60000)]
    [InlineData("tenant")]
    [InlineData("usuario")]
    [InlineData("papel")]
    public async Task CA_08_7_RejeicaoAoReferenciarRecursoInexistente(string tipoRecurso)
    {
        var groupId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcde");
        var tenantId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcd1");
        var adminId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcd9");
        var targetUserId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcd3");
        var roleBuyer = Guid.Parse("01900000-0000-7000-8000-000000000001");

        await _fixture.InsertEconomicGroupAsync(groupId, "Grupo Recurso Inexistente", "ACTIVE");
        if (tipoRecurso != "tenant")
        {
            await _fixture.InsertTenantAsync(tenantId, groupId, "99000001000101", "Tenant Existente LTDA", "HQ", 0, 0, "ACTIVE");
            await _fixture.InsertTenantScopeAsync(Guid.NewGuid(), tenantId, "BUYER");
        }

        await _fixture.InsertUserAccountAsync(adminId, "platadmin@example.test", "Utilizador Teste 99", "ACTIVE", "PLATFORM_ADMIN");
        if (tipoRecurso != "usuario")
        {
            await _fixture.InsertUserAccountAsync(targetUserId, "alvo@example.test", "Utilizador Teste 03", "ACTIVE", "NONE");
        }

        var tIdParaRequisicao = (tipoRecurso == "tenant") ? Guid.NewGuid() : tenantId;
        var uIdParaRequisicao = (tipoRecurso == "usuario") ? Guid.NewGuid() : targetUserId;
        var rIdParaRequisicao = (tipoRecurso == "papel") ? Guid.NewGuid() : roleBuyer;

        var token = _fixture.CunharToken(sub: adminId, platformRole: "PLATFORM_ADMIN", viewMode: "NONE");

        using var client = _fixture.CreateHttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new
        {
            user_id = uIdParaRequisicao.ToString("D"),
            is_owner = false,
            can_view_group = true,
            role_ids = new[] { rIdParaRequisicao.ToString("D") }
        };

        var response = await client.PostAsJsonAsync($"/api/v1/tenants/{tIdParaRequisicao:D}/members", payload);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("ERR-CTX-008", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact(Timeout = 60000)]
    public async Task CA_08_7_Borda_RejeicaoAoReferenciarRecursoInexistente()
    {
        var groupId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcde");
        var tokenTenantId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcd1");
        var inexistenteTenantId = Guid.Parse("01923456-789a-7bc8-9012-000000000000");
        var callerId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcd0");
        var targetUserId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcd3");
        var roleBuyer = Guid.Parse("01900000-0000-7000-8000-000000000001");

        await _fixture.InsertEconomicGroupAsync(groupId, "Grupo Borda Inexistente", "ACTIVE");
        await _fixture.InsertTenantAsync(tokenTenantId, groupId, "99000001000101", "Tenant Token LTDA", "HQ", 0, 0, "ACTIVE");
        await _fixture.InsertTenantScopeAsync(Guid.NewGuid(), tokenTenantId, "BUYER");

        await _fixture.InsertUserAccountAsync(callerId, "caller@example.test", "Utilizador Teste 01", "ACTIVE", "NONE");
        await _fixture.InsertUserAccountAsync(targetUserId, "alvo@example.test", "Utilizador Teste 03", "ACTIVE", "NONE");

        var token = _fixture.CunharToken(
            sub: callerId,
            platformRole: "NONE",
            viewMode: "SINGLE",
            groupId: groupId,
            tenantId: tokenTenantId,
            allowedTenants: new[] { tokenTenantId },
            activeScope: "TENANT_ADMIN",
            permissions: new[] { "membro:vincular" });

        using var client = _fixture.CreateHttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new
        {
            user_id = targetUserId.ToString("D"),
            is_owner = false,
            can_view_group = true,
            role_ids = new[] { roleBuyer.ToString("D") }
        };

        var response = await client.PostAsJsonAsync($"/api/v1/tenants/{inexistenteTenantId:D}/members", payload);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("ERR-CTX-007", doc.RootElement.GetProperty("code").GetString());
    }

    #endregion

    #region CA-08.8

    [Theory(Timeout = 60000)]
    [InlineData("{\"role_ids\":[]}")]
    [InlineData("{\"role_ids\":[\"01900000-0000-7000-8000-000000000001\",\"01900000-0000-7000-8000-000000000001\"]}")]
    [InlineData("{\"user_id\":\"nao-uuid\"}")]
    public async Task CA_08_8_RejeicaoDeFormatoDeEntradaMalformada(string caso)
    {
        var adminId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcd9");
        var tenantId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcd1");

        await _fixture.InsertUserAccountAsync(adminId, "platadmin@example.test", "Utilizador Teste 99", "ACTIVE", "PLATFORM_ADMIN");

        var token = _fixture.CunharToken(sub: adminId, platformRole: "PLATFORM_ADMIN", viewMode: "NONE");

        using var client = _fixture.CreateHttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var baseJson = new JsonObject
        {
            ["user_id"] = "01923456-789a-7bc8-9012-3456789abcd3",
            ["is_owner"] = false,
            ["can_view_group"] = true,
            ["role_ids"] = new JsonArray("01900000-0000-7000-8000-000000000001")
        };

        var patchDoc = JsonDocument.Parse(caso);
        foreach (var prop in patchDoc.RootElement.EnumerateObject())
        {
            baseJson[prop.Name] = JsonNode.Parse(prop.Value.GetRawText());
        }

        var content = new StringContent(baseJson.ToJsonString(), Encoding.UTF8, "application/json");
        var response = await client.PostAsync($"/api/v1/tenants/{tenantId:D}/members", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("ERR-VAL-001", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact(Timeout = 60000)]
    public async Task CA_08_8_Borda_RejeicaoDeFormatoDeEntradaMalformada()
    {
        var adminId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcd9");
        var tenantId = Guid.Parse("01923456-789a-7bc8-9012-3456789abcd1");

        await _fixture.InsertUserAccountAsync(adminId, "platadmin@example.test", "Utilizador Teste 99", "ACTIVE", "PLATFORM_ADMIN");

        var token = _fixture.CunharToken(sub: adminId, platformRole: "PLATFORM_ADMIN", viewMode: "NONE");

        using var client = _fixture.CreateHttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var roleArray11 = new JsonArray();
        for (int i = 0; i < 11; i++)
        {
            roleArray11.Add(Guid.NewGuid().ToString("D"));
        }

        var payload11 = new JsonObject
        {
            ["user_id"] = "01923456-789a-7bc8-9012-3456789abcd3",
            ["is_owner"] = false,
            ["can_view_group"] = true,
            ["role_ids"] = roleArray11
        };

        var content11 = new StringContent(payload11.ToJsonString(), Encoding.UTF8, "application/json");
        var res11 = await client.PostAsync($"/api/v1/tenants/{tenantId:D}/members", content11);
        Assert.Equal(HttpStatusCode.BadRequest, res11.StatusCode);

        var contentBadPath = new StringContent(payload11.ToJsonString(), Encoding.UTF8, "application/json");
        var resBadPath = await client.PostAsync("/api/v1/tenants/nao-uuid/members", contentBadPath);
        Assert.Equal(HttpStatusCode.BadRequest, resBadPath.StatusCode);
    }

    #endregion

    #region Fixture e Infraestrutura

    public sealed class F02Fixture : IAsyncLifetime
    {
        private readonly PostgreSqlContainer _postgres;
        private readonly RedisContainer _redis;
        private readonly KafkaContainer _kafka;
        private WebApplicationFactory<Program>? _factory;
        private RSA _rsa = null!;
        private TestClock _clock = null!;
        private string _connectionString = null!;
        private IConnectionMultiplexer _redisMultiplexer = null!;

        public F02Fixture()
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
            var redisConn = _redis.GetConnectionString();
            var kafkaBootstrap = _kafka.GetBootstrapAddress().Replace("plaintext://", "").TrimEnd('/');

            Environment.SetEnvironmentVariable("DB_CONNECTION_STRING", _connectionString);
            Environment.SetEnvironmentVariable("REDIS_CONNECTION_STRING", redisConn);
            Environment.SetEnvironmentVariable("KAFKA_BOOTSTRAP_SERVERS", kafkaBootstrap);
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
            Environment.SetEnvironmentVariable("JWT_RSA_PRIVATE_KEY_PEM", pem);

            _redisMultiplexer = await ConnectionMultiplexer.ConnectAsync($"{redisConn},allowAdmin=true");

            _factory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.UseEnvironment("Testing");
                    builder.ConfigureServices(services =>
                    {
                        services.AddSingleton<TimeProvider>(_clock);
                    });
                });

            // Forçar inicialização para rodar migrações
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
            if (viewMode == "SINGLE")
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
            cmd.CommandText = "INSERT INTO tenant (id, group_id, cnpj, legal_name, branch_type, geo_lat, geo_lng, status, created_at, updated_at) VALUES (@id, @groupId, @cnpj, @legalName, @branchType, @lat, @lng, @status, @now, @now);";
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
            cmd.CommandText = "INSERT INTO tenant_scope (id, tenant_id, scope, is_active, created_at) VALUES (@id, @tenantId, @scope, true, @now);";
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("scope", scope);
            cmd.Parameters.AddWithValue("now", _clock.GetUtcNow());
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task InsertUserAccountAsync(Guid id, string email, string name, string status, string platformRole)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO user_account (id, email, password_hash, name, status, platform_role, failed_login_attempts, locked_until, created_at, updated_at) VALUES (@id, @email, @pwd, @name, @status, @role, 0, NULL, @now, @now) ON CONFLICT (id) DO NOTHING;";
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("email", email);
            cmd.Parameters.AddWithValue("pwd", "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHRzb21lc2FsdA$abcdefghijklmnopqrstuvwxyz0123456789ABCDEFG");
            cmd.Parameters.AddWithValue("name", name);
            cmd.Parameters.AddWithValue("status", status);
            cmd.Parameters.AddWithValue("role", platformRole);
            cmd.Parameters.AddWithValue("now", _clock.GetUtcNow());
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task InsertTenantMemberAsync(Guid id, Guid tenantId, Guid userId, bool isOwner, bool canViewGroup, string status)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO tenant_member (id, tenant_id, user_id, is_owner, can_view_group, status, created_at, updated_at) VALUES (@id, @tenantId, @userId, @isOwner, @canViewGroup, @status, @now, @now);";
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("userId", userId);
            cmd.Parameters.AddWithValue("isOwner", isOwner);
            cmd.Parameters.AddWithValue("canViewGroup", canViewGroup);
            cmd.Parameters.AddWithValue("status", status);
            cmd.Parameters.AddWithValue("now", _clock.GetUtcNow());
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task InsertMemberRoleAsync(Guid id, Guid tenantId, Guid memberId, Guid roleId)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO member_role (id, tenant_id, tenant_member_id, role_id, created_at) VALUES (@id, @tenantId, @memberId, @roleId, @now);";
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("memberId", memberId);
            cmd.Parameters.AddWithValue("roleId", roleId);
            cmd.Parameters.AddWithValue("now", _clock.GetUtcNow());
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task DeleteTenantByCnpjAsync(string cnpj)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                DELETE FROM auth_audit_log WHERE tenant_id IN (SELECT id FROM tenant WHERE cnpj = @cnpj);
                DELETE FROM member_role WHERE tenant_id IN (SELECT id FROM tenant WHERE cnpj = @cnpj);
                DELETE FROM tenant_member WHERE tenant_id IN (SELECT id FROM tenant WHERE cnpj = @cnpj);
                DELETE FROM tenant_scope WHERE tenant_id IN (SELECT id FROM tenant WHERE cnpj = @cnpj);
                DELETE FROM outbox_message WHERE topic = 'empresa_cadastrada' AND partition_key IN (SELECT id::text FROM tenant WHERE cnpj = @cnpj);
                DELETE FROM tenant WHERE cnpj = @cnpj;";
            cmd.Parameters.AddWithValue("cnpj", cnpj);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task DeleteMemberByTenantAndUserAsync(Guid tenantId, Guid userId)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                DELETE FROM member_role WHERE tenant_member_id IN (SELECT id FROM tenant_member WHERE tenant_id = @tenantId AND user_id = @userId);
                DELETE FROM auth_audit_log WHERE tenant_id = @tenantId AND user_id = @userId;
                DELETE FROM tenant_member WHERE tenant_id = @tenantId AND user_id = @userId;";
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("userId", userId);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<bool> TenantExistsByCnpjAsync(string cnpj)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM tenant WHERE cnpj = @cnpj;";
            cmd.Parameters.AddWithValue("cnpj", cnpj);
            var count = (long)(await cmd.ExecuteScalarAsync() ?? 0L);
            return count > 0;
        }

        public sealed record TenantRecord(string Status, string Cnpj, string LegalName);

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

        public async Task<int> GetMemberCountAsync(Guid tenantId)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM tenant_member WHERE tenant_id = @id;";
            cmd.Parameters.AddWithValue("id", tenantId);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync() ?? 0);
        }

        public async Task<bool> HasTenantMemberWithRoleAsync(Guid tenantId, Guid userId, string roleName)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT COUNT(*) FROM tenant_member tm
                JOIN member_role mr ON mr.tenant_member_id = tm.id
                JOIN role r ON r.id = mr.role_id
                WHERE tm.tenant_id = @tenantId AND tm.user_id = @userId AND r.name = @roleName;";
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("userId", userId);
            cmd.Parameters.AddWithValue("roleName", roleName);
            var count = Convert.ToInt32(await cmd.ExecuteScalarAsync() ?? 0);
            return count > 0;
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

        public async Task<int> GetOutboxCountByCnpjAsync(string cnpj)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM outbox_message WHERE payload->>'cnpj' = @cnpj;";
            cmd.Parameters.AddWithValue("cnpj", cnpj);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync() ?? 0);
        }

        public async Task<int> GetAuditCountAsync(string eventType, Guid? tenantId = null, Guid? userId = null)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            var sql = "SELECT COUNT(*) FROM auth_audit_log WHERE event_type = @eventType";
            cmd.Parameters.AddWithValue("eventType", eventType);
            if (tenantId.HasValue)
            {
                sql += " AND tenant_id = @tenantId";
                cmd.Parameters.AddWithValue("tenantId", tenantId.Value);
            }
            if (userId.HasValue)
            {
                sql += " AND user_id = @userId";
                cmd.Parameters.AddWithValue("userId", userId.Value);
            }
            cmd.CommandText = sql;
            return Convert.ToInt32(await cmd.ExecuteScalarAsync() ?? 0);
        }

        public async Task CreateScopeTriggerToFailAsync()
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE OR REPLACE FUNCTION trigger_fail_scope() RETURNS trigger AS $$
                BEGIN
                    RAISE EXCEPTION 'Falha simulada na gravacao de tenant_scope';
                END;
                $$ LANGUAGE plpgsql;
                DROP TRIGGER IF EXISTS trg_fail_scope ON tenant_scope;
                CREATE TRIGGER trg_fail_scope BEFORE INSERT ON tenant_scope FOR EACH ROW EXECUTE FUNCTION trigger_fail_scope();
            ";
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task DropScopeTriggerToFailAsync()
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                DROP TRIGGER IF EXISTS trg_fail_scope ON tenant_scope;
                DROP FUNCTION IF EXISTS trigger_fail_scope();
            ";
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task CreateMemberRoleTriggerToFailAsync()
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE OR REPLACE FUNCTION trigger_fail_member_role() RETURNS trigger AS $$
                BEGIN
                    RAISE EXCEPTION 'Falha simulada na gravacao de member_role';
                END;
                $$ LANGUAGE plpgsql;
                DROP TRIGGER IF EXISTS trg_fail_member_role ON member_role;
                CREATE TRIGGER trg_fail_member_role BEFORE INSERT ON member_role FOR EACH ROW EXECUTE FUNCTION trigger_fail_member_role();
            ";
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task DropMemberRoleTriggerToFailAsync()
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                DROP TRIGGER IF EXISTS trg_fail_member_role ON member_role;
                DROP FUNCTION IF EXISTS trigger_fail_member_role();
            ";
            await cmd.ExecuteNonQueryAsync();
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
