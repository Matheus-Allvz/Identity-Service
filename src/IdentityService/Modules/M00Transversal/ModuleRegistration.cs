using IdentityService.Modules.M00Transversal.Audit;
using IdentityService.Modules.M00Transversal.Auth;
using IdentityService.Modules.M00Transversal.Middleware;
using IdentityService.Modules.M00Transversal.Redis;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

namespace IdentityService.Modules.M00Transversal;

public static class ModuleRegistration
{
    public static IServiceCollection AddM00Transversal(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);

        services.TryAddSingleton<ISigningKey, SigningKeyProvider>();
        services.AddScoped<IAuthContext, AuthContext>();
        services.AddScoped<IAuditLog, AuditLog>();

        var redisCs = configuration["REDIS_CONNECTION_STRING"];
        if (!string.IsNullOrWhiteSpace(redisCs))
        {
            var mux = ConnectionMultiplexer.Connect(redisCs + ",abortConnect=false");
            services.TryAddSingleton<IConnectionMultiplexer>(mux);
        }

        services.AddScoped<IDenylist, DenylistService>();

        return services;
    }

    public static IApplicationBuilder UseM00Transversal(this IApplicationBuilder app)
    {
        app.UseMiddleware<GlobalExceptionHandlerMiddleware>();
        app.UseMiddleware<CorrelationAndIpMiddleware>();
        app.UseMiddleware<TransportValidationMiddleware>();
        app.UseMiddleware<AuthContextMiddleware>();
        return app;
    }
}
