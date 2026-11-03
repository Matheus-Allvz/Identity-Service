using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using IdentityService.Modules.M00Transversal.Audit;
using IdentityService.Modules.M00Transversal.Auth;
using IdentityService.Modules.M00Transversal.Errors;
using IdentityService.Modules.M00Transversal.Redis;
using IdentityService.Modules.M02Refresh.Interface;
using IdentityService.Modules.M07Chaves.Interface;
using IdentityService.Modules.M08Acessos.Interface;
using IdentityService.Modules.M11Esquema.Persistence;
using IdentityService.Modules.M11Esquema.Persistence.Entities;
using IdentityService.Modules.M11Esquema.Persistence.Enums;
using IdentityService.Modules.M14Eventos.Interface;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

namespace IdentityService.Modules.M02Refresh;

public static class RefreshEndpoint
{
    private static readonly Regex RefreshTokenRegex = new(@"^[A-Za-z0-9_-]{43}$", RegexOptions.Compiled);

    public static async Task<IResult> HandleAsync(
        HttpContext httpContext,
        IdentityDbContext dbContext,
        ISessionStore sessionStore,
        ITokenIssuer tokenIssuer,
        IAccessQueries accessQueries,
        IAuditLog auditLog,
        IDenylist denylist,
        IEventOutbox eventOutbox,
        IAuthContext authContext,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var rawToken = await ReadAndValidateJsonAsync(httpContext.Request);

        if (!RefreshTokenRegex.IsMatch(rawToken))
        {
            throw new ApiErrorException("ERR-VAL-001", "Pedido inválido ou malformado");
        }

        var tokenHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
        var now = timeProvider.GetUtcNow();

        var token = await dbContext.RefreshTokens
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.TokenHash == tokenHash, cancellationToken);

        if (token == null)
        {
            throw new ApiErrorException("ERR-AUTH-005", "Refresh token inválido, revogado ou expirado");
        }

        if (token.Status == RefreshTokenStatus.CONSUMED)
        {
            await HandleReuseAsync(token, dbContext, sessionStore, denylist, auditLog, eventOutbox, authContext, now, cancellationToken);
            throw new ApiErrorException("ERR-AUTH-006", "Reuso de refresh token detetado e sessão revogada");
        }

        if (token.Status != RefreshTokenStatus.ACTIVE || token.ExpiresAt <= now)
        {
            throw new ApiErrorException("ERR-AUTH-005", "Refresh token inválido, revogado ou expirado");
        }

        // RN-41, D-19, PRS-84: Revalidate context
        var userCheck = await accessQueries.CheckUserAsync(token.UserId, token.CreatedAt.ToUnixTimeSeconds(), cancellationToken);
        if (!userCheck.Ok)
        {
            await sessionStore.RevokeFamilyAsync(token.FamilyId, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            throw new ApiErrorException(userCheck.ErrorCode ?? "ERR-AUTH-004");
        }

        IReadOnlyList<string> roles = [];
        IReadOnlyList<string> permissions = [];

        if (token.ViewMode == ViewMode.SINGLE)
        {
            var tenantCheck = await accessQueries.CheckTenantAsync(token.ActiveTenantId!.Value, cancellationToken);
            if (!tenantCheck.Ok)
            {
                await sessionStore.RevokeFamilyAsync(token.FamilyId, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
                throw new ApiErrorException(tenantCheck.ErrorCode ?? "ERR-CTX-002");
            }

            var memberCheck = await accessQueries.CheckMembershipAsync(token.UserId, token.ActiveTenantId!.Value, cancellationToken);
            if (!memberCheck.Ok)
            {
                await sessionStore.RevokeFamilyAsync(token.FamilyId, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
                throw new ApiErrorException(memberCheck.ErrorCode ?? "ERR-CTX-001");
            }

            var rolesCheck = await accessQueries.ResolveRolesAsync(token.UserId, token.ActiveTenantId!.Value, token.ActiveScope!.Value, cancellationToken);
            if (!rolesCheck.Ok)
            {
                await sessionStore.RevokeFamilyAsync(token.FamilyId, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
                throw new ApiErrorException(rolesCheck.ErrorCode ?? "ERR-CTX-003");
            }

            roles = rolesCheck.Value!.Roles;
            permissions = rolesCheck.Value!.Permissions;
        }
        else if (token.ViewMode == ViewMode.MACRO_GROUP)
        {
            var macroCheck = await accessQueries.ResolveMacroGroupAsync(token.UserId, token.GroupId!.Value, token.AllowedTenants, token.ActiveScope!.Value, cancellationToken);
            if (!macroCheck.Ok)
            {
                await sessionStore.RevokeFamilyAsync(token.FamilyId, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
                throw new ApiErrorException(macroCheck.ErrorCode ?? "ERR-CTX-004");
            }

            roles = macroCheck.Value!.Roles;
            permissions = macroCheck.Value!.Permissions;
        }

        // RN-43, RN-45: Atomic rotation
        var randomBytes = RandomNumberGenerator.GetBytes(32);
        var newRefreshToken = Base64UrlEncoder.Encode(randomBytes);
        var newHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(newRefreshToken)));
        var childId = Guid.CreateVersion7(now);

        var childEntity = new RefreshToken
        {
            Id = childId,
            FamilyId = token.FamilyId,
            UserId = token.UserId,
            TokenHash = newHash,
            Status = RefreshTokenStatus.ACTIVE,
            ReplacedById = null,
            ViewMode = token.ViewMode,
            GroupId = token.GroupId,
            ActiveTenantId = token.ActiveTenantId,
            AllowedTenants = token.AllowedTenants,
            ActiveScope = token.ActiveScope,
            CreatedAt = now,
            ExpiresAt = now.AddSeconds(604800),
            ConsumedAt = null,
            RevokedAt = null
        };

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var rowsUpdated = await dbContext.RefreshTokens
                .Where(r => r.Id == token.Id && r.Status == RefreshTokenStatus.ACTIVE)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Status, RefreshTokenStatus.CONSUMED)
                    .SetProperty(r => r.ConsumedAt, now),
                    cancellationToken);

            if (rowsUpdated == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                await HandleReuseAsync(token, dbContext, sessionStore, denylist, auditLog, eventOutbox, authContext, now, cancellationToken);
                throw new ApiErrorException("ERR-AUTH-006", "Reuso de refresh token detetado e sessão revogada");
            }

            dbContext.RefreshTokens.Add(childEntity);
            auditLog.Append(AuditEventType.TOKEN_REFRESHED, token.UserId, null, token.ActiveTenantId, token.GroupId, null, null);

            await dbContext.SaveChangesAsync(cancellationToken);

            await dbContext.RefreshTokens
                .Where(r => r.Id == token.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.ReplacedById, childId), cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            await transaction.RollbackAsync(cancellationToken);
            await HandleReuseAsync(token, dbContext, sessionStore, denylist, auditLog, eventOutbox, authContext, now, cancellationToken);
            throw new ApiErrorException("ERR-AUTH-006", "Reuso de refresh token detetado e sessão revogada");
        }

