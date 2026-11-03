using IdentityService.Modules.M11Esquema.Persistence.Enums;

namespace IdentityService.Modules.M01Login.Interface;

public interface IUserAccountStatusWriter
{
    Task<UserStatus?> SetAdministrativeStatusAsync(Guid userId, UserStatus status, CancellationToken cancellationToken = default);
}
