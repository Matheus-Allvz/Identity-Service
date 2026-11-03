namespace IdentityService.Modules.M01Login.Interface;

public interface IPasswordHasher
{
    string HashPassword(string password);
    bool VerifyPassword(string password, string passwordHash);
    void VerifyPasswordDummy(string password);
}
