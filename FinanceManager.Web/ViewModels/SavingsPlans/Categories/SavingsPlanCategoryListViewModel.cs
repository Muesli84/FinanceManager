using FinanceManager.Shared;
using FinanceManager.Shared.Dtos.SavingsPlans;
using FinanceManager.Web.ViewModels.Common;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Components;

namespace FinanceManager.Web.ViewModels.SavingsPlans.Categories;

/// <summary>
/// List view model for savings plan categories. Loads category items from the API and
/// exposes list records for rendering in the UI.
/// </summary>
public sealed class SavingsPlanCategoryListViewModel : BaseListViewModel<SavingsPlanCategoryListViewModel.SavingsPlanCategoryListItem>
{
    private readonly IApiClient _api;

    /// <summary>
    /// Initializes a new instance of <see cref="SavingsPlanCategoryListViewModel"/>.
    /// </summary>
    /// <param name="services">Service provider used to resolve required services such as <see cref="IApiClient"/>.</param>
    public SavingsPlanCategoryListViewModel(IServiceProvider services) : base(services)
    {
        _api = services.GetRequiredService<IApiClient>();
    }

    /// <summary>
    /// Indicates whether the list supports range filtering. Savings plan categories do not support range filtering.
    /// </summary>
    public override bool AllowRangeFiltering => false;

    /// <summary>
    /// Loads a page of category items from the API and populates the <see cref="FinanceManager.Web.ViewModels.Common.BaseListViewModel{TItem}.Items"/> collection.
    /// This implementation ignores paging and loads all categories.
    /// </summary>
    /// <param name="resetPaging">When true the paging state should be reset; ignored in this implementation.</param>
    /// <returns>A task that completes when the page has been loaded.</returns>
    /// <exception cref="OperationCanceledException">May be thrown if the underlying API call is cancelled by a caller that passes a cancellation token (not used here).</exception>
    protected override async Task LoadPageAsync(bool resetPaging)
    {
        try
        {
            var list = await _api.SavingsPlanCategories_ListAsync();
            Items.Clear();
            if (list != null)
            {
                Items.AddRange(list.Select(c => new SavingsPlanCategoryListItem(c.Id, c.Name ?? string.Empty, c.SymbolAttachmentId)));
            }
            CanLoadMore = false;
        }
        catch (Exception ex)
        {
            SetError(_api.LastErrorCode ?? null, _api.LastError ?? ex.Message);
            CanLoadMore = false;
        }
        finally { RaiseStateChanged(); }
    }

    /// <summary>
    /// Builds the column definitions and the list records used by the UI renderer.
    /// </summary>
    protected override void BuildRecords()
    {
        var L = ServiceProvider.GetRequiredService<IStringLocalizer<Pages>>();
        Columns = new List<ListColumn>
        {
            new ListColumn("symbol", string.Empty, "56px", ListColumnAlign.Left),
            new ListColumn("name", L["List_Th_SavingsPlan_Name"], "", ListColumnAlign.Left)
        };

        Records = Items.Select(i => new ListRecord(new List<ListCell>
        {
            new ListCell(ListCellKind.Symbol, SymbolId: i.SymbolId),
            new ListCell(ListCellKind.Text, Text: i.Name)
        }, i)).ToList();
    }

    /// <summary>
    /// Provides ribbon register definitions for the list view (navigation and create action).
    /// </summary>
    /// <param name="localizer">Localizer used to resolve UI labels for ribbon actions.</param>
    /// <returns>A list of <see cref="UiRibbonRegister"/> instances describing available actions.</returns>
    protected override IReadOnlyList<UiRibbonRegister>? GetRibbonRegisterDefinition(Microsoft.Extensions.Localization.IStringLocalizer localizer)
    {
        var nav = new UiRibbonTab(localizer["Ribbon_Group_Navigation"], new List<UiRibbonAction>
        {
            new UiRibbonAction("Back", localizer["Ribbon_Back"].Value, "<svg><use href='/icons/sprite.svg#back'/></svg>", UiRibbonItemSize.Large, false, null, () => {
                var nav = ServiceProvider.GetRequiredService<NavigationManager>();
                nav.NavigateTo("/list/savings-plans");
                return Task.CompletedTask;
            })
        });

        var manage = new UiRibbonTab(localizer["Ribbon_Group_Manage"], new List<UiRibbonAction>
        {
            new UiRibbonAction("New", localizer["Ribbon_New"].Value, "<svg><use href='/icons/sprite.svg#plus'/></svg>", UiRibbonItemSize.Large, false, null, () => {
                RaiseUiActionRequested("New");
                return Task.CompletedTask;
            })
        });

        return new List<UiRibbonRegister> { new UiRibbonRegister(UiRibbonRegisterKind.Actions, new List<UiRibbonTab> { nav, manage }) };
    }

    /// <summary>
    /// Immutable list item used by the savings plan category list. Implements navigation to the category card.
    /// </summary>
    /// <param name="Id">Category identifier.</param>
    /// <param name="Name">Display name of the category.</param>
    /// <param name="SymbolId">Optional symbol attachment id.</param>
    /// <returns>The result.</returns>
    public sealed record SavingsPlanCategoryListItem(Guid Id, string Name, Guid? SymbolId) : IListItemNavigation
    {
        /// <summary>
        /// Returns the navigation URL for the item's detail card.
        /// </summary>
        /// <returns>Detail card route for the category.</returns>
        public string GetNavigateUrl() => $"/card/savings-plans/categories/{Id}";
    }
}
