namespace FinanceManager.Web.Services.Help;

/// <summary>
/// A single searchable help document entry.
/// </summary>
/// <param name="Id">The route-safe help document identifier.</param>
/// <param name="Title">The display title.</param>
/// <param name="Excerpt">A short searchable excerpt.</param>
/// <param name="Keywords">Normalized search keywords.</param>
/// <returns>The result.</returns>
public sealed record HelpSearchDocumentDto(string Id, string Title, string Excerpt, IReadOnlyList<string> Keywords);
