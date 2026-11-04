using System.Security.Cryptography;
using Confluent.Kafka;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using StackExchange.Redis;
using Testcontainers.Kafka;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Xunit;
using IdentityService.Modules.M00Transversal.Auth;

namespace IdentityService.Acceptance.Infrastructure;

public class FailingSigningKey(string keyId = "key-2026-01") : ISigningKey
{
    public string KeyId => keyId;
    public RsaSecurityKey SigningKey => throw new CryptographicException("Chave indisponível para assinatura.");
    public string Modulus => throw new CryptographicException("Chave indisponível.");
    public string Exponent => throw new CryptographicException("Chave indisponível.");
}

public class AcceptanceEnvironmentFixture : IAsyncLifetime
{
    public static readonly Guid RoleBuyerOperatorId = Guid.Parse("01900000-0000-7000-8000-000000000001");
    public static readonly Guid RoleSupplierSalesId = Guid.Parse("01900000-0000-7000-8000-000000000002");
    public static readonly Guid RoleCarrierDispatcherId = Guid.Parse("01900000-0000-7000-8000-000000000003");
    public static readonly Guid RoleGroupManagerId = Guid.Parse("01900000-0000-7000-8000-000000000004");

    public PostgreSqlContainer PostgreSql { get; private set; } = null!;
    public RedisContainer Redis { get; private set; } = null!;
    public KafkaContainer Kafka { get; private set; } = null!;
    public IConnectionMultiplexer RedisMultiplexer { get; private set; } = null!;

    public RSA SessionRsa { get; private set; } = null!;
    public string JwtKeyId { get; } = "key-2026-01";
    public string JwtRsaPrivateKeyPem { get; private set; } = null!;
    public RelogioTeste Relogio { get; private set; } = null!;

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;

    public string ConnectionString => PostgreSql.GetConnectionString();
    public string RedisConnectionString => Redis.GetConnectionString() + ",allowAdmin=true";
    public string KafkaBootstrapServers => CleanKafkaBootstrap(Kafka.GetBootstrapAddress());

