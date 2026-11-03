using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using IdentityService.Modules.M11Esquema.Persistence;
using IdentityService.Modules.M11Esquema.Persistence.Entities;
using IdentityService.Modules.M11Esquema.Persistence.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IdentityService.Modules.M14Eventos;

public class OutboxPublisherWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IProducer<string, string> _producer;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OutboxPublisherWorker> _logger;

    public OutboxPublisherWorker(
        IServiceScopeFactory scopeFactory,
        IProducer<string, string> producer,
        TimeProvider timeProvider,
        ILogger<OutboxPublisherWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _producer = producer;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro no ciclo do worker da outbox.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), _timeProvider, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public async Task<int> ProcessBatchAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        // RN-166: Até 50 registos PENDING ordenados por created_at ASC, id ASC
        var messages = await dbContext.OutboxMessages
            .Where(m => m.Status == OutboxStatus.PENDING)
            .OrderBy(m => m.CreatedAt)
            .ThenBy(m => m.Id)
            .Take(50)
            .ToListAsync(cancellationToken);

        if (messages.Count == 0)
        {
            return 0;
        }

        var publishedCount = 0;

        foreach (var message in messages)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var kafkaHeaders = new Headers();
            if (!string.IsNullOrWhiteSpace(message.Headers))
            {
                var headersDict = JsonSerializer.Deserialize<Dictionary<string, string>>(message.Headers);
                if (headersDict != null)
                {
                    foreach (var (k, v) in headersDict)
                    {
                        kafkaHeaders.Add(k, Encoding.UTF8.GetBytes(v));
                    }
                }
            }

            var kafkaMessage = new Message<string, string>
            {
                Key = message.PartitionKey,
                Value = message.Payload,
                Headers = kafkaHeaders
            };

            var now = _timeProvider.GetUtcNow();

            try
            {
                // RN-167: Publicar com Acks = All e timeout 3000 ms configurados no produtor
                await _producer.ProduceAsync(message.Topic, kafkaMessage, cancellationToken);

                // RN-168: Confirmado pelo broker
                message.Status = OutboxStatus.PUBLISHED;
                message.ProcessedAt = now;
                publishedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao enviar mensagem {EventId} para Kafka.", message.Id);

                // RN-169: Ciclo de retentativas
                var nextRetry = message.RetryCount + 1;
                if (nextRetry >= 5)
                {
                    message.Status = OutboxStatus.FAILED;
                    message.RetryCount = 5;
                    message.ProcessedAt = now;
                }
                else
                {
                    message.RetryCount = nextRetry;
                    message.Status = OutboxStatus.PENDING;
                    message.ProcessedAt = null;
                }
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return publishedCount;
    }
}
