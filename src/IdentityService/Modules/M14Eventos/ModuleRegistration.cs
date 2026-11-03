using Confluent.Kafka;
using IdentityService.Modules.M14Eventos.Interface;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IdentityService.Modules.M14Eventos;

public static class ModuleRegistration
{
    public static IServiceCollection AddM14Eventos(this IServiceCollection services, IConfiguration? configuration = null)
    {
        services.TryAddSingleton<IProducer<string, string>>(sp =>
        {
            var config = configuration ?? sp.GetRequiredService<IConfiguration>();
            var bootstrapServers = config["KAFKA_BOOTSTRAP_SERVERS"] ?? "localhost:9092";

            var producerConfig = new ProducerConfig
            {
                BootstrapServers = bootstrapServers,
                Acks = Acks.All,
                MessageTimeoutMs = 3000,
                EnableIdempotence = true
            };

            return new ProducerBuilder<string, string>(producerConfig).Build();
        });

        services.AddScoped<IEventOutbox, EventOutbox>();
        services.AddHostedService<OutboxPublisherWorker>();

        return services;
    }
}
