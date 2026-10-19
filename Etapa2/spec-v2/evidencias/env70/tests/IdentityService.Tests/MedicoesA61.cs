using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics;
using System.Net.Sockets;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Xunit;
using Xunit.Abstractions;

namespace IdentityService.Tests;

public static class RegistradorMedicoes
{
    private static readonly object _lock = new();

    public static void Registrar(string id, string situacao, string tipoExterior, string cadeia, string sqlstate, string comportamento)
    {
        var caminho = Environment.GetEnvironmentVariable("MEDICOES_OUT_FILE");
        if (string.IsNullOrWhiteSpace(caminho)) return;

        lock (_lock)
        {
            // ID|Situação|Tipo Exterior|Cadeia|SQLSTATE|Comportamento
            File.AppendAllText(caminho, $"{id}|{situacao}|{tipoExterior}|{cadeia}|{sqlstate}|{comportamento}{Environment.NewLine}");
        }
    }
}

public class TestEntity
{
    public int Id { get; set; }
    public string TenantId { get; set; } = "";
    public string UniqueKey { get; set; } = "";
}

public class TestDbContext : DbContext
{
    public DbSet<TestEntity> TestEntities => Set<TestEntity>();

    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TestEntity>(b =>
        {
            b.ToTable("test_entity");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.TenantId).HasColumnName("tenant_id");
            b.Property(x => x.UniqueKey).HasColumnName("unique_key");
            b.HasIndex(x => x.UniqueKey).IsUnique();
        });
    }
}

public class MedicoesA61(ITestOutputHelper o)
{
    /// <summary>
    /// A-61 (c): Hierarquia de tipos de RedisTimeoutException e RedisConnectionException
    /// </summary>
    [Fact]
    public void Medicao_C_Redis_HierarquiaExcecoes()
    {
        // 1. RedisTimeoutException
        var timeoutEx = new RedisTimeoutException("timeout", CommandStatus.Unknown);
        o.WriteLine($"RedisTimeoutException base: {timeoutEx.GetType().BaseType?.FullName}");
        Assert.True(typeof(TimeoutException).IsAssignableFrom(typeof(RedisTimeoutException)));
        Assert.True(typeof(SystemException).IsAssignableFrom(typeof(RedisTimeoutException)));
        Assert.True(typeof(Exception).IsAssignableFrom(typeof(RedisTimeoutException)));
        Assert.False(typeof(RedisException).IsAssignableFrom(typeof(RedisTimeoutException)), "RedisTimeoutException NÃO herda de RedisException");

        RegistradorMedicoes.Registrar(
            "REDIS_TIMEOUT",
            "Redis: Timeout de Operação",
            typeof(RedisTimeoutException).FullName!,
            $"{nameof(RedisTimeoutException)} → {typeof(RedisTimeoutException).BaseType?.Name} → {typeof(RedisTimeoutException).BaseType?.BaseType?.Name} → Exception",
            "-",
            "503 ERR-SYS-002 (NÃO herda de RedisException!)"
        );

        // 2. RedisConnectionException
        var connEx = new RedisConnectionException(ConnectionFailureType.UnableToConnect, "failed");
        o.WriteLine($"RedisConnectionException base: {connEx.GetType().BaseType?.FullName}");
        Assert.True(typeof(RedisException).IsAssignableFrom(typeof(RedisConnectionException)));
        Assert.True(typeof(Exception).IsAssignableFrom(typeof(RedisConnectionException)));
        Assert.False(typeof(TimeoutException).IsAssignableFrom(typeof(RedisConnectionException)), "RedisConnectionException NÃO herda de TimeoutException");

        RegistradorMedicoes.Registrar(
            "REDIS_CONEXAO",
            "Redis: Conexão Indisponível / Parado",
            typeof(RedisConnectionException).FullName!,
            $"{nameof(RedisConnectionException)} → {typeof(RedisConnectionException).BaseType?.FullName} → Exception",
            "-",
            "503 ERR-SYS-002"
        );
    }

