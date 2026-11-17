using IdentityService.Modules.M00Transversal.Auth;
using Microsoft.AspNetCore.Http;

namespace IdentityService.Modules.M01Login;

// EP-11 (QW-02): projeção pura das claims já validadas pelo middleware. Sem BD, sem cache.
public static class MeEndpoint
{
    public static IResult Handle(HttpContext httpContext, IAuthContext authContext)
    {
        httpContext.Response.Headers.CacheControl = "no-store";
        httpContext.Response.Headers.Pragma = "no-cache";

        return Results.Ok(new
        {
            sub = authContext.UserId!.Value.ToString("D"),
            platform_role = authContext.PlatformRole!.Value.ToString(),
            view_mode = authContext.ViewMode!.Value.ToString(),
            group_id = authContext.GroupId?.ToString("D"),
            tenant_id = authContext.ActiveTenantId?.ToString("D"),
            allowed_tenants = authContext.AllowedTenants.Select(t => t.ToString("D")).ToArray(),
            active_scope = authContext.ActiveScope?.ToString(),
            roles = authContext.Roles,
            permissions = authContext.Permissions,
            act = authContext.ActorUserId is { } actor
                ? new { sub = actor.ToString("D"), ticket_id = authContext.TicketId }
                : null,
            jti = authContext.Jti,
            iat = authContext.Iat,
            exp = authContext.Exp
        });
    }
}
