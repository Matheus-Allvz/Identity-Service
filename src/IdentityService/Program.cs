using System.Text;
using IdentityService.Modules.M00Transversal;
using IdentityService.Modules.M07Chaves;
using IdentityService.Modules.M08Acessos;
using IdentityService.Modules.M11Esquema;
using IdentityService.Modules.M13Isolamento;
using IdentityService.Modules.M14Eventos;

var builder = WebApplication.CreateBuilder(args);

// PRS-54: Decodificação Latin-1 para headers em Kestrel
builder.WebHost.ConfigureKestrel(options =>
{
    options.RequestHeaderEncodingSelector = _ => Encoding.Latin1;
});

var isTesting = builder.Environment.IsEnvironment("Testing") || args.Contains("--testing");
var dbConnectionString = builder.Configuration["DB_CONNECTION_STRING"];
var redisConnectionString = builder.Configuration["REDIS_CONNECTION_STRING"];
var kafkaBootstrapServers = builder.Configuration["KAFKA_BOOTSTRAP_SERVERS"];

// D-25: Validação das variáveis de ambiente obrigatórias
if (!isTesting)
{
    if (string.IsNullOrWhiteSpace(dbConnectionString) ||
        string.IsNullOrWhiteSpace(redisConnectionString) ||
        string.IsNullOrWhiteSpace(kafkaBootstrapServers))
    {
        Console.Error.WriteLine("Variáveis de ambiente obrigatórias não fornecidas (DB_CONNECTION_STRING, REDIS_CONNECTION_STRING, KAFKA_BOOTSTRAP_SERVERS).");
        Environment.Exit(1);
        return;
    }
}

// Registro dos módulos
builder.Services.AddM00Transversal(builder.Configuration);
builder.Services.AddM07Chaves();
builder.Services.AddM08Acessos();
builder.Services.AddM11Esquema(builder.Configuration);
builder.Services.AddM13Isolamento();
builder.Services.AddM14Eventos(builder.Configuration);

var app = builder.Build();

// Migrações no arranque quando connection string está disponível
if (!string.IsNullOrWhiteSpace(dbConnectionString))
{
    try
    {
        await IdentityService.Modules.M11Esquema.ModuleRegistration.MigrateDatabaseAsync(dbConnectionString);
    }
    catch (Exception ex)
    {
        if (!isTesting)
        {
            Console.Error.WriteLine($"Falha ao executar migrações: {ex.Message}");
            Environment.Exit(1);
            return;
        }
    }
}

// Pipeline transversal
app.UseM00Transversal();

// Endpoints M07
app.MapM07Chaves();

// Placeholder endpoints das rotas de negócio para validação transversal (40 §1.1)
app.MapPost("/api/v1/auth/login", () => Results.Ok(new { message = "login placeholder" }));
app.MapPost("/api/v1/auth/refresh", () => Results.Ok(new { message = "refresh placeholder" }));
app.MapPost("/api/v1/auth/switch-context", () => Results.Ok(new { message = "switch-context placeholder" }));
app.MapPost("/api/v1/auth/logout", () => Results.Ok(new { message = "logout placeholder" }));
app.MapPost("/api/v1/auth/impersonate", () => Results.Ok(new { message = "impersonate placeholder" }));
app.MapPost("/api/v1/tenants", () => Results.Created("/api/v1/tenants/00000000-0000-0000-0000-000000000000", new { message = "tenant created placeholder" }));
app.MapGet("/api/v1/tenants/{tenant_id}/members", (string tenant_id) => Results.Ok(new { message = "members placeholder" }));
app.MapPatch("/api/v1/tenants/{tenant_id}/status", (string tenant_id) => Results.Ok(new { message = "tenant status placeholder" }));
app.MapPatch("/api/v1/users/{user_id}/status", (string user_id) => Results.Ok(new { message = "user status placeholder" }));

app.Run();

public partial class Program;
