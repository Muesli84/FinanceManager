using FinanceManager.Shared;
using FinanceManager.Web.ViewModels.Common;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Components;

namespace FinanceManager.Web.ViewModels.Contacts.Groups;

/// <summary>
/// List view model for contact categories (groups). Provides loading and rendering logic for the contact category overview list.
/// </summary>
public sealed class ContactGroupListViewModel : BaseListViewModel<ContactGroupListViewModel.ContactGroupListItem>
{
    private readonly IApiClient _api;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContactGroupListViewModel"/> class.
    /// </summary>
    /// <param name="services">Service provider used to resolve required services such as <see cref="IApiClient"/> and <see cref="IStringLocalizer{Pages}"/>.</param>
    public ContactGroupListViewModel(IServiceProvider services) : base(services)
    {
        _api = services.GetRequiredService<IApiClient>();
    }

    /// <summary>
    /// Indicates whether the list supports range filtering. Contact groups do not support range filtering.
    /// </summary>
    public override bool AllowRangeFiltering => false;

    /// <summary>
    /// Loads the page of contact group items. The implementation replaces the Items collection with the full set
    /// of contact categories returned by the API and disables further paging.
    /// </summary>
    /// <param name="resetPaging">When <c>true</c> the paging state should be reset by the implementation.</param>
    /// <returns>A task that completes when the load operation has finished.</returns>
    /// <exception cref="System.Exception">Propagates exceptions thrown by the underlying API client.</exception>
    protected override async Task LoadPageAsync(bool resetPaging)
    {
        try
        {
            var list = await _api.ContactCategories_ListAsync();
            Items.Clear();
            if (list != null)
            {
                Items.AddRange(list.Select(c => new ContactGroupListItem(c.Id, c.Name ?? string.Empty, c.SymbolAttachmentId)));
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
    /// Builds column definitions and list records used by the UI to render the contact categories table.
    /// </summary>
    protected override void BuildRecords()
    {
        var L = ServiceProvider.GetRequiredService<IStringLocalizer<Pages>>();
        Columns = new List<ListColumn>
        {
            new ListColumn("symbol", string.Empty, "56px", ListColumnAlign.Left),
            new ListColumn("name", L["List_Th_Contact_Name"], "", ListColumnAlign.Left)
        };

        Records = Items.Select(i => new ListRecord(new List<ListCell>
        {
            new ListCell(ListCellKind.Symbol, SymbolId: i.SymbolId),
            new ListCell(ListCellKind.Text, Text: i.Name)
        }, i)).ToList();
    }

    /// <summary>
    /// Returns ribbon register definitions for the contact group list view. The provided <paramref name="localizer"/> is used to resolve UI labels.
    /// </summary>
    /// <param name="localizer">Localizer used to obtain localized labels for ribbon actions.</param>
    /// <returns>A list of <see cref="UiRibbonRegister"/> describing the ribbon tabs and actions for this view.</returns>
    protected override IReadOnlyList<UiRibbonRegister>? GetRibbonRegisterDefinition(IStringLocalizer localizer)
    {
        var tab = new UiRibbonTab(localizer["Ribbon_Group_Navigation"], new List<UiRibbonAction>
        {
            new UiRibbonAction("New", localizer["Ribbon_New"], "<svg><use href='/icons/sprite.svg#plus'/></svg>", UiRibbonItemSize.Large, false, null, () => {
                RaiseUiActionRequested("New");
                return Task.CompletedTask;
            }),
            new UiRibbonAction("Back", localizer["Ribbon_Back"], "<svg><use href='/icons/sprite.svg#back'/></svg>", UiRibbonItemSize.Small, false, null, () => {
                var nav = ServiceProvider.GetRequiredService<NavigationManager>();
                nav.NavigateTo("/list/contacts");
                return Task.CompletedTask;
            })
        });

        return new List<UiRibbonRegister> { new UiRibbonRegister(UiRibbonRegisterKind.Actions, new List<UiRibbonTab> { tab }) };
    }

    /// <summary>
    /// Represents a single contact category item shown in the list and provides navigation support.
    /// </summary>
    /// <param name="Id">Category identifier.</param>
    /// <param name="Name">Display name of the category.</param>
    /// <param name="SymbolId">Optional attachment id used as the category symbol.</param>
    /// <returns>The result.</returns>
    public sealed record ContactGroupListItem(Guid Id, string Name, Guid? SymbolId) : IListItemNavigation
    {
        /// <summary>
        /// Returns the navigation URL for the category card.
        /// </summary>
        /// <returns>Relative URL to the category card page.</returns>
        public string GetNavigateUrl() => $"/card/contacts/categories/{Id}";
    }
}
