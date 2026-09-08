using FinanceManager.Domain.Users;
using FinanceManager.Infrastructure.Auth;
using Microsoft.AspNetCore.Identity;

namespace FinanceManager.Tests.Auth;

/// <summary>
/// Covers <see cref="Pbkdf2IdentityPasswordHasher"/>: that hashing is salted (equal passwords produce
/// different hashes but both still verify), that correct and incorrect passwords are distinguished, that
/// malformed stored hashes are rejected rather than throwing, and that the produced hash format meets the
/// expected structure and minimum iteration/salt/key sizes for PBKDF2.
/// </summary>
public sealed class Pbkdf2PasswordHasherTests
{
    private readonly Pbkdf2IdentityPasswordHasher _sut = new();

    /// <summary>
    /// Verifies that hashing the same password twice yields two different hash strings (because each
    /// call generates a fresh random salt), while both hashes still verify successfully against the
    /// original password - guarding against a salt bug that would make hashes predictable or rainbow-
    /// table-attackable.
    /// </summary>
    [Fact]
    public void Hash_ShouldProduceDifferentHashes_ForSamePassword_DueToRandomSalt()
    {
        // Arrange
        var password = "MySecurePw!";
        var user = new User("testuser", "initial", false);

        // Act
        var h1 = _sut.HashPassword(user, password);
        var h2 = _sut.HashPassword(user, password);

        // Assert
        Assert.NotEqual(h1, h2);
        Assert.Equal(PasswordVerificationResult.Success, _sut.VerifyHashedPassword(user, h1, password));
        Assert.Equal(PasswordVerificationResult.Success, _sut.VerifyHashedPassword(user, h2, password));
    }

    /// <summary>
    /// Baseline check that verifying a hash against the exact password it was created from succeeds.
    /// </summary>
    [Fact]
    public void Verify_ShouldReturnTrue_ForCorrectPassword()
    {
        var user = new User("u1", "initial", false);
        var hash = _sut.HashPassword(user, "secret");
        Assert.Equal(PasswordVerificationResult.Success, _sut.VerifyHashedPassword(user, hash, "secret"));
    }

    /// <summary>
    /// Baseline check that verifying a hash against a different password fails rather than accidentally
    /// succeeding.
    /// </summary>
    [Fact]
    public void Verify_ShouldReturnFalse_ForWrongPassword()
    {
        var user = new User("u2", "initial", false);
        var hash = _sut.HashPassword(user, "secret");
        Assert.Equal(PasswordVerificationResult.Failed, _sut.VerifyHashedPassword(user, hash, "other"));
    }

    /// <summary>
    /// Ensures the hasher fails closed on malformed or corrupted stored hash values - an empty string, a
    /// hash with too few segments, a non-numeric iteration count, and invalid Base64 salt/key segments -
    /// returning <see cref="PasswordVerificationResult.Failed"/> instead of throwing, so a corrupted
    /// database value cannot crash the login path or accidentally grant access.
    /// </summary>
    /// <param name="malformed">The malformed.</param>
    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("pbkdf2|notanumber|salt|key")]
    [InlineData("pbkdf2|100|###|###")]
    public void Verify_MalformedHash_False(string malformed)
    {
        var user = new User("u3", "initial", false);
        Assert.Equal(PasswordVerificationResult.Failed, _sut.VerifyHashedPassword(user, malformed, "pw"));
    }

    /// <summary>
    /// Verifies the on-the-wire format of a produced hash: a 4-part <c>pbkdf2|iterations|salt|key</c>
    /// string with an iteration count above the safety floor and salt/key byte lengths of 16/32 bytes -
    /// pinning the format so future changes to the hasher are caught if they silently weaken it.
    /// </summary>
    [Fact]
    public void Hash_FormatIsValid()
    {
        var user = new User("u4", "initial", false);
        var hash = _sut.HashPassword(user, "pw");
        var parts = hash.Split('|');
        Assert.Equal(4, parts.Length);
        Assert.Equal("pbkdf2", parts[0]);
        int iterations = int.Parse(parts[1]);
        Assert.True(iterations > 50_000, "iterations should be greater than safety floor");
        var salt = Convert.FromBase64String(parts[2]);
        var key = Convert.FromBase64String(parts[3]);
        Assert.Equal(16, salt.Length);
        Assert.Equal(32, key.Length);
    }
}
