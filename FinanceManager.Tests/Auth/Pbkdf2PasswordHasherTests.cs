using System;
using System.Linq;
using FinanceManager.Infrastructure.Auth;
using FluentAssertions;
using Xunit;

namespace FinanceManager.Tests.Auth;

public sealed class Pbkdf2PasswordHasherTests
{
    private readonly Pbkdf2PasswordHasher _sut = new();

    [Fact]
    public void Hash_ShouldProduceDifferentHashes_ForSamePassword_DueToRandomSalt()
    {
        // Arrange
        var password = "MySecurePw!";

        // Act
        var h1 = _sut.Hash(password);
        var h2 = _sut.Hash(password);

        // Assert
        h1.Should().NotBe(h2, "salt must be random");
        _sut.Verify(password, h1).Should().BeTrue();
        _sut.Verify(password, h2).Should().BeTrue();
    }

    [Fact]
    public void Verify_ShouldReturnTrue_ForCorrectPassword()
    {
        var hash = _sut.Hash("secret");
        _sut.Verify("secret", hash).Should().BeTrue();
    }

    [Fact]
    public void Verify_ShouldReturnFalse_ForWrongPassword()
    {
        var hash = _sut.Hash("secret");
        _sut.Verify("other", hash).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("pbkdf2|notanumber|salt|key")]
    [InlineData("pbkdf2|100|###|###")]
    public void Verify_MalformedHash_False(string malformed)
    {
        _sut.Verify("pw", malformed).Should().BeFalse();
    }

    [Fact]
    public void Hash_FormatIsValid()
    {
        var hash = _sut.Hash("pw");
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
