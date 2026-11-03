using System.Text.Json;
using IdentityService.Modules.M00Transversal.Audit;
using IdentityService.Modules.M00Transversal.Auth;
using IdentityService.Modules.M00Transversal.Errors;
using IdentityService.Modules.M00Transversal.Redis;
using IdentityService.Modules.M02Refresh.Interface;
using IdentityService.Modules.M07Chaves.Interface;
using IdentityService.Modules.M08Acessos.Interface;
using IdentityService.Modules.M11Esquema.Persistence;
using IdentityService.Modules.M11Esquema.Persistence.Enums;
using Microsoft.AspNetCore.Http;

namespace IdentityService.Modules.M03Contexto;

public static class SwitchContextEndpoint
{
    public static async Task<IResult> HandleAsync(
        HttpContext httpContext,
        IdentityDbContext dbContext,
        IAuthContext authContext,
        ISessionStore sessionStore,
        ITokenIssuer tokenIssuer,
        IAccessQueries accessQueries,
        IAuditLog auditLog,
        IDenylist denylist,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        // RN-51: Reject tokens with act claim
        if (authContext.ActorUserId != null || authContext.TicketId != null)
        {
            throw new ApiErrorException("ERR-CTX-007", "Violação de isolamento de contexto ou permissão insuficiente");
        }

        var requestData = await ReadAndValidateJsonAsync(httpContext.Request);

        // Layer 6: Business logic
        var userCheck = await accessQueries.CheckUserAsync(authContext.UserId!.Value, authContext.Iat, cancellationToken);
        if (!userCheck.Ok)
        {
            throw new ApiErrorException(userCheck.ErrorCode ?? "ERR-AUTH-004");
        }

        Guid groupId;
        Guid? activeTenantId;
        IReadOnlyList<Guid> allowedTenants;
        IReadOnlyList<string> roles;
        IReadOnlyList<string> permissions;

        if (requestData.ViewMode == ViewMode.SINGLE)
        {
            var targetTenantId = requestData.TargetTenantId!.Value;
            var tenantInfo = await accessQueries.ResolveTenantAsync(targetTenantId, cancellationToken);
            if (tenantInfo == null)
            {
                // PRS-58: Non-existent tenant fails RG-03 -> 403 ERR-CTX-001
                throw new ApiErrorException("ERR-CTX-001");
            }

            var tenantCheck = await accessQueries.CheckTenantAsync(targetTenantId, cancellationToken);
            if (!tenantCheck.Ok)
            {
                throw new ApiErrorException(tenantCheck.ErrorCode ?? "ERR-CTX-002");
            }

            var memberCheck = await accessQueries.CheckMembershipAsync(authContext.UserId!.Value, targetTenantId, cancellationToken);
            if (!memberCheck.Ok)
            {
                throw new ApiErrorException(memberCheck.ErrorCode ?? "ERR-CTX-001");
            }

            var rolesCheck = await accessQueries.ResolveRolesAsync(authContext.UserId!.Value, targetTenantId, requestData.TargetScope, cancellationToken);
            if (!rolesCheck.Ok)
            {
                throw new ApiErrorException(rolesCheck.ErrorCode ?? "ERR-CTX-003");
            }

            groupId = tenantInfo.GroupId;
            activeTenantId = targetTenantId;
            allowedTenants = [targetTenantId];
            roles = rolesCheck.Value!.Roles;
            permissions = rolesCheck.Value!.Permissions;
        }
        else // MACRO_GROUP
        {
            var targetGroupId = requestData.TargetGroupId!.Value;
            var targetTenants = requestData.TargetTenants!;

            var macroCheck = await accessQueries.ResolveMacroGroupAsync(authContext.UserId!.Value, targetGroupId, targetTenants, requestData.TargetScope, cancellationToken);
            if (!macroCheck.Ok)
            {
                throw new ApiErrorException(macroCheck.ErrorCode ?? "ERR-CTX-004");
            }

            groupId = targetGroupId;
            activeTenantId = null;
            allowedTenants = macroCheck.Value!.AllowedTenants;
            roles = macroCheck.Value!.Roles;
            permissions = macroCheck.Value!.Permissions;
        }

        // Transaction to serialize concurrent switch-context requests and ensure atomic revocation (CA-03.4, A-132)
        await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // RN-54: Update active context in session store (locks row FOR UPDATE)
        var updateOk = await sessionStore.UpdateActiveContextAsync(
            authContext.FamilyId!.Value,
            requestData.ViewMode,
            groupId,
            activeTenantId,
            allowedTenants,
            requestData.TargetScope,
            cancellationToken);

        if (!updateOk)
        {
            await tx.RollbackAsync(cancellationToken);
            // PRS-73: Family without ACTIVE line -> 401 ERR-AUTH-002
            throw new ApiErrorException("ERR-AUTH-002", "Token de acesso ausente, inválido, expirado ou revogado");
        }

        // Check if jti was already revoked by a concurrent request while waiting for the lock
        if (await denylist.IsJtiRevokedAsync(authContext.Jti!, cancellationToken))
        {
            await tx.RollbackAsync(cancellationToken);
            throw new ApiErrorException("ERR-AUTH-002", "Token de acesso ausente, inválido, expirado ou revogado");
        }

        // RN-55: Revoke previous jti in Redis before commit
        await denylist.RevokeJtiAsync(authContext.Jti!, authContext.Exp!.Value, cancellationToken);

        // Audit log CONTEXT_SWITCHED
        auditLog.Append(
            AuditEventType.CONTEXT_SWITCHED,
            authContext.UserId,
            null,
            activeTenantId,
            groupId,
            null,
            new
            {
                view_mode = requestData.ViewMode.ToString(),
                active_scope = requestData.TargetScope.ToString()
            });

        await dbContext.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        // Issue new JWT with same fid
        var claims = new AccessTokenClaims(
            Sub: authContext.UserId!.Value,
            FamilyId: authContext.FamilyId!.Value,
            PlatformRole: authContext.PlatformRole!.Value,
            ViewMode: requestData.ViewMode,
            GroupId: groupId,
            TenantId: activeTenantId,
            AllowedTenants: allowedTenants,
            ActiveScope: requestData.TargetScope,
            Roles: roles,
            Permissions: permissions,
            Act: null
        );

        var issuedToken = tokenIssuer.Issue(claims);

        httpContext.Response.Headers.CacheControl = "no-store";
        httpContext.Response.Headers.Pragma = "no-cache";

        var responsePayload = new
        {
            access_token = issuedToken.AccessToken,
            token_type = "Bearer",
            expires_in = 900,
            context = new
            {
                sub = authContext.UserId!.Value.ToString("D"),
                group_id = groupId.ToString("D"),
                tenant_id = activeTenantId?.ToString("D"),
                allowed_tenants = allowedTenants.Select(t => t.ToString("D")).ToArray(),
                view_mode = requestData.ViewMode.ToString(),
                active_scope = requestData.TargetScope.ToString(),
                roles = roles,
                permissions = permissions
            }
        };

        return Results.Ok(responsePayload);
    }

