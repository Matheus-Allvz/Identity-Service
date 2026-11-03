using IdentityService.Modules.M01Login.Interface;
using IdentityService.Modules.M11Esquema.Persistence;
using IdentityService.Modules.M11Esquema.Persistence.Enums;
using Microsoft.EntityFrameworkCore;

namespace IdentityService.Modules.M01Login;

public class UserAccountStatusWriter : IUserAccountStatusWriter
{
    private readonly IdentityDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public UserAccountStatusWriter(IdentityDbContext dbContext, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
    }

    public async Task<UserStatus?> SetAdministrativeStatusAsync(Guid userId, UserStatus status, CancellationToken cancellationToken = default)
    {
        if (status == UserStatus.LOCKED)
        {
            throw new ArgumentException("Status LOCKED cannot be set administratively.", nameof(status));
        }

        var user = await _dbContext.UserAccounts.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user == null)
        {
            return null;
        }

        var previousStatus = user.Status;
        user.Status = status;
        user.FailedLoginAttempts = 0;
        user.LockedUntil = null;
        user.UpdatedAt = _timeProvider.GetUtcNow();

        return previousStatus;
    }
}
