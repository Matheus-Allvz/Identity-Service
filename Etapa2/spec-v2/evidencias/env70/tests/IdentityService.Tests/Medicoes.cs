using System.Diagnostics;
using System.Net;
using Confluent.Kafka;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.Kafka;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Xunit;
using Xunit.Abstractions;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace IdentityService.Tests;

public sealed class RelogioTeste : TimeProvider
{
    private DateTimeOffset _agora = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    public override DateTimeOffset GetUtcNow() => _agora;
    public void Avancar(TimeSpan d) => _agora += d;
}

public class Medicoes(ITestOutputHelper o)
{
    [Fact]
    public async Task L03_Kafka()
    {
        var sw = Stopwatch.StartNew();
        await using var k = new KafkaBuilder("apache/kafka:3.9.1").Build();
        await k.StartAsync();
        var bs = k.GetBootstrapAddress();
        o.WriteLine($"kafka start {sw.ElapsedMilliseconds} ms, bs={bs}");
        Assert.NotNull(bs);
        Assert.StartsWith("plaintext://", bs, StringComparison.OrdinalIgnoreCase);

        using var p = new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = bs }).Build();
        var r = await p.ProduceAsync("t1", new Message<string, string> { Key = "a", Value = "v" });
        o.WriteLine($"produce {r.Status} {sw.ElapsedMilliseconds} ms");
        Assert.Equal(PersistenceStatus.Persisted, r.Status);

        using var c = new ConsumerBuilder<string, string>(new ConsumerConfig { BootstrapServers = bs, GroupId = Guid.NewGuid().ToString(), AutoOffsetReset = AutoOffsetReset.Earliest }).Build();
        c.Assign(new TopicPartitionOffset("t1", 0, Offset.Beginning));
        var m = c.Consume(TimeSpan.FromSeconds(10));
        Assert.NotNull(m);
        Assert.Equal("v", m.Message.Value);

        await k.PauseAsync();
        using var p2 = new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = bs, MessageTimeoutMs = 3000 }).Build();
        sw.Restart();
        var ex = await Assert.ThrowsAsync<ProduceException<string, string>>(() => p2.ProduceAsync("t1", new Message<string, string> { Key = "a", Value = "v2" }));
        sw.Stop();
        o.WriteLine($"paused: {ex.Error.Code} after {sw.ElapsedMilliseconds} ms");
        Assert.Equal(ErrorCode.Local_MsgTimedOut, ex.Error.Code);
        Assert.InRange(sw.ElapsedMilliseconds, 2800, 7000);

        await k.UnpauseAsync();
        sw.Restart();
        var r2 = await p.ProduceAsync("t1", new Message<string, string> { Key = "a", Value = "v3" });
        sw.Stop();
        o.WriteLine($"after unpause: {r2.Status} {sw.ElapsedMilliseconds} ms (produtor antigo)");
        Assert.Equal(PersistenceStatus.Persisted, r2.Status);

        await k.StopAsync();
        sw.Restart();
        using var p3 = new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = bs, MessageTimeoutMs = 3000 }).Build();
        var ex3 = await Assert.ThrowsAsync<ProduceException<string, string>>(() => p3.ProduceAsync("t1", new Message<string, string> { Key = "a", Value = "v4" }));
        sw.Stop();
        o.WriteLine($"stopped: {ex3.Error.Code} after {sw.ElapsedMilliseconds} ms");
        Assert.Equal(ErrorCode.Local_MsgTimedOut, ex3.Error.Code);
        Assert.InRange(sw.ElapsedMilliseconds, 2800, 7000);
    }

    [Fact]
    public async Task L04_PoolReset()
    {
        await using var pg = new PostgreSqlBuilder("postgres:17.6").Build();
        await pg.StartAsync();
        var cs = pg.GetConnectionString();
        o.WriteLine("CS: " + cs);

        await using (var c = new NpgsqlConnection(cs))
        {
            await c.OpenAsync();
            await new NpgsqlCommand("CREATE ROLE identity_app NOLOGIN; GRANT identity_app TO CURRENT_USER;", c).ExecuteNonQueryAsync();
        }

        // Conexão 1: define role e variáveis na sessão
        await using (var c = new NpgsqlConnection(cs))
        {
            await c.OpenAsync();
            await new NpgsqlCommand("SET ROLE identity_app; SELECT set_config('app.allowed_tenants','x',false), set_config('app.auth_flow','true',false)", c).ExecuteNonQueryAsync();
            await using var cmd = new NpgsqlCommand("SELECT current_user, coalesce(current_setting('app.auth_flow', true), '')", c);
            await using var reader = await cmd.ExecuteReaderAsync();
            await reader.ReadAsync();
            var user = reader.GetString(0);
            var authFlow = reader.GetString(1);
            o.WriteLine($"dentro: {user}|{authFlow}");
            Assert.Equal("identity_app", user);
            Assert.Equal("true", authFlow);
        }

        // Conexão 2 (pool por omissão com reset automático): variáveis e role voltam aos padrões (dono e vazios)
        await using (var c = new NpgsqlConnection(cs))
        {
            await c.OpenAsync();
            await using var cmd = new NpgsqlCommand("SELECT current_user, coalesce(current_setting('app.auth_flow', true), ''), coalesce(current_setting('app.allowed_tenants', true), '')", c);
            await using var reader = await cmd.ExecuteReaderAsync();
            await reader.ReadAsync();
            var user = reader.GetString(0);
            var authFlow = reader.GetString(1);
            var allowedTenants = reader.GetString(2);
            o.WriteLine($"reaberta no pool padrao: {user}|{authFlow}|{allowedTenants}");
            Assert.Equal("postgres", user);
            Assert.True(string.IsNullOrEmpty(authFlow), "app.auth_flow deve estar vazio no pool padrão");
            Assert.True(string.IsNullOrEmpty(allowedTenants), "app.allowed_tenants deve estar vazio no pool padrão");
        }

        // Conexão 3 (com No Reset On Close=true): define role e variável
        await using (var c = new NpgsqlConnection(cs + ";No Reset On Close=true"))
        {
            await c.OpenAsync();
            await new NpgsqlCommand("SET ROLE identity_app; SELECT set_config('app.auth_flow','true',false)", c).ExecuteNonQueryAsync();
        }

        // Conexão 4 (com No Reset On Close=true reaproveitando a conexão física): herda papel e variável
        await using (var c = new NpgsqlConnection(cs + ";No Reset On Close=true"))
        {
            await c.OpenAsync();
            await using var cmd = new NpgsqlCommand("SELECT current_user, coalesce(current_setting('app.auth_flow', true), '')", c);
            await using var reader = await cmd.ExecuteReaderAsync();
            await reader.ReadAsync();
            var user = reader.GetString(0);
            var authFlow = reader.GetString(1);
            o.WriteLine($"NoReset=true reaberta: {user}|{authFlow}");
            Assert.Equal("identity_app", user);
            Assert.Equal("true", authFlow);
        }
    }

    [Fact]
    public async Task L05_FalhasViaWaf()
    {
        await using var pg = new PostgreSqlBuilder("postgres:17.6").Build();
        await using var rd = new RedisBuilder("redis:7.4.6").Build();
        await Task.WhenAll(pg.StartAsync(), rd.StartAsync());
        Environment.SetEnvironmentVariable("DB_CONNECTION_STRING", pg.GetConnectionString());
        Environment.SetEnvironmentVariable("REDIS_CONNECTION_STRING", rd.GetConnectionString());
        Environment.SetEnvironmentVariable("KAFKA_BOOTSTRAP_SERVERS", "127.0.0.1:1");

        var rel = new RelogioTeste();
        await using var f = new WebApplicationFactory<Program>().WithWebHostBuilder(b => {
            b.UseEnvironment("Testing");
            b.ConfigureTestServices(s => s.AddSingleton<TimeProvider>(rel));
        });
        var h = f.CreateClient();
        rel.Avancar(TimeSpan.FromSeconds(1000));

        var jwks = await h.GetStringAsync("/.well-known/jwks.json");
        o.WriteLine(jwks + " real=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        Assert.Contains("\"env\":\"Testing\"", jwks);

        Assert.Equal(HttpStatusCode.OK, (await h.GetAsync("/probe/redis")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await h.GetAsync("/probe/pg")).StatusCode);

        // Redis indisponível
        await rd.StopAsync();
        var sw = Stopwatch.StartNew();
        var rRedis = await h.GetAsync("/probe/redis");
        sw.Stop();
        o.WriteLine($"redis parado: {(int)rRedis.StatusCode} {await rRedis.Content.ReadAsStringAsync()} em {sw.ElapsedMilliseconds} ms");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, rRedis.StatusCode);
        var rRedisBody = await rRedis.Content.ReadAsStringAsync();
        Assert.Contains("RedisConnectionException", rRedisBody);

        sw.Restart();
        var rRedis2 = await h.GetAsync("/probe/redis");
        sw.Stop();
        o.WriteLine($"redis parado (2.º): {(int)rRedis2.StatusCode} em {sw.ElapsedMilliseconds} ms");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, rRedis2.StatusCode);

        // PostgreSQL indisponível
        await pg.StopAsync();
        sw.Restart();
        var rPg = await h.GetAsync("/probe/pg");
        sw.Stop();
        o.WriteLine($"pg parado: {(int)rPg.StatusCode} {await rPg.Content.ReadAsStringAsync()} em {sw.ElapsedMilliseconds} ms");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, rPg.StatusCode);
        var rPgBody = await rPg.Content.ReadAsStringAsync();
        Assert.Contains("NpgsqlException", rPgBody);
    }

    [Fact]
    public async Task ArranqueSemVariavelFalha()
    {
        Environment.SetEnvironmentVariable("DB_CONNECTION_STRING", null);
        await using var f = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Testing"));
        var ex = Record.Exception(() => f.CreateClient());
        o.WriteLine("sem DB: " + ex?.GetType().Name + " " + ex?.Message);
        Assert.NotNull(ex);
        Assert.IsType<InvalidOperationException>(ex);
        Assert.Contains("missing DB_CONNECTION_STRING", ex.Message);
    }

    [Fact(Timeout = 10000)]
    public async Task TimeoutXunit()
    {
        // Demonstra que Fact(Timeout = ...) suporta métodos async Task no xUnit 2.9.3 sem deadlock nem erro
        var sw = Stopwatch.StartNew();
        await Task.Delay(20);
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds >= 10);

        var method = typeof(Medicoes).GetMethod(nameof(TimeoutXunit));
        var factAttr = method?.GetCustomAttributes(typeof(FactAttribute), false).Cast<FactAttribute>().FirstOrDefault();
        Assert.NotNull(factAttr);
        Assert.Equal(10000, factAttr.Timeout);
    }
}