    public async Task InitializeAsync()
    {
        PostgreSql = new PostgreSqlBuilder("postgres:17.6").Build();
        Redis = new RedisBuilder("redis:7.4.6").Build();
        Kafka = new KafkaBuilder("apache/kafka:3.9.1").Build();

        await Task.WhenAll(PostgreSql.StartAsync(), Redis.StartAsync(), Kafka.StartAsync());

        SessionRsa = RSA.Create(2048);
        JwtRsaPrivateKeyPem = SessionRsa.ExportPkcs8PrivateKeyPem();

        Environment.SetEnvironmentVariable("DB_CONNECTION_STRING", PostgreSql.GetConnectionString());
        Environment.SetEnvironmentVariable("REDIS_CONNECTION_STRING", RedisConnectionString);
        Environment.SetEnvironmentVariable("KAFKA_BOOTSTRAP_SERVERS", KafkaBootstrapServers);
        Environment.SetEnvironmentVariable("JWT_RSA_PRIVATE_KEY_PEM", JwtRsaPrivateKeyPem);

        RedisMultiplexer = await ConnectionMultiplexer.ConnectAsync(RedisConnectionString);
        Relogio = new RelogioTeste();

        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<TimeProvider>(Relogio);
            });
        });

        Factory.UseKestrel(0);
        Client = Factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await Factory.DisposeAsync();

        if (RedisMultiplexer != null)
        {
            await RedisMultiplexer.DisposeAsync();
        }

        SessionRsa?.Dispose();

        await Task.WhenAll(
            PostgreSql.DisposeAsync().AsTask(),
            Redis.DisposeAsync().AsTask(),
            Kafka.DisposeAsync().AsTask()
        );
    }

    public async Task LimparBaseEDerivadosAsync()
    {
        await DbHelper.LimparAsync(ConnectionString, RedisMultiplexer);
        await GarantirSeedRolesAsync();
        Relogio.Repor();
    }

    public async Task GarantirSeedRolesAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        var sql = @"
            CREATE TABLE IF NOT EXISTS role (
                id uuid PRIMARY KEY,
                name varchar(64) NOT NULL UNIQUE,
                scope varchar(16) NOT NULL,
                permissions text[] NOT NULL,
                created_at timestamptz NOT NULL
            );
            INSERT INTO role (id, name, scope, permissions, created_at) VALUES
            ('01900000-0000-7000-8000-000000000001', 'BUYER_OPERATOR', 'BUYER', '{demanda:criar,demanda:listar,leilao:participar,pedido:visualizar}', now()),
            ('01900000-0000-7000-8000-000000000002', 'SUPPLIER_SALES', 'SUPPLIER', '{estoque:atualizar,fornecimento:publicar,leilao:lance,pedido:confirmar}', now()),
            ('01900000-0000-7000-8000-000000000003', 'CARRIER_DISPATCHER', 'CARRIER', '{frete:cotar,frete:visualizar,transportadora:configurar}', now()),
            ('01900000-0000-7000-8000-000000000004', 'GROUP_MANAGER', 'TENANT_ADMIN', '{mapa:visualizar,membro:listar,membro:vincular,tenant:criar,tenant:listar,usuario:suspender}', now())
            ON CONFLICT (name) DO NOTHING;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    public WebApplicationFactory<Program> CriarFabricaComFalhaChave()
    {
        return Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<ISigningKey>(new FailingSigningKey(JwtKeyId));
            });
        });
    }

    public IConsumer<string, string> CriarConsumidorKafka(string topico)
    {
        var conf = new ConsumerConfig
        {
            BootstrapServers = KafkaBootstrapServers,
            GroupId = "test-group-" + Guid.NewGuid().ToString("D"),
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };
        var consumer = new ConsumerBuilder<string, string>(conf).Build();
        consumer.Assign(new TopicPartitionOffset(topico, 0, Offset.Beginning));
        return consumer;
    }

    public async Task<ConsumeResult<string, string>?> ConsumirMensagemKafkaAsync(string topico, string eventId, TimeSpan timeout)
    {
        using var consumer = CriarConsumidorKafka(topico);
        var teto = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < teto)
        {
            var res = consumer.Consume(TimeSpan.FromMilliseconds(200));
            if (res != null)
            {
                // Verifica no cabeçalho ou payload
                var evIdHeader = res.Message.Headers?.FirstOrDefault(h => h.Key == "event_id");
                if (evIdHeader != null)
                {
                    var val = System.Text.Encoding.UTF8.GetString(evIdHeader.GetValueBytes());
                    if (string.Equals(val, eventId, StringComparison.OrdinalIgnoreCase))
                    {
                        return res;
                    }
                }
                else if (res.Message.Value != null && res.Message.Value.Contains(eventId, StringComparison.OrdinalIgnoreCase))
                {
                    return res;
                }
            }
            await Task.Delay(50);
        }

        return null;
    }

    public string CunharTokenSessao(
        string jti,
        string sub,
        string? fid = null,
        long? iat = null,
        long? exp = null,
        string platformRole = "NONE",
        string viewMode = "NONE",
        string? groupId = null,
        string? tenantId = null,
        IEnumerable<string>? allowedTenants = null,
        string? activeScope = null,
        IEnumerable<string>? roles = null,
        IEnumerable<string>? permissions = null,
        object? act = null)
    {
        var agora = iat ?? Relogio.GetUtcNow().ToUnixTimeSeconds();
        var expira = exp ?? (agora + 900);
        return TokenHelper.CunharToken(
            SessionRsa,
            JwtKeyId,
            jti,
            sub,
            fid,
            agora,
            expira,
            platformRole,
            viewMode,
            groupId,
            tenantId,
            allowedTenants,
            activeScope,
            roles,
            permissions,
            act
        );
    }

    public static string CleanKafkaBootstrap(string raw)
    {
        var clean = raw;
        if (clean.StartsWith("plaintext://", StringComparison.OrdinalIgnoreCase))
        {
            clean = clean.Substring("plaintext://".Length);
        }
        return clean.TrimEnd('/');
    }
}

[CollectionDefinition("AcceptanceTests")]
public class AcceptanceCollection : ICollectionFixture<AcceptanceEnvironmentFixture>
{
}
