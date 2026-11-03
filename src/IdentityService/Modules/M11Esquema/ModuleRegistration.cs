using IdentityService.Modules.M11Esquema.Persistence;
using IdentityService.Modules.M13Isolamento.Interface;
using Microsoft.EntityFrameworkCore;

namespace IdentityService.Modules.M11Esquema;

public static class ModuleRegistration
{
    public static IServiceCollection AddM11Esquema(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<IdentityDbContext>((sp, options) =>
        {
            var isolation = sp.GetRequiredService<ITenantIsolation>();
            var connStr = configuration["DB_CONNECTION_STRING"];
            options.UseNpgsql(connStr)
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(isolation.CreateInterceptors(sp));
        });

        return services;
    }

    public static async Task MigrateDatabaseAsync(string connectionString)
    {
        var optionsBuilder = new DbContextOptionsBuilder<IdentityDbContext>();
        optionsBuilder.UseNpgsql(connectionString).UseSnakeCaseNamingConvention();
        await using var dbContext = new IdentityDbContext(optionsBuilder.Options);
        await dbContext.Database.MigrateAsync();
    }
}
