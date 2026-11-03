using System.Security.Cryptography;
using System.Text;
using IdentityService.Modules.M01Login.Interface;
using Konscious.Security.Cryptography;

namespace IdentityService.Modules.M01Login;

public class PasswordHasher : IPasswordHasher
{
    private const int MemorySize = 19456;
    private const int Iterations = 2;
    private const int DegreeOfParallelism = 1;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    private static readonly byte[] DummySalt = new byte[SaltSize];

    public string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            MemorySize = MemorySize,
            Iterations = Iterations,
            DegreeOfParallelism = DegreeOfParallelism
        };
        var hash = argon2.GetBytes(HashSize);
        var saltB64 = Convert.ToBase64String(salt);
        var hashB64 = Convert.ToBase64String(hash);
        return $"$argon2id$v=19$m={MemorySize},t={Iterations},p={DegreeOfParallelism}${saltB64}${hashB64}";
    }

    public bool VerifyPassword(string password, string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            VerifyPasswordDummy(password);
            return false;
        }

        var parts = passwordHash.Split('$');
        // Expected format: $argon2id$v=19$m=19456,t=2,p=1$<salt_b64>$<hash_b64>
        // parts[0] = "", parts[1] = "argon2id", parts[2] = "v=19", parts[3] = "m=19456,t=2,p=1", parts[4] = salt, parts[5] = hash
        if (parts.Length != 6 || parts[1] != "argon2id" || parts[2] != "v=19" || parts[3] != "m=19456,t=2,p=1")
        {
            VerifyPasswordDummy(password);
            return false;
        }

        byte[] salt;
        byte[] expectedHash;
        try
        {
            salt = DecodeBase64(parts[4]);
            expectedHash = DecodeBase64(parts[5]);
        }
        catch
        {
            VerifyPasswordDummy(password);
            return false;
        }

        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            MemorySize = MemorySize,
            Iterations = Iterations,
            DegreeOfParallelism = DegreeOfParallelism
        };
        var computedHash = argon2.GetBytes(expectedHash.Length);
        return CryptographicOperations.FixedTimeEquals(computedHash, expectedHash);
    }

    public void VerifyPasswordDummy(string password)
    {
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = DummySalt,
            MemorySize = MemorySize,
            Iterations = Iterations,
            DegreeOfParallelism = DegreeOfParallelism
        };
        _ = argon2.GetBytes(HashSize);
    }

    private static byte[] DecodeBase64(string input)
    {
        var mod = input.Length % 4;
        if (mod > 0)
        {
            input += new string('=', 4 - mod);
        }
        return Convert.FromBase64String(input.Replace('-', '+').Replace('_', '/'));
    }
}
