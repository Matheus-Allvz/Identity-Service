using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Headers;
using IdentityService.Modules.M00Transversal;
using IdentityService.Modules.M00Transversal.Auth;
using IdentityService.Modules.M00Transversal.Redis;
using IdentityService.Modules.M00Transversal.Telemetry;
using IdentityService.Modules.M01Login;
using IdentityService.Modules.M02Refresh;
using IdentityService.Modules.M07Chaves;
using IdentityService.Modules.M07Chaves.Interface;
using IdentityService.Modules.M08Acessos;
using IdentityService.Modules.M11Esquema;
using IdentityService.Modules.M11Esquema.Persistence.Enums;
using IdentityService.Modules.M13Isolamento;
using IdentityService.Modules.M14Eventos;
using IdentityService.Tests.Fixtures;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IdentityService.Tests;

// NEW-07: métricas OpenTelemetry (CA-NEW07.1 a .4)
[Trait("Fase", "F06")]
[Collection("SharedContainers")]
public class TelemetryTests
{
    private readonly TestcontainersFixture _fixture;
    private readonly TestSigningKey _signingKey = new("key-2026-01");
    private readonly TestTimeProvider _timeProvider = new(new DateTimeOffset(2026, 11, 2, 22, 0, 0, TimeSpan.Zero));

    public TelemetryTests(TestcontainersFixture fixture)
    {
        _fixture = fixture;
    }

    // O meter é estático e partilhado com os testes que correm em paralelo: o recorder só vê o que
    // acontece durante o teste e as asserções são sempre sobre deltas (antes/depois da ação).
    private sealed class MetricRecorder : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly ConcurrentQueue<(string Name, KeyValuePair<string, object?>[] Tags)> _measurements = new();

        public MetricRecorder()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == IdentityTelemetry.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) =>
                _measurements.Enqueue((instrument.Name, tags.ToArray())));
            _listener.Start();
        }

        public int Count(string name, params (string Key, object Value)[] tags) =>
            _measurements.Count(m => m.Name == name &&
                tags.All(t => m.Tags.Any(mt => mt.Key == t.Key && Equals(mt.Value, t.Value))));

        public void Dispose() => _listener.Dispose();
    }

    private WebApplication CreateTestApp(string? otlpEndpoint = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRouting();

        builder.Configuration["DB_CONNECTION_STRING"] = _fixture.GetConnectionString();
        builder.Configuration["REDIS_CONNECTION_STRING"] = _fixture.GetRedisConnectionString();
        builder.Configuration["KAFKA_BOOTSTRAP_SERVERS"] = _fixture.GetKafkaBootstrapServers();
        if (otlpEndpoint != null)
        {
            builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] = otlpEndpoint;
        }

        builder.Services.AddSingleton<ISigningKey>(_signingKey);
        builder.Services.AddSingleton<TimeProvider>(_timeProvider);

        builder.Services.AddM00Transversal(builder.Configuration);
        builder.Services.AddM01Login();
        builder.Services.AddM02Refresh();
        builder.Services.AddM07Chaves();
        builder.Services.AddM08Acessos();
        builder.Services.AddM11Esquema(builder.Configuration);
        builder.Services.AddM13Isolamento();
        builder.Services.AddM14Eventos(builder.Configuration);

        var app = builder.Build();
        app.UseM00Transversal();
        app.MapM01Login();
        app.MapM07Chaves();
        return app;
    }

    private IssuedToken Issue(AccessTokenClaims claims) =>
        new TokenIssuer(_signingKey, _timeProvider, new ConfigurationBuilder().Build()).Issue(claims);

    private static Task<HttpResponseMessage> GetMeAsync(HttpClient client, string? token)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        if (token != null)
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client.SendAsync(req);
    }

    [Fact]
    public void CA_NEW07_1_EmissaoDeTokens_IncrementaTokensIssuedPorTipo()
    {
        using var recorder = new MetricRecorder();
        var tenantId = Guid.NewGuid();

        var before = recorder.Count("identity.tokens.issued", ("kind", "user"), ("view_mode", "NONE"));
        Issue(new AccessTokenClaims(
            Guid.NewGuid(), Guid.NewGuid(), PlatformRole.NONE, ViewMode.NONE,
            null, null, [], null, [], []));
        Assert.True(recorder.Count("identity.tokens.issued", ("kind", "user"), ("view_mode", "NONE")) > before);

        before = recorder.Count("identity.tokens.issued", ("kind", "impersonation"), ("view_mode", "SINGLE"));
        Issue(new AccessTokenClaims(
            Guid.NewGuid(), null, PlatformRole.NONE, ViewMode.SINGLE,
            Guid.NewGuid(), tenantId, [tenantId], OperationalScope.SUPPLIER, [], [],
            new ActorClaim(Guid.NewGuid(), "SUP-123")));
        Assert.True(recorder.Count("identity.tokens.issued", ("kind", "impersonation"), ("view_mode", "SINGLE")) > before);

        before = recorder.Count("identity.tokens.issued", ("kind", "service"), ("view_mode", "SINGLE"));
        Issue(new AccessTokenClaims(
            Guid.NewGuid(), null, PlatformRole.NONE, ViewMode.SINGLE,
            Guid.NewGuid(), tenantId, [tenantId], OperationalScope.BUYER, [], [],
            null, "svc-telemetria"));
        Assert.True(recorder.Count("identity.tokens.issued", ("kind", "service"), ("view_mode", "SINGLE")) > before);
    }

    [Fact(Timeout = 60000)]
    public async Task CA_NEW07_2_SemToken_IncrementaProblemsComCodigoEStatus()
    {
        await using var app = CreateTestApp();
        await app.StartAsync();
        using var recorder = new MetricRecorder();

        var before = recorder.Count("identity.problems", ("code", "ERR-AUTH-002"), ("status", 401));
        var response = await GetMeAsync(app.GetTestClient(), null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(recorder.Count("identity.problems", ("code", "ERR-AUTH-002"), ("status", 401)) > before);
    }

    [Fact(Timeout = 60000)]
    public async Task CA_NEW07_3_JtiRevogado_IncrementaDenylistHits()
    {
        var issued = Issue(new AccessTokenClaims(
            Guid.NewGuid(), Guid.NewGuid(), PlatformRole.NONE, ViewMode.NONE,
            null, null, [], null, [], []));

        await using var app = CreateTestApp();
        await app.StartAsync();
        using (var scope = app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IDenylist>().RevokeJtiAsync(issued.Jti, issued.Exp);
        }

        using var recorder = new MetricRecorder();
        var before = recorder.Count("identity.denylist.hits", ("kind", "jti"));
        var response = await GetMeAsync(app.GetTestClient(), issued.AccessToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(recorder.Count("identity.denylist.hits", ("kind", "jti")) > before);
    }

    // RN-NEW07.1: com o collector inacessível a aplicação arranca, responde e encerra sem bloquear.
    [Fact(Timeout = 30000)]
    public async Task CA_NEW07_4_CollectorInacessivel_AplicacaoArrancaEResponde()
    {
        using var recorder = new MetricRecorder();
        var before = recorder.Count("identity.jwks.requests");

        await using (var app = CreateTestApp(otlpEndpoint: "http://127.0.0.1:1"))
        {
            await app.StartAsync();
            var response = await app.GetTestClient().GetAsync("/.well-known/jwks.json");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.True(recorder.Count("identity.jwks.requests") > before);
    }
}
