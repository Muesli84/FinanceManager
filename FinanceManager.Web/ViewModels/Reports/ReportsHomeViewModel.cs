using FinanceManager.Shared;

namespace FinanceManager.Web.ViewModels.Reports;

/// <summary>
/// View model for the reports home page. Exposes a list of saved favorites and handles loading them from the API.
/// </summary>
public sealed class ReportsHomeViewModel : ViewModelBase
{
    private readonly IApiClient _api;

    /// <summary>
    /// Initializes a new instance of <see cref="ReportsHomeViewModel"/>.
    /// </summary>
    /// <param name="sp">Service provider used to resolve required services (API client).</param>
    public ReportsHomeViewModel(IServiceProvider sp) : base(sp)
    {
        _api = sp.GetRequiredService<IApiClient>();
    }

    /// <summary>
    /// Indicates whether favorites are currently being loaded.
    /// </summary>
    public bool Loading { get; private set; }

    /// <summary>
    /// Collection of saved report favorites available to the user.
    /// </summary>
    /// <returns>The result.</returns>
    public List<ReportFavoriteDto> Favorites { get; } = new();

    /// <summary>
    /// Optional asynchronous initialization entry point called by consumers. When the user is not authenticated
    /// the method requests authentication via <see cref="FinanceManager.Web.ViewModels.ViewModelBase.RequireAuthentication(string?)"/>. Otherwise it loads favorites.
    /// </summary>
    /// <param name="ct">Cancellation token used to cancel initialization.</param>
    /// <returns>A ValueTask that completes when initialization is finished.</returns>
    public override async ValueTask InitializeAsync(CancellationToken ct = default)
    {
        if (!IsAuthenticated)
        {
            RequireAuthentication(null);
            return;
        }
        await ReloadAsync(ct);
        RaiseStateChanged();
    }

    /// <summary>
    /// Reloads the favorites list from the API and updates the <see cref="Favorites"/> collection.
    /// </summary>
    /// <param name="ct">Cancellation token used to cancel the reload operation.</param>
    /// <returns>A task that completes when the reload has finished.</returns>
    public async Task ReloadAsync(CancellationToken ct = default)
    {
        if (Loading) { return; }
        Loading = true; RaiseStateChanged();
        try
        {
            var list = await _api.Reports_ListFavoritesAsync(ct);
            Favorites.Clear();
            Favorites.AddRange(list.OrderBy(f => f.Name));
        }
        catch { }
        finally { Loading = false; RaiseStateChanged(); }
    }

    /// <summary>
    /// Provides ribbon register definitions for the reports home view. The register contains actions such as reload and create new report.
    /// </summary>
    /// <param name="localizer">Localizer used to resolve label texts for ribbon actions.</param>
    /// <returns>A list of <see cref="UiRibbonRegister"/> instances or <c>null</c> when none are provided.</returns>
    protected override IReadOnlyList<UiRibbonRegister>? GetRibbonRegisterDefinition(Microsoft.Extensions.Localization.IStringLocalizer localizer)
    {
        var actions = new List<UiRibbonAction>
        {
            new UiRibbonAction("Reload", localizer["Ribbon_Reload"].Value, "<svg><use href='/icons/sprite.svg#refresh'/></svg>", UiRibbonItemSize.Small, Loading, null, new Func<Task>(() => { _ = ReloadAsync(); return Task.CompletedTask; })) { MobileShortcut = true },
            new UiRibbonAction("NewReport", localizer["Ribbon_NewReport"].Value, "<svg><use href='/icons/sprite.svg#plus'/></svg>", UiRibbonItemSize.Large, false, null, new Func<Task>(() => { RaiseUiActionRequested("New"); return Task.CompletedTask; }))
        };
        var tabs = new List<UiRibbonTab> { new UiRibbonTab(localizer["Ribbon_Group_Actions"].Value, actions) };
        return new List<UiRibbonRegister> { new UiRibbonRegister(UiRibbonRegisterKind.Actions, tabs) };
    }
}
