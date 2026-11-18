using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using IdentityService.Modules.M00Transversal.Auth;
using IdentityService.Modules.M00Transversal.Errors;
using IdentityService.Modules.M11Esquema.Persistence;
using IdentityService.Modules.M11Esquema.Persistence.Entities;
using IdentityService.Modules.M11Esquema.Persistence.Enums;
using IdentityService.Modules.M14Eventos;
using IdentityService.Modules.M14Eventos.Interface;
using IdentityService.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace IdentityService.Tests;

[Trait("Fase", "F00")]
[Collection("SharedContainers")]
public class M14EventosTests
{
    private readonly TestcontainersFixture _fixture;
    private readonly TestSigningKey _signingKey = new("key-2026-01");
    private readonly TimeProvider _timeProvider = TimeProvider.System;

    public M14EventosTests(TestcontainersFixture fixture)
    {
        _fixture = fixture;
    }

    private OutboxPublisherWorker CreateWorker(IProducer<string, string>? customProducer = null)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => _fixture.CreateDbContext(new AuthContext { BypassTenantFilter = true }));
        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();

        IProducer<string, string> producer = customProducer ?? CreateProducer();
        var logger = new LoggerFactory().CreateLogger<OutboxPublisherWorker>();

        return new OutboxPublisherWorker(scopeFactory, producer, _timeProvider, logger);
    }

    private IProducer<string, string> CreateProducer()
    {
        var config = new ProducerConfig
        {
            BootstrapServers = _fixture.GetKafkaBootstrapServers(),
            Acks = Acks.All,
            MessageTimeoutMs = 3000,
            EnableIdempotence = true
        };
        return new ProducerBuilder<string, string>(config).Build();
    }

    private IConsumer<string, string> CreateConsumer(string topic)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = _fixture.GetKafkaBootstrapServers(),
            GroupId = $"test-group-{Guid.NewGuid():N}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };
        var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(topic);
        return consumer;
    }

    private static string? GetHeaderValue(Headers headers, string key)
    {
        if (headers.TryGetLastBytes(key, out var bytes))
        {
            return Encoding.UTF8.GetString(bytes);
        }
        return null;
    }

    private ConsumeResult<string, string>? ConsumeUntilEventId(IConsumer<string, string> consumer, Guid expectedEventId, TimeSpan timeout)
    {
        var expectedEventIdStr = expectedEventId.ToString("D");
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var res = consumer.Consume(TimeSpan.FromMilliseconds(500));
                if (res != null && GetHeaderValue(res.Message.Headers, "event_id") == expectedEventIdStr)
                {
                    return res;
                }
            }
            catch (ConsumeException ex) when (ex.Error.Code == ErrorCode.UnknownTopicOrPart)
            {
                Thread.Sleep(200);
            }
        }
        return null;
    }

    [Fact(Timeout = 60000)]
    public async Task CA_14_1_Borda_EnfileiramentoEPublicacao_EV01_EmpresaCadastrada()
    {
        await _fixture.CleanDatabaseAsync();

        var tenantId = Guid.Parse("0192e2c0-0010-7000-8000-000000000010");
        var groupId = Guid.Parse("0192e2c0-0001-7000-8000-000000000001");
        var userId = Guid.Parse("0192e2c0-0099-7000-8000-000000000099");
        var now = DateTimeOffset.UtcNow;

        var authContext = new AuthContext
        {
            BypassTenantFilter = true,
            UserId = userId,
            ActiveScope = OperationalScope.TENANT_ADMIN,
            ActorUserId = null,
            AllowedTenants = [tenantId]
        };

        Guid eventId;
        await using (var db = _fixture.CreateDbContext(authContext))
        {
            var outbox = new EventOutbox(db, authContext, _signingKey, _timeProvider);
            var payload = new TenantCreatedPayload(
                TenantId: tenantId,
                GroupId: groupId,
                Cnpj: "99000001000101",
                LegalName: "Empresa Cadastrada Teste",
                BranchType: BranchType.HQ,
                GeoLat: -23.5505m,
                GeoLng: -46.6333m,
                Scopes: [OperationalScope.BUYER],
                CreatedAt: now
            );

            eventId = outbox.EnqueueTenantCreated(payload);
            await db.SaveChangesAsync();
        }

        // Executar worker outbox
        var worker = CreateWorker();
        var processed = await worker.ProcessBatchAsync();
        Assert.True(processed > 0);

        using var consumer = CreateConsumer("empresa_cadastrada");

        // Verificar outbox_message
        await using (var db = _fixture.CreateDbContext(new AuthContext { BypassTenantFilter = true }))
        {
            var msg = await db.OutboxMessages.FindAsync(eventId);
            Assert.NotNull(msg);
            Assert.Equal(OutboxStatus.PUBLISHED, msg.Status);
            Assert.Equal(0, msg.RetryCount);
            Assert.NotNull(msg.ProcessedAt);
        }

        // Consumir mensagem do Kafka
        var consumeResult = ConsumeUntilEventId(consumer, eventId, TimeSpan.FromSeconds(10));
        Assert.NotNull(consumeResult);
        Assert.Equal(tenantId.ToString("D"), consumeResult.Message.Key);

        var headers = consumeResult.Message.Headers;
        Assert.Equal("TENANT_ADMIN", GetHeaderValue(headers, "acting_scope"));
        Assert.Null(GetHeaderValue(headers, "actor_user_id"));
        Assert.Equal(eventId.ToString("D"), GetHeaderValue(headers, "event_id"));

        var contextJws = GetHeaderValue(headers, "context_jws");
        Assert.NotNull(contextJws);

        // Validar assinatura RS256 e exp = iat + 300
        var tokenHandler = new JsonWebTokenHandler();
        var validation = await tokenHandler.ValidateTokenAsync(contextJws, new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = false,
            IssuerSigningKey = _signingKey.SigningKey
        });
        Assert.True(validation.IsValid);

        var jwt = (JsonWebToken)validation.SecurityToken;
        var iatClaim = long.Parse(jwt.GetClaim("iat").Value);
        var expClaim = long.Parse(jwt.GetClaim("exp").Value);
        Assert.Equal(iatClaim + 300, expClaim);

        // Validar Envelope Híbrido: Kit padrão (camelCase) + raiz para interoperabilidade
        using var jsonDoc = JsonDocument.Parse(consumeResult.Message.Value);
        var root = jsonDoc.RootElement;
        Assert.Equal(eventId.ToString("D"), root.GetProperty("eventId").GetString());
        Assert.Equal("empresa_cadastrada", root.GetProperty("eventType").GetString());
        Assert.Equal("1.0", root.GetProperty("eventVersion").GetString());
        Assert.Equal("identity-service", root.GetProperty("source").GetString());
        Assert.Equal(tenantId.ToString("D"), root.GetProperty("aggregateId").GetString());
        Assert.True(root.TryGetProperty("payload", out var payloadElem));
        Assert.Equal(tenantId.ToString("D"), payloadElem.GetProperty("tenantId").GetString());
        Assert.Equal(groupId.ToString("D"), payloadElem.GetProperty("groupId").GetString());
        Assert.Equal("HQ", payloadElem.GetProperty("branchType").GetString());
        Assert.Equal("Empresa Cadastrada Teste", payloadElem.GetProperty("legalName").GetString());
        Assert.Equal(tenantId.ToString("D"), root.GetProperty("tenant_id").GetString());
        Assert.Equal("HQ", root.GetProperty("branch_type").GetString());
    }

    [Fact(Timeout = 60000)]
    public async Task CA_14_2_Borda_Publicacao_EV02_TenantBloqueado_PersonificacaoAtiva()
    {
        await _fixture.CleanDatabaseAsync();

        var tenantId = Guid.Parse("0192e2c0-0020-7000-8000-000000000020");
        var groupId = Guid.Parse("0192e2c0-0001-7000-8000-000000000001");
        var userId = Guid.Parse("0192e2c0-0005-7000-8000-000000000005");
        var actorUserId = Guid.Parse("0192e2c0-0009-7000-8000-000000000009");
        var now = DateTimeOffset.UtcNow;

        var authContext = new AuthContext
        {
            BypassTenantFilter = true,
            UserId = userId,
            ActorUserId = actorUserId,
            AllowedTenants = [tenantId]
        };

        Guid eventId;
        await using (var db = _fixture.CreateDbContext(authContext))
        {
            var outbox = new EventOutbox(db, authContext, _signingKey, _timeProvider);
            var payload = new TenantBlockedPayload(
                TenantId: tenantId,
                GroupId: groupId,
                Cnpj: "99000002000101",
                Reason: "Bloqueio administrativo",
                BlockedByUserId: userId,
                BlockedAt: now
            );

            eventId = outbox.EnqueueTenantBlocked(payload);
            await db.SaveChangesAsync();
        }

        var worker = CreateWorker();
        var processed = await worker.ProcessBatchAsync();
        Assert.True(processed > 0);

        using var consumer = CreateConsumer("tenant_bloqueado");

        await using (var db = _fixture.CreateDbContext(new AuthContext { BypassTenantFilter = true }))
        {
            var msg = await db.OutboxMessages.FindAsync(eventId);
            Assert.NotNull(msg);
            Assert.Equal(OutboxStatus.PUBLISHED, msg.Status);
        }

        var consumeResult = ConsumeUntilEventId(consumer, eventId, TimeSpan.FromSeconds(10));
        Assert.NotNull(consumeResult);
        Assert.Equal(tenantId.ToString("D"), consumeResult.Message.Key);

        var headers = consumeResult.Message.Headers;
        Assert.Equal(userId.ToString("D"), GetHeaderValue(headers, "initiator_user_id"));
        Assert.Equal(actorUserId.ToString("D"), GetHeaderValue(headers, "actor_user_id"));
    }

    [Fact(Timeout = 60000)]
    public async Task CA_14_3_Borda_Publicacao_EV03_UsuarioBloqueado_TentativasLogin()
    {
        await _fixture.CleanDatabaseAsync();

        var userId = Guid.Parse("0192e2c0-0003-7000-8000-000000000003");
        var now = DateTimeOffset.UtcNow;
        var lockedUntil = now.AddMinutes(15);

        var authContext = new AuthContext { BypassTenantFilter = true };

        Guid eventId;
        await using (var db = _fixture.CreateDbContext(authContext))
        {
            var outbox = new EventOutbox(db, authContext, _signingKey, _timeProvider);
            eventId = outbox.EnqueueUserLocked(userId, lockedUntil, now);
            await db.SaveChangesAsync();
        }

        using var consumer = CreateConsumer("usuario_bloqueado");

        var worker = CreateWorker();
        await worker.ProcessBatchAsync();

        var consumeResult = ConsumeUntilEventId(consumer, eventId, TimeSpan.FromSeconds(10));
        Assert.NotNull(consumeResult);
        Assert.Equal(userId.ToString("D"), consumeResult.Message.Key);

        var headers = consumeResult.Message.Headers;
        Assert.Null(GetHeaderValue(headers, "tenant_id"));
        Assert.Null(GetHeaderValue(headers, "group_id"));
        Assert.Null(GetHeaderValue(headers, "actor_user_id"));
        Assert.Equal("NONE", GetHeaderValue(headers, "acting_scope"));

        using var payloadDoc = JsonDocument.Parse(consumeResult.Message.Value);
        var root = payloadDoc.RootElement;
        Assert.Equal("LOCKED", root.GetProperty("status").GetString());
        Assert.Equal("Excesso de tentativas consecutivas de login com palavra-passe incorreta", root.GetProperty("reason").GetString());
    }

    [Fact(Timeout = 60000)]
    public async Task CA_14_4_Borda_Publicacao_EV03_UsuarioBloqueado_SuspensaoAdministrativa()
    {
        await _fixture.CleanDatabaseAsync();

        var targetUserId = Guid.Parse("0192e2c0-0003-7000-8000-000000000003");
        var adminUserId = Guid.Parse("0192e2c0-0001-7000-8000-000000000001");
        var groupId = Guid.Parse("0192e2c0-0002-7000-8000-000000000002");
        var now = DateTimeOffset.UtcNow;

        var authContext = new AuthContext
        {
            BypassTenantFilter = true,
            UserId = adminUserId,
            GroupId = groupId,
            ActiveTenantId = null,
            ActorUserId = null
        };

        Guid eventId;
        await using (var db = _fixture.CreateDbContext(authContext))
        {
            var outbox = new EventOutbox(db, authContext, _signingKey, _timeProvider);
            eventId = outbox.EnqueueUserSuspended(targetUserId, "Violacao de politica", now);
            await db.SaveChangesAsync();
        }

        using var consumer = CreateConsumer("usuario_bloqueado");

        var worker = CreateWorker();
        await worker.ProcessBatchAsync();

        var consumeResult = ConsumeUntilEventId(consumer, eventId, TimeSpan.FromSeconds(10));
        Assert.NotNull(consumeResult);
        Assert.Equal(targetUserId.ToString("D"), consumeResult.Message.Key);

        var headers = consumeResult.Message.Headers;
        Assert.Equal(groupId.ToString("D"), GetHeaderValue(headers, "group_id"));
        Assert.Null(GetHeaderValue(headers, "tenant_id"));

        using var payloadDoc = JsonDocument.Parse(consumeResult.Message.Value);
        var root = payloadDoc.RootElement;
        Assert.Equal("SUSPENDED", root.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("locked_until").ValueKind);
    }

    [Fact(Timeout = 60000)]
    public async Task CA_14_5_Borda_Publicacao_EV04_SessaoComprometida_FamilyContext()
    {
        await _fixture.CleanDatabaseAsync();

        var userId = Guid.Parse("0192e2c0-0007-7000-8000-000000000007");
        var groupId = Guid.Parse("0192e2c0-0001-7000-8000-000000000001");
        var tenantId = Guid.Parse("0192e2c0-0010-7000-8000-000000000010");
        var familyId = Guid.NewGuid();
        var reusedTokenId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var familyContext = new FamilyContext(
            UserId: userId,
            GroupId: groupId,
            ActiveTenantId: tenantId,
            AllowedTenants: [tenantId],
            ActiveScope: OperationalScope.BUYER,
            ViewMode: ViewMode.SINGLE
        );

        var payload = new SessionCompromisedPayload(
            UserId: userId,
            FamilyId: familyId,
            ReusedRefreshTokenId: reusedTokenId,
            IpAddress: "198.51.100.1",
            DetectedAt: now
        );

        var authContext = new AuthContext { BypassTenantFilter = true };

        Guid eventId;
        await using (var db = _fixture.CreateDbContext(authContext))
        {
            var outbox = new EventOutbox(db, authContext, _signingKey, _timeProvider);
            eventId = outbox.EnqueueSessionCompromised(payload, familyContext);
            await db.SaveChangesAsync();
        }

        var worker = CreateWorker();
        await worker.ProcessBatchAsync();

        using var consumer = CreateConsumer("sessao_comprometida");

        var consumeResult = ConsumeUntilEventId(consumer, eventId, TimeSpan.FromSeconds(10));
        Assert.NotNull(consumeResult);
        Assert.Equal(userId.ToString("D"), consumeResult.Message.Key);

        var headers = consumeResult.Message.Headers;
        Assert.Equal("BUYER", GetHeaderValue(headers, "acting_scope"));
        Assert.Null(GetHeaderValue(headers, "actor_user_id"));

        var contextJws = GetHeaderValue(headers, "context_jws");
        Assert.NotNull(contextJws);

        var tokenHandler = new JsonWebTokenHandler();
        var validation = await tokenHandler.ValidateTokenAsync(contextJws, new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = false,
            IssuerSigningKey = _signingKey.SigningKey
        });
        Assert.True(validation.IsValid);

        var jwt = (JsonWebToken)validation.SecurityToken;
        var payloadBytes = Base64UrlEncoder.DecodeBytes(jwt.EncodedPayload);
        using var jwsDoc = JsonDocument.Parse(payloadBytes);
        var allowedArray = jwsDoc.RootElement.GetProperty("allowed_tenants");
        Assert.Equal(1, allowedArray.GetArrayLength());
        Assert.Equal(tenantId.ToString("D"), allowedArray[0].GetString());
    }

    [Theory(Timeout = 60000)]
    [InlineData(1)]
    [InlineData(50)]
    public async Task CA_14_6_Borda_LimiarTemporalPublicacaoOutbox_Lote(int qtd)
    {
        await _fixture.CleanDatabaseAsync();

        var now = DateTimeOffset.UtcNow;
        var messageIds = new List<Guid>();

        await using (var db = _fixture.CreateDbContext(new AuthContext { BypassTenantFilter = true }))
        {
            for (int i = 0; i < qtd; i++)
            {
                var id = Guid.CreateVersion7(now.AddMilliseconds(i));
                messageIds.Add(id);

                var msg = new OutboxMessage
                {
                    Id = id,
                    Topic = "usuario_bloqueado",
                    PartitionKey = id.ToString("D"),
                    Headers = "{\"event_id\":\"" + id.ToString("D") + "\"}",
                    Payload = "{\"test_index\":" + i + "}",
                    Status = OutboxStatus.PENDING,
                    RetryCount = 0,
                    CreatedAt = now.AddMilliseconds(i),
                    ProcessedAt = null
                };
                db.OutboxMessages.Add(msg);
            }
            await db.SaveChangesAsync();
        }

        var worker = CreateWorker();
        var sw = Stopwatch.StartNew();
        var processed = await worker.ProcessBatchAsync();
        sw.Stop();

        Assert.Equal(qtd, processed);
        Assert.True(sw.ElapsedMilliseconds <= 3000, $"Lote de {qtd} mensagens demorou {sw.ElapsedMilliseconds} ms (limite: 3000 ms).");

        await using (var db = _fixture.CreateDbContext(new AuthContext { BypassTenantFilter = true }))
        {
            var publishedCount = await db.OutboxMessages
                .Where(m => messageIds.Contains(m.Id) && m.Status == OutboxStatus.PUBLISHED)
                .CountAsync();
            Assert.Equal(qtd, publishedCount);
        }
    }

    [Theory(Timeout = 60000)]
    [InlineData(0, OutboxStatus.PENDING, 1)]
    [InlineData(3, OutboxStatus.PENDING, 4)]
    [InlineData(4, OutboxStatus.FAILED, 5)]
    public async Task CA_14_7_Borda_CicloRetentativasETransicaoFailed_AposCincoFalhas(int retryInicial, OutboxStatus statusFinal, int retryFinal)
    {
        await _fixture.CleanDatabaseAsync();

        var now = DateTimeOffset.UtcNow;
        var eventId = Guid.CreateVersion7(now);

        await using (var db = _fixture.CreateDbContext(new AuthContext { BypassTenantFilter = true }))
        {
            var msg = new OutboxMessage
            {
                Id = eventId,
                Topic = "usuario_bloqueado",
                PartitionKey = eventId.ToString("D"),
                Headers = "{\"event_id\":\"" + eventId.ToString("D") + "\"}",
                Payload = "{\"retry_test\":true}",
                Status = OutboxStatus.PENDING,
                RetryCount = retryInicial,
                CreatedAt = now,
                ProcessedAt = null
            };
            db.OutboxMessages.Add(msg);
            await db.SaveChangesAsync();
        }

        // Produtor que falha simulando rejeição pelo broker Kafka
        var failingProducer = new FailingKafkaProducer();
        var worker = CreateWorker(failingProducer);

        await worker.ProcessBatchAsync();

        await using (var db = _fixture.CreateDbContext(new AuthContext { BypassTenantFilter = true }))
        {
            var msg = await db.OutboxMessages.FindAsync(eventId);
            Assert.NotNull(msg);
            Assert.Equal(statusFinal, msg.Status);
            Assert.Equal(retryFinal, msg.RetryCount);

            if (statusFinal == OutboxStatus.FAILED)
            {
                Assert.NotNull(msg.ProcessedAt);
            }
            else
            {
                Assert.Null(msg.ProcessedAt);
            }
        }
    }

    [Fact(Timeout = 60000)]
    public async Task CA_14_8_Borda_ResilienciaWorker_IndisponibilidadeBrokerKafka()
    {
        await _fixture.CleanDatabaseAsync();

        var now = DateTimeOffset.UtcNow;
        var id1 = Guid.CreateVersion7(now);
        var id2 = Guid.CreateVersion7(now.AddMilliseconds(1));

        await using (var db = _fixture.CreateDbContext(new AuthContext { BypassTenantFilter = true }))
        {
            db.OutboxMessages.AddRange(
                new OutboxMessage
                {
                    Id = id1,
                    Topic = "usuario_bloqueado",
                    PartitionKey = id1.ToString("D"),
                    Headers = "{\"event_id\":\"" + id1.ToString("D") + "\"}",
                    Payload = "{\"resilience\":1}",
                    Status = OutboxStatus.PENDING,
                    RetryCount = 0,
                    CreatedAt = now,
                    ProcessedAt = null
                },
                new OutboxMessage
                {
                    Id = id2,
                    Topic = "usuario_bloqueado",
                    PartitionKey = id2.ToString("D"),
                    Headers = "{\"event_id\":\"" + id2.ToString("D") + "\"}",
                    Payload = "{\"resilience\":2}",
                    Status = OutboxStatus.PENDING,
                    RetryCount = 0,
                    CreatedAt = now.AddMilliseconds(1),
                    ProcessedAt = null
                }
            );
            await db.SaveChangesAsync();
        }

        var containerId = _fixture.Kafka.Id;

        // Pausar contentor Kafka
        var pausePsi = new ProcessStartInfo("docker", $"pause {containerId}")
        {
            CreateNoWindow = true,
            UseShellExecute = false
        };
        using (var p = Process.Start(pausePsi))
        {
            await p!.WaitForExitAsync();
        }

        try
        {
            // Worker processa o ciclo de envio com Kafka pausado
            var worker = CreateWorker();
            // Deve capturar o erro sem abortar a aplicação
            await worker.ProcessBatchAsync();

            // Mensagens têm retry_count incrementado em 1 mantendo status PENDING
            await using (var db = _fixture.CreateDbContext(new AuthContext { BypassTenantFilter = true }))
            {
                var m1 = await db.OutboxMessages.FindAsync(id1);
                var m2 = await db.OutboxMessages.FindAsync(id2);
                Assert.NotNull(m1);
                Assert.NotNull(m2);
                Assert.Equal(OutboxStatus.PENDING, m1.Status);
                Assert.Equal(1, m1.RetryCount);
                Assert.Equal(OutboxStatus.PENDING, m2.Status);
                Assert.Equal(1, m2.RetryCount);
            }
        }
        finally
        {
            // Retomar operação normal do Kafka
            var unpausePsi = new ProcessStartInfo("docker", $"unpause {containerId}")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using (var p = Process.Start(unpausePsi))
            {
                await p!.WaitForExitAsync();
            }
        }

        // Aguardar o broker restabelecer prontidão
        await Task.Delay(1000);

        // Novo ciclo com Kafka operacional
        var normalWorker = CreateWorker();
        var processed = await normalWorker.ProcessBatchAsync();
        Assert.Equal(2, processed);

        // Mensagens transitam para PUBLISHED
        await using (var db = _fixture.CreateDbContext(new AuthContext { BypassTenantFilter = true }))
        {
            var m1 = await db.OutboxMessages.FindAsync(id1);
            var m2 = await db.OutboxMessages.FindAsync(id2);
            Assert.NotNull(m1);
            Assert.NotNull(m2);
            Assert.Equal(OutboxStatus.PUBLISHED, m1.Status);
            Assert.Equal(OutboxStatus.PUBLISHED, m2.Status);
            Assert.NotNull(m1.ProcessedAt);
            Assert.NotNull(m2.ProcessedAt);
        }
    }

    private sealed class FailingKafkaProducer : IProducer<string, string>
    {
        public Handle Handle => throw new NotImplementedException();
        public string Name => "FailingProducer";

        public Task<DeliveryResult<string, string>> ProduceAsync(string topic, Message<string, string> message, CancellationToken cancellationToken = default)
        {
            throw new KafkaException(new Error(ErrorCode.BrokerNotAvailable, "Broker unavailable"));
        }

        public Task<DeliveryResult<string, string>> ProduceAsync(TopicPartition topicPartition, Message<string, string> message, CancellationToken cancellationToken = default)
        {
            throw new KafkaException(new Error(ErrorCode.BrokerNotAvailable, "Broker unavailable"));
        }

        public void Produce(string topic, Message<string, string> message, Action<DeliveryReport<string, string>>? deliveryHandler = null)
        {
            throw new KafkaException(new Error(ErrorCode.BrokerNotAvailable, "Broker unavailable"));
        }

        public void Produce(TopicPartition topicPartition, Message<string, string> message, Action<DeliveryReport<string, string>>? deliveryHandler = null)
        {
            throw new KafkaException(new Error(ErrorCode.BrokerNotAvailable, "Broker unavailable"));
        }

        public int Poll(TimeSpan timeout) => 0;
        public int Flush(TimeSpan timeout) => 0;
        public void Flush(CancellationToken cancellationToken = default) { }
        public void InitTransactions(TimeSpan timeout) { }
        public void BeginTransaction() { }
        public void CommitTransaction(TimeSpan timeout) { }
        public void CommitTransaction() { }
        public void AbortTransaction(TimeSpan timeout) { }
        public void AbortTransaction() { }
        public void SendOffsetsToTransaction(IEnumerable<TopicPartitionOffset> offsets, IConsumerGroupMetadata groupMetadata, TimeSpan timeout) { }
        public void SetSaslCredentials(string username, string password) { }
        public int AddBrokers(string brokers) => 0;
        public void Dispose() { }
    }
}
