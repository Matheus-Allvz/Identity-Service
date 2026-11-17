namespace IdentityService.Modules.M16ContasServico;

public static class ModuleRegistration
{
    public const string TokenPath = "/api/v1/auth/token";

    public static IEndpointRouteBuilder MapM16ContasServico(this IEndpointRouteBuilder app)
    {
        app.MapPost(TokenPath, TokenEndpoint.HandleAsync);
        return app;
    }
}