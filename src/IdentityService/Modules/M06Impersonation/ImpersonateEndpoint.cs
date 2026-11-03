using System.Text.Json;
using System.Text.RegularExpressions;
using IdentityService.Modules.M00Transversal.Audit;
using IdentityService.Modules.M00Transversal.Auth;
using IdentityService.Modules.M00Transversal.Errors;
using IdentityService.Modules.M07Chaves.Interface;
using IdentityService.Modules.M08Acessos.Interface;
using IdentityService.Modules.M11Esquema.Persistence;
using IdentityService.Modules.M11Esquema.Persistence.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace IdentityService.Modules.M06Impersonation;

public static class ImpersonateEndpoint
{
    private static readonly Regex TicketIdRegex = new(@"^[A-Z]{2,10}-[0-9]{1,10}$", RegexOptions.Compiled);

    public static async Task<IResult> HandleAsync(
        HttpContext httpContext,
        IdentityDbContext dbContext,
        IAuthContext authContext,
        ITokenIssuer tokenIssuer,
        IAccessQueries accessQueries,
        IAuditLog auditLog,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        // Layer 4 (RN-13): Operator must have platform_role in {SUPPORT_AGENT, PLATFORM_ADMIN} and token without act
        if (authContext.ActorUserId != null ||
            (authContext.PlatformRole != PlatformRole.SUPPORT_AGENT && authContext.PlatformRole != PlatformRole.PLATFORM_ADMIN))
        {
            throw new ApiErrorException("ERR-AUTH-010", "Operação de impersonation não autorizada");
        }

        var requestData = await ReadAndValidateJsonAsync(httpContext.Request);

        // Layer 6 (RN-13, RN-80..87)
        var now = timeProvider.GetUtcNow();

        // 9. Target tenant exists
        var tenantInfo = await accessQueries.ResolveTenantAsync(requestData.TargetTenantId, cancellationToken);
        if (tenantInfo == null)
        {
            throw new ApiErrorException("ERR-CTX-008", "Recurso de domínio não encontrado");
        }

        // 10. Target user exists
        var targetUser = await dbContext.UserAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == requestData.TargetUserId, cancellationToken);

        if (targetUser == null)
        {
            throw new ApiErrorException("ERR-CTX-008", "Recurso de domínio não encontrado");
        }

        // 11. RN-80: Target platform_role == NONE; RN-81: Target != caller
        if (targetUser.PlatformRole != PlatformRole.NONE || targetUser.Id == authContext.UserId!.Value)
        {
            throw new ApiErrorException("ERR-AUTH-010", "Operação de impersonation não autorizada");
        }

        // 12. RN-82: LOCKED and not expired; RN-83: SUSPENDED or denylist:user tRef = 0
        if (targetUser.Status == UserStatus.LOCKED && targetUser.LockedUntil > now)
        {
            throw new ApiErrorException("ERR-AUTH-004", "Utilizador suspenso ou revogado globalmente");
        }

        if (targetUser.Status == UserStatus.SUSPENDED)
        {
            throw new ApiErrorException("ERR-AUTH-004", "Utilizador suspenso ou revogado globalmente");
        }

        var userCheck = await accessQueries.CheckUserAsync(requestData.TargetUserId, 0, cancellationToken);
        if (!userCheck.Ok)
        {
            throw new ApiErrorException(userCheck.ErrorCode ?? "ERR-AUTH-004");
        }

        // 13. RN-84: Target tenant and group active
        var tenantCheck = await accessQueries.CheckTenantAsync(requestData.TargetTenantId, cancellationToken);
        if (!tenantCheck.Ok)
        {
            throw new ApiErrorException(tenantCheck.ErrorCode ?? "ERR-CTX-002");
        }

        // 14. RN-85: Target member active
        var memberCheck = await accessQueries.CheckMembershipAsync(requestData.TargetUserId, requestData.TargetTenantId, cancellationToken);
        if (!memberCheck.Ok)
        {
            throw new ApiErrorException(memberCheck.ErrorCode ?? "ERR-CTX-001");
        }

        // 15. RN-86: Target scope active and roles not empty
        var rolesCheck = await accessQueries.ResolveRolesAsync(requestData.TargetUserId, requestData.TargetTenantId, requestData.TargetScope, cancellationToken);
        if (!rolesCheck.Ok)
        {
            throw new ApiErrorException(rolesCheck.ErrorCode ?? "ERR-CTX-003");
        }