    /// <summary>
    /// A-61 (d): Exceção em IssuerSigningKeyResolver e JsonWebTokenHandler.ValidateTokenAsync
    /// </summary>
    [Fact]
    public async Task Medicao_D_Jwt_IssuerSigningKeyResolver()
    {
        using var rsa = RSA.Create(2048);
        var cred = new SigningCredentials(new RsaSecurityKey(rsa) { KeyId = "key-2026-01" }, SecurityAlgorithms.RsaSha256);
        var token = new JsonWebTokenHandler().CreateToken("{\"sub\":\"u\",\"iss\":\"identity-service\",\"aud\":\"b2b-platform\"}", cred);

        var handler = new JsonWebTokenHandler();
        var validationParams = new TokenValidationParameters
        {
            ValidIssuer = "identity-service",
            ValidAudience = "b2b-platform",
            ValidateLifetime = false,
            IssuerSigningKeyResolver = (tokenStr, securityToken, kid, tvp) =>
                throw new CryptographicException("Chave criptografica indisponivel")
        };

        // Medição: a exceção NÃO sai diretamente de ValidateTokenAsync;
        // o método captura internamente e devolve TokenValidationResult com IsValid = false e Exception preenchida.
        var result = await handler.ValidateTokenAsync(token, validationParams);

        o.WriteLine($"ValidateTokenAsync RETORNOU: IsValid={result.IsValid}, Exception={result.Exception?.GetType().FullName}: {result.Exception?.Message}");

        Assert.NotNull(result);
        Assert.False(result.IsValid, "ValidateTokenAsync deve retornar IsValid=false");
        Assert.NotNull(result.Exception);
        Assert.IsType<CryptographicException>(result.Exception);
        Assert.Equal("Chave criptografica indisponivel", result.Exception.Message);

        RegistradorMedicoes.Registrar(
            "JWT_RESOLVER_EXCEPTION",
            "Chave Indisponível no IssuerSigningKeyResolver",
            "Nenhuma (ValidateTokenAsync retorna sem lançar)",
            $"TokenValidationResult.IsValid = false, result.Exception = {result.Exception.GetType().Name}",
            "-",
            "503 ERR-SYS-002 (ler ISigningKey antes de validar ou inspecionar result.Exception!)"
        );
    }

