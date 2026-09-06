using FinanceManager.Infrastructure.Auth;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace FinanceManager.Tests.Infrastructure.Auth;

/// <summary>
/// Guards <see cref="JwtOptionsValidator"/>, the startup-time gate that stops the application from booting with
/// insecure or incomplete JWT configuration. Covers the environment-sensitive rules that are strict in
/// production-like environments (a real, non-placeholder, sufficiently long signing key; a bounded token
/// lifetime) but deliberately relaxed in development (placeholder keys allowed) so local development is not
/// blocked by production-grade secret requirements, plus the rules that always apply regardless of environment
/// (issuer and audience must be configured).
/// </summary>
public sealed class JwtOptionsValidatorTests
{
    /// <summary>
    /// Verifies that a missing signing key is rejected in production-like environments, since an empty key would
    /// let <see cref="JwtTokenService"/> throw at runtime (or worse, silently sign with a weak/default key
    /// depending on library behavior) rather than failing fast at startup.
    /// </summary>
    [Fact]
    public void Validate_ShouldFailInProduction_WhenJwtKeyMissing()
    {
        var result = CreateProductionValidator().Validate(null, CreateValidOptions(key: string.Empty));

        Assert.False(result.Succeeded);
        Assert.Contains("Jwt:Key must be configured", result.FailureMessage);
    }

    /// <summary>
    /// Verifies that a known placeholder value (e.g. copied verbatim from an example/appsettings template and
    /// never replaced) is rejected in production-like environments, guarding against deployments that accidentally
    /// ship with a publicly known, non-secret signing key.
    /// </summary>
    [Fact]
    public void Validate_ShouldFailInProduction_WhenJwtKeyIsPlaceholder()
    {
        var result = CreateProductionValidator().Validate(null, CreateValidOptions(key: "PLEASE_REPLACE_WITH_LONG_RANDOM_256BIT_SECRET_BASE64"));

        Assert.False(result.Succeeded);
        Assert.Contains("placeholder", result.FailureMessage);
    }

    /// <summary>
    /// Verifies that a signing key shorter than the minimum 256-bit (32-byte) length is rejected in
    /// production-like environments, since a short HMAC key is brute-forceable and would let an attacker forge
    /// valid JWTs.
    /// </summary>
    [Fact]
    public void Validate_ShouldFailInProduction_WhenJwtKeyIsShorterThan256Bits()
    {
        var result = CreateProductionValidator().Validate(null, CreateValidOptions(key: "short-key"));

        Assert.False(result.Succeeded);
        Assert.Contains("at least 32 UTF-8 bytes", result.FailureMessage);
    }

    /// <summary>
    /// Verifies that a missing issuer is rejected in every environment, including development, since the issuer
    /// is one of the values <see cref="FinanceManager.Web.Infrastructure.Auth.JwtCookieAuthTokenProvider"/> checks when validating incoming tokens; an
    /// unset issuer would make that validation meaningless.
    /// </summary>
    [Fact]
    public void Validate_ShouldFail_WhenIssuerMissing()
    {
        var result = CreateDevelopmentValidator().Validate(null, CreateValidOptions(issuer: string.Empty));

        Assert.False(result.Succeeded);
        Assert.Contains("Jwt:Issuer must be configured", result.FailureMessage);
    }

    /// <summary>
    /// Verifies that a missing audience is rejected in every environment, including development, mirroring the
    /// issuer check: an unset audience would similarly make the audience validation performed on incoming tokens
    /// meaningless.
    /// </summary>
    [Fact]
    public void Validate_ShouldFail_WhenAudienceMissing()
    {
        var result = CreateDevelopmentValidator().Validate(null, CreateValidOptions(audience: string.Empty));

        Assert.False(result.Succeeded);
        Assert.Contains("Jwt:Audience must be configured", result.FailureMessage);
    }

    /// <summary>
    /// Verifies that a configured token lifetime beyond 1440 minutes (24 hours) is rejected in production-like
    /// environments, preventing a misconfiguration from issuing tokens that stay valid for an unreasonably long
    /// time and widening the window an attacker could exploit a stolen token.
    /// </summary>
    [Fact]
    public void Validate_ShouldFailInProduction_WhenLifetimeExceedsMaximum()
    {
        var result = CreateProductionValidator().Validate(null, CreateValidOptions(lifetimeMinutes: 1441));

        Assert.False(result.Succeeded);
        Assert.Contains("must not exceed 1440", result.FailureMessage);
    }

