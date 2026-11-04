using System.Data;
using System.Text.Json;
using IdentityService.Modules.M00Transversal.Audit;
using IdentityService.Modules.M00Transversal.Auth;
using IdentityService.Modules.M00Transversal.Errors;
using IdentityService.Modules.M11Esquema.Persistence;
using IdentityService.Modules.M11Esquema.Persistence.Entities;
using IdentityService.Modules.M11Esquema.Persistence.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace IdentityService.Modules.M05Membros;

public static class AddMemberEndpoint
{
    private static readonly HashSet<string> AllowedKeys = new(StringComparer.Ordinal)
    {
        "user_id",
        "is_owner",
        "can_view_group",
        "role_ids"
    };

    public static async Task<IResult> HandleAsync(
        string tenant_id,
        HttpContext httpContext,
        IdentityDbContext dbContext,
        IAuthContext authContext,
        IAuditLog auditLog,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        // Layer 3 (b): Validate path parameter tenant_id
        if (!Guid.TryParseExact(tenant_id, "D", out var tenantId))
        {
            throw new ApiErrorException("ERR-VAL-001", "Pedido inválido ou malformado");
        }

        var (userId, isOwner, canViewGroup, roleIds) =
            await ReadAndValidateRequestAsync(httpContext.Request);

        // Layer 4: Authorization (RN-71)
        var isPlatformAdmin = authContext.PlatformRole == PlatformRole.PLATFORM_ADMIN;
        var isTenantAdmin = authContext.ActiveScope == OperationalScope.TENANT_ADMIN &&
                            authContext.Permissions.Contains("membro:vincular") &&
                            authContext.AllowedTenants.Contains(tenantId);

        if (!isPlatformAdmin && !isTenantAdmin)
        {
            throw new ApiErrorException("ERR-CTX-007", "Violação de isolamento de contexto ou permissão insuficiente");
        }

        // PRS-51: AuthorizeTenantWrite before opening transaction connection
        var conn = dbContext.Database.GetDbConnection();
        if (conn.State == ConnectionState.Open)
        {
            await dbContext.Database.CloseConnectionAsync();
        }

        authContext.AuthorizeTenantWrite(tenantId);

        // Layer 5: Existence (RN-72)
        var tenant = await dbContext.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);

        if (tenant == null)
        {
            throw new ApiErrorException("ERR-CTX-008", "Recurso de domínio não encontrado");
        }

