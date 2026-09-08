using FinanceManager.Shared.Dtos.Securities;
using FinanceManager.Web.ViewModels.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.AspNetCore.Components;

namespace FinanceManager.Web.ViewModels.Securities.Categories;

/// <summary>
/// Lightweight list item representing a security category used in list views.
/// </summary>
/// <param name="Id">Identifier of the security category.</param>
/// <param name="Name">Display name of the category.</param>
/// <param name="SymbolId">Optional attachment id used as display symbol.</param>
/// <returns>The result.</returns>
public sealed record SecurityCategoryItem(Guid Id, string Name, Guid? SymbolId) : IListItemNavigation
{
    /// <summary>
    /// Gets the navigation URL to the security category card view.
    /// </summary>
    /// <returns>Relative URL to navigate to the security category card.</returns>
    public string GetNavigateUrl() => $"/card/securities/categories/{Id}";
}

/// <summary>
/// List view model for security categories. Loads available security categories and builds list records for UI rendering.
/// </summary>
public sealed class SecurityCategoriesListViewModel : BaseListViewModel<SecurityCategoryItem>
{
    private readonly FinanceManager.Shared.IApiClient _api;
    private readonly NavigationManager _nav;

    /// <summary>
    /// Initializes a new instance of <see cref="SecurityCategoriesListViewModel"/>.
    /// </summary>
    /// <param name="sp">Service provider used to resolve required services, such as the API client and navigation manager.</param>
    public SecurityCategoriesListViewModel(IServiceProvider sp) : base(sp)
    {
        _api = sp.GetRequiredService<FinanceManager.Shared.IApiClient>();
        _nav = sp.GetRequiredService<NavigationManager>();
    }

    /// <summary>
    /// Disables the search filter UI for security categories (not supported server-side).
    /// </summary>
    public override bool AllowSearchFiltering { get => false; protected set => base.AllowSearchFiltering = value; }

    /// <summary>
    /// Disables the range filter UI for security categories (not applicable).
    /// </summary>
    public override bool AllowRangeFiltering { get => false; protected set => base.AllowRangeFiltering = value; }

    /// <summary>
    /// Loads all security categories from the API and populates the <see cref="FinanceManager.Web.ViewModels.Common.BaseListViewModel{TItem}.Items"/> collection.
    /// </summary>
    /// <param name="ct">Cancellation token used to cancel the operation.</param>
    /// <returns>A task that completes when loading has finished.</returns>
    /// <exception cref="OperationCanceledException">Thrown when the provided cancellation token requests cancellation.</exception>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (!IsAuthenticated) return;
        try
        {
            var list = await _api.SecurityCategories_ListAsync(ct);
            Items.Clear();
            Items.AddRange(list.Select(c => new SecurityCategoryItem(c.Id, c.Name ?? string.Empty, c.SymbolAttachmentId)));
        }
        catch
        {
            Items.Clear();
            RaiseStateChanged();
            return;
        }

        RaiseStateChanged();
    }

    /// <summary>
    /// Loads a page of categories. Categories are small and therefore the implementation loads all categories.
    /// </summary>
    /// <param name="resetPaging">When true resets paging state. Ignored in this implementation.</param>
    /// <returns>A task that completes when the page has been loaded.</returns>
    protected override async Task LoadPageAsync(bool resetPaging)
    {
        // categories are small - load all
        await LoadAsync();
        CanLoadMore = false;
    }

    /// <summary>
    /// Builds list columns and records used by the UI renderer from the current <see cref="FinanceManager.Web.ViewModels.Common.BaseListViewModel{TItem}.Items"/> collection.
    /// </summary>
    protected override void BuildRecords()
    {
        var L = ServiceProvider.GetRequiredService<IStringLocalizer<Pages>>();
        Columns = new List<ListColumn>
        {
            new ListColumn("symbol", string.Empty, "48px", ListColumnAlign.Left),
            new ListColumn("name", L["List_Th_Name"], "", ListColumnAlign.Left)
        };

        Records = Items.Select(i => new ListRecord(new List<ListCell>
        {
            new ListCell(ListCellKind.Symbol, SymbolId: i.SymbolId),
            new ListCell(ListCellKind.Text, Text: i.Name)
        }, i)).ToList();
    }

    /// <summary>
    /// Builds ribbon register definitions for the categories list (navigation and management actions).
    /// </summary>
    /// <param name="localizer">Localizer used to resolve UI labels for ribbon actions.</param>
    /// <returns>Collection of ribbon registers describing available tabs and actions.</returns>
    protected override IReadOnlyList<UiRibbonRegister>? GetRibbonRegisterDefinition(IStringLocalizer localizer)
    {
        var actions = new List<UiRibbonAction>
        {
            new UiRibbonAction("Back", localizer["Ribbon_Back"].Value, "<svg><use href='/icons/sprite.svg#back'/></svg>", UiRibbonItemSize.Small, false, null, () => { RaiseUiActionRequested("Back"); return Task.CompletedTask; }) { MobileShortcut = true }
        };
        var manage = new List<UiRibbonAction>
        {
            new UiRibbonAction("New", localizer["Ribbon_New"].Value, "<svg><use href='/icons/sprite.svg#plus'/></svg>", UiRibbonItemSize.Small, false, null, () => { RaiseUiActionRequested("New"); return Task.CompletedTask; }) { MobileShortcut = true }
        };
        var tabs = new List<UiRibbonTab> { new UiRibbonTab(localizer["Ribbon_Group_Navigation"].Value, actions), new UiRibbonTab(localizer["Ribbon_Group_Manage"].Value, manage) };
        return new List<UiRibbonRegister> { new UiRibbonRegister(UiRibbonRegisterKind.Actions, tabs) };
    }
}
