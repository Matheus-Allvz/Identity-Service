using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using IdentityService.Modules.M11Esquema.Persistence;

namespace IdentityService.Modules.M13Isolamento.Interface;

public interface ITenantIsolation
{
    void ApplyQueryFilters(ModelBuilder modelBuilder, IdentityDbContext context);
    IReadOnlyList<IInterceptor> CreateInterceptors(IServiceProvider requestServices);
    Task<T> ExecuteWithBypassAsync<T>(IdentityDbContext context, Func<IdentityDbContext, Task<T>> operation);
}
