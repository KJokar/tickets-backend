using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Infrastructure.Authentication;

// Preserve the existing abstraction and legacy hashes while issuing Identity V3 hashes.
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private static readonly object User = new();
    private readonly PasswordHasher<object> _identity = new(Options.Create(new PasswordHasherOptions
    {
        IterationCount = 210_000,
        CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3
    }));

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        return _identity.HashPassword(User, password);
    }

    public bool Verify(string passwordHash, string providedPassword)
    {
        if (string.IsNullOrWhiteSpace(passwordHash) || string.IsNullOrWhiteSpace(providedPassword))
            return false;
        try
        {
            if (!passwordHash.StartsWith("pbkdf2-sha256$", StringComparison.Ordinal))
                return _identity.VerifyHashedPassword(User, passwordHash, providedPassword) != PasswordVerificationResult.Failed;

            string[] parts = passwordHash.Split('$');
            if (parts.Length != 4 || !int.TryParse(parts[1], out int iterations) || iterations is < 1 or > 1_000_000)
                return false;
            byte[] salt = Convert.FromBase64String(parts[2]);
            byte[] expected = Convert.FromBase64String(parts[3]);
            if (salt.Length != 16 || expected.Length != 32)
                return false;
            byte[] actual = Rfc2898DeriveBytes.Pbkdf2(providedPassword, salt, iterations, HashAlgorithmName.SHA256, 32);
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            return false;
        }
    }

    public bool NeedsRehash(string passwordHash, string providedPassword) =>
        passwordHash.StartsWith("pbkdf2-sha256$", StringComparison.Ordinal) ||
        _identity.VerifyHashedPassword(User, passwordHash, providedPassword) == PasswordVerificationResult.SuccessRehashNeeded;
}
