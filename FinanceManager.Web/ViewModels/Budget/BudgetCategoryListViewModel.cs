using FinanceManager.Shared;
using FinanceManager.Shared.Dtos.Budget;
using FinanceManager.Web.ViewModels.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace FinanceManager.Web.ViewModels.Budget;

/// <summary>
/// List view model for budget categories.
/// </summary>
public sealed class BudgetCategoryListViewModel : BaseListViewModel<BudgetCategoryListViewModel.BudgetCategoryListItem>
{
    private readonly IApiClient _api;

    /// <summary>
    /// Creates a new instance.
    /// </summary>
    public BudgetCategoryListViewModel(IServiceProvider services) : base(services)
    {
        _api = services.GetRequiredService<IApiClient>();
    }

    /// <inheritdoc />
    public override bool AllowRangeFiltering => true;

    /// <summary>
    /// Initializes the view model, setting the default date range to the current month.
    /// </summary>
    public override async Task InitializeAsync()
    {
        if (RangeFrom == null || RangeTo == null)
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var monthFrom = new DateTime(today.Year, today.Month, 1);
            var monthTo = monthFrom.AddMonths(1).AddDays(-1);
            SetRange(monthFrom, monthTo);
        }

        await base.InitializeAsync();
    }

    /// <inheritdoc />
    protected override async Task LoadPageAsync(bool resetPaging)
    {
        try
        {
            var from = RangeFrom.HasValue ? DateOnly.FromDateTime(RangeFrom.Value) : (DateOnly?)null;
            var to = RangeTo.HasValue ? DateOnly.FromDateTime(RangeTo.Value) : (DateOnly?)null;
            var list = await _api.Budgets_ListCategoriesAsync(from, to);
            Items.Clear();
            Items.AddRange(list.Select(c => new BudgetCategoryListItem(c.Id, c.Name ?? string.Empty, c.Budget, c.Actual, c.Delta, c.PurposeCount)));
            CanLoadMore = false;
        }
        catch (Exception ex)
        {
            SetError(_api.LastErrorCode ?? null, _api.LastError ?? ex.Message);
            CanLoadMore = false;
        }
        finally
        {
            RaiseStateChanged();
        }
    }

    /// <inheritdoc />
    protected override void BuildRecords()
    {
        var L = ServiceProvider.GetRequiredService<IStringLocalizer<Pages>>();
        Columns = new List<ListColumn>
        {
            new ListColumn("name", L["List_Th_Name"], "", ListColumnAlign.Left),
            new ListColumn("budget", L["List_Th_Budget"], "", ListColumnAlign.Right),
            new ListColumn("actual", L["List_Th_Actual"], "", ListColumnAlign.Right),
            new ListColumn("delta", L["List_Th_Delta"], "", ListColumnAlign.Right),
            new ListColumn("purposeCount", L["List_Th_PurposeCount"], "", ListColumnAlign.Right)
        };

        Records = Items.Select(i => new ListRecord(new List<ListCell>
        {
            new ListCell(ListCellKind.Text, Text: i.Name),
            new ListCell(ListCellKind.Text, Text: i.Budget.ToString("N2")),
            new ListCell(ListCellKind.Text, Text: i.Actual.ToString("N2")),
            new ListCell(ListCellKind.Text, Text: i.Delta.ToString("N2")),
            new ListCell(ListCellKind.Text, Text: i.PurposeCount.ToString())
        }, i)).ToList();
    }

    /// <inheritdoc />
    protected override IReadOnlyList<UiRibbonRegister>? GetRibbonRegisterDefinition(IStringLocalizer localizer)
    {
        var nav = new UiRibbonTab(localizer["Ribbon_Group_Navigation"], new List<UiRibbonAction>
        {
            new UiRibbonAction("Back", localizer["Ribbon_Back"].Value, "<svg><use href='/icons/sprite.svg#back'/></svg>", UiRibbonItemSize.Large, false, null, () => { RaiseUiActionRequested("Back", "/list/budget/purposes"); return Task.CompletedTask; }) { MobileShortcut = true }
        });

        var manage = new UiRibbonTab(localizer["Ribbon_Group_Manage"], new List<UiRibbonAction>
        {
            new UiRibbonAction("New", localizer["Ribbon_New"].Value, "<svg><use href='/icons/sprite.svg#plus'/></svg>", UiRibbonItemSize.Large, false, null, () => { RaiseUiActionRequested("New"); return Task.CompletedTask; }) { MobileShortcut = true }
        });

        return new List<UiRibbonRegister> { new UiRibbonRegister(UiRibbonRegisterKind.Actions, new List<UiRibbonTab> { nav, manage }) };
    }

    /// <summary>
    /// Item used in the budget category list.
    /// </summary>
    /// <param name="Id">Identifier of the entity.</param>
    /// <param name="Name">The name.</param>
    /// <param name="Budget">The budget.</param>
    /// <param name="Actual">The actual.</param>
    /// <param name="Delta">The delta.</param>
    /// <param name="PurposeCount">The purpose count.</param>
    /// <returns>The result.</returns>
    public sealed record BudgetCategoryListItem(Guid Id, string Name, decimal Budget, decimal Actual, decimal Delta, int PurposeCount) : IListItemNavigation
    {
        /// <inheritdoc />
        public string GetNavigateUrl() => $"/card/budget/categories/{Id}";
    }
}