    /// <summary>
    /// A-61 (a): Violação de RLS (SQLSTATE 42501) e Unicidade (SQLSTATE 23505) via SaveChangesAsync
    /// </summary>
    [Fact]
    public async Task Medicao_A_EfCore_Rls_E_Unicidade()
    {
        await using var pg = new PostgreSqlBuilder("postgres:17.6").Build();
        await pg.StartAsync();
        var cs = pg.GetConnectionString();

        // 1. Configurar schema, tabela, RLS e utilizador de aplicação
        await using (var c = new NpgsqlConnection(cs))
        {
            await c.OpenAsync();
            var cmd = c.CreateCommand();
            cmd.CommandText = @"
                CREATE ROLE identity_app WITH LOGIN PASSWORD 'secret';
                GRANT USAGE ON SCHEMA public TO identity_app;
                CREATE TABLE test_entity (
                    id serial PRIMARY KEY,
                    tenant_id text NOT NULL,
                    unique_key text NOT NULL
                );
                CREATE UNIQUE INDEX uq_test_entity_unique_key ON test_entity (unique_key);
                ALTER TABLE test_entity ENABLE ROW LEVEL SECURITY;
                ALTER TABLE test_entity FORCE ROW LEVEL SECURITY;
                CREATE POLICY p_test_entity ON test_entity
                    FOR ALL TO identity_app
                    USING (tenant_id = current_setting('app.tenant_id', true))
                    WITH CHECK (tenant_id = current_setting('app.tenant_id', true));
                GRANT ALL ON test_entity TO identity_app;
                GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO identity_app;
            ";
            await cmd.ExecuteNonQueryAsync();
        }

        var csApp = new NpgsqlConnectionStringBuilder(cs) { Username = "identity_app", Password = "secret" }.ToString();

        // A. Violação de RLS (SQLSTATE 42501) via SaveChangesAsync
        var optionsApp = new DbContextOptionsBuilder<TestDbContext>()
            .UseNpgsql(csApp)
            .Options;

        await using (var db = new TestDbContext(optionsApp))
        {
            // app.tenant_id não está definido -> política RLS rejeita INSERT com SQLSTATE 42501
            db.TestEntities.Add(new TestEntity { TenantId = "t1", UniqueKey = "k1" });
            var exRls = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

            o.WriteLine($"RLS Exception: {exRls.GetType().FullName}");
            o.WriteLine($"RLS InnerException: {exRls.InnerException?.GetType().FullName}");

            Assert.IsType<PostgresException>(exRls.InnerException);
            var pgEx = (PostgresException)exRls.InnerException;
            o.WriteLine($"RLS SqlState: {pgEx.SqlState}");
            Assert.Equal("42501", pgEx.SqlState);

            RegistradorMedicoes.Registrar(
                "RLS",
                "Violação de RLS (EF Core SaveChangesAsync)",
                exRls.GetType().FullName!,
                $"{exRls.GetType().Name} → {exRls.InnerException?.GetType().FullName}",
                pgEx.SqlState,
                "403 ERR-CTX-007"
            );
        }

        // B. Violação de Unicidade (SQLSTATE 23505) via SaveChangesAsync
        var optionsDono = new DbContextOptionsBuilder<TestDbContext>()
            .UseNpgsql(cs)
            .Options;

        await using (var db = new TestDbContext(optionsDono))
        {
            db.TestEntities.Add(new TestEntity { TenantId = "t1", UniqueKey = "dup1" });
            await db.SaveChangesAsync();

            // Segundo registo com a mesma unique_key -> violação de unicidade SQLSTATE 23505
            db.TestEntities.Add(new TestEntity { TenantId = "t1", UniqueKey = "dup1" });
            var exDup = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

            o.WriteLine($"Unicidade Exception: {exDup.GetType().FullName}");
            o.WriteLine($"Unicidade InnerException: {exDup.InnerException?.GetType().FullName}");

            Assert.IsType<PostgresException>(exDup.InnerException);
            var pgEx = (PostgresException)exDup.InnerException;
            o.WriteLine($"Unicidade SqlState: {pgEx.SqlState}");
            Assert.Equal("23505", pgEx.SqlState);

            RegistradorMedicoes.Registrar(
                "UNICIDADE",
                "Violação de Unicidade (EF Core SaveChangesAsync)",
                exDup.GetType().FullName!,
                $"{exDup.GetType().Name} → {exDup.InnerException?.GetType().FullName}",
                pgEx.SqlState,
                "409 ERR-CTX-005 (salvo tratamento específico)"
            );
        }
    }

