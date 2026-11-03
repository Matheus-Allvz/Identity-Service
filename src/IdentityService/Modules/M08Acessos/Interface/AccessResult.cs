namespace IdentityService.Modules.M08Acessos.Interface;

public record AccessResult(bool Ok, string? ErrorCode)
{
    public static AccessResult Success() => new(true, null);
    public static AccessResult Failure(string errorCode) => new(false, errorCode);
}

public record AccessResult<T>(bool Ok, T? Value, string? ErrorCode)
{
    public static AccessResult<T> Success(T value) => new(true, value, null);
    public static AccessResult<T> Failure(string errorCode) => new(false, default, errorCode);
}
