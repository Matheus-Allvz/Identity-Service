using System.Net.Mail;
using System.Text.Json;
using IdentityService.Modules.M00Transversal.Audit;
using IdentityService.Modules.M00Transversal.Errors;
using IdentityService.Modules.M00Transversal.Redis;
using IdentityService.Modules.M01Login.Interface;
using IdentityService.Modules.M02Refresh.Interface;
using IdentityService.Modules.M07Chaves.Interface;
using IdentityService.Modules.M08Acessos.Interface;
using IdentityService.Modules.M11Esquema.Persistence;
using IdentityService.Modules.M11Esquema.Persistence.Enums;
using IdentityService.Modules.M14Eventos.Interface;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace IdentityService.Modules.M01Login;

public static class LoginEndpoint
{
    public static async Task<IResult> HandleAsync(
        HttpContext httpContext,
        IdentityDbContext dbContext,
        IPasswordHasher passwordHasher,
        ISessionStore sessionStore,
        ITokenIssuer tokenIssuer,
        IAccessQueries accessQueries,
        IAuditLog auditLog,
        IDenylist denylist,
        IEventOutbox eventOutbox,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        // 1. Read body and validate JSON
        var body = await ReadAndValidateJsonAsync(httpContext.Request);

        var (email, password) = ValidateFormat(body);

        var normalizedEmail = email.ToLowerInvariant();
        var now = timeProvider.GetUtcNow();

        var user = await dbContext.UserAccounts
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);

        if (user == null)
        {
            passwordHasher.VerifyPasswordDummy(password);
            auditLog.Append(AuditEventType.LOGIN_FAILED, null, null, null, null, null, null);
            await dbContext.SaveChangesAsync(cancellationToken);
            throw new ApiErrorException("ERR-AUTH-001", "Credenciais inválidas");
        }

        if (user.Status == UserStatus.LOCKED)
        {
            if (user.LockedUntil.HasValue && user.LockedUntil.Value > now)
            {
                passwordHasher.VerifyPasswordDummy(password);
                auditLog.Append(AuditEventType.LOGIN_FAILED, user.Id, null, null, null, null, null);
                await dbContext.SaveChangesAsync(cancellationToken);
                throw new ApiErrorException("ERR-AUTH-001", "Credenciais inválidas");
            }

            // Lock expired -> Automatic unlock (RN-31, D-08)
            user.Status = UserStatus.ACTIVE;
            user.FailedLoginAttempts = 0;
            user.LockedUntil = null;
            user.UpdatedAt = now;
        }

        var isPasswordValid = passwordHasher.VerifyPassword(password, user.PasswordHash);

        if (!isPasswordValid)
        {
            if (user.Status == UserStatus.SUSPENDED)
            {
                auditLog.Append(AuditEventType.LOGIN_FAILED, user.Id, null, null, null, null, null);
                await dbContext.SaveChangesAsync(cancellationToken);
                throw new ApiErrorException("ERR-AUTH-001", "Credenciais inválidas");
            }

            if (user.FailedLoginAttempts + 1 < 5)
            {
                user.FailedLoginAttempts += 1;
                user.UpdatedAt = now;
                auditLog.Append(AuditEventType.LOGIN_FAILED, user.Id, null, null, null, null, null);
                await dbContext.SaveChangesAsync(cancellationToken);
                throw new ApiErrorException("ERR-AUTH-001", "Credenciais inválidas");
            }

            // 5th failure -> Lock account
            user.Status = UserStatus.LOCKED;
            user.FailedLoginAttempts = 5;
            user.LockedUntil = now.AddSeconds(900);
            user.UpdatedAt = now;

            await denylist.BlockUserAsync(user.Id, cancellationToken);
            await sessionStore.RevokeByUserAsync(user.Id, cancellationToken);
            eventOutbox.EnqueueUserLocked(user.Id, user.LockedUntil.Value, now);
            auditLog.Append(AuditEventType.USER_LOCKED, user.Id, null, null, null, null, null);

            await dbContext.SaveChangesAsync(cancellationToken);
            throw new ApiErrorException("ERR-AUTH-001", "Credenciais inválidas");
        }

        // Password is valid
        if (user.Status == UserStatus.SUSPENDED)
        {
            // RN-33, RG-05: sem alterar BD
            throw new ApiErrorException("ERR-AUTH-004", "Utilizador suspenso ou revogado globalmente");
        }

