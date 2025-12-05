using FinanceManager.Domain.Users;
using FinanceManager.Infrastructure.Auth;
using Microsoft.AspNetCore.Identity;

namespace FinanceManager.Tests.Auth;

public sealed class Pbkdf2PasswordHasherTests
{
    private readonly Pbkdf2IdentityPasswordHasher _sut = new();

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

    [Fact]
    public void Verify_ShouldReturnTrue_ForCorrectPassword()
    {
        var user = new User("u1", "initial", false);
        var hash = _sut.HashPassword(user, "secret");
        Assert.Equal(PasswordVerificationResult.Success, _sut.VerifyHashedPassword(user, hash, "secret"));
    }

    [Fact]
    public void Verify_ShouldReturnFalse_ForWrongPassword()
    {
        var user = new User("u2", "initial", false);
        var hash = _sut.HashPassword(user, "secret");
        Assert.Equal(PasswordVerificationResult.Failed, _sut.VerifyHashedPassword(user, hash, "other"));
    }

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
