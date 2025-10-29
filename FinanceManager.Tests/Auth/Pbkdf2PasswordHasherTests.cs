using System;
using System.Linq;
using FinanceManager.Infrastructure.Auth;
using FluentAssertions;
using Xunit;
using Microsoft.AspNetCore.Identity;
using FinanceManager.Domain.Users;

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
        h1.Should().NotBe(h2, "salt must be random");
        _sut.VerifyHashedPassword(user, h1, password).Should().Be(PasswordVerificationResult.Success);
        _sut.VerifyHashedPassword(user, h2, password).Should().Be(PasswordVerificationResult.Success);
    }

    [Fact]
    public void Verify_ShouldReturnTrue_ForCorrectPassword()
    {
        var user = new User("u1", "initial", false);
        var hash = _sut.HashPassword(user, "secret");
        _sut.VerifyHashedPassword(user, hash, "secret").Should().Be(PasswordVerificationResult.Success);
    }

    [Fact]
    public void Verify_ShouldReturnFalse_ForWrongPassword()
    {
        var user = new User("u2", "initial", false);
        var hash = _sut.HashPassword(user, "secret");
        _sut.VerifyHashedPassword(user, hash, "other").Should().Be(PasswordVerificationResult.Failed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("pbkdf2|notanumber|salt|key")]
    [InlineData("pbkdf2|100|###|###")]
    public void Verify_MalformedHash_False(string malformed)
    {
        var user = new User("u3", "initial", false);
        _sut.VerifyHashedPassword(user, malformed, "pw").Should().Be(PasswordVerificationResult.Failed);
    }

    [Fact]
    public void Hash_FormatIsValid()
    {
        var user = new User("u4", "initial", false);
        var hash = _sut.HashPassword(user, "pw");
        var parts = hash.Split('|');
        parts.Length.Should().Be(4);
        parts[0].Should().Be("pbkdf2");
        int iterations = int.Parse(parts[1]);
        iterations.Should().BeGreaterThan(50_000); // safety floor
        var salt = Convert.FromBase64String(parts[2]);
        var key = Convert.FromBase64String(parts[3]);
        salt.Length.Should().Be(16);
        key.Length.Should().Be(32);
    }
}
