using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityService.Modules.M10EstadoUsuario;

public static class ModuleRegistration
{
    public static IServiceCollection AddM10EstadoUsuario(this IServiceCollection services)
    {
        return services;
    }

    public static IEndpointRouteBuilder MapM10EstadoUsuario(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPatch("/api/v1/users/{user_id}/status", UserStatusEndpoint.HandleAsync);
        return endpoints;
    }
}
