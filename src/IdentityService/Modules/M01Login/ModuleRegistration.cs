using IdentityService.Modules.M01Login.Interface;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityService.Modules.M01Login;

public static class ModuleRegistration
{
    public static IServiceCollection AddM01Login(this IServiceCollection services)
    {
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddScoped<IUserAccountStatusWriter, UserAccountStatusWriter>();
        return services;
    }

    public static IEndpointRouteBuilder MapM01Login(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/auth/login", LoginEndpoint.HandleAsync);
        app.MapPost("/api/v1/auth/introspect", IntrospectEndpoint.HandleAsync).AllowAnonymous();
        app.MapGet("/api/v1/auth/me", MeEndpoint.Handle);
        return app;
    }
}
