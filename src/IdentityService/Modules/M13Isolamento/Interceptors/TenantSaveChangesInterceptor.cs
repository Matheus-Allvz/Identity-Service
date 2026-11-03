using IdentityService.Modules.M00Transversal.Auth;
using IdentityService.Modules.M00Transversal.Errors;
using IdentityService.Modules.M11Esquema.Persistence;
using IdentityService.Modules.M13Isolamento.Interface;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace IdentityService.Modules.M13Isolamento.Interceptors;

public class TenantSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly IAuthContext _authContext;

    public TenantSaveChangesInterceptor(IAuthContext authContext)
    {
        _authContext = authContext;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ValidateTenantAccess(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ValidateTenantAccess(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void ValidateTenantAccess(DbContext? context)
    {
        if (context is not IdentityDbContext db)
        {
            return;
        }

        if (_authContext.BypassTenantFilter || db.TenantIsolationBypass)
        {
            return;
        }

        var entries = db.ChangeTracker.Entries<ITenantScoped>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);

        foreach (var entry in entries)
        {
            var tenantId = entry.Entity.TenantId;
            if (tenantId == Guid.Empty || !_authContext.WriteTenants.Contains(tenantId))
            {
                throw new ApiErrorException("ERR-CTX-007", "Violação de isolamento de contexto ou permissão insuficiente");
            }
        }
    }
}
