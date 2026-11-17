using System.Data;
using System.Data.Common;
using IdentityService.Modules.M00Transversal.Auth;
using IdentityService.Modules.M11Esquema.Persistence;
using IdentityService.Modules.M11Esquema.Persistence.Entities;
using IdentityService.Modules.M13Isolamento.Interceptors;
using IdentityService.Modules.M13Isolamento.Interface;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace IdentityService.Modules.M13Isolamento;

public class TenantIsolation : ITenantIsolation
{
    public void ApplyQueryFilters(ModelBuilder modelBuilder, IdentityDbContext context)
    {
        modelBuilder.Entity<TenantScope>()
            .HasQueryFilter(e => context.AuthContext.BypassTenantFilter || context.TenantIsolationBypass || context.AuthContext.AllowedTenants.Contains(e.TenantId));

        modelBuilder.Entity<TenantMember>()
            .HasQueryFilter(e => context.AuthContext.BypassTenantFilter || context.TenantIsolationBypass || context.AuthContext.AllowedTenants.Contains(e.TenantId));

        modelBuilder.Entity<MemberRole>()
            .HasQueryFilter(e => context.AuthContext.BypassTenantFilter || context.TenantIsolationBypass || context.AuthContext.AllowedTenants.Contains(e.TenantId));

        modelBuilder.Entity<ServiceAccount>()
            .HasQueryFilter(e => context.AuthContext.BypassTenantFilter || context.TenantIsolationBypass || context.AuthContext.AllowedTenants.Contains(e.TenantId));
    }

    public IReadOnlyList<IInterceptor> CreateInterceptors(IServiceProvider requestServices)
    {
        var authContext = requestServices.GetRequiredService<IAuthContext>();
        return
        [
            new TenantSaveChangesInterceptor(authContext),
            new RlsConnectionInterceptor(authContext)
        ];
    }

    public async Task<T> ExecuteWithBypassAsync<T>(IdentityDbContext context, Func<IdentityDbContext, Task<T>> operation)
    {
        if (context.TenantIsolationBypass)
        {
            throw new InvalidOperationException("Nested ExecuteWithBypassAsync is not permitted.");
        }

        var connection = context.Database.GetDbConnection();
        bool openedLocally = false;
        if (connection.State != ConnectionState.Open)
        {
            await context.Database.OpenConnectionAsync();
            openedLocally = true;
        }

        context.TenantIsolationBypass = true;
        try
        {
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT set_config('app.auth_flow', 'true', false);";
                await cmd.ExecuteNonQueryAsync();
            }

            return await operation(context);
        }
        catch
        {
            await ResetBypassStateAsync(context, connection, openedLocally, suppressResetException: true);
            throw;
        }
        finally
        {
            if (context.TenantIsolationBypass)
            {
                await ResetBypassStateAsync(context, connection, openedLocally, suppressResetException: false);
            }
        }
    }

    private static async Task ResetBypassStateAsync(IdentityDbContext context, DbConnection connection, bool openedLocally, bool suppressResetException)
    {
        context.TenantIsolationBypass = false;
        try
        {
            if (connection.State == ConnectionState.Open)
            {
                await using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT set_config('app.auth_flow', @b, false);";
                var pB = cmd.CreateParameter();
                pB.ParameterName = "b";
                pB.Value = context.AuthContext.BypassTenantFilter ? "true" : "false";
                cmd.Parameters.Add(pB);
                await cmd.ExecuteNonQueryAsync();
            }
        }
        catch when (suppressResetException)
        {
            // Ignore reset failure when operation threw (e.g. 25P02 transaction aborted)
        }
        finally
        {
            if (openedLocally && connection.State == ConnectionState.Open)
            {
                await context.Database.CloseConnectionAsync();
            }
        }
    }
}
