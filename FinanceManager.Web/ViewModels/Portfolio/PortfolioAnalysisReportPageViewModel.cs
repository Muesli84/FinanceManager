using FinanceManager.Shared.Dtos.Portfolio;
using FinanceManager.Web.ViewModels.Common;
using Microsoft.Extensions.Localization;

namespace FinanceManager.Web.ViewModels.Portfolio;

/// <summary>
/// View model for the portfolio analysis report page. Manages loading the (cached) report, entering/leaving
/// edit mode for the KPI tile configuration, saving configuration changes and forcing a cache refresh.
/// </summary>
public sealed class PortfolioAnalysisReportPageViewModel : BaseViewModel
{
    /// <summary>
    /// Creates a new instance.
    /// </summary>
    /// <param name="services">Service provider.</param>
    public PortfolioAnalysisReportPageViewModel(IServiceProvider services) : base(services) { }

    /// <summary>
    /// The computed portfolio analysis report, or <c>null</c> before the first successful load.
    /// </summary>
    public PortfolioAnalysisReportDto? PortfolioReportData { get; private set; }

    /// <summary>
    /// The current user's KPI tile configuration, or <c>null</c> before the first successful load.
    /// </summary>
    public PortfolioKpiConfigurationDto? CurrentConfiguration { get; private set; }

    /// <summary>
    /// Whether the page is currently in edit mode (tile order/visibility editing).
    /// </summary>
    public bool IsEditMode { get; private set; }

    /// <summary>
    /// Tile order currently being edited. Only meaningful while <see cref="IsEditMode"/> is <c>true</c>.
    /// </summary>
    /// <returns>The result.</returns>
    public List<PortfolioTileId> EditOrder { get; } = new();

    /// <summary>
    /// Tile ids currently marked as active/visible while editing. Only meaningful while <see cref="IsEditMode"/> is <c>true</c>.
    /// </summary>
    /// <returns>The result.</returns>
    public HashSet<PortfolioTileId> EditActive { get; } = new();

    /// <summary>
    /// Marks the given tile as active or inactive within the current edit session.
    /// </summary>
    /// <param name="tile">Tile to toggle.</param>
    /// <param name="isActive">Whether the tile should be active.</param>
    public void ToggleTileActive(PortfolioTileId tile, bool isActive)
    {
        if (isActive) { EditActive.Add(tile); }
        else { EditActive.Remove(tile); }
        RaiseStateChanged();
    }

    /// <summary>
    /// Moves the tile at <paramref name="index"/> within <see cref="EditOrder"/> by <paramref name="delta"/> positions.
    /// </summary>
    /// <param name="index">Current index of the tile to move.</param>
    /// <param name="delta">Offset to move the tile by (e.g. -1 to move up, 1 to move down).</param>
    public void MoveEditTile(int index, int delta)
    {
        var newIndex = index + delta;
        if (newIndex < 0 || newIndex >= EditOrder.Count) { return; }
        (EditOrder[index], EditOrder[newIndex]) = (EditOrder[newIndex], EditOrder[index]);
        RaiseStateChanged();
    }

    /// <summary>
    /// Loads the portfolio analysis report for the current user.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task LoadReportAsync(CancellationToken ct = default)
    {
        if (!CheckAuthentication()) { return; }

        Loading = true;
        RaiseStateChanged();

        try
        {
            PortfolioReportData = await ApiClient.Portfolio_GetAnalysisReportAsync(ct);
            CurrentConfiguration = await ApiClient.Portfolio_GetKpiConfigurationAsync(ct);
        }
        catch (Exception ex)
        {
            SetError(ApiClient.LastErrorCode ?? string.Empty, ApiClient.LastError ?? ex.Message);
        }
        finally
        {
            Loading = false;
            RaiseStateChanged();
        }
    }

    /// <summary>
    /// Loads the current KPI tile configuration and switches the page into edit mode.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task EnterEditModeAsync(CancellationToken ct = default)
    {
        if (!CheckAuthentication()) { return; }

        try
        {
            CurrentConfiguration = await ApiClient.Portfolio_GetKpiConfigurationAsync(ct);
            IsEditMode = true;
            EditOrder.Clear();
            EditOrder.AddRange(CurrentConfiguration.TileOrder);
            EditActive.Clear();
            foreach (var tile in CurrentConfiguration.ActiveTileIds) { EditActive.Add(tile); }
        }
        catch (Exception ex)
        {
            SetError(ApiClient.LastErrorCode ?? string.Empty, ApiClient.LastError ?? ex.Message);
        }

        RaiseStateChanged();
    }

    /// <summary>
    /// Leaves edit mode without persisting changes.
    /// </summary>
    public void CancelEditMode()
    {
        IsEditMode = false;
        EditOrder.Clear();
        EditActive.Clear();
        RaiseStateChanged();
    }