    /// <summary>
    /// A-61 (b): Consulta EF com PostgreSQL parado: estratégia por omissão vs desligada
    /// </summary>
    [Fact]
    public async Task Medicao_B_EfCore_PostgresParado()
    {
        await using var pg = new PostgreSqlBuilder("postgres:17.6").Build();
        await pg.StartAsync();
        var cs = pg.GetConnectionString();

        // Inicializar schema
        var optionsInit = new DbContextOptionsBuilder<TestDbContext>().UseNpgsql(cs).Options;
        await using (var db = new TestDbContext(optionsInit))
        {
            await db.Database.EnsureCreatedAsync();
        }

        // Parar PostgreSQL e limpar o pool de conexões
        await pg.StopAsync();
        NpgsqlConnection.ClearAllPools();

        // 1. Estratégia por omissão do Npgsql EF (NpgsqlExecutionStrategy padrão sem retries configurados)
        var optDefault = new DbContextOptionsBuilder<TestDbContext>()
            .UseNpgsql(cs)
            .Options;

        await using (var db = new TestDbContext(optDefault))
        {
            var exDefault = await Assert.ThrowsAsync<InvalidOperationException>(() => db.TestEntities.ToListAsync());
            o.WriteLine("--- CASO B1 (estratégia por omissão): ---");
            o.WriteLine($"Excecao exterior: {exDefault.GetType().FullName}");
            o.WriteLine($"Mensagem: {exDefault.Message}");
            o.WriteLine($"InnerException: {exDefault.InnerException?.GetType().FullName}: {exDefault.InnerException?.Message}");

            Assert.Contains("transient failure", exDefault.Message);
            Assert.IsType<NpgsqlException>(exDefault.InnerException);
            Assert.IsAssignableFrom<SocketException>(exDefault.InnerException.InnerException);

            RegistradorMedicoes.Registrar(
                "PG_PARADO_DEFAULT",
                "PostgreSQL Parado (EF Core: estratégia por omissão)",
                $"{exDefault.GetType().FullName} (\"transient failure\")",
                $"{exDefault.GetType().Name} → {exDefault.InnerException?.GetType().FullName} → {exDefault.InnerException?.InnerException?.GetType().Name}",
                "-",
                "503 ERR-SYS-002 (deve percorrer InnerException!)"
            );
        }

        // 2. Estratégia de execução desligada explicitamente (NonRetryingExecutionStrategy)
        var optNonRetry = new DbContextOptionsBuilder<TestDbContext>()
            .UseNpgsql(cs, b => b.ExecutionStrategy(c => new NonRetryingExecutionStrategy(c)))
            .Options;

        await using (var db = new TestDbContext(optNonRetry))
        {
            var exNonRetry = await Assert.ThrowsAsync<NpgsqlException>(() => db.TestEntities.ToListAsync());
            o.WriteLine("--- CASO B2 (estratégia desligada): ---");
            o.WriteLine($"Excecao exterior: {exNonRetry.GetType().FullName}");
            o.WriteLine($"Mensagem: {exNonRetry.Message}");
            o.WriteLine($"InnerException: {exNonRetry.InnerException?.GetType().FullName}: {exNonRetry.InnerException?.Message}");

            Assert.IsAssignableFrom<SocketException>(exNonRetry.InnerException);

            RegistradorMedicoes.Registrar(
                "PG_PARADO_DESLIGADA",
                "PostgreSQL Parado (EF Core: estratégia desligada)",
                exNonRetry.GetType().FullName!,
                $"{exNonRetry.GetType().Name} → {exNonRetry.InnerException?.GetType().FullName}",
                "-",
                "503 ERR-SYS-002"
            );
        }

        // 3. Estratégia com retries habilitados explicitamente (EnableRetryOnFailure)
        var optRetry = new DbContextOptionsBuilder<TestDbContext>()
            .UseNpgsql(cs, b => b.EnableRetryOnFailure(maxRetryCount: 1, maxRetryDelay: TimeSpan.FromMilliseconds(50), errorCodesToAdd: null))
            .Options;

        await using (var db = new TestDbContext(optRetry))
        {
            var exRetry = await Assert.ThrowsAsync<RetryLimitExceededException>(() => db.TestEntities.ToListAsync());
            o.WriteLine("--- CASO B3 (EnableRetryOnFailure com maxRetryCount=1): ---");
            o.WriteLine($"Excecao exterior: {exRetry.GetType().FullName}");
            o.WriteLine($"Mensagem: {exRetry.Message}");
            o.WriteLine($"InnerException: {exRetry.InnerException?.GetType().FullName}: {exRetry.InnerException?.Message}");

            Assert.IsType<NpgsqlException>(exRetry.InnerException);
            Assert.IsAssignableFrom<SocketException>(exRetry.InnerException.InnerException);

            RegistradorMedicoes.Registrar(
                "PG_PARADO_RETRY",
                "PostgreSQL Parado (EF Core: EnableRetryOnFailure)",
                exRetry.GetType().FullName!,
                $"{exRetry.GetType().Name} → {exRetry.InnerException?.GetType().FullName} → {exRetry.InnerException?.InnerException?.GetType().Name}",
                "-",
                "503 ERR-SYS-002 (deve percorrer InnerException!)"
            );
        }
    }
}
