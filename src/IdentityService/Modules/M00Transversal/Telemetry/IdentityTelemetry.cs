using System.Diagnostics.Metrics;
using System.Reflection;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace IdentityService.Modules.M00Transversal.Telemetry;

// NEW-07: ponto único de instrumentação. As métricas usam a API nativa System.Diagnostics.Metrics,
// por isso contar é sempre barato e inerte; só há exportação quando OTEL_EXPORTER_OTLP_ENDPOINT existe.
// RN-NEW07.2: nenhuma etiqueta leva identificadores de utilizador, tenant, token ou jti.
public static class IdentityTelemetry
{
    public const string MeterName = "IdentityService";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> TokensIssuedCounter =
        Meter.CreateCounter<long>("identity.tokens.issued", description: "Access tokens emitidos");

    private static readonly Counter<long> ProblemsCounter =
        Meter.CreateCounter<long>("identity.problems", description: "Respostas de erro RFC 9457 por código");

    private static readonly Counter<long> DenylistHitsCounter =
        Meter.CreateCounter<long>("identity.denylist.hits", description: "Pedidos recusados pela denylist");

    private static readonly Counter<long> JwksRequestsCounter =
        Meter.CreateCounter<long>("identity.jwks.requests", description: "Pedidos ao JWKS");

    private static readonly Counter<long> OutboxPublishedCounter =
        Meter.CreateCounter<long>("identity.outbox.published", description: "Eventos confirmados pelo broker");

    private static readonly Counter<long> OutboxFailuresCounter =
        Meter.CreateCounter<long>("identity.outbox.failures", description: "Falhas de publicação de eventos");

    // kind: user | impersonation | service
    public static void TokenIssued(string kind, string viewMode) =>
        TokensIssuedCounter.Add(1, new KeyValuePair<string, object?>("kind", kind), new KeyValuePair<string, object?>("view_mode", viewMode));

    public static void Problem(string code, int status) =>
        ProblemsCounter.Add(1, new KeyValuePair<string, object?>("code", code), new KeyValuePair<string, object?>("status", status));

    // kind: jti | user | tenant
    public static void DenylistHit(string kind) =>
        DenylistHitsCounter.Add(1, new KeyValuePair<string, object?>("kind", kind));

    public static void JwksRequested() => JwksRequestsCounter.Add(1);

    public static void OutboxPublished(string topic) =>
        OutboxPublishedCounter.Add(1, new KeyValuePair<string, object?>("topic", topic));

    // final = true quando a mensagem esgotou as tentativas e ficou FAILED
    public static void OutboxFailed(string topic, bool final) =>
        OutboxFailuresCounter.Add(1, new KeyValuePair<string, object?>("topic", topic), new KeyValuePair<string, object?>("final", final));

    public static IServiceCollection AddIdentityTelemetry(this IServiceCollection services, IConfiguration configuration)
    {
        // RN-NEW07.3: sem endpoint OTLP não há SDK, exportador nem threads de exportação.
        if (string.IsNullOrWhiteSpace(configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            return services;
        }

        var version = typeof(IdentityTelemetry).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

        // Endpoint, protocolo e amostragem seguem as variáveis OTEL_* padrão, lidas pelo próprio SDK
        // (OTEL_TRACES_SAMPLER, OTEL_TRACES_SAMPLER_ARG...). O nome do serviço é fixo: AddService
        // prevalece sobre OTEL_SERVICE_NAME.
        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService("identity-service", serviceVersion: version))
            .WithMetrics(metrics => metrics
                .AddMeter(MeterName)
                .AddAspNetCoreInstrumentation()
                .AddRuntimeInstrumentation()
                .AddOtlpExporter())
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation(options =>
                    options.Filter = context => !context.Request.Path.StartsWithSegments("/health"))
                .AddNpgsql()
                .AddOtlpExporter());

        return services;
    }
}
