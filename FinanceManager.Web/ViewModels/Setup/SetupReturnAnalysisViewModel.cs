using FinanceManager.Shared.Dtos.Securities;
using FinanceManager.Web.ViewModels.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FinanceManager.Web.ViewModels.Setup;

/// <summary>
/// View model for the return analysis / benchmark settings section inside the setup card.
/// Loads the list of available securities and the current benchmark selection, and persists changes.
/// </summary>
public sealed class SetupReturnAnalysisViewModel : BaseViewModel
{
    private readonly ILogger<SetupReturnAnalysisViewModel> _logger;
    private ReturnAnalysisSettingsResponse? _currentSettings;

    /// <summary>Creates a new instance.</summary>
    /// <param name="sp">Service provider.</param>
    public SetupReturnAnalysisViewModel(IServiceProvider sp) : base(sp)
    {
        _logger = sp.GetRequiredService<ILogger<SetupReturnAnalysisViewModel>>();
    }

    // Loading is inherited from BaseViewModel (protected set) - no local redeclaration needed.

    /// <summary>Whether a save operation is in progress.</summary>
    public bool Saving { get; private set; }

    /// <summary>True after a successful save.</summary>
    public bool SavedOk { get; private set; }

    /// <summary>Error message from the last load operation, if any.</summary>
    public string? Error { get; private set; }

    /// <summary>Error message from the last save operation, if any.</summary>
    public string? SaveError { get; private set; }

    /// <summary>All securities available for selection as benchmark.</summary>
    /// <returns>The result.</returns>
    public IReadOnlyList<SecurityDto> AvailableSecurities { get; private set; }
        = Array.Empty<SecurityDto>();

    /// <summary>Currently selected benchmark security id. Null means no benchmark.</summary>
    public Guid? SelectedBenchmarkSecurityId { get; set; }

    /// <summary>Whether the Sharpe Ratio should be shown in analyses.</summary>
    public bool ShowSharpeRatio { get; set; }

    /// <summary>Risk-free rate used for return calculations (decimal, e.g. 0.04 = 4%). Must be >= 0.</summary>
    public decimal RiskFreeRate { get; set; }

    /// <summary>Indicates whether the current settings differ from the last loaded or saved snapshot.</summary>
    public bool Dirty { get; private set; }

    /// <summary>
    /// Loads available securities and the current benchmark settings in parallel.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Loading return analysis settings");

        Loading = true;
        Error = null;
        SavedOk = false;
        RaiseStateChanged();

        try
        {
            var securitiesTask = ApiClient.Securities_ListAsync(onlyActive: false, ct);
            var settingsTask = ApiClient.Securities_GetReturnAnalysisSettingsAsync(ct);

            await Task.WhenAll(securitiesTask, settingsTask);

            AvailableSecurities = securitiesTask.Result;
            _currentSettings = settingsTask.Result;
            SelectedBenchmarkSecurityId = _currentSettings?.BenchmarkSecurityId;
            ShowSharpeRatio = _currentSettings?.ShowSharpeRatio ?? false;
            RiskFreeRate = _currentSettings?.RiskFreeRate ?? 0m;
            RecomputeDirty();
        }
        catch (Exception ex)
        {
            Error = ApiClient.LastError ?? ex.Message;
        }
        finally
        {
            Loading = false;
            RaiseStateChanged();
        }
    }

    /// <summary>
    /// Saves the selected benchmark, preserving the existing Sharpe Ratio and risk-free rate values.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task SaveAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Saving return analysis settings: BenchmarkSecurityId={BenchmarkSecurityId}, ShowSharpeRatio={ShowSharpeRatio}, RiskFreeRate={RiskFreeRate}", SelectedBenchmarkSecurityId, ShowSharpeRatio, RiskFreeRate);

        Saving = true;
        SavedOk = false;
        SaveError = null;
        RaiseStateChanged();

        try
        {
            // Validation: risk-free rate must be non-negative
            if (RiskFreeRate < 0m)
            {
                SaveError = "RiskFreeRate must be >= 0.";
                Saving = false;
                RaiseStateChanged();
                return;
            }

            var req = new ReturnAnalysisSettingsUpdateRequest(
                SelectedBenchmarkSecurityId,
                ShowSharpeRatio,
                RiskFreeRate);

            var ok = await ApiClient.Securities_UpdateReturnAnalysisSettingsAsync(req, ct);

            if (ok)
            {
                _currentSettings = new ReturnAnalysisSettingsResponse(
                    SelectedBenchmarkSecurityId,
                    AvailableSecurities.FirstOrDefault(s => s.Id == SelectedBenchmarkSecurityId)?.Name,
                    ShowSharpeRatio,
                    RiskFreeRate);
                SavedOk = true;
                RecomputeDirty();
            }
            else
            {
                SaveError = ApiClient.LastError ?? "Speichern fehlgeschlagen.";
            }
        }
        catch (Exception ex)
        {
            SaveError = ApiClient.LastError ?? ex.Message;
        }
        finally
        {
            Saving = false;
            RaiseStateChanged();
        }
    }

    /// <summary>Restores the last loaded or saved benchmark settings.</summary>
    public void Reset()
    {
        if (_currentSettings is null)
        {
            return;
        }

        SelectedBenchmarkSecurityId = _currentSettings.BenchmarkSecurityId;
        ShowSharpeRatio = _currentSettings.ShowSharpeRatio;
        RiskFreeRate = _currentSettings.RiskFreeRate;
        SavedOk = false;
        SaveError = null;
        RecomputeDirty();
        RaiseStateChanged();
    }

    /// <summary>Marks the settings as changed and recomputes the dirty state.</summary>
    public void OnChanged()
    {
        SavedOk = false;
        SaveError = null;
        RecomputeDirty();
        RaiseStateChanged();
    }

    private void RecomputeDirty()
    {
        Dirty = _currentSettings is not null
            && (SelectedBenchmarkSecurityId != _currentSettings.BenchmarkSecurityId
                || ShowSharpeRatio != _currentSettings.ShowSharpeRatio
                || RiskFreeRate != _currentSettings.RiskFreeRate);
    }
}
