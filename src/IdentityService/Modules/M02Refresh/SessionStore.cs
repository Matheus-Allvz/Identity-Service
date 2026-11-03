using System.Security.Cryptography;
using System.Text;
using IdentityService.Modules.M02Refresh.Interface;
using IdentityService.Modules.M11Esquema.Persistence;
using IdentityService.Modules.M11Esquema.Persistence.Entities;
using IdentityService.Modules.M11Esquema.Persistence.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace IdentityService.Modules.M02Refresh;

public class SessionStore : ISessionStore
{
    private readonly IdentityDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public SessionStore(IdentityDbContext dbContext, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
    }

    public Task<NewSession> CreateFamilyAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        var randomBytes = RandomNumberGenerator.GetBytes(32);
        var refreshToken = Base64UrlEncoder.Encode(randomBytes);
        var tokenHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));

        var familyId = Guid.CreateVersion7(now);
        var expiresAt = now.AddSeconds(604800);

        var entity = new RefreshToken
        {
            Id = Guid.CreateVersion7(now),
            FamilyId = familyId,
            UserId = userId,
            TokenHash = tokenHash,
            Status = RefreshTokenStatus.ACTIVE,
            ReplacedById = null,
            ViewMode = ViewMode.NONE,
            GroupId = null,
            ActiveTenantId = null,
            AllowedTenants = [],
            ActiveScope = null,
            CreatedAt = now,
            ExpiresAt = expiresAt,
            ConsumedAt = null,
            RevokedAt = null
        };

        _dbContext.RefreshTokens.Add(entity);
        return Task.FromResult(new NewSession(refreshToken, familyId, expiresAt));
    }

    public async Task<bool> UpdateActiveContextAsync(
        Guid familyId,
        ViewMode viewMode,
        Guid groupId,
        Guid? activeTenantId,
        IReadOnlyList<Guid> allowedTenants,
        OperationalScope activeScope,
        CancellationToken cancellationToken = default)
    {
        if (viewMode == ViewMode.NONE)
        {
            throw new ArgumentException("viewMode cannot be NONE in UpdateActiveContextAsync.", nameof(viewMode));
        }

        var activeToken = await _dbContext.RefreshTokens
            .FirstOrDefaultAsync(r => r.FamilyId == familyId && r.Status == RefreshTokenStatus.ACTIVE, cancellationToken);

        if (activeToken == null)
        {
            return false;
        }

        activeToken.ViewMode = viewMode;
        activeToken.GroupId = groupId;
        activeToken.ActiveTenantId = activeTenantId;
        activeToken.AllowedTenants = allowedTenants.OrderBy(g => g).ToArray();
        activeToken.ActiveScope = activeScope;

        return true;
    }

    public async Task<int> RevokeFamilyAsync(Guid familyId, CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        return await _dbContext.RefreshTokens
            .Where(r => r.FamilyId == familyId && (r.Status == RefreshTokenStatus.ACTIVE || r.Status == RefreshTokenStatus.CONSUMED))
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, RefreshTokenStatus.REVOKED)
                .SetProperty(r => r.RevokedAt, now),
                cancellationToken);
    }

    public async Task<int> RevokeByUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        return await _dbContext.RefreshTokens
            .Where(r => r.UserId == userId && (r.Status == RefreshTokenStatus.ACTIVE || r.Status == RefreshTokenStatus.CONSUMED))
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, RefreshTokenStatus.REVOKED)
                .SetProperty(r => r.RevokedAt, now),
                cancellationToken);
    }

    public async Task<int> RevokeByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        return await _dbContext.RefreshTokens
            .Where(r => (r.Status == RefreshTokenStatus.ACTIVE || r.Status == RefreshTokenStatus.CONSUMED) &&
                        (r.ActiveTenantId == tenantId || r.AllowedTenants.Contains(tenantId)))
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, RefreshTokenStatus.REVOKED)
                .SetProperty(r => r.RevokedAt, now),
                cancellationToken);
    }

    public async Task<int> RevokeByUserAndTenantsAsync(Guid userId, IReadOnlyList<Guid> tenantIds, CancellationToken cancellationToken = default)
    {
        var activeFamilies = await _dbContext.RefreshTokens
            .Where(r => r.UserId == userId && r.Status == RefreshTokenStatus.ACTIVE)
            .Where(r => (r.ActiveTenantId.HasValue && tenantIds.Contains(r.ActiveTenantId.Value)) ||
                        r.AllowedTenants.Any(t => tenantIds.Contains(t)))
            .Select(r => r.FamilyId)
            .ToListAsync(cancellationToken);

        if (activeFamilies.Count == 0)
        {
            return 0;
        }

        var now = _timeProvider.GetUtcNow();
        return await _dbContext.RefreshTokens
            .Where(r => activeFamilies.Contains(r.FamilyId) &&
                        (r.Status == RefreshTokenStatus.ACTIVE || r.Status == RefreshTokenStatus.CONSUMED))
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, RefreshTokenStatus.REVOKED)
                .SetProperty(r => r.RevokedAt, now),
                cancellationToken);
    }
}