        var user = await dbContext.UserAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user == null)
        {
            throw new ApiErrorException("ERR-CTX-008", "Recurso de domínio não encontrado");
        }

        var roles = await dbContext.Roles
            .AsNoTracking()
            .Where(r => roleIds.Contains(r.Id))
            .ToListAsync(cancellationToken);

        if (roles.Count != roleIds.Count)
        {
            throw new ApiErrorException("ERR-CTX-008", "Recurso de domínio não encontrado");
        }

        // Layer 6: Business rules
        // RN-73: Uniqueness of (tenant_id, user_id)
        var alreadyMember = await dbContext.TenantMembers
            .AsNoTracking()
            .AnyAsync(m => m.TenantId == tenantId && m.UserId == userId, cancellationToken);

        if (alreadyMember)
        {
            throw new ApiErrorException("ERR-CTX-005", "Conflito de unicidade de registo");
        }

        // RN-74: Active scopes in tenant matching each role's scope
        var activeScopes = await dbContext.TenantScopes
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.IsActive)
            .Select(s => s.Scope)
            .ToListAsync(cancellationToken);

        var activeScopesSet = activeScopes.ToHashSet();

        if (roles.Any(r => !activeScopesSet.Contains(r.Scope)))
        {
            throw new ApiErrorException("ERR-CTX-006", "Papel incompatível com os escopos ativos do Tenant");
        }

        var now = timeProvider.GetUtcNow();
        var memberId = Guid.CreateVersion7(now);

        await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var member = new TenantMember
        {
            Id = memberId,
            TenantId = tenantId,
            UserId = userId,
            IsOwner = isOwner,
            CanViewGroup = canViewGroup,
            Status = EntityStatus.ACTIVE,
            CreatedAt = now,
            UpdatedAt = now
        };
        dbContext.TenantMembers.Add(member);

        foreach (var roleId in roleIds)
        {
            dbContext.MemberRoles.Add(new MemberRole
            {
                Id = Guid.CreateVersion7(now),
                TenantId = tenantId,
                TenantMemberId = memberId,
                RoleId = roleId,
                CreatedAt = now
            });
        }

        // Audit log (AUD.M05)
        auditLog.Append(
            AuditEventType.MEMBER_ADDED,
            userId,
            authContext.UserId,
            tenantId,
            tenant.GroupId,
            null,
            new { });

        await dbContext.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        var sortedRoles = roles
            .Select(r => r.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        var responsePayload = new
        {
            member_id = memberId.ToString("D"),
            tenant_id = tenantId.ToString("D"),
            user_id = userId.ToString("D"),
            is_owner = isOwner,
            can_view_group = canViewGroup,
            status = "ACTIVE",
            roles = sortedRoles,
            created_at = now.ToString("yyyy-MM-ddTHH:mm:ssZ")
        };

        return Results.Created($"/api/v1/tenants/{tenantId:D}/members/{memberId:D}", responsePayload);
    }

    private static async Task<(Guid UserId, bool IsOwner, bool CanViewGroup, List<Guid> RoleIds)>
        ReadAndValidateRequestAsync(HttpRequest request)
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

            if (prop.Value.ValueKind == JsonValueKind.String && prop.Value.GetString()!.Contains('\0'))
            {
                throw new ApiErrorException("ERR-VAL-001", "Pedido inválido ou malformado");
            }
        }

        if (seenKeys.Count != 4)
        {
            throw new ApiErrorException("ERR-VAL-001", "Pedido inválido ou malformado");
        }

        // Layer 3 (b): Structural format checks
        var userIdProp = root.GetProperty("user_id");
        if (userIdProp.ValueKind != JsonValueKind.String || !Guid.TryParseExact(userIdProp.GetString()!, "D", out var userId))
        {
            throw new ApiErrorException("ERR-VAL-001", "Pedido inválido ou malformado");
        }

        var isOwnerProp = root.GetProperty("is_owner");
        if (isOwnerProp.ValueKind != JsonValueKind.True && isOwnerProp.ValueKind != JsonValueKind.False)
        {
            throw new ApiErrorException("ERR-VAL-001", "Pedido inválido ou malformado");
        }
        var isOwner = isOwnerProp.GetBoolean();

        var canViewGroupProp = root.GetProperty("can_view_group");
        if (canViewGroupProp.ValueKind != JsonValueKind.True && canViewGroupProp.ValueKind != JsonValueKind.False)
        {
            throw new ApiErrorException("ERR-VAL-001", "Pedido inválido ou malformado");
        }
        var canViewGroup = canViewGroupProp.GetBoolean();

        var roleIdsProp = root.GetProperty("role_ids");
        if (roleIdsProp.ValueKind != JsonValueKind.Array)
        {
            throw new ApiErrorException("ERR-VAL-001", "Pedido inválido ou malformado");
        }

        var count = roleIdsProp.GetArrayLength();
        if (count is < 1 or > 10)
        {
            throw new ApiErrorException("ERR-VAL-001", "Pedido inválido ou malformado");
        }

        var roleIds = new List<Guid>();
        var seenRoleIds = new HashSet<Guid>();
        foreach (var elem in roleIdsProp.EnumerateArray())
        {
            if (elem.ValueKind != JsonValueKind.String)
            {
                throw new ApiErrorException("ERR-VAL-001", "Pedido inválido ou malformado");
            }
            var str = elem.GetString()!;
            if (str.Contains('\0') || !Guid.TryParseExact(str, "D", out var rId))
            {
                throw new ApiErrorException("ERR-VAL-001", "Pedido inválido ou malformado");
            }
            if (!seenRoleIds.Add(rId))
            {
                throw new ApiErrorException("ERR-VAL-001", "Pedido inválido ou malformado");
            }
            roleIds.Add(rId);
        }

        return (userId, isOwner, canViewGroup, roleIds);
    }
}
