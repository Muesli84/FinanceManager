namespace FinanceManager.Web.Services.Help;

/// <summary>
/// Search index payload for help documents.
/// </summary>
/// <param name="Documents">The searchable help documents.</param>
/// <returns>The result.</returns>
public sealed record HelpSearchIndexDto(IReadOnlyList<HelpSearchDocumentDto> Documents);
