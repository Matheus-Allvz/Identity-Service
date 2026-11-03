using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityService.Modules.M09Logout;

public static class ModuleRegistration
{
    public static IServiceCollection AddM09Logout(this IServiceCollection services)
    {
        return services;
    }

    public static IEndpointRouteBuilder MapM09Logout(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/auth/logout", LogoutEndpoint.HandleAsync);
        return app;
    }
}
