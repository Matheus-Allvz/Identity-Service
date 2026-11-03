using IdentityService.Modules.M00Transversal.Auth;
using IdentityService.Modules.M11Esquema.Persistence;
using IdentityService.Modules.M13Isolamento;
using IdentityService.Modules.M13Isolamento.Interface;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.Kafka;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Xunit;

namespace IdentityService.Tests.Fixtures;

public class TestcontainersFixture : IAsyncLifetime
{
    public PostgreSqlContainer PostgreSql { get; } = new PostgreSqlBuilder("postgres:17.6")
        .WithDatabase("identity")
        .WithUsername("identity")
        .WithPassword("identity-test")
        .Build();

    public RedisContainer Redis { get; } = new RedisBuilder("redis:7.4.6")
        .Build();

    public KafkaContainer Kafka { get; } = new KafkaBuilder("apache/kafka:3.9.1")
        .Build();

    public string GetConnectionString() => PostgreSql.GetConnectionString();

    public string GetRedisConnectionString() => Redis.GetConnectionString();

    public string GetKafkaBootstrapServers()
    {
        var address = Kafka.GetBootstrapAddress();
        return address.Replace("PLAINTEXT://", "", StringComparison.OrdinalIgnoreCase).TrimEnd('/');
    }

    public async Task InitializeAsync()
    {
        await Task.WhenAll(
            PostgreSql.StartAsync(),
            Redis.StartAsync(),
            Kafka.StartAsync()
        );

        // Executar migrações iniciais (M-11)
        await IdentityService.Modules.M11Esquema.ModuleRegistration.MigrateDatabaseAsync(GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        await Task.WhenAll(
            PostgreSql.DisposeAsync().AsTask(),
            Redis.DisposeAsync().AsTask(),
            Kafka.DisposeAsync().AsTask()
        );
    }

    public async Task CleanDatabaseAsync()
    {
        await using var conn = new NpgsqlConnection(GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "TRUNCATE economic_group, tenant, tenant_scope, user_account, tenant_member, member_role, refresh_token, auth_audit_log, outbox_message CASCADE;";
        await cmd.ExecuteNonQueryAsync();
    }

    public IdentityDbContext CreateDbContext(IAuthContext? authContext = null)
    {
        var optionsBuilder = new DbContextOptionsBuilder<IdentityDbContext>();
        optionsBuilder.UseNpgsql(GetConnectionString())
            .UseSnakeCaseNamingConvention();

        var isolation = new TenantIsolation();
        if (authContext != null)
        {
            var services = new ServiceCollection();
            services.AddSingleton(authContext);
            services.AddSingleton<ITenantIsolation>(isolation);
            var sp = services.BuildServiceProvider();
            optionsBuilder.AddInterceptors(isolation.CreateInterceptors(sp));
        }

        return new IdentityDbContext(optionsBuilder.Options, authContext, isolation);
    }
}

[CollectionDefinition("SharedContainers")]
public class SharedContainersCollection : ICollectionFixture<TestcontainersFixture>
{
}
