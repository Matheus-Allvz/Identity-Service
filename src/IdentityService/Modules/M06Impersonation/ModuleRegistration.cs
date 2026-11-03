using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityService.Modules.M06Impersonation;

public static class ModuleRegistration
{
    public static IServiceCollection AddM06Impersonation(this IServiceCollection services)
    {
        return services;
    }

    public static IEndpointRouteBuilder MapM06Impersonation(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/auth/impersonate", ImpersonateEndpoint.HandleAsync);
        return app;
    }
}
