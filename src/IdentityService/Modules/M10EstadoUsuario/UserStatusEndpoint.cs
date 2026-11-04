using System.Text.Json;
using IdentityService.Modules.M00Transversal.Audit;
using IdentityService.Modules.M00Transversal.Auth;
using IdentityService.Modules.M00Transversal.Errors;
using IdentityService.Modules.M00Transversal.Redis;
using IdentityService.Modules.M01Login.Interface;
using IdentityService.Modules.M02Refresh.Interface;
using IdentityService.Modules.M05Membros.Interface;
using IdentityService.Modules.M08Acessos.Interface;
using IdentityService.Modules.M11Esquema.Persistence;
using IdentityService.Modules.M11Esquema.Persistence.Enums;
using IdentityService.Modules.M14Eventos.Interface;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace IdentityService.Modules.M10EstadoUsuario;

public static class UserStatusEndpoint
{
    private static readonly HashSet<string> AllowedKeys = new(StringComparer.Ordinal)
    {
        "status",
        "reason"
    };

    public static async Task<IResult> HandleAsync(
        string user_id,
        HttpContext httpContext,
        IdentityDbContext dbContext,
        IAuthContext authContext,
        IUserAccountStatusWriter userAccountStatusWriter,
        IMembershipWriter membershipWriter,
        IAccessQueries accessQueries,
        ISessionStore sessionStore,
        IDenylist denylist,
        IEventOutbox eventOutbox,
        IAuditLog auditLog,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        // Layer 3 (b): Validate path parameter user_id
        if (!Guid.TryParseExact(user_id, "D", out var targetUserId))
        {
            throw new ApiErrorException("ERR-VAL-001", "Pedido inválido ou malformado");
        }

        var (statusStr, trimmedReason) = await ReadAndValidateRequestAsync(httpContext.Request, cancellationToken);

        // Layer 4: Authorization (RN-121, 40 §5)
        var isPlatformAdmin = authContext.PlatformRole == PlatformRole.PLATFORM_ADMIN;

        if (!isPlatformAdmin)
        {
            var isTenantAdmin = authContext.ActiveScope == OperationalScope.TENANT_ADMIN &&
                                authContext.Permissions.Contains("usuario:suspender") &&
                                authContext.UserId.HasValue &&
                                targetUserId != authContext.UserId.Value &&
                                authContext.GroupId.HasValue;

            if (!isTenantAdmin)
            {
                throw new ApiErrorException("ERR-CTX-007", "Violação de isolamento de contexto ou permissão insuficiente");
            }

            var callerGroupId = authContext.GroupId!.Value;

            // Check target user in user_account: must exist and have platform_role = NONE
            var targetUser = await dbContext.UserAccounts
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == targetUserId, cancellationToken);

            if (targetUser == null || targetUser.PlatformRole != PlatformRole.NONE)
            {
                // Non-existent target or platform role != NONE makes C9 false -> 403 ERR-CTX-007
                throw new ApiErrorException("ERR-CTX-007", "Violação de isolamento de contexto ou permissão insuficiente");
            }

            // Check group membership kind via IF-09.7 (must be MEMBER, not OWNER or NONE)
            var membershipKind = await accessQueries.GetGroupMembershipAsync(
                targetUserId,
                callerGroupId,
                cancellationToken);

            if (membershipKind != GroupMembershipKind.MEMBER)
            {
                // OWNER or NONE makes C9 false -> 403 ERR-CTX-007
                throw new ApiErrorException("ERR-CTX-007", "Violação de isolamento de contexto ou permissão insuficiente");
            }
        }

