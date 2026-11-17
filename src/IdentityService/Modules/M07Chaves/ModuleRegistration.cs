using IdentityService.Modules.M00Transversal.Auth;
using IdentityService.Modules.M00Transversal.Telemetry;
using IdentityService.Modules.M07Chaves.Interface;

namespace IdentityService.Modules.M07Chaves;

public static class ModuleRegistration
{
    public static IServiceCollection AddM07Chaves(this IServiceCollection services)
    {
        services.AddSingleton<ITokenIssuer, TokenIssuer>();
        return services;
    }

    public static IEndpointRouteBuilder MapM07Chaves(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/.well-known/jwks.json", (ISigningKey signingKey, HttpContext context) =>
        {
            IdentityTelemetry.JwksRequested();

            var keyId = signingKey.KeyId;
            var modulus = signingKey.Modulus;
            var exponent = signingKey.Exponent;

            context.Response.Headers.CacheControl = "public, max-age=3600, stale-while-revalidate=86400";

            var jwks = new
            {
                keys = new[]
                {
                    new
                    {
                        kty = "RSA",
                        use = "sig",
                        alg = "RS256",
                        kid = keyId,
                        n = modulus,
                        e = exponent
                    }
                }
            };

            return Results.Json(jwks, contentType: "application/json; charset=utf-8");
        });

        return endpoints;
    }
}
