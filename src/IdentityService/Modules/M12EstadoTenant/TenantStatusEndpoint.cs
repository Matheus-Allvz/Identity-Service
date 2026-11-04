using System.Text.Json;
using IdentityService.Modules.M00Transversal.Audit;
using IdentityService.Modules.M00Transversal.Auth;
using IdentityService.Modules.M00Transversal.Errors;
using IdentityService.Modules.M00Transversal.Redis;
using IdentityService.Modules.M02Refresh.Interface;
using IdentityService.Modules.M04Tenants.Interface;
using IdentityService.Modules.M11Esquema.Persistence;
using IdentityService.Modules.M11Esquema.Persistence.Enums;
using IdentityService.Modules.M14Eventos.Interface;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace IdentityService.Modules.M12EstadoTenant;

public static class TenantStatusEndpoint
{
    private static readonly HashSet<string> AllowedKeys = new(StringComparer.Ordinal)
    {
        "status",
        "reason"
    };

    public static async Task<IResult> HandleAsync(
        string tenant_id,
        HttpContext httpContext,
        IdentityDbContext dbContext,
        IAuthContext authContext,
        ITenantStatusWriter tenantStatusWriter,
        ISessionStore sessionStore,
        IDenylist denylist,
        IEventOutbox eventOutbox,
        IAuditLog auditLog,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        // Layer 3 (b): Validate path parameter tenant_id
        if (!Guid.TryParseExact(tenant_id, "D", out var tenantId))
        {
            throw new ApiErrorException("ERR-VAL-001", "Pedido inválido ou malformado");
        }

        var (targetStatus, trimmedReason) = await ReadAndValidateRequestAsync(httpContext.Request, cancellationToken);

        // Layer 4: Authorization (RN-144)
        var isPlatformAdmin = authContext.PlatformRole == PlatformRole.PLATFORM_ADMIN;
        var isAuthorized = isPlatformAdmin;

        if (!isAuthorized)
        {
            if (authContext.ActiveScope == OperationalScope.TENANT_ADMIN &&
                authContext.AllowedTenants.Contains(tenantId) &&
                authContext.UserId.HasValue)
            {
                var isOwner = await dbContext.TenantMembers
                    .AsNoTracking()
                    .AnyAsync(m => m.TenantId == tenantId &&
                                   m.UserId == authContext.UserId.Value &&
                                   m.IsOwner, cancellationToken);

                isAuthorized = isOwner;
            }
        }

        if (!isAuthorized)
        {
            throw new ApiErrorException("ERR-CTX-007", "Violação de isolamento de contexto ou permissão insuficiente");
        }

        // Layer 5 & 6: Existence & Business execution (RN-142, RN-143, RN-145)
        await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var statusChange = await tenantStatusWriter.SetStatusAsync(tenantId, targetStatus, cancellationToken);
        if (statusChange == null)
        {
            throw new ApiErrorException("ERR-CTX-008", "Recurso de domínio não encontrado");
        }

        if (targetStatus == EntityStatus.SUSPENDED)
        {
            // (1) SET denylist:tenant:{id} "1" (TTL 900 s) in Redis before commit (IF-04.3, RN-142)
            await denylist.BlockTenantAsync(tenantId, cancellationToken);

            // (2) Revoke sessions in DB (IF-01.5)
            await sessionStore.RevokeByTenantAsync(tenantId, cancellationToken);

            // (3) Enqueue EV-02 tenant_bloqueado (IF-07.2)
            eventOutbox.EnqueueTenantBlocked(new TenantBlockedPayload(
                TenantId: tenantId,
                GroupId: statusChange.GroupId,
                Cnpj: statusChange.Cnpj,
                Reason: trimmedReason,
                BlockedByUserId: authContext.UserId!.Value,
                BlockedAt: statusChange.UpdatedAt
            ));

            // (4) Audit log TENANT_STATUS_CHANGED (IF-03.1)
            auditLog.Append(
                AuditEventType.TENANT_STATUS_CHANGED,
                userId: authContext.UserId,
                actorUserId: null,
                tenantId: tenantId,
                groupId: statusChange.GroupId,
                ticketId: null,
                metadata: new
                {
                    status = targetStatus.ToString(),
                    reason = trimmedReason
                });

            await dbContext.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        else // ACTIVE
        {
            // Audit log TENANT_STATUS_CHANGED (IF-03.1)
            auditLog.Append(
                AuditEventType.TENANT_STATUS_CHANGED,
                userId: authContext.UserId,
                actorUserId: null,
                tenantId: tenantId,
                groupId: statusChange.GroupId,
                ticketId: null,
                metadata: new
                {
                    status = targetStatus.ToString(),
                    reason = trimmedReason
                });

            await dbContext.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            // DEL denylist:tenant:{id} in Redis ONLY IF Previous == SUSPENDED (PRS-15, RN-143)
            if (statusChange.Previous == EntityStatus.SUSPENDED)
            {
                try
                {
                    await denylist.UnblockTenantAsync(tenantId, cancellationToken);
                }
                catch (Exception)
                {
                    // EX-12.5: Redis failure post-commit -> 200 OK returned, key expires by TTL
                }
            }
        }

        return Results.Ok(new
        {
            tenant_id = tenantId.ToString("D"),
            status = targetStatus.ToString(),
            updated_at = statusChange.UpdatedAt.ToString("yyyy-MM-ddTHH:mm:ssZ")
        });
    }

    private static async Task<(EntityStatus Status, string Reason)> ReadAndValidateRequestAsync(
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
        var status = Enum.Parse<EntityStatus>(statusStr);

        var reasonStr = root.GetProperty("reason").GetString()!;
        var trimmedReason = reasonStr.Trim();
        if (trimmedReason.Length is < 5 or > 255)
        {
            throw new ApiErrorException("ERR-VAL-001", "Pedido inválido ou malformado");
        }

        return (status, trimmedReason);
    }
}
