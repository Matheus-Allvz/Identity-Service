using Microsoft.EntityFrameworkCore;
using Npgsql;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);
string Req(string k) => builder.Configuration[k] is { Length: > 0 } v ? v : throw new InvalidOperationException("missing " + k);
var db = Req("DB_CONNECTION_STRING");
var redisCs = Req("REDIS_CONNECTION_STRING");
Req("KAFKA_BOOTSTRAP_SERVERS");
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDbContext<ProbeDbContext>(opt => opt.UseNpgsql(db));
var mux = ConnectionMultiplexer.Connect(redisCs + ",abortConnect=false");
builder.Services.AddSingleton<IConnectionMultiplexer>(mux);
var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var ctx = scope.ServiceProvider.GetRequiredService<ProbeDbContext>();
    await ctx.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS probe(x int)");
}

app.MapGet("/.well-known/jwks.json", (TimeProvider t) => Results.Json(new { keys = Array.Empty<object>(), now = t.GetUtcNow().ToUnixTimeSeconds(), env = app.Environment.EnvironmentName }));
app.MapGet("/probe/redis", async (IConnectionMultiplexer m) => {
    try { await m.GetDatabase().StringGetAsync("k"); return Results.Ok(); }
    catch (Exception e) when (e is RedisException or RedisTimeoutException) { return Results.Problem(e.GetType().Name, statusCode: 503); } });
app.MapGet("/probe/pg", async (ProbeDbContext ctx) => {
    try {
        await ctx.Database.ExecuteSqlRawAsync("select 1");
        return Results.Ok();
    }
    catch (Exception e) when (e is NpgsqlException || e.InnerException is NpgsqlException) {
        var baseEx = (e.InnerException as NpgsqlException) ?? (e as NpgsqlException);
        return Results.Problem(baseEx!.GetType().Name + ":" + baseEx.InnerException?.GetType().Name, statusCode: 503);
    }
});
app.Run();

public class ProbeDbContext(DbContextOptions<ProbeDbContext> options) : DbContext(options);
public partial class Program;