    /// <summary>
    /// Persists the tile order/visibility currently held in <see cref="EditOrder"/> and <see cref="EditActive"/>.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task SaveEditConfigurationAsync(CancellationToken ct = default)
    {
        var request = new PortfolioKpiConfigurationRequest
        {
            ActiveTileIds = EditOrder.Where(t => EditActive.Contains(t)).ToList(),
            TileOrder = EditOrder.ToList()
        };

        await SaveConfigurationAsync(request, ct);
        if (!IsEditMode)
        {
            EditOrder.Clear();
            EditActive.Clear();
        }
    }

    /// <summary>
    /// Persists the given KPI tile configuration, invalidates the report cache and reloads the report.
    /// </summary>
    /// <param name="newConfig">Configuration payload to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task SaveConfigurationAsync(PortfolioKpiConfigurationRequest newConfig, CancellationToken ct = default)
    {
        if (!CheckAuthentication()) { return; }

        Loading = true;
        RaiseStateChanged();

        try
        {
            var saved = await ApiClient.Portfolio_SaveKpiConfigurationAsync(newConfig, ct);
            if (saved == null)
            {
                SetError(ApiClient.LastErrorCode ?? string.Empty, ApiClient.LastError ?? "Speichern fehlgeschlagen.");
                return;
            }

            CurrentConfiguration = saved;
            IsEditMode = false;
            PortfolioReportData = await ApiClient.Portfolio_GetAnalysisReportAsync(ct);
        }
        catch (Exception ex)
        {
            SetError(ApiClient.LastErrorCode ?? string.Empty, ApiClient.LastError ?? ex.Message);
        }
        finally
        {
            Loading = false;
            RaiseStateChanged();
        }
    }

    /// <summary>
    /// Forces recalculation of the portfolio analysis report by invalidating the server-side cache and reloading.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task RefreshReportAsync(CancellationToken ct = default)
    {
        if (!CheckAuthentication()) { return; }

        Loading = true;
        RaiseStateChanged();

        try
        {
            await ApiClient.Portfolio_ResetCacheAsync(ct);
            PortfolioReportData = await ApiClient.Portfolio_GetAnalysisReportAsync(ct);
        }
        catch (Exception ex)
        {
            SetError(ApiClient.LastErrorCode ?? string.Empty, ApiClient.LastError ?? ex.Message);
        }
        finally
        {
            Loading = false;
            RaiseStateChanged();
        }
    }

    /// <inheritdoc />
    protected override IReadOnlyList<UiRibbonRegister>? GetRibbonRegisterDefinition(IStringLocalizer localizer)
    {
        var actions = new List<UiRibbonAction>
        {
            new UiRibbonAction(
                "Back",
                localizer["Ribbon_Back"].Value,
                "<svg><use href='/icons/sprite.svg#back'/></svg>",
                UiRibbonItemSize.Large,
                false,
                null,
                () => { Navigation.NavigateTo("/card/securities"); return Task.CompletedTask; }
            ),
            new UiRibbonAction(
                "Refresh",
                localizer["Ribbon_Refresh"].Value,
                "<svg><use href='/icons/sprite.svg#refresh'/></svg>",
                UiRibbonItemSize.Large,
                false,
                null,
                async () => { await RefreshReportAsync(); }
            ),
        };

        if (IsEditMode)
        {
            actions.Add(new UiRibbonAction(
                "SaveEdit",
                localizer["PortfolioReport_Save"].Value,
                "<svg><use href='/icons/sprite.svg#save'/></svg>",
                UiRibbonItemSize.Large,
                EditActive.Count == 0,
                null,
                async () => { await SaveEditConfigurationAsync(); }
            ));
            actions.Add(new UiRibbonAction(
                "CancelEdit",
                localizer["Ribbon_Cancel"].Value,
                "<svg><use href='/icons/sprite.svg#close'/></svg>",
                UiRibbonItemSize.Large,
                false,
                null,
                () => { CancelEditMode(); return Task.CompletedTask; }
            ));
        }
        else
        {
            actions.Add(new UiRibbonAction(
                "Edit",
                localizer["Ribbon_Edit"].Value,
                "<svg><use href='/icons/sprite.svg#edit'/></svg>",
                UiRibbonItemSize.Large,
                false,
                null,
                async () => { await EnterEditModeAsync(); }
            ));
        }

        return new List<UiRibbonRegister>
        {
            new UiRibbonRegister(UiRibbonRegisterKind.Actions, new List<UiRibbonTab>
            {
                new UiRibbonTab(localizer["Ribbon_Group_Actions"].Value, actions, Sort: 0)
            })
        };
    }
}
