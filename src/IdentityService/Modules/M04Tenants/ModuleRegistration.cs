using IdentityService.Modules.M04Tenants.Interface;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityService.Modules.M04Tenants;

public static class ModuleRegistration
{
    public static IServiceCollection AddM04Tenants(this IServiceCollection services)
    {
        services.AddScoped<ITenantStatusWriter, TenantStatusWriter>();
        return services;
    }

    public static IEndpointRouteBuilder MapM04Tenants(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/tenants", CreateTenantEndpoint.HandleAsync);
        return endpoints;
    }
}