        var claims = new AccessTokenClaims(
            Sub: token.UserId,
            FamilyId: token.FamilyId,
            PlatformRole: token.User?.PlatformRole ?? PlatformRole.NONE,
            ViewMode: token.ViewMode,
            GroupId: token.GroupId,
            TenantId: token.ActiveTenantId,
            AllowedTenants: token.AllowedTenants,
            ActiveScope: token.ActiveScope,
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
            refresh_token = newRefreshToken,
            refresh_expires_in = 604800
        };

        return Results.Ok(responsePayload);
    }

    private static async Task HandleReuseAsync(
        RefreshToken token,
        IdentityDbContext dbContext,
        ISessionStore sessionStore,
        IDenylist denylist,
        IAuditLog auditLog,
        IEventOutbox eventOutbox,
        IAuthContext authContext,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await sessionStore.RevokeFamilyAsync(token.FamilyId, cancellationToken);
        await denylist.BlockUserAsync(token.UserId, cancellationToken);

        var activeLine = await dbContext.RefreshTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.FamilyId == token.FamilyId && r.Status == RefreshTokenStatus.ACTIVE, cancellationToken);

        var tenantId = activeLine?.ActiveTenantId ?? token.ActiveTenantId;
        var groupId = activeLine?.GroupId ?? token.GroupId;

        auditLog.Append(AuditEventType.TOKEN_REUSE_DETECTED, token.UserId, null, tenantId, groupId, null, null);

        var familyContext = new FamilyContext(
            token.UserId,
            activeLine?.ViewMode ?? token.ViewMode,
            groupId,
            tenantId,
            activeLine?.AllowedTenants ?? token.AllowedTenants,
            activeLine?.ActiveScope ?? token.ActiveScope
        );

        var compromisedPayload = new SessionCompromisedPayload(
            token.UserId,
            token.FamilyId,
            token.Id,
            authContext.IpAddress,
            now
        );

        eventOutbox.EnqueueSessionCompromised(compromisedPayload, familyContext);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        for (Exception? current = ex; current != null; current = current.InnerException)
        {
            if (current is PostgresException pgEx && pgEx.SqlState == "23505")
            {
                return true;
            }
        }
        return false;
    }

    private static async Task<string> ReadAndValidateJsonAsync(HttpRequest request)
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

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var prop in root.EnumerateObject())
        {
            if (!keys.Add(prop.Name))
            {
                throw new ApiErrorException("ERR-VAL-001", "Pedido inválido ou malformado");
            }

            if (prop.Name != "refresh_token")
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

        if (!keys.Contains("refresh_token"))
        {
            throw new ApiErrorException("ERR-VAL-001", "Pedido inválido ou malformado");
        }

        return root.GetProperty("refresh_token").GetString()!;
    }
}