    private record SwitchContextRequest(
        ViewMode ViewMode,
        OperationalScope TargetScope,
        Guid? TargetTenantId,
        Guid? TargetGroupId,
        IReadOnlyList<Guid>? TargetTenants);

    private static async Task<SwitchContextRequest> ReadAndValidateJsonAsync(HttpRequest request)
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
            "view_mode",
            "target_scope",
            "target_tenant_id",
            "target_group_id",
            "target_tenants"
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

            if (prop.Value.ValueKind == JsonValueKind.String && prop.Value.GetString()!.Contains('\0'))
            {
                throw new ApiErrorException("ERR-VAL-001", "Pedido inválido ou malformado");
            }
        }

        if (!keys.Contains("view_mode") || !keys.Contains("target_scope"))
        {
            throw new ApiErrorException("ERR-VAL-001", "Pedido inválido ou malformado");
        }

        if (root.GetProperty("view_mode").ValueKind != JsonValueKind.String ||
            root.GetProperty("target_scope").ValueKind != JsonValueKind.String)
        {
            throw new ApiErrorException("ERR-VAL-001", "Pedido inválido ou malformado");
        }

        var viewModeStr = root.GetProperty("view_mode").GetString()!;
        if (viewModeStr == "NONE" || !Enum.TryParse<ViewMode>(viewModeStr, false, out var viewMode))
        {
            throw new ApiErrorException("ERR-VAL-001", "Pedido inválido ou malformado");
        }

        var targetScopeStr = root.GetProperty("target_scope").GetString()!;
        if (!Enum.TryParse<OperationalScope>(targetScopeStr, false, out var targetScope))
        {
            throw new ApiErrorException("ERR-VAL-001", "Pedido inválido ou malformado");
        }

        if (viewMode == ViewMode.SINGLE)
        {
            if (!keys.Contains("target_tenant_id") || root.GetProperty("target_tenant_id").ValueKind != JsonValueKind.String)
            {
                throw new ApiErrorException("ERR-VAL-001", "Pedido inválido ou malformado");
            }

            var tenantIdStr = root.GetProperty("target_tenant_id").GetString()!;
            if (!Guid.TryParseExact(tenantIdStr, "D", out var targetTenantId))
            {
                throw new ApiErrorException("ERR-VAL-001", "Pedido inválido ou malformado");
            }

            if (keys.Contains("target_group_id") && root.GetProperty("target_group_id").ValueKind != JsonValueKind.Null)
            {
                throw new ApiErrorException("ERR-VAL-001", "Pedido inválido ou malformado");
            }

            if (keys.Contains("target_tenants") && root.GetProperty("target_tenants").ValueKind != JsonValueKind.Null)
            {
                throw new ApiErrorException("ERR-VAL-001", "Pedido inválido ou malformado");
            }

            return new SwitchContextRequest(viewMode, targetScope, targetTenantId, null, null);
        }
        else // MACRO_GROUP
        {
            if (!keys.Contains("target_group_id") || root.GetProperty("target_group_id").ValueKind != JsonValueKind.String)
            {
                throw new ApiErrorException("ERR-VAL-001", "Pedido inválido ou malformado");
            }

            var groupIdStr = root.GetProperty("target_group_id").GetString()!;
            if (!Guid.TryParseExact(groupIdStr, "D", out var targetGroupId))
            {
                throw new ApiErrorException("ERR-VAL-001", "Pedido inválido ou malformado");
            }

            if (!keys.Contains("target_tenants") || root.GetProperty("target_tenants").ValueKind != JsonValueKind.Array)
            {
                throw new ApiErrorException("ERR-VAL-001", "Pedido inválido ou malformado");
            }

            if (keys.Contains("target_tenant_id") && root.GetProperty("target_tenant_id").ValueKind != JsonValueKind.Null)
            {
                throw new ApiErrorException("ERR-VAL-001", "Pedido inválido ou malformado");
            }

            var tenantsList = new List<Guid>();
            foreach (var elem in root.GetProperty("target_tenants").EnumerateArray())
            {
                if (elem.ValueKind != JsonValueKind.String)
                {
                    throw new ApiErrorException("ERR-VAL-001", "Pedido inválido ou malformado");
                }

                var elemStr = elem.GetString()!;
                if (elemStr.Contains('\0') || !Guid.TryParseExact(elemStr, "D", out var parsedGuid))
                {
                    throw new ApiErrorException("ERR-VAL-001", "Pedido inválido ou malformado");
                }

                tenantsList.Add(parsedGuid);
            }

            return new SwitchContextRequest(viewMode, targetScope, null, targetGroupId, tenantsList);
        }
    }
}
