using System.Data;
using System.Text.Json;
using IdentityService.Modules.M00Transversal.Audit;
using IdentityService.Modules.M00Transversal.Auth;
using IdentityService.Modules.M00Transversal.Errors;
using IdentityService.Modules.M05Membros.Interface;
using IdentityService.Modules.M11Esquema.Persistence;
using IdentityService.Modules.M11Esquema.Persistence.Entities;
using IdentityService.Modules.M11Esquema.Persistence.Enums;
using IdentityService.Modules.M14Eventos.Interface;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace IdentityService.Modules.M04Tenants;

public static class CreateTenantEndpoint
{
    private static readonly HashSet<string> AllowedKeys = new(StringComparer.Ordinal)
    {
        "group_id",
        "cnpj",
        "legal_name",
        "branch_type",
        "geo_lat",
        "geo_lng",
        "scopes"
    };

    public static async Task<IResult> HandleAsync(
        HttpContext httpContext,
        IdentityDbContext dbContext,
        IAuthContext authContext,
        IMembershipWriter membershipWriter,
        IEventOutbox eventOutbox,
        IAuditLog auditLog,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var (groupId, cnpj, trimmedLegalName, branchType, geoLat, geoLng, scopes) =
            await ReadAndValidateRequestAsync(httpContext.Request);

        // Layer 4: Authorization (RN-62)
        var isPlatformAdmin = authContext.PlatformRole == PlatformRole.PLATFORM_ADMIN;
        var isTenantAdmin = authContext.ActiveScope == OperationalScope.TENANT_ADMIN &&
                            authContext.Permissions.Contains("tenant:criar") &&
                            authContext.GroupId.HasValue &&
                            authContext.GroupId.Value == groupId;

        if (!isPlatformAdmin && !isTenantAdmin)
        {
            throw new ApiErrorException("ERR-CTX-007", "Violação de isolamento de contexto ou permissão insuficiente");
        }

        // Layer 5: Existence of economic_group (RN-63)
        var group = await dbContext.EconomicGroups
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == groupId, cancellationToken);

        if (group == null)
        {
            throw new ApiErrorException("ERR-CTX-008", "Recurso de domínio não encontrado");
        }

        // Layer 6: Business rules
        // RN-64: Group cannot be SUSPENDED
        if (group.Status == EntityStatus.SUSPENDED)
        {
            throw new ApiErrorException("ERR-CTX-002", "Tenant ou Grupo Económico suspenso");
        }

        // RN-65: CNPJ uniqueness
        var cnpjExists = await dbContext.Tenants
            .AsNoTracking()
            .AnyAsync(t => t.Cnpj == cnpj, cancellationToken);

        if (cnpjExists)
        {
            throw new ApiErrorException("ERR-CTX-005", "Conflito de unicidade de registo");
        }

        // RN-66: If caller is TENANT_ADMIN and scopes does not contain TENANT_ADMIN, append TENANT_ADMIN
        if (authContext.ActiveScope == OperationalScope.TENANT_ADMIN && !scopes.Contains(OperationalScope.TENANT_ADMIN))
        {
            scopes.Add(OperationalScope.TENANT_ADMIN);
        }

        var now = timeProvider.GetUtcNow();
        var newTenantId = Guid.CreateVersion7(now);

        // PRS-51: AuthorizeTenantWrite before opening transaction connection
        var conn = dbContext.Database.GetDbConnection();
        if (conn.State == ConnectionState.Open)
        {
            await dbContext.Database.CloseConnectionAsync();
        }

        authContext.AuthorizeTenantWrite(newTenantId);

        await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var tenant = new Tenant
        {
            Id = newTenantId,
            GroupId = groupId,
            Cnpj = cnpj,
            LegalName = trimmedLegalName,
            BranchType = branchType,
            GeoLat = geoLat,
            GeoLng = geoLng,
            Status = EntityStatus.ACTIVE,
            CreatedAt = now,
            UpdatedAt = now
        };
        dbContext.Tenants.Add(tenant);

        foreach (var scope in scopes)
        {
            dbContext.TenantScopes.Add(new TenantScope
            {
                Id = Guid.CreateVersion7(now),
                TenantId = newTenantId,
                Scope = scope,
                IsActive = true,
                CreatedAt = now
            });
        }

        if (authContext.ActiveScope == OperationalScope.TENANT_ADMIN)
        {
            membershipWriter.AddCreatorMembership(newTenantId, authContext.UserId!.Value);
        }

        // Audit log (AUD.M04)
        auditLog.Append(
            AuditEventType.TENANT_CREATED,
            authContext.UserId,
            null,
            newTenantId,
            groupId,
            null,
            new { });

        if (authContext.ActiveScope == OperationalScope.TENANT_ADMIN)
        {
            auditLog.Append(
                AuditEventType.MEMBER_ADDED,
                authContext.UserId,
                null,
                newTenantId,
                groupId,
                null,
                new { });
        }

        // Outbox EV-01
        eventOutbox.EnqueueTenantCreated(new TenantCreatedPayload(
            newTenantId,
            groupId,
            cnpj,
            trimmedLegalName,
            branchType,
            geoLat,
            geoLng,
            scopes,
            now
        ));