    /// <summary>
    /// Verifies that the placeholder key is explicitly tolerated in development, so a fresh local checkout using
    /// the shipped example configuration can run immediately without a developer having to generate and configure
    /// a real signing secret first.
    /// </summary>
    [Fact]
    public void Validate_ShouldAllowPlaceholderKeyInDevelopment()
    {
        var result = CreateDevelopmentValidator().Validate(null, CreateValidOptions(key: "PLEASE_REPLACE_WITH_LONG_RANDOM_256BIT_SECRET_BASE64"));

        Assert.True(result.Succeeded);
    }

    /// <summary>
    /// Builds a validator whose host environment reports "Production", exercising the stricter, production-only
    /// validation rules (key strength/placeholder checks, maximum lifetime).
    /// </summary>
    /// <returns>A validator configured for a production-like environment.</returns>
    private static JwtOptionsValidator CreateProductionValidator()
    {
        return new JwtOptionsValidator(new TestHostEnvironment("Production"));
    }

    /// <summary>
    /// Builds a validator whose host environment reports "Development", exercising the relaxed rules that allow a
    /// placeholder signing key and an unbounded lifetime while still requiring issuer and audience.
    /// </summary>
    /// <returns>A validator configured for the development environment.</returns>
    private static JwtOptionsValidator CreateDevelopmentValidator()
    {
        return new JwtOptionsValidator(new TestHostEnvironment(Environments.Development));
    }

    /// <summary>
    /// Builds a fully valid <see cref="JwtOptions"/> instance, letting each test override exactly the one field it
    /// wants to make invalid so the resulting failure can be attributed unambiguously to that field.
    /// </summary>
    /// <param name="key">Signing key to use; defaults to a value that satisfies the production strength check.</param>
    /// <param name="issuer">Issuer to use; defaults to a non-empty value.</param>
    /// <param name="audience">Audience to use; defaults to a non-empty value.</param>
    /// <param name="lifetimeMinutes">Token lifetime in minutes; defaults to a value within the production maximum.</param>
    /// <returns>A <see cref="JwtOptions"/> instance valid under both development and production rules unless overridden.</returns>
    private static JwtOptions CreateValidOptions(
        string key = "test-signing-key-with-sufficient-length-1234567890",
        string issuer = "financemanager",
        string audience = "financemanager",
        int lifetimeMinutes = 30)
    {
        return new JwtOptions
        {
            Key = key,
            Issuer = issuer,
            Audience = audience,
            LifetimeMinutes = lifetimeMinutes
        };
    }

    /// <summary>
    /// Minimal <see cref="IHostEnvironment"/> stand-in that reports a fixed environment name, letting tests select
    /// between production-like and development validation behavior without bootstrapping a real ASP.NET Core host.
    /// </summary>
    private sealed class TestHostEnvironment : IHostEnvironment
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TestHostEnvironment"/> class reporting the given
        /// environment name.
        /// </summary>
        /// <param name="environmentName">Environment name to report, e.g. "Production" or "Development".</param>
        public TestHostEnvironment(string environmentName)
        {
            EnvironmentName = environmentName;
        }

        /// <summary>
        /// Environment name used by <see cref="JwtOptionsValidator"/> to decide whether production-only rules apply.
        /// </summary>
        public string EnvironmentName { get; set; }

        /// <summary>
        /// Application name reported to satisfy the <see cref="IHostEnvironment"/> contract; not exercised by these tests.
        /// </summary>
        public string ApplicationName { get; set; } = "FinanceManager.Tests";

        /// <summary>
        /// Content root path reported to satisfy the <see cref="IHostEnvironment"/> contract; not exercised by these tests.
        /// </summary>
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        /// <summary>
        /// File provider reported to satisfy the <see cref="IHostEnvironment"/> contract; not exercised by these tests.
        /// </summary>
        /// <returns>The result.</returns>
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
