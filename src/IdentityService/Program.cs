using System.Text;
using IdentityService.Modules.M00Transversal;
using IdentityService.Modules.M00Transversal.Errors;
using IdentityService.Modules.M01Login;
using IdentityService.Modules.M02Refresh;
using IdentityService.Modules.M03Contexto;
using IdentityService.Modules.M04Tenants;
using IdentityService.Modules.M05Membros;
using IdentityService.Modules.M06Impersonation;
using IdentityService.Modules.M07Chaves;
using IdentityService.Modules.M08Acessos;
using IdentityService.Modules.M09Logout;
using IdentityService.Modules.M10EstadoUsuario;
using IdentityService.Modules.M11Esquema;
using IdentityService.Modules.M12EstadoTenant;
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
builder.Services.AddM01Login();
builder.Services.AddM02Refresh();
builder.Services.AddM03Contexto();
builder.Services.AddM04Tenants();
builder.Services.AddM05Membros();
builder.Services.AddM06Impersonation();
builder.Services.AddM07Chaves();
builder.Services.AddM08Acessos();
builder.Services.AddM09Logout();
builder.Services.AddM10EstadoUsuario();
builder.Services.AddM11Esquema(builder.Configuration);
builder.Services.AddM12EstadoTenant();
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

// Endpoints dos módulos
app.MapM01Login();
app.MapM02Refresh();
app.MapM03Contexto();
app.MapM04Tenants();
app.MapM05Membros();
app.MapM06Impersonation();
app.MapM07Chaves();
app.MapM09Logout();
app.MapM10EstadoUsuario();
app.MapM12EstadoTenant();

app.Run();

public partial class Program;
