using System.Data.Common;
using IdentityService.Modules.M00Transversal.Auth;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace IdentityService.Modules.M13Isolamento.Interceptors;

public class RlsConnectionInterceptor : DbConnectionInterceptor
{
    private readonly IAuthContext _authContext;

    public RlsConnectionInterceptor(IAuthContext authContext)
    {
        _authContext = authContext;
    }

    public override void ConnectionOpened(
        DbConnection connection,
        ConnectionEndEventData eventData)
    {
        ApplyRlsSessionVariables(connection);
        base.ConnectionOpened(connection, eventData);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await ApplyRlsSessionVariablesAsync(connection, cancellationToken);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }

    private void ApplyRlsSessionVariables(DbConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SET ROLE identity_app; SELECT set_config('app.allowed_tenants', @a, false), set_config('app.write_tenants', @w, false), set_config('app.auth_flow', @b, false);";

        var pA = cmd.CreateParameter();
        pA.ParameterName = "a";
        pA.Value = string.Join(",", _authContext.AllowedTenants);
        cmd.Parameters.Add(pA);

        var pW = cmd.CreateParameter();
        pW.ParameterName = "w";
        pW.Value = string.Join(",", _authContext.WriteTenants);
        cmd.Parameters.Add(pW);

        var pB = cmd.CreateParameter();
        pB.ParameterName = "b";
        pB.Value = _authContext.BypassTenantFilter ? "true" : "false";
        cmd.Parameters.Add(pB);

        cmd.ExecuteNonQuery();
    }

    private async Task ApplyRlsSessionVariablesAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SET ROLE identity_app; SELECT set_config('app.allowed_tenants', @a, false), set_config('app.write_tenants', @w, false), set_config('app.auth_flow', @b, false);";

        var pA = cmd.CreateParameter();
        pA.ParameterName = "a";
        pA.Value = string.Join(",", _authContext.AllowedTenants);
        cmd.Parameters.Add(pA);

        var pW = cmd.CreateParameter();
        pW.ParameterName = "w";
        pW.Value = string.Join(",", _authContext.WriteTenants);
        cmd.Parameters.Add(pW);

        var pB = cmd.CreateParameter();
        pB.ParameterName = "b";
        pB.Value = _authContext.BypassTenantFilter ? "true" : "false";
        cmd.Parameters.Add(pB);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
