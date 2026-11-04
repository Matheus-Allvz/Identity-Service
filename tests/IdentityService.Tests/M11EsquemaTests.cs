using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using IdentityService.Modules.M00Transversal;
using IdentityService.Modules.M00Transversal.Auth;
using IdentityService.Modules.M07Chaves;
using IdentityService.Modules.M08Acessos;
using IdentityService.Modules.M11Esquema;
using IdentityService.Modules.M11Esquema.Persistence;
using IdentityService.Modules.M11Esquema.Persistence.Entities;
using IdentityService.Modules.M11Esquema.Persistence.Enums;
using IdentityService.Modules.M13Isolamento;
using IdentityService.Modules.M13Isolamento.Interceptors;
using IdentityService.Modules.M13Isolamento.Interface;
using IdentityService.Modules.M14Eventos;
using IdentityService.Tests.Fixtures;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;
using M11Module = IdentityService.Modules.M11Esquema.ModuleRegistration;

namespace IdentityService.Tests;

[Trait("Fase", "F00")]
[Collection("SharedContainers")]
public class M11EsquemaTests
{
    private readonly TestcontainersFixture _fixture;

    public M11EsquemaTests(TestcontainersFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(Timeout = 60000)]
    public async Task CA_19_1_Borda_MigracaoESeedNumaBaseDeDadosVazia()
    {
        // 1. Criar uma base de dados limpa temporária no PostgreSQL
        var masterConnStr = _fixture.GetConnectionString();
        var tempDbName = $"identity_test_ca191_{Guid.NewGuid():N}";

        await using (var masterConn = new NpgsqlConnection(masterConnStr))
        {
            await masterConn.OpenAsync();
            await using var cmd = masterConn.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE \"{tempDbName}\";";
            await cmd.ExecuteNonQueryAsync();
        }

        var builder = new NpgsqlConnectionStringBuilder(masterConnStr)
        {
            Database = tempDbName
        };
        var tempConnStr = builder.ConnectionString;

        try
        {
            // 2. Quando arranque invoca Database.MigrateAsync()
            await M11Module.MigrateDatabaseAsync(tempConnStr);

            // 3. Então tabelas ENT-01 a ENT-10 são criadas
            var expectedTables = new[]
            {
                "economic_group",
                "tenant",
                "tenant_scope",
                "user_account",
                "tenant_member",
                "role",
                "member_role",
                "refresh_token",
                "auth_audit_log",
                "outbox_message"
            };

            await using (var conn = new NpgsqlConnection(tempConnStr))
            {
                await conn.OpenAsync();

                // Verificar existência de cada uma das 10 tabelas
                foreach (var tableName in expectedTables)
                {
                    await using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name = @name;";
                    cmd.Parameters.AddWithValue("name", tableName);
                    var count = Convert.ToInt64(await cmd.ExecuteScalarAsync());
                    Assert.True(count == 1, $"Tabela esperada {tableName} não encontrada na base migrada.");
                }

                // Verificar papel identity_app existe
                await using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM pg_roles WHERE rolname = 'identity_app';";
                    var roleCount = Convert.ToInt64(await cmd.ExecuteScalarAsync());
                    Assert.True(roleCount == 1, "Papel identity_app não foi criado.");
                }

                // Verificar tabelas ITenantScoped têm RLS forçado (tenant_scope, tenant_member, member_role)
                var rlsTables = new[] { "tenant_scope", "tenant_member", "member_role" };
                foreach (var rlsTable in rlsTables)
                {
                    await using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT relrowsecurity, relforcerowsecurity FROM pg_class WHERE relname = @name;";
                    cmd.Parameters.AddWithValue("name", rlsTable);
                    await using var reader = await cmd.ExecuteReaderAsync();
                    Assert.True(await reader.ReadAsync(), $"Tabela {rlsTable} não encontrada em pg_class.");
                    var rowSecurity = reader.GetBoolean(0);
                    var forceRowSecurity = reader.GetBoolean(1);
                    Assert.True(rowSecurity, $"Tabela {rlsTable} deve ter relrowsecurity = true.");
                    Assert.True(forceRowSecurity, $"Tabela {rlsTable} deve ter relforcerowsecurity = true.");
                }

                // Verificar tabela role contém os 4 papéis com 17 permissões
                await using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT name, permissions FROM role ORDER BY name;";
                    await using var reader = await cmd.ExecuteReaderAsync();
                    var roles = new Dictionary<string, string[]>();
                    var allPermissions = new HashSet<string>();
                    while (await reader.ReadAsync())
                    {
                        var name = reader.GetString(0);
                        var perms = (string[])reader.GetValue(1);
                        roles[name] = perms;
                        foreach (var p in perms)
                        {
                            allPermissions.Add(p);
                        }
                    }

                    Assert.Equal(4, roles.Count);
                    Assert.Contains("BUYER_OPERATOR", roles.Keys);
                    Assert.Contains("SUPPLIER_SALES", roles.Keys);
                    Assert.Contains("CARRIER_DISPATCHER", roles.Keys);
                    Assert.Contains("GROUP_MANAGER", roles.Keys);
                    Assert.Equal(17, allPermissions.Count);
                }
            }

