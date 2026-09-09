using FinanceManager.Shared.Dtos.Users;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;
using static FinanceManager.Web.ViewModels.Setup.UserListViewModel;

namespace FinanceManager.Web.ViewModels.Setup;

/// <summary>
/// Represents a single user row in the user list and provides navigation to the user card.
/// </summary>
/// <param name="Id">Identifier of the user.</param>
/// <param name="Username">Username displayed in the list.</param>
/// <param name="IsAdmin">Whether the user has administrative rights.</param>
/// <param name="Active">Whether the user account is active.</param>
/// <param name="LockoutEnd">Optional lockout end date/time (UTC) when the account is blocked.</param>
/// <param name="LastLoginUtc">Optional last login timestamp (UTC).</param>
/// <returns>The result.</returns>
public record UserListItem(Guid Id, string Username, bool IsAdmin, bool Active, DateTime? LockoutEnd, DateTime? LastLoginUtc) : IListItemNavigation
{
    /// <summary>
    /// Returns the navigation URL for the user's detail card.
    /// </summary>
    /// <returns>Relative URL to the user card.</returns>
    public string GetNavigateUrl() => $"/card/users/{Id}";
}

/// <summary>
/// View model for the users list. Loads users from the admin API and exposes list records for rendering.
/// </summary>
public sealed class UserListViewModel : BaseListViewModel<UserListItem>
{
    /// <summary>
    /// Initializes a new instance of <see cref="UserListViewModel"/>.
    /// </summary>
    /// <param name="sp">Service provider used to resolve dependencies (localizer, api client provided by base).</param>
    public UserListViewModel(IServiceProvider sp)
        : base(sp)
    {
    }

    /// <summary>
    /// Loads the full user list from the admin API and populates the view model's <see cref="FinanceManager.Web.ViewModels.Common.BaseListViewModel{TItem}.Items"/> collection.
    /// This method is safe to call from the UI and will clear the list on failure.
    /// </summary>
    /// <param name="ct">Cancellation token used to cancel the operation.</param>
    /// <returns>A task that completes when loading has finished.</returns>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (!IsAuthenticated) return;
        try
        {
            var list = await ApiClient.Admin_ListUsersAsync(ct);
            Items.Clear();
            if (list != null)
            {
                Items.AddRange(list.Select(u => new UserListItem(u.Id, u.Username, u.IsAdmin, u.Active, u.LockoutEnd, u.LastLoginUtc)));
            }
        }
        catch
        {
            Items.Clear();
        }
        BuildRecords();
        RaiseStateChanged();
    }

    /// <summary>
    /// Loads a page of items. For the users list paging is disabled and the full list is loaded.
    /// </summary>
    /// <param name="resetPaging">When true the paging state should be reset; ignored in this implementation.</param>
    protected override async Task LoadPageAsync(bool resetPaging)
    {
        // The generic list provider expects paging support; for users we load full list and disable paging
        await LoadAsync(CancellationToken.None);
        CanLoadMore = false;
    }

    /// <summary>
    /// Builds the column definitions and list records used by the UI renderer based on <see cref="FinanceManager.Web.ViewModels.Common.BaseListViewModel{TItem}.Items"/>.
    /// </summary>
    protected override void BuildRecords()
    {
        var L = ServiceProvider.GetRequiredService<IStringLocalizer<Pages>>();
        Columns = new List<ListColumn>
        {
            new ListColumn("username", L["UserList_Th_Username"].Value ?? "Username", "", ListColumnAlign.Left),
            new ListColumn("admin", L["UserList_Th_Admin"].Value ?? "Admin", "80px", ListColumnAlign.Left),
            new ListColumn("active", L["UserList_Th_Active"].Value ?? "Active", "80px", ListColumnAlign.Left),
            new ListColumn("lockedUntil", L["UserList_Th_LockedUntil"].Value ?? "LockedUntil", "160px", ListColumnAlign.Left),
            new ListColumn("lastLogin", L["UserList_Th_LastLogin"].Value ?? "LastLogin", "160px", ListColumnAlign.Left)
        };

        Records = Items.Select(i => new ListRecord(new List<ListCell>
        {
            new ListCell(ListCellKind.Text, Text: i.Username),
            new ListCell(ListCellKind.Text, Text: i.IsAdmin ? (L["Value_Yes"].Value ?? "Yes") : (L["Value_No"].Value ?? "No")),
            new ListCell(ListCellKind.Text, Text: i.Active ? (L["Value_Yes"].Value ?? "Yes") : (L["Value_No"].Value ?? "No")),
            new ListCell(ListCellKind.Text, Text: i.LockoutEnd?.ToString("u") ?? "-"),
            new ListCell(ListCellKind.Text, Text: i.LastLoginUtc?.ToString("u") ?? "-")
        }, i)).ToList();
    }

    /// <summary>
    /// Provides ribbon register definitions for the users list including a New action.
    /// </summary>
    /// <param name="localizer">Localizer used to resolve UI labels.</param>
    /// <returns>A collection of <see cref="UiRibbonRegister"/> instances or <c>null</c> when none are provided.</returns>
    protected override IReadOnlyList<UiRibbonRegister>? GetRibbonRegisterDefinition(IStringLocalizer localizer)
    {
        var actions = new List<UiRibbonAction>
        {
            new UiRibbonAction(
                "New",
                localizer["Ribbon_New"].Value,
                "<svg><use href='/icons/sprite.svg#plus'/></svg>",
                UiRibbonItemSize.Large,
                false,
                null,
                new Func<Task>(() => { RaiseUiActionRequested("New"); return Task.CompletedTask; })
            )
        };

        var tabs = new List<UiRibbonTab> { new UiRibbonTab(localizer["Ribbon_Group_Manage"].Value, actions) };
        return new List<UiRibbonRegister> { new UiRibbonRegister(UiRibbonRegisterKind.Actions, tabs) };
    }
}
