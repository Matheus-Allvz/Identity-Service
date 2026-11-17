using System.Net;
using IdentityService.Modules.M00Transversal.Auth;

namespace IdentityService.Modules.M00Transversal.Middleware;

public class CorrelationAndIpMiddleware
{
    private readonly RequestDelegate _next;

    public CorrelationAndIpMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IAuthContext authContext,
        TimeProvider timeProvider,
        ILogger<CorrelationAndIpMiddleware> logger)
    {
        var correlationId = ResolveCorrelationId(context, timeProvider);
        context.Response.Headers["X-Correlation-Id"] = correlationId.ToString("D");
        System.Diagnostics.Activity.Current?.SetTag("correlation.id", correlationId.ToString("D"));

        var clientIp = ResolveClientIp(context);

        if (authContext is AuthContext mutableAuthContext)
        {
            mutableAuthContext.CorrelationId = correlationId;
            mutableAuthContext.IpAddress = clientIp;
        }

        using (logger.BeginScope(new Dictionary<string, object?>
        {
            ["correlation_id"] = correlationId.ToString("D"),
            ["user_id"] = authContext.UserId?.ToString("D"),
            ["actor_user_id"] = authContext.ActorUserId?.ToString("D"),
            ["tenant_id"] = authContext.ActiveTenantId?.ToString("D"),
            ["group_id"] = authContext.GroupId?.ToString("D"),
            ["active_scope"] = authContext.ActiveScope?.ToString()
        }))
        {
            await _next(context);
        }
    }

    private static Guid ResolveCorrelationId(HttpContext context, TimeProvider timeProvider)
    {
        if (context.Request.Headers.TryGetValue("X-Correlation-Id", out var values) &&
            !string.IsNullOrWhiteSpace(values))
        {
            var headerValue = values.ToString();
            if (Guid.TryParseExact(headerValue, "D", out var cid))
            {
                return cid;
            }
        }

        return Guid.CreateVersion7(timeProvider.GetUtcNow());
    }

    private static string ResolveClientIp(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue("X-Forwarded-For", out var values) &&
            !string.IsNullOrWhiteSpace(values))
        {
            var firstPart = values.ToString().Split(',')[0].Trim();
            if (IPAddress.TryParse(firstPart, out var parsedIp))
            {
                var ipStr = parsedIp.ToString();
                if (ipStr.Length <= 45)
                {
                    return ipStr;
                }
            }
        }

        var remoteIp = context.Connection.RemoteIpAddress?.ToString();
        if (!string.IsNullOrWhiteSpace(remoteIp) && remoteIp.Length <= 45)
        {
            return remoteIp;
        }

        return "0.0.0.0";
    }
}
