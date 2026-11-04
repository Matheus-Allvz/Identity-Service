using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityService.Modules.M12EstadoTenant;

public static class ModuleRegistration
{
    public static IServiceCollection AddM12EstadoTenant(this IServiceCollection services)
    {
        return services;
    }

    public static IEndpointRouteBuilder MapM12EstadoTenant(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPatch("/api/v1/tenants/{tenant_id}/status", TenantStatusEndpoint.HandleAsync);
        return endpoints;
    }
}
