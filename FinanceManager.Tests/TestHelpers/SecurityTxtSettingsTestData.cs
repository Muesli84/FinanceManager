using System;
using FinanceManager.Shared.Dtos.Admin;

namespace FinanceManager.Tests.TestHelpers;

/// <summary>
/// Factory methods for security.txt test fixtures.
/// </summary>
public static class SecurityTxtSettingsTestData
{
    /// <summary>
    /// Returns a fully populated, valid update request.
    /// </summary>
    /// <param name="contact">The contact.</param>
    /// <param name="expires">The expires.</param>
    /// <param name="canonical">The canonical.</param>
    /// <returns>The result.</returns>
    public static SecurityTxtSettingsUpdateRequest ValidRequest(
        string contact = "mailto:security@example.com",
        DateTimeOffset? expires = null,
        string? canonical = null) =>
        new(
            Contact: contact,
            Expires: expires ?? DateTimeOffset.UtcNow.AddYears(1),
            Encryption: "https://example.com/pgp-key.asc",
            Acknowledgments: "https://example.com/thanks",
            PreferredLanguages: "en, de",
            Policy: "https://example.com/security-policy",
            Hiring: "https://example.com/jobs",
            Canonical: canonical);

    /// <summary>
    /// Returns a valid update request including Canonical.
    /// </summary>
    /// <param name="canonical">The canonical.</param>
    /// <returns>The result.</returns>
    public static SecurityTxtSettingsUpdateRequest ValidRequestWithCanonical(
        string canonical = "https://security.example.com/.well-known/security.txt") =>
        ValidRequest(canonical: canonical);

    /// <summary>
    /// Returns a minimal update request with no optional fields set.
    /// </summary>
    /// <param name="contact">The contact.</param>
    /// <returns>The result.</returns>
    public static SecurityTxtSettingsUpdateRequest MinimalRequest(
        string contact = "mailto:security@example.com") =>
        new(
            Contact: contact,
            Expires: DateTimeOffset.UtcNow.AddYears(1),
            Encryption: null,
            Acknowledgments: null,
            PreferredLanguages: null,
            Policy: null,
            Hiring: null,
            Canonical: null);

}
