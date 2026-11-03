using System.Security.Cryptography;
using System.Text.Json;
using IdentityService.Modules.M00Transversal.Auth;
using IdentityService.Modules.M00Transversal.Errors;
using Npgsql;
using StackExchange.Redis;

namespace IdentityService.Modules.M00Transversal.Middleware;

public class GlobalExceptionHandlerMiddleware
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionHandlerMiddleware> _logger;

    public GlobalExceptionHandlerMiddleware(RequestDelegate next, ILogger<GlobalExceptionHandlerMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IAuthContext authContext)
    {
        try
        {
            await _next(context);

            if (!context.Response.HasStarted)
            {
                if (context.Response.StatusCode == StatusCodes.Status404NotFound)
                {
                    await WriteProblemResponseAsync(context, authContext, "ERR-REQ-001");
                }
                else if (context.Response.StatusCode == StatusCodes.Status405MethodNotAllowed)
                {
                    await WriteProblemResponseAsync(context, authContext, "ERR-REQ-002");
                }
            }
        }
        catch (Exception ex)
        {
            if (context.Response.HasStarted)
            {
                _logger.LogError(ex, "Response already started, unable to write problem response.");
                throw;
            }

            var errorCode = ClassifyException(ex);
            var (statusCode, _) = ErrorCatalog.GetInfo(errorCode);

            if (statusCode is 500 or 503)
            {
                using (_logger.BeginScope(new Dictionary<string, object?>
                {
                    ["correlation_id"] = authContext.CorrelationId.ToString("D")
                }))
                {
                    _logger.LogError("Internal or infrastructure error occurred. Code: {ErrorCode}", errorCode);
                }
            }

            await WriteProblemResponseAsync(context, authContext, errorCode);
        }
    }

    private static string ClassifyException(Exception ex)
    {
        for (var current = ex; current != null; current = current.InnerException)
        {
            if (current is ApiErrorException apiEx)
            {
                return apiEx.Code;
            }

            if (current is PostgresException pgEx)
            {
                if (pgEx.SqlState == "42501")
                {
                    return "ERR-CTX-007";
                }

                if (pgEx.SqlState == "23505")
                {
                    return "ERR-CTX-005";
                }

                if (pgEx.SqlState.StartsWith("08", StringComparison.Ordinal) ||
                    pgEx.SqlState is "57P01" or "57P02" or "57P03")
                {
                    return "ERR-SYS-002";
                }
            }

            if (current is NpgsqlException and not PostgresException)
            {
                return "ERR-SYS-002";
            }

            if (current is RedisConnectionException or RedisTimeoutException)
            {
                return "ERR-SYS-002";
            }

            if (current is CryptographicException)
            {
                return "ERR-SYS-002";
            }
        }

        return "ERR-SYS-001";
    }

    private static async Task WriteProblemResponseAsync(
        HttpContext context,
        IAuthContext authContext,
        string errorCode)
    {
        var (status, title) = ErrorCatalog.GetInfo(errorCode);

        context.Response.Clear();
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json; charset=utf-8";
        context.Response.Headers["X-Correlation-Id"] = authContext.CorrelationId.ToString("D");

        var problem = new ProblemDetailsDto(
            Type: $"https://identity.b2b.local/errors/{errorCode}",
            Title: title,
            Status: status,
            Code: errorCode,
            Detail: title,
            CorrelationId: authContext.CorrelationId.ToString("D"));

        await JsonSerializer.SerializeAsync(context.Response.Body, problem, JsonOptions);
    }

    private sealed record ProblemDetailsDto(
        string Type,
        string Title,
        int Status,
        string Code,
        string Detail,
        string CorrelationId);
}
