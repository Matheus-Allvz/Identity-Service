using IdentityService.Modules.M00Transversal.Errors;
using IdentityService.Modules.M08Acessos.Interface;
using IdentityService.Modules.M11Esquema.Persistence.Enums;

namespace IdentityService.Modules.M03Contexto;

public sealed record ResolvedSingleContext(
    Guid GroupId,
    Guid TenantId,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions);

// Verificações do contexto SINGLE (existência e estado do tenant, vínculo, escopo e papéis).
// Fonte única para o EP-02 (switch-context) e para o auto-contexto do EP-01 (RN-QW01.4).
public static class SingleContextResolver
{
    public static async Task<ResolvedSingleContext> ResolveAsync(
        IAccessQueries accessQueries,
        Guid userId,
        Guid targetTenantId,
        OperationalScope targetScope,
        CancellationToken cancellationToken)
    {
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

        var memberCheck = await accessQueries.CheckMembershipAsync(userId, targetTenantId, cancellationToken);
        if (!memberCheck.Ok)
        {
            throw new ApiErrorException(memberCheck.ErrorCode ?? "ERR-CTX-001");
        }

        var rolesCheck = await accessQueries.ResolveRolesAsync(userId, targetTenantId, targetScope, cancellationToken);
        if (!rolesCheck.Ok)
        {
            throw new ApiErrorException(rolesCheck.ErrorCode ?? "ERR-CTX-003");
        }

        return new ResolvedSingleContext(
            tenantInfo.GroupId,
            targetTenantId,
            rolesCheck.Value!.Roles,
            rolesCheck.Value!.Permissions);
    }
}