            // 4. E arranque conclui com 200 em GET /.well-known/jwks.json
            var appBuilder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = "Testing"
            });
            appBuilder.WebHost.UseTestServer();
            appBuilder.Configuration["DB_CONNECTION_STRING"] = tempConnStr;
            appBuilder.Configuration["REDIS_CONNECTION_STRING"] = _fixture.GetRedisConnectionString();
            appBuilder.Configuration["KAFKA_BOOTSTRAP_SERVERS"] = _fixture.GetKafkaBootstrapServers();

            appBuilder.Services.AddM00Transversal(appBuilder.Configuration);
            appBuilder.Services.AddM07Chaves();
            appBuilder.Services.AddM08Acessos();
            appBuilder.Services.AddM11Esquema(appBuilder.Configuration);
            appBuilder.Services.AddM13Isolamento();
            appBuilder.Services.AddM14Eventos(appBuilder.Configuration);

            var app = appBuilder.Build();
            app.UseM00Transversal();
            app.MapM07Chaves();
            await app.StartAsync();

            var client = app.GetTestClient();
            var response = await client.GetAsync("/.well-known/jwks.json");
            var content = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"Status {response.StatusCode}, Body: {content}");
            await app.StopAsync();
        }
        finally
        {
            // Limpar base temporária
            await using var masterConn = new NpgsqlConnection(masterConnStr);
            await masterConn.OpenAsync();
            await using var cmd = masterConn.CreateCommand();
            cmd.CommandText = $"DROP DATABASE IF EXISTS \"{tempDbName}\" WITH (FORCE);";
            await cmd.ExecuteNonQueryAsync();
        }
    }

    [Fact(Timeout = 60000)]
    public async Task CA_19_2_Borda_IdempotenciaDaMigracaoESeedEmReinicio()
    {
        var connStr = _fixture.GetConnectionString();

        // 1. Base já migrada com os 4 papéis em "role"
        await using (var conn = new NpgsqlConnection(connStr))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM role;";
            var initialCount = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            Assert.Equal(4, initialCount);
        }

        // 2. Quando serviço reinicia e executa Database.MigrateAsync() novamente
        await M11Module.MigrateDatabaseAsync(connStr);

        // 3. Então nenhuma nova migração é aplicada e "role" mantém exatamente 4 registos
        await using (var conn = new NpgsqlConnection(connStr))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM role;";
            var countAfter = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            Assert.Equal(4, countAfter);
        }

        // 4. E arranque conclui com 200 em GET /.well-known/jwks.json
        var appBuilder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing"
        });
        appBuilder.WebHost.UseTestServer();
        appBuilder.Configuration["DB_CONNECTION_STRING"] = connStr;
        appBuilder.Configuration["REDIS_CONNECTION_STRING"] = _fixture.GetRedisConnectionString();
        appBuilder.Configuration["KAFKA_BOOTSTRAP_SERVERS"] = _fixture.GetKafkaBootstrapServers();

        appBuilder.Services.AddM00Transversal(appBuilder.Configuration);
        appBuilder.Services.AddM07Chaves();
        appBuilder.Services.AddM08Acessos();
        appBuilder.Services.AddM11Esquema(appBuilder.Configuration);
        appBuilder.Services.AddM13Isolamento();
        appBuilder.Services.AddM14Eventos(appBuilder.Configuration);

        var app = appBuilder.Build();
        app.UseM00Transversal();
        app.MapM07Chaves();
        await app.StartAsync();

        var client = app.GetTestClient();
        var response = await client.GetAsync("/.well-known/jwks.json");
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Status {response.StatusCode}, Body: {content}");
        await app.StopAsync();
    }

    [Theory(Timeout = 60000)]
    [InlineData("")]
    [InlineData("Host=127.0.0.1;Port=5433;Database=identity;Username=invalido;Timeout=1;CommandTimeout=1")]
    public async Task CA_19_3_Borda_FalhaArranqueSemConfiguracaoBaseDados(string configuracao)
    {
        var dllPath = Path.Combine(AppContext.BaseDirectory, "IdentityService.dll");
        if (!File.Exists(dllPath))
        {
            dllPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../src/IdentityService/bin/Debug/net10.0/IdentityService.dll"));
        }

        Assert.True(File.Exists(dllPath), $"IdentityService.dll não encontrado em {dllPath}");

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"\"{dllPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        psi.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        psi.Environment["DB_CONNECTION_STRING"] = configuracao;
        psi.Environment["REDIS_CONNECTION_STRING"] = "127.0.0.1:6379";
        psi.Environment["KAFKA_BOOTSTRAP_SERVERS"] = "127.0.0.1:9092";

        using var process = Process.Start(psi);
        Assert.NotNull(process);

        var exited = await Task.Run(() => process.WaitForExit(30000));
        Assert.True(exited, "O processo não encerrou no tempo esperado.");
        Assert.Equal(1, process.ExitCode);
        Assert.True(process.HasExited, "A aplicação não deve continuar em execução nem abrir portas de atendimento.");
    }

    [Fact(Timeout = 60000)]
    public async Task CA_19_4_Borda_IntegridadeReferencial_OnDeleteRestrict()
    {
        await _fixture.CleanDatabaseAsync();

        var groupId = Guid.Parse("01900000-0000-7000-8000-000000000001");
        var tenantId = Guid.Parse("01900000-0000-7000-8000-000000000010");
        var userId = Guid.Parse("01900000-0000-7000-8000-000000000020");
        var memberId = Guid.Parse("01900000-0000-7000-8000-000000000030");
        var now = DateTimeOffset.UtcNow;

        var authContext = new AuthContext { BypassTenantFilter = true };
        await using (var db = _fixture.CreateDbContext(authContext))
        {
            var group = new EconomicGroup
            {
                Id = groupId,
                Name = "Grupo Teste Restrict",
                Status = EntityStatus.ACTIVE,
                CreatedAt = now,
                UpdatedAt = now
            };
            var tenant = new Tenant
            {
                Id = tenantId,
                GroupId = groupId,
                Cnpj = "99000001000101",
                LegalName = "Empresa Teste Restrict",
                BranchType = BranchType.HQ,
                GeoLat = -23.5505m,
                GeoLng = -46.6333m,
                Status = EntityStatus.ACTIVE,
                CreatedAt = now,
                UpdatedAt = now
            };
            var user = new UserAccount
            {
                Id = userId,
                Email = "user.restrict@example.test",
                PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g",
                Name = "Utilizador Teste Restrict",
                Status = UserStatus.ACTIVE,
                PlatformRole = PlatformRole.NONE,
                CreatedAt = now,
                UpdatedAt = now
            };
            var member = new TenantMember
            {
                Id = memberId,
                TenantId = tenantId,
                UserId = userId,
                IsOwner = true,
                CanViewGroup = false,
                Status = EntityStatus.ACTIVE,
                CreatedAt = now
            };

            db.EconomicGroups.Add(group);
            db.Tenants.Add(tenant);
            db.UserAccounts.Add(user);
            db.TenantMembers.Add(member);
            await db.SaveChangesAsync();
        }

        // Tentativa de DELETE direto em tenant deve falhar com violação de chave estrangeira (SQLSTATE 23503)
        await using (var conn = new NpgsqlConnection(_fixture.GetConnectionString()))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM tenant WHERE id = @id;";
            cmd.Parameters.AddWithValue("id", tenantId);

            var ex = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
            Assert.Equal("23503", ex.SqlState);
        }
    }

    [Fact(Timeout = 60000)]
    public async Task CA_19_5_Borda_RlsForcadoEPrivilegiosRestritosIdentityApp()
    {
        var connStr = _fixture.GetConnectionString();

        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();

        // 1. Verificar FORCE RLS nas tabelas ITenantScoped
        var rlsTables = new[] { "tenant_scope", "tenant_member", "member_role" };
        foreach (var table in rlsTables)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT relrowsecurity, relforcerowsecurity FROM pg_class WHERE relname = @name;";
            cmd.Parameters.AddWithValue("name", table);
            await using var reader = await cmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.True(reader.GetBoolean(0), $"Tabela {table} sem RLS ativo.");
            Assert.True(reader.GetBoolean(1), $"Tabela {table} sem FORCE RLS.");
        }

        // 2. Verificar que identity_app não possui privilégios de DDL
        await using (var setRoleCmd = conn.CreateCommand())
        {
            setRoleCmd.CommandText = "SET ROLE identity_app;";
            await setRoleCmd.ExecuteNonQueryAsync();
        }

        // Tentar instrução DDL (CREATE TABLE) como identity_app deve ser rejeitada com 42501 (insufficient_privilege)
        await using (var ddlCmd = conn.CreateCommand())
        {
            ddlCmd.CommandText = "CREATE TABLE tent_ddl_proibida (id uuid PRIMARY KEY);";
            var ex = await Assert.ThrowsAsync<PostgresException>(() => ddlCmd.ExecuteNonQueryAsync());
            Assert.Equal("42501", ex.SqlState);
        }

        // Restaurar role
        await using (var resetRoleCmd = conn.CreateCommand())
        {
            resetRoleCmd.CommandText = "RESET ROLE;";
            await resetRoleCmd.ExecuteNonQueryAsync();
        }
    }

    [Fact(Timeout = 60000)]
    public async Task CA_19_6_Borda_RegistoDbContextContainerDi()
    {
        var inMemoryConfig = new Dictionary<string, string?>
        {
            ["DB_CONNECTION_STRING"] = _fixture.GetConnectionString(),
            ["REDIS_CONNECTION_STRING"] = _fixture.GetRedisConnectionString(),
            ["KAFKA_BOOTSTRAP_SERVERS"] = _fixture.GetKafkaBootstrapServers(),
            ["ASPNETCORE_ENVIRONMENT"] = "Testing"
        };

        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemoryConfig).Build();
        var services = new ServiceCollection();

        services.AddSingleton<IConfiguration>(config);
        services.AddM00Transversal(config);
        services.AddM07Chaves();
        services.AddM08Acessos();
        services.AddM11Esquema(config);
        services.AddM13Isolamento();
        services.AddM14Eventos(config);

        var sp = services.BuildServiceProvider();

        // 1. Resolução em dois escopos distintos
        IdentityDbContext db1;
        IdentityDbContext db2;

        using (var scope1 = sp.CreateScope())
        {
            db1 = scope1.ServiceProvider.GetRequiredService<IdentityDbContext>();
            Assert.NotNull(db1);

            // Verificar interceptores de IF-11.2 associados
            var interceptorAggregator = ((IInfrastructure<IServiceProvider>)db1).Instance.GetService<IInterceptors>();
            Assert.NotNull(interceptorAggregator);

            // Resolução dentro do mesmo escopo devolve a mesma instância (Scoped)
            var db1SameScope = scope1.ServiceProvider.GetRequiredService<IdentityDbContext>();
            Assert.Same(db1, db1SameScope);
        }

        using (var scope2 = sp.CreateScope())
        {
            db2 = scope2.ServiceProvider.GetRequiredService<IdentityDbContext>();
            Assert.NotNull(db2);
        }

        // Instâncias em escopos distintos são diferentes (Scoped sem pooling)
        Assert.NotSame(db1, db2);
        Assert.Equal(typeof(IdentityDbContext), db1.GetType());
        Assert.Equal(typeof(IdentityDbContext), db2.GetType());

        await Task.CompletedTask;
    }
}
