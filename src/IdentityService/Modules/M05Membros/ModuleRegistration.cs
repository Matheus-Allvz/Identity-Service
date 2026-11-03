using IdentityService.Modules.M05Membros.Interface;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityService.Modules.M05Membros;

public static class ModuleRegistration
{
    public static IServiceCollection AddM05Membros(this IServiceCollection services)
    {
        services.AddScoped<IMembershipWriter, MembershipWriter>();
        return services;
    }

    public static IEndpointRouteBuilder MapM05Membros(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/tenants/{tenant_id}/members", AddMemberEndpoint.HandleAsync);
        return endpoints;
    }
}