        // Layer 5 & 6: Existence and Business execution (RN-122 to RN-129)
        if (isPlatformAdmin)
        {
            var targetStatus = Enum.Parse<UserStatus>(statusStr);

            await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var now = timeProvider.GetUtcNow();

            var previousStatus = await userAccountStatusWriter.SetAdministrativeStatusAsync(
                targetUserId,
                targetStatus,
                cancellationToken);

            if (previousStatus == null)
            {
                throw new ApiErrorException("ERR-CTX-008", "Recurso de domínio não encontrado");
            }

            if (targetStatus == UserStatus.SUSPENDED)
            {
                // Revoke all refresh families of the user across all tenants (IF-01.4)
                await sessionStore.RevokeByUserAsync(targetUserId, cancellationToken);

                // Put user in Redis denylist:user (IF-04.2)
                await denylist.BlockUserAsync(targetUserId, cancellationToken);

                // Enqueue EV-03 usuario_bloqueado (IF-07.4)
                eventOutbox.EnqueueUserSuspended(targetUserId, trimmedReason, now);
            }

            // Audit log USER_STATUS_CHANGED (IF-03.1)
            auditLog.Append(
                AuditEventType.USER_STATUS_CHANGED,
                userId: targetUserId,
                actorUserId: authContext.UserId,
                tenantId: null,
                groupId: null,
                ticketId: null,
                metadata: new
                {
                    status = targetStatus.ToString(),
                    reason = trimmedReason,
                    scope = "GLOBAL"
                });

            await dbContext.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            return Results.Ok(new
            {
                user_id = targetUserId.ToString("D"),
                status = targetStatus.ToString(),
                scope = "GLOBAL",
                group_id = (string?)null,
                updated_at = now.ToString("yyyy-MM-ddTHH:mm:ssZ")
            });
        }
        else
        {
            var targetStatus = Enum.Parse<EntityStatus>(statusStr);
            var callerGroupId = authContext.GroupId!.Value;

            await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var now = timeProvider.GetUtcNow();

            var affectedTenants = await membershipWriter.SetGroupMembershipsStatusAsync(
                targetUserId,
                callerGroupId,
                targetStatus,
                cancellationToken);

            if (targetStatus == EntityStatus.SUSPENDED && affectedTenants.Count > 0)
            {
                await sessionStore.RevokeByUserAndTenantsAsync(
                    targetUserId,
                    affectedTenants,
                    cancellationToken);
            }

            // Audit log USER_STATUS_CHANGED (IF-03.1)
            auditLog.Append(
                AuditEventType.USER_STATUS_CHANGED,
                userId: targetUserId,
                actorUserId: authContext.UserId,
                tenantId: null,
                groupId: callerGroupId,
                ticketId: null,
                metadata: new
                {
                    status = targetStatus.ToString(),
                    reason = trimmedReason,
                    scope = "GROUP"
                });

            await dbContext.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            return Results.Ok(new
            {
                user_id = targetUserId.ToString("D"),
                status = targetStatus.ToString(),
                scope = "GROUP",
                group_id = callerGroupId.ToString("D"),
                updated_at = now.ToString("yyyy-MM-ddTHH:mm:ssZ")
            });
        }
    }

    private static async Task<(string Status, string Reason)> ReadAndValidateRequestAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        request.EnableBuffering();
        using var reader = new StreamReader(request.Body, leaveOpen: true);
        var jsonText = await reader.ReadToEndAsync(cancellationToken);
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

        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var prop in root.EnumerateObject())
        {
            if (!seenKeys.Add(prop.Name))
            {
                throw new ApiErrorException("ERR-VAL-001", "Pedido inválido ou malformado");
            }

            if (!AllowedKeys.Contains(prop.Name))
            {
                throw new ApiErrorException("ERR-VAL-001", "Pedido inválido ou malformado");
            }

            if (prop.Value.ValueKind == JsonValueKind.Null)
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

        if (seenKeys.Count != 2)
        {
            throw new ApiErrorException("ERR-VAL-001", "Pedido inválido ou malformado");
        }

        var statusStr = root.GetProperty("status").GetString();
        if (statusStr is not ("ACTIVE" or "SUSPENDED"))
        {
            throw new ApiErrorException("ERR-VAL-001", "Pedido inválido ou malformado");
        }

        var reasonStr = root.GetProperty("reason").GetString()!;
        var trimmedReason = reasonStr.Trim();
        if (trimmedReason.Length is < 5 or > 255)
        {
            throw new ApiErrorException("ERR-VAL-001", "Pedido inválido ou malformado");
        }

        return (statusStr, trimmedReason);
    }
}
