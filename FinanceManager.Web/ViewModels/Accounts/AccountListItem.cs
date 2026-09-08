using FinanceManager.Shared.Dtos;

namespace FinanceManager.Web.ViewModels.Accounts
{
    /// <summary>
    /// View model used to render an account in a list view.
    /// </summary>
    /// <param name="Id">Unique identifier of the account.</param>
    /// <param name="Name">Human readable account name.</param>
    /// <param name="Type">Account type.</param>
    /// <param name="Iban">Optional IBAN for the account.</param>
    /// <param name="CurrentBalance">Current account balance.</param>
    /// <param name="SymbolId">Optional attachment id used as symbol/icon for the account.</param>
    /// <returns>The result.</returns>
    public sealed record AccountListItem(Guid Id, string Name, AccountType Type, string? Iban, decimal CurrentBalance, Guid? SymbolId) : IListItemNavigation
    {
        /// <summary>
        /// Returns the relative navigation URL for this account list item used by the UI.
        /// </summary>
        /// <returns>A string containing the relative URL to the account detail page.</returns>
        public string GetNavigateUrl() => $"/card/accounts/{Id}";
    }
}
