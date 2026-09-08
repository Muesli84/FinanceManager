namespace FinanceManager.Application.Contacts;

/// <summary>
/// Catalog of known contact definitions shipped with the application.
/// </summary>
public interface IKnownContactCatalog
{
    /// <summary>
    /// Finds one unambiguous known contact for the provided statement texts.
    /// </summary>
    /// <param name="searchTexts">Texts from a statement entry, such as recipient, subject or booking description.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The unique match, or null when no or multiple definitions match.</returns>
    Task<KnownContactMatch?> FindMatchAsync(IEnumerable<string?> searchTexts, CancellationToken ct);
}

/// <summary>
/// Result returned when a known contact definition matched a statement entry.
/// </summary>
/// <param name="Name">Display name for the contact.</param>
/// <param name="Type">Contact type to create.</param>
/// <param name="Aliases">Alias patterns to store for the created contact.</param>
/// <returns>The result.</returns>
public sealed record KnownContactMatch(string Name, ContactType Type, IReadOnlyList<string> Aliases);
