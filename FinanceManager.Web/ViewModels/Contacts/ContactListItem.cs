using FinanceManager.Web.ViewModels.Common;

namespace FinanceManager.Web.ViewModels.Contacts;

/// <summary>
/// View model item used to render a contact in a list. Implements navigation helper to the contact card.
/// </summary>
/// <param name="Id">Unique identifier of the contact.</param>
/// <param name="Name">Display name of the contact.</param>
/// <param name="Type">Contact type name (e.g. "Person" or "Organization").</param>
/// <param name="CategoryName">Optional category name assigned to the contact.</param>
/// <param name="SymbolId">Optional attachment id used as a symbol/icon for the contact.</param>
/// <returns>The result.</returns>
public sealed record ContactListItem(Guid Id, string Name, string Type, string? CategoryName, Guid? SymbolId) : IListItemNavigation
{
    /// <summary>
    /// Returns the relative navigation URL for this contact list item used by the UI.
    /// </summary>
    /// <returns>A string containing the relative URL to the contact card page.</returns>
    public string GetNavigateUrl() => $"/card/contacts/{Id}";
}
