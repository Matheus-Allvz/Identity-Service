using System.Text.Json;
using IdentityService.Modules.M00Transversal.Audit;
using IdentityService.Modules.M00Transversal.Auth;
using IdentityService.Modules.M00Transversal.Errors;
using IdentityService.Modules.M00Transversal.Redis;
using IdentityService.Modules.M02Refresh.Interface;
using IdentityService.Modules.M11Esquema.Persistence;
using IdentityService.Modules.M11Esquema.Persistence.Enums;
using Microsoft.AspNetCore.Http;

namespace IdentityService.Modules.M09Logout;

public static class LogoutEndpoint
{
    public static async Task<IResult> HandleAsync(
        HttpContext httpContext,
        IdentityDbContext dbContext,
        IAuthContext authContext,
        ISessionStore sessionStore,
        IAuditLog auditLog,
        IDenylist denylist,
        CancellationToken cancellationToken)
    {
        // Layer 3 (RN-110): Validate body (must be empty 0 bytes or {})
        await ValidateBodyAsync(httpContext.Request);

        // Transaction ensures atomicity between refresh_token revocation, audit log, and Redis revocation (RN-114, PRS-190)
        await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // Layer 6 (RN-10, RN-111..116)
        if (authContext.FamilyId.HasValue)
        {
            await sessionStore.RevokeFamilyAsync(authContext.FamilyId.Value, cancellationToken);
        }

        auditLog.Append(
            AuditEventType.LOGOUT,
            authContext.UserId,
            authContext.ActorUserId,
            authContext.ActiveTenantId,
            authContext.GroupId,
            authContext.TicketId,
            null);

        // RN-111: SET denylist:jti in Redis before commit
        await denylist.RevokeJtiAsync(authContext.Jti!, authContext.Exp!.Value, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        // RN-116: Cache-Control: no-store and omit Pragma
        httpContext.Response.Headers.CacheControl = "no-store";
        httpContext.Response.Headers.Remove("Pragma");

        return Results.NoContent();
    }

    private static async Task ValidateBodyAsync(HttpRequest request)
    {
        request.EnableBuffering();
        using var reader = new StreamReader(request.Body, leaveOpen: true);
        var jsonText = await reader.ReadToEndAsync();
        request.Body.Position = 0;

        if (string.IsNullOrWhiteSpace(jsonText))
        {
            // 0 bytes or whitespace is accepted per RN-110
            return;
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(jsonText);
        }
        catch (JsonException)
        {
            throw new ApiErrorException("ERR-VAL-001", "Pedido inválido ou malformado");
        }

        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new ApiErrorException("ERR-VAL-001", "Pedido inválido ou malformado");
        }

        // RN-110: If body is JSON object, it must have 0 properties
        if (root.EnumerateObject().Any())
        {
            throw new ApiErrorException("ERR-VAL-001", "Pedido inválido ou malformado");
        }
    }
}
