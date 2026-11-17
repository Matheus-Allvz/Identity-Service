using System.Net;
using System.Text.Json;
using IdentityService.Modules.M00Transversal.Auth;
using IdentityService.Tests.Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IdentityService.Tests;

// QW-04 / EP-13, EP-14: OpenAPI 3.1 e Swagger UI, contra o Program real.
[Trait("Fase", "F06")]
[Collection("SharedContainers")]
public class M15DocsTests
{
    private static readonly string[] HttpVerbs = ["get", "post", "put", "patch", "delete"];

    private readonly TestcontainersFixture _fixture;

    public M15DocsTests(TestcontainersFixture fixture)
    {
        _fixture = fixture;
    }

    private WebApplicationFactory<Program> CreateFactory(string? openApiEnabled = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("DB_CONNECTION_STRING", _fixture.GetConnectionString());
            builder.UseSetting("REDIS_CONNECTION_STRING", _fixture.GetRedisConnectionString());
            builder.UseSetting("KAFKA_BOOTSTRAP_SERVERS", _fixture.GetKafkaBootstrapServers());
            if (openApiEnabled != null)
            {
                builder.UseSetting("OPENAPI_ENABLED", openApiEnabled);
            }

            builder.ConfigureTestServices(services =>
                services.AddSingleton<ISigningKey>(new TestSigningKey("key-2026-01")));
        });

    [Fact(Timeout = 60000)]
    public async Task CA_QW04_1_OpenApiJson_DevolveDocumento31Valido()
    {
        await using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/openapi.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(TimeSpan.FromSeconds(300), response.Headers.CacheControl!.MaxAge);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.StartsWith("3.1", doc.RootElement.GetProperty("openapi").GetString());
        Assert.True(doc.RootElement.GetProperty("components").GetProperty("schemas").TryGetProperty("Problem", out _));
    }

    [Fact(Timeout = 60000)]
    public async Task CA_QW04_2_Deriva_RotasDoCodigoIguaisAsDoDocumento()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var mapped = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])
                .Select(m => $"{m.ToUpperInvariant()} {e.RoutePattern.RawText}"))
            .Where(r => r is not ("GET /openapi.json" or "GET /swagger"))
            .Order(StringComparer.Ordinal)
            .ToArray();

        using var doc = JsonDocument.Parse(await client.GetStringAsync("/openapi.json"));
        var documented = doc.RootElement.GetProperty("paths").EnumerateObject()
            .SelectMany(p => p.Value.EnumerateObject()
                .Where(op => HttpVerbs.Contains(op.Name))
                .Select(op => $"{op.Name.ToUpperInvariant()} {p.Name}"))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(mapped);
        Assert.Equal(mapped, documented);
    }

    [Fact(Timeout = 60000)]
    public async Task CA_QW04_3_Swagger_DevolveHtmlApontandoParaODocumento()
    {
        await using var factory = CreateFactory();
        // Um Authorization inválido não pode quebrar rota anónima.
        var request = new HttpRequestMessage(HttpMethod.Get, "/swagger");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer lixo");
        var response = await factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType!.MediaType);
        Assert.Contains("/openapi.json", await response.Content.ReadAsStringAsync());
    }

    [Fact(Timeout = 60000)]
    public async Task CA_QW04_4_Desativado_DevolveRotaInexistente()
    {
        await using var factory = CreateFactory(openApiEnabled: "false");
        var client = factory.CreateClient();

        foreach (var path in new[] { "/openapi.json", "/swagger" })
        {
            var response = await client.GetAsync(path);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("ERR-REQ-001", doc.RootElement.GetProperty("code").GetString());
        }
    }
}
