namespace FinanceManager.Domain.Security;

/// <summary>
/// Value object containing all configurable security.txt directives.
/// </summary>
/// <param name="Contact">The contact.</param>
/// <param name="Expires">The expires.</param>
/// <param name="Encryption">The encryption.</param>
/// <param name="Acknowledgments">The acknowledgments.</param>
/// <param name="PreferredLanguages">The preferred languages.</param>
/// <param name="Policy">The policy.</param>
/// <param name="Hiring">The hiring.</param>
/// <param name="Canonical">The canonical.</param>
/// <returns>The result.</returns>
public sealed record SecurityTxtDirectives(
    string Contact,
    DateTimeOffset Expires,
    string? Encryption,
    string? Acknowledgments,
    string? PreferredLanguages,
    string? Policy,
    string? Hiring,
    string? Canonical);
