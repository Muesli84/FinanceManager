namespace FinanceManager.Web.ViewModels.Securities;

/// <summary>
/// Lightweight list item representing a security used in list views.
/// </summary>
/// <param name="Id">Identifier of the security.</param>
/// <param name="Name">Display name of the security.</param>
/// <param name="Identifier">Ticker, ISIN or other identifier shown in lists.</param>
/// <param name="AlphaVantageCode">Optional external provider code used for price lookups.</param>
/// <param name="CategoryId">Optional category identifier the security belongs to.</param>
/// <param name="CategoryName">Optional category display name.</param>
/// <param name="IsActive">Indicates whether the security is active (not archived).</param>
/// <param name="SymbolId">Optional attachment id used as display symbol.</param>
/// <returns>The result.</returns>
public sealed record SecurityListItem(Guid Id, string Name, string Identifier, string? AlphaVantageCode, Guid? CategoryId, string? CategoryName, bool IsActive, Guid? SymbolId) : IListItemNavigation
{
    /// <summary>
    /// Returns the relative navigation URL to the security card for this item.
    /// </summary>
    /// <returns>Relative URL string to navigate to the security detail card.</returns>
    public string GetNavigateUrl() => $"/card/securities/{Id}";
}
