using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using FinanceManager.Domain.Users;

namespace FinanceManager.Infrastructure.Auth;

public interface IPasswordHashingService
{
    string Hash(string password);
    bool Verify(string providedPassword, string storedHash);
}

public sealed class Pbkdf2IdentityPasswordHasher : IPasswordHasher<User>, IPasswordHashingService
{
    private const int Iterations = 100_000;
    private const int SaltSize = 16;
    private const int KeySize = 32;

    public string Hash(string password)
    {
        if (password is null) throw new ArgumentNullException(nameof(password));
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = KeyDerivation.Pbkdf2(password, salt, KeyDerivationPrf.HMACSHA256, Iterations, KeySize);
        return $"pbkdf2|{Iterations}|{Convert.ToBase64String(salt)}|{Convert.ToBase64String(key)}";
    }

    public bool Verify(string providedPassword, string storedHash)
    {
        if (string.IsNullOrWhiteSpace(storedHash) || string.IsNullOrWhiteSpace(providedPassword))
            return false;

        var parts = storedHash.Split('|');
        if (parts.Length != 4 || parts[0] != "pbkdf2")
            return false;

        if (!int.TryParse(parts[1], out var iterations) || iterations <= 0)
            return false;

        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var key = Convert.FromBase64String(parts[3]);
            var attempted = KeyDerivation.Pbkdf2(providedPassword, salt, KeyDerivationPrf.HMACSHA256, iterations, key.Length);
            return CryptographicOperations.FixedTimeEquals(key, attempted);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    // Identity IPasswordHasher<User> implementation delegates to the same logic
    public string HashPassword(User user, string password) => Hash(password);

    public PasswordVerificationResult VerifyHashedPassword(User user, string hashedPassword, string providedPassword)
        => Verify(providedPassword, hashedPassword) ? PasswordVerificationResult.Success : PasswordVerificationResult.Failed;
}
