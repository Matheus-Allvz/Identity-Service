using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Xunit;
using Xunit.Abstractions;

namespace IdentityService.Tests;

public class KestrelTeste(ITestOutputHelper o)
{
    [Fact]
    public async Task WafComKestrel()
    {
        await using var pg = new PostgreSqlBuilder("postgres:17.6").Build();
        await using var rd = new RedisBuilder("redis:7.4.6").Build();
        await Task.WhenAll(pg.StartAsync(), rd.StartAsync());
        Environment.SetEnvironmentVariable("DB_CONNECTION_STRING", pg.GetConnectionString());
        Environment.SetEnvironmentVariable("REDIS_CONNECTION_STRING", rd.GetConnectionString());
        Environment.SetEnvironmentVariable("KAFKA_BOOTSTRAP_SERVERS", "127.0.0.1:1");
        var rel = new RelogioTeste();
        rel.Avancar(TimeSpan.FromSeconds(500));
        var f = new WebApplicationFactory<Program>().WithWebHostBuilder(b => {
            b.UseEnvironment("Testing");
            b.ConfigureServices(s => s.AddSingleton<TimeProvider>(rel));
        });
        f.UseKestrel(0);
        f.StartServer();
        var h = f.CreateClient();

        o.WriteLine("base=" + h.BaseAddress + " jwks=" + await h.GetStringAsync("/.well-known/jwks.json") + " real=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        Assert.NotNull(h.BaseAddress);
        Assert.True(h.BaseAddress.Port > 0);

        var res = await h.GetAsync("/.well-known/jwks.json");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("\"keys\":[]", body);
        Assert.Contains("\"env\":\"Testing\"", body);

        await f.DisposeAsync();
    }
}