        await dbContext.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        var sortedScopes = scopes
            .OrderBy(s => s.ToString(), StringComparer.Ordinal)
            .Select(s => s.ToString())
            .ToArray();

        var responsePayload = new
        {
            id = newTenantId.ToString("D"),
            group_id = groupId.ToString("D"),
            cnpj = cnpj,
            legal_name = trimmedLegalName,
            branch_type = branchType.ToString(),
            geo_lat = geoLat,
            geo_lng = geoLng,
            status = "ACTIVE",
            scopes = sortedScopes,
            created_at = now.ToString("yyyy-MM-ddTHH:mm:ssZ")
        };

        return Results.Created($"/api/v1/tenants/{newTenantId:D}", responsePayload);
    }

    private static async Task<(Guid GroupId, string Cnpj, string LegalName, BranchType BranchType, decimal GeoLat, decimal GeoLng, List<OperationalScope> Scopes)>
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

        if (seenKeys.Count != 7)
        {
            throw new ApiErrorException("ERR-VAL-001", "Pedido inválido ou malformado");
        }

        // Layer 3 (b): Structural format checks
        var groupProp = root.GetProperty("group_id");
        if (groupProp.ValueKind != JsonValueKind.String || !Guid.TryParseExact(groupProp.GetString()!, "D", out var groupId))
        {
            throw new ApiErrorException("ERR-VAL-001", "Pedido inválido ou malformado");
        }

        var legalNameProp = root.GetProperty("legal_name");
        if (legalNameProp.ValueKind != JsonValueKind.String)
        {
            throw new ApiErrorException("ERR-VAL-001", "Pedido inválido ou malformado");
        }
        var rawLegalName = legalNameProp.GetString()!;
        var trimmedLegalName = rawLegalName.Trim();
        if (trimmedLegalName.Length is < 2 or > 200)
        {
            throw new ApiErrorException("ERR-VAL-001", "Pedido inválido ou malformado");
        }

        var branchTypeProp = root.GetProperty("branch_type");
        if (branchTypeProp.ValueKind != JsonValueKind.String)
        {
            throw new ApiErrorException("ERR-VAL-001", "Pedido inválido ou malformado");
        }
        var branchTypeStr = branchTypeProp.GetString()!;
        if (branchTypeStr is not ("HQ" or "BRANCH") || !Enum.TryParse<BranchType>(branchTypeStr, out var branchType))
        {
            throw new ApiErrorException("ERR-VAL-001", "Pedido inválido ou malformado");
        }

        var scopesProp = root.GetProperty("scopes");
        if (scopesProp.ValueKind != JsonValueKind.Array)
        {
            throw new ApiErrorException("ERR-VAL-001", "Pedido inválido ou malformado");
        }
        var scopesCount = scopesProp.GetArrayLength();
        if (scopesCount is < 1 or > 4)
        {
            throw new ApiErrorException("ERR-VAL-001", "Pedido inválido ou malformado");
        }

        var scopes = new List<OperationalScope>();
        var seenScopes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var elem in scopesProp.EnumerateArray())
        {
            if (elem.ValueKind != JsonValueKind.String)
            {
                throw new ApiErrorException("ERR-VAL-001", "Pedido inválido ou malformado");
            }
            var scopeStr = elem.GetString()!;
            if (scopeStr.Contains('\0') || !seenScopes.Add(scopeStr))
            {
                throw new ApiErrorException("ERR-VAL-001", "Pedido inválido ou malformado");
            }
            if (scopeStr is not ("BUYER" or "SUPPLIER" or "CARRIER" or "TENANT_ADMIN") ||
                !Enum.TryParse<OperationalScope>(scopeStr, out var scopeEnum))
            {
                throw new ApiErrorException("ERR-VAL-001", "Pedido inválido ou malformado");
            }
            scopes.Add(scopeEnum);
        }

        var latProp = root.GetProperty("geo_lat");
        var lngProp = root.GetProperty("geo_lng");
        if (latProp.ValueKind != JsonValueKind.Number || lngProp.ValueKind != JsonValueKind.Number)
        {
            throw new ApiErrorException("ERR-VAL-001", "Pedido inválido ou malformado");
        }
        if (!latProp.TryGetDecimal(out var geoLat) || !lngProp.TryGetDecimal(out var geoLng))
        {
            throw new ApiErrorException("ERR-VAL-001", "Pedido inválido ou malformado");
        }

        var cnpjProp = root.GetProperty("cnpj");
        if (cnpjProp.ValueKind != JsonValueKind.String)
        {
            throw new ApiErrorException("ERR-VAL-001", "Pedido inválido ou malformado");
        }
        var cnpj = cnpjProp.GetString()!;

        // Layer 3 (c): Content validations -> 422 ERR-VAL-002
        if (!CnpjValidator.IsValid(cnpj))
        {
            throw new ApiErrorException("ERR-VAL-002", "Dados de CNPJ ou geolocalização inválidos");
        }

        if (geoLat is < -90.0m or > 90.0m || geoLng is < -180.0m or > 180.0m)
        {
            throw new ApiErrorException("ERR-VAL-002", "Dados de CNPJ ou geolocalização inválidos");
        }

        return (groupId, cnpj, trimmedLegalName, branchType, geoLat, geoLng, scopes);
    }
}