        // RN-34: Success
        user.FailedLoginAttempts = 0;
        user.LockedUntil = null;
        user.UpdatedAt = now;

        var newSession = await sessionStore.CreateFamilyAsync(user.Id, cancellationToken);
        var contextsResult = await accessQueries.ListContextsAsync(user.Id, cancellationToken);
        var availableContexts = contextsResult.Ok && contextsResult.Value != null
            ? contextsResult.Value
            : Array.Empty<AvailableContext>();

        var claims = new AccessTokenClaims(
            Sub: user.Id,
            FamilyId: newSession.FamilyId,
            PlatformRole: user.PlatformRole,
            ViewMode: ViewMode.NONE,
            GroupId: null,
            TenantId: null,
            AllowedTenants: Array.Empty<Guid>(),
            ActiveScope: null,
            Roles: Array.Empty<string>(),
            Permissions: Array.Empty<string>(),
            Act: null
        );

        var issuedToken = tokenIssuer.Issue(claims);

        auditLog.Append(AuditEventType.LOGIN_SUCCESS, user.Id, null, null, null, null, null);
        await dbContext.SaveChangesAsync(cancellationToken);

        httpContext.Response.Headers.CacheControl = "no-store";
        httpContext.Response.Headers.Pragma = "no-cache";

        var responsePayload = new
        {
            access_token = issuedToken.AccessToken,
            token_type = "Bearer",
            expires_in = 900,
            refresh_token = newSession.RefreshToken,
            refresh_expires_in = 604800,
            user = new
            {
                id = user.Id.ToString("D"),
                email = user.Email,
                name = user.Name,
                platform_role = user.PlatformRole.ToString()
            },
            available_contexts = availableContexts.Select(c => new
            {
                group_id = c.GroupId.ToString("D"),
                group_name = c.GroupName,
                tenant_id = c.TenantId.ToString("D"),
                cnpj = c.Cnpj,
                legal_name = c.LegalName,
                branch_type = c.BranchType.ToString(),
                is_owner = c.IsOwner,
                can_view_group = c.CanViewGroup,
                available_scopes = c.AvailableScopes.Select(s => s.ToString()).ToArray(),
                roles = c.Roles
            }).ToArray()
        };

        return Results.Ok(responsePayload);
    }

    private static async Task<JsonElement> ReadAndValidateJsonAsync(HttpRequest request)
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

            if (prop.Name is not ("email" or "password"))
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

        if (!keys.Contains("email") || !keys.Contains("password"))
        {
            throw new ApiErrorException("ERR-VAL-001", "Pedido inválido ou malformado");
        }

        return root;
    }

    private static (string Email, string Password) ValidateFormat(JsonElement root)
    {
        var rawEmail = root.GetProperty("email").GetString()!;
        var rawPassword = root.GetProperty("password").GetString()!;

        var trimmedEmail = rawEmail.Trim();
        if (trimmedEmail.Length is < 5 or > 320)
        {
            throw new ApiErrorException("ERR-VAL-003", "Formato de e-mail ou política de palavra-passe violada");
        }

        if (!MailAddress.TryCreate(trimmedEmail, out var parsedAddress) || parsedAddress.Address != trimmedEmail)
        {
            throw new ApiErrorException("ERR-VAL-003", "Formato de e-mail ou política de palavra-passe violada");
        }

        // Password validation without trim: length 12..128, 4 classes: [A-Z], [a-z], [0-9], special
        if (rawPassword.Length is < 12 or > 128)
        {
            throw new ApiErrorException("ERR-VAL-003", "Formato de e-mail ou política de palavra-passe violada");
        }

        var hasUpper = rawPassword.Any(char.IsUpper);
        var hasLower = rawPassword.Any(char.IsLower);
        var hasDigit = rawPassword.Any(char.IsDigit);
        var hasSpecial = rawPassword.Any(c => !char.IsLetterOrDigit(c));

        if (!hasUpper || !hasLower || !hasDigit || !hasSpecial)
        {
            throw new ApiErrorException("ERR-VAL-003", "Formato de e-mail ou política de palavra-passe violada");
        }

        return (trimmedEmail, rawPassword);
    }
}
