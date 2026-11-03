using IdentityService.Modules.M13Isolamento.Interface;

namespace IdentityService.Modules.M13Isolamento;

public static class ModuleRegistration
{
    public static IServiceCollection AddM13Isolamento(this IServiceCollection services)
    {
        services.AddSingleton<ITenantIsolation, TenantIsolation>();
        return services;
    }
}
