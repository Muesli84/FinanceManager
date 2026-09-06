using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Net;

namespace FinanceManager.Shared.Dtos.Admin;

/// <summary>
/// Request payload for updating security.txt settings.
/// </summary>
/// <param name="Contact">Contact directive.</param>
/// <param name="Expires">Expires directive.</param>
/// <param name="Encryption">Encryption directive.</param>
/// <param name="Acknowledgments">Acknowledgments directive.</param>
/// <param name="PreferredLanguages">Preferred-Languages directive.</param>
/// <param name="Policy">Policy directive.</param>
/// <param name="Hiring">Hiring directive.</param>
/// <param name="Canonical">Canonical directive.</param>
/// <returns>The result.</returns>
public sealed record SecurityTxtSettingsUpdateRequest(
    [Required, MaxLength(2048)] string Contact,
    DateTimeOffset Expires,
    [MaxLength(2048)] string? Encryption,
    [MaxLength(2048)] string? Acknowledgments,
    [MaxLength(2048)] string? PreferredLanguages,
    [MaxLength(2048)] string? Policy,
    [MaxLength(2048)] string? Hiring,
    [MaxLength(2048)] string? Canonical) : IValidatableObject
{
    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Expires <= DateTimeOffset.UtcNow)
        {
            yield return new ValidationResult("Expires must be in the future.", [nameof(Expires)]);
        }

        if (string.IsNullOrWhiteSpace(Canonical))
        {
            yield break;
        }

        if (!Uri.TryCreate(Canonical, UriKind.Absolute, out var canonicalUri) || canonicalUri.Scheme != Uri.UriSchemeHttps)
        {
            yield return new ValidationResult("Canonical must be an absolute HTTPS URL.", [nameof(Canonical)]);
            yield break;
        }

        if (!string.IsNullOrEmpty(canonicalUri.Query))
        {
            yield return new ValidationResult("Canonical must not contain a query string.", [nameof(Canonical)]);
        }

        if (!string.IsNullOrEmpty(canonicalUri.Fragment))
        {
            yield return new ValidationResult("Canonical must not contain a fragment.", [nameof(Canonical)]);
        }

        var host = canonicalUri.Host;
        if (canonicalUri.IsLoopback || string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) || IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address))
        {
            yield return new ValidationResult("Canonical host must not be localhost or a loopback address.", [nameof(Canonical)]);
        }
    }
}
