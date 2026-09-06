using FinanceManager.Web.ViewModels.Common;

namespace FinanceManager.Web.ViewModels.SavingsPlans;

/// <summary>
/// Lightweight list item representing a savings plan used in list views.
/// </summary>
/// <param name="Id">Identifier of the savings plan.</param>
/// <param name="Name">Display name of the savings plan.</param>
/// <param name="Type">Type description of the savings plan.</param>
/// <param name="CategoryName">Optional category display name.</param>
/// <param name="SymbolId">Optional attachment id used as display symbol.</param>
/// <returns>The result.</returns>
public sealed record SavingsPlanListItem(Guid Id, string Name, string Type, string? CategoryName, Guid? SymbolId) : IListItemNavigation
{
    /// <summary>
    /// Gets the navigation URL to the savings plan card view.
    /// </summary>
    /// <returns>Relative URL to navigate to the savings plan card.</returns>
    public string GetNavigateUrl() => $"/card/savings-plans/{Id}";
}
