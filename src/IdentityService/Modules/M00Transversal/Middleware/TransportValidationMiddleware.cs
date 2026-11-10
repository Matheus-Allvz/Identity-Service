using System.Text.RegularExpressions;
using IdentityService.Modules.M00Transversal.Errors;

namespace IdentityService.Modules.M00Transversal.Middleware;

public class TransportValidationMiddleware
{
    private static readonly Regex ContentTypeRegex = new(
        @"^application/json(?:\s*;\s*charset\s*=\s*utf-8)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex IntrospectContentTypeRegex = new(
        @"^(?:application/json|application/x-www-form-urlencoded)(?:\s*;\s*charset\s*=\s*utf-8)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly RequestDelegate _next;

    public TransportValidationMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (path.Equals("/.well-known/jwks.json", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/health/live", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/health/ready", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        if (context.Request.ContentLength > 65536)
        {
            throw new ApiErrorException("ERR-REQ-003", "Corpo do pedido demasiado grande");
        }

        context.Request.EnableBuffering();

        if (context.Request.ContentLength is > 0 or null)
        {
            var bodyStream = context.Request.Body;
            if (bodyStream.CanSeek)
            {
                bodyStream.Seek(0, SeekOrigin.Begin);
            }

            using var ms = new MemoryStream();
            var buffer = new byte[8192];
            int read;
            long totalRead = 0;
            while ((read = await bodyStream.ReadAsync(buffer)) > 0)
            {
                totalRead += read;
                if (totalRead > 65536)
                {
                    throw new ApiErrorException("ERR-REQ-003", "Corpo do pedido demasiado grande");
                }
                ms.Write(buffer, 0, read);
            }

            bodyStream.Position = 0;

            if (totalRead > 0)
            {
                var contentType = context.Request.ContentType;
                var isIntrospect = path.Equals("/api/v1/auth/introspect", StringComparison.OrdinalIgnoreCase);
                var isMatch = isIntrospect
                    ? (!string.IsNullOrWhiteSpace(contentType) && IntrospectContentTypeRegex.IsMatch(contentType.Trim()))
                    : (!string.IsNullOrWhiteSpace(contentType) && ContentTypeRegex.IsMatch(contentType.Trim()));

                if (!isMatch)
                {
                    throw new ApiErrorException("ERR-REQ-004", "Tipo de conteúdo não suportado");
                }
            }
        }

        await _next(context);
    }
}
