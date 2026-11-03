using IdentityService.Modules.M08Acessos.Interface;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityService.Modules.M08Acessos;

public static class ModuleRegistration
{
    public static IServiceCollection AddM08Acessos(this IServiceCollection services)
    {
        services.AddScoped<IAccessQueries, AccessQueries>();
        return services;
    }
}
