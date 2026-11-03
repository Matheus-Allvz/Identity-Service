using IdentityService.Modules.M02Refresh.Interface;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityService.Modules.M02Refresh;

public static class ModuleRegistration
{
    public static IServiceCollection AddM02Refresh(this IServiceCollection services)
    {
        services.AddScoped<ISessionStore, SessionStore>();
        return services;
    }

    public static IEndpointRouteBuilder MapM02Refresh(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/auth/refresh", RefreshEndpoint.HandleAsync);
        return app;
    }
}