        // 16. RN-14: Issue JWT
        var claims = new AccessTokenClaims(
            Sub: requestData.TargetUserId,
            FamilyId: null, // Impersonation tokens omit family_id
            PlatformRole: targetUser.PlatformRole,
            ViewMode: ViewMode.SINGLE,
            GroupId: tenantInfo.GroupId,
            TenantId: requestData.TargetTenantId,
            AllowedTenants: [requestData.TargetTenantId],
            ActiveScope: requestData.TargetScope,
            Roles: rolesCheck.Value!.Roles,
            Permissions: rolesCheck.Value!.Permissions,
            Act: new ActorClaim(authContext.UserId!.Value, requestData.TicketId)
        );

        var token = tokenIssuer.Issue(claims);

        // RN-87: Audit IMPERSONATION_STARTED
        auditLog.Append(
            AuditEventType.IMPERSONATION_STARTED,
            requestData.TargetUserId,
            authContext.UserId,
            requestData.TargetTenantId,
            tenantInfo.GroupId,
            requestData.TicketId,
            null);

        await dbContext.SaveChangesAsync(cancellationToken);

        httpContext.Response.Headers.CacheControl = "no-store";
        httpContext.Response.Headers.Pragma = "no-cache";

        var responsePayload = new
        {
            access_token = token.AccessToken,
            token_type = "Bearer",
            expires_in = 900,
            impersonation_context = new
            {
                sub = requestData.TargetUserId.ToString("D"),
                group_id = tenantInfo.GroupId.ToString("D"),
                tenant_id = requestData.TargetTenantId.ToString("D"),
                allowed_tenants = new[] { requestData.TargetTenantId.ToString("D") },
                view_mode = "SINGLE",
                active_scope = requestData.TargetScope.ToString(),
                roles = rolesCheck.Value!.Roles,
                permissions = rolesCheck.Value!.Permissions,
                act = new
                {
                    sub = authContext.UserId!.Value.ToString("D"),
                    ticket_id = requestData.TicketId
                }
            }
        };

        return Results.Ok(responsePayload);
    }

    private record ImpersonateRequest(
        Guid TargetUserId,
        Guid TargetTenantId,
        OperationalScope TargetScope,
        string TicketId);

    private static async Task<ImpersonateRequest> ReadAndValidateJsonAsync(HttpRequest request)
    {
        request.EnableBuffering();
        using var reader = new StreamReader(request.Body, leaveOpen: true);
        var jsonText = await reader.ReadToEndAsync();
        request.Body.Position = 0;

        if (string.IsNullOrWhiteSpace(jsonText))
        {
            throw new ApiErrorException("ERR-VAL-001", "Pedido inválido ou malformado");
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

        var allowedKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "target_user_id",
            "target_tenant_id",
            "target_scope",
            "ticket_id"
        };

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var prop in root.EnumerateObject())
        {
            if (!keys.Add(prop.Name))
            {
                throw new ApiErrorException("ERR-VAL-001", "Pedido inválido ou malformado");
            }

            if (!allowedKeys.Contains(prop.Name))
            {
                throw new ApiErrorException("ERR-VAL-001", "Pedido inválido ou malformado");
            }

            if (prop.Value.ValueKind != JsonValueKind.String)
            {
                throw new ApiErrorException("ERR-VAL-001", "Pedido inválido ou malformado");
            }

            if (prop.Value.GetString()!.Contains('\0'))
            {
                throw new ApiErrorException("ERR-VAL-001", "Pedido inválido ou malformado");
            }
        }

        if (!keys.Contains("target_user_id") ||
            !keys.Contains("target_tenant_id") ||
            !keys.Contains("target_scope") ||
            !keys.Contains("ticket_id"))
        {
            throw new ApiErrorException("ERR-VAL-001", "Pedido inválido ou malformado");
        }

        var targetUserIdStr = root.GetProperty("target_user_id").GetString()!;
        if (!Guid.TryParseExact(targetUserIdStr, "D", out var targetUserId))
        {
            throw new ApiErrorException("ERR-VAL-001", "Pedido inválido ou malformado");
        }

        var targetTenantIdStr = root.GetProperty("target_tenant_id").GetString()!;
        if (!Guid.TryParseExact(targetTenantIdStr, "D", out var targetTenantId))
        {
            throw new ApiErrorException("ERR-VAL-001", "Pedido inválido ou malformado");
        }

        var targetScopeStr = root.GetProperty("target_scope").GetString()!;
        if (!Enum.TryParse<OperationalScope>(targetScopeStr, false, out var targetScope))
        {
            throw new ApiErrorException("ERR-VAL-001", "Pedido inválido ou malformado");
        }

        var ticketId = root.GetProperty("ticket_id").GetString()!;
        if (!TicketIdRegex.IsMatch(ticketId))
        {
            throw new ApiErrorException("ERR-VAL-004", "Código de chamado técnico inválido");
        }

        return new ImpersonateRequest(targetUserId, targetTenantId, targetScope, ticketId);
    }
}
