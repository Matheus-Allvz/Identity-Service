using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityService.Modules.M03Contexto;

public static class ModuleRegistration
{
    public static IServiceCollection AddM03Contexto(this IServiceCollection services)
    {
        return services;
    }

    public static IEndpointRouteBuilder MapM03Contexto(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/auth/switch-context", SwitchContextEndpoint.HandleAsync);
        return app;
    }
}
