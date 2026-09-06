using FinanceManager.Shared.Dtos.Update;
using FinanceManager.Web.ViewModels.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Localization;

namespace FinanceManager.Web.ViewModels.Setup;

/// <summary>
/// View model backing the update setup tab. Loads and persists update settings, triggers update checks
/// and installs, and tracks install progress state for the UI.
/// </summary>
public sealed class SetupUpdateViewModel : BaseViewModel
{
    private const string MissingInstallConfirmationErrorCode = "Err_Update_ConfirmationRequired";
    private const string MissingInstallConfirmationMessage = "Update installation requires an active downtime confirmation.";

    private readonly ILogger<SetupUpdateViewModel>? _logger;
    private UpdateSettingsDto? _originalSettings;
    private Func<ValueTask<bool>>? _confirmInstallAsync;

    /// <summary>
    /// Creates a new instance of <see cref="SetupUpdateViewModel"/>.
    /// </summary>
    /// <param name="sp">Service provider used to resolve dependencies such as <see cref="FinanceManager.Shared.IApiClient"/> and logging.</param>
    public SetupUpdateViewModel(IServiceProvider sp) : base(sp)
    {
        _logger = sp.GetService<ILogger<SetupUpdateViewModel>>();
    }

    /// <summary>
    /// Currently loaded update settings, or <c>null</c> before <see cref="LoadAsync"/> has completed.
    /// </summary>
    public UpdateSettingsDto? Settings { get; private set; }

    /// <summary>
    /// Currently loaded update status, or <c>null</c> before <see cref="LoadAsync"/> has completed.
    /// </summary>
    public UpdateStatusDto? Status { get; private set; }

    /// <summary>
    /// Service name suggestions loaded from the server for the autocomplete field.
    /// </summary>
    /// <returns>The result.</returns>
    public IReadOnlyList<string> ServiceSuggestions { get; private set; } = Array.Empty<string>();

    /// <summary>
    /// Callback supplied by the UI to confirm expected downtime before starting an update from the ribbon.
    /// </summary>
    public Func<ValueTask<bool>>? ConfirmInstallAsync
    {
        get => _confirmInstallAsync;
        set
        {
            if (ReferenceEquals(_confirmInstallAsync, value))
            {
                return;
            }

            _confirmInstallAsync = value;
            RaiseStateChanged();
        }
    }

    /// <summary>
    /// Raised after an installation was started so the UI can begin health polling.
    /// </summary>
    public event EventHandler? InstallStarted;

    /// <summary>
    /// Indicates that a setup action (save, check, install, reset lock) is in progress.
    /// Distinct from the base <see cref="BaseViewModel.Loading"/> flag by design: this view model has no
    /// separate initial-load phase, and other setup view models in this codebase (e.g.
    /// SetupAttachmentCategoriesViewModel) use the same convention of a dedicated <c>Busy</c> flag for
    /// action-in-progress state.
    /// </summary>
    public bool Busy { get; private set; }

    /// <summary>
    /// Indicates whether one of the remaining editable settings differs from the last loaded/saved state.
    /// </summary>
    public bool Dirty { get; private set; }

    /// <summary>
    /// Indicates that an update install is currently in progress.
    /// </summary>
    public bool Installing { get; private set; }

    /// <summary>
    /// Localization key describing the current phase of an ongoing install (e.g. installing, waiting for restart).
    /// </summary>
    public string? InstallPhase { get; private set; }

    /// <summary>
    /// Loads the current update settings and status from the API.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous load operation.</returns>
    public Task LoadAsync(CancellationToken ct = default)
    {
        return RunBusyAsync(async ct =>
        {
            Settings = await ApiClient.Updates_GetSettingsAsync(ct);
            _originalSettings = Settings;
            Dirty = false;
            Status = await ApiClient.Updates_GetStatusAsync(ct);
        }, ct);
    }

    /// <summary>
    /// Persists the current <see cref="Settings"/> via the API and refreshes the status. No-op when <see cref="Settings"/> is <c>null</c>.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous save operation.</returns>
    public Task SaveAsync(CancellationToken ct = default)
    {
        if (Settings is null)
        {
            return Task.CompletedTask;
        }

        return RunBusyAsync(async ct =>
        {
            await SaveSettingsCoreAsync(ct);
            Status = await ApiClient.Updates_GetStatusAsync(ct);
        }, ct);
    }

    /// <summary>
    /// Triggers an immediate update check via the API and refreshes <see cref="Status"/> with the result.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous check operation.</returns>
    public Task CheckAsync(CancellationToken ct = default)
    {
        return RunBusyAsync(async ct =>
        {
            if (Dirty && Settings is not null)
            {
                await SaveSettingsCoreAsync(ct);
            }

            var result = await ApiClient.Updates_CheckAsync(ct);
            Status = result.Status;
        }, ct);
    }

    /// <summary>
    /// Starts installing the available update via the API and updates <see cref="Status"/> and <see cref="Installing"/>.
    /// </summary>
    /// <param name="confirmDowntime">Whether the caller confirmed the expected downtime.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous install-start operation.</returns>
    public Task StartInstallAsync(bool confirmDowntime, CancellationToken ct = default)
    {
        return RunBusyAsync(async ct =>
        {
            var installStatus = await ApiClient.Updates_StartInstallAsync(new UpdateStartRequest(confirmDowntime), ct);
            if (installStatus is not null)
            {
                Status = installStatus;
                Installing = installStatus.Status == UpdateStatusKind.Installing;
                if (Installing)
                {
                    InstallStarted?.Invoke(this, EventArgs.Empty);
                }
            }
        }, ct);
    }

    /// <summary>
    /// Confirms expected downtime through the UI callback and starts installing the ready update.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous install-start operation.</returns>
    public async Task StartInstallWithConfirmationAsync(CancellationToken ct = default)
    {
        var confirmInstallAsync = ConfirmInstallAsync;
        if (confirmInstallAsync is null)
        {
            SetError(MissingInstallConfirmationErrorCode, MissingInstallConfirmationMessage);
            RaiseStateChanged();
            return;
        }

        if (!await confirmInstallAsync())
        {
            return;
        }

        await StartInstallAsync(confirmDowntime: true, ct);
    }

    /// <summary>
    /// Resets the update lock via the API and refreshes <see cref="Status"/>.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous reset-lock operation.</returns>
    public Task ResetLockAsync(CancellationToken ct = default)
    {
        return RunBusyAsync(async ct =>
        {
            await ApiClient.Updates_ResetLockAsync(new UpdateLockResetRequest("Reset from setup UI"), ct);
            Status = await ApiClient.Updates_GetStatusAsync(ct);
        }, ct);
    }

    /// <summary>
    /// Replaces the in-memory <see cref="Settings"/> with the supplied values (used by form bindings before saving).
    /// </summary>
    /// <param name="settings">Updated settings values.</param>
    public void UpdateSettings(UpdateSettingsDto settings)
    {
        Settings = settings;
        Dirty = IsDirty(Settings, _originalSettings);
        RaiseStateChanged();
    }

    /// <summary>
    /// Resets the in-memory settings to the last loaded or saved state.
    /// </summary>
    public void Reset()
    {
        if (_originalSettings is null)
        {
            return;
        }

        Settings = _originalSettings;
        Dirty = false;
        RaiseStateChanged();
    }

    /// <summary>
    /// Loads service name suggestions for the autocomplete field.
    /// </summary>
    /// <param name="query">Optional user-entered query.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous load operation.</returns>
    public Task LoadServiceSuggestionsAsync(string? query, CancellationToken ct = default)
    {
        return RunServiceSuggestionsAsync(query, ct);
    }

    private async Task RunServiceSuggestionsAsync(string? query, CancellationToken ct)
    {
        try
        {
            ServiceSuggestions = await ApiClient.Updates_GetServiceNamesAsync(query, 20, ct);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Loading update service suggestions failed.");
            ServiceSuggestions = Array.Empty<string>();
        }

        RaiseStateChanged();
    }

    private async Task SaveSettingsCoreAsync(CancellationToken ct)
    {
        if (Settings is null)
        {
            return;
        }

        Settings = await ApiClient.Updates_UpdateSettingsAsync(new UpdateSettingsUpdateRequest(
            Settings.Enabled,
            Settings.RepositoryOwner,
            Settings.RepositoryName,
            Settings.ManifestAssetName,
            Settings.SourceCheckStartTime,
            Settings.SourceCheckEndTime,
            Settings.ScheduledInstallTime,
            Settings.ServiceName,
            Settings.ExecutablePath,
            Settings.WorkingDirectory,
            Settings.HealthTimeoutSeconds,
            Settings.IncludePrereleases), ct);
        _originalSettings = Settings;
        Dirty = false;
    }

    /// <summary>
    /// Marks the current install as having timed out while waiting for the application to become reachable again.
    /// </summary>
    public void MarkHealthTimeout()
    {
        SetError("Err_Update_HealthTimeout", "Die Anwendung wurde nach dem Update nicht innerhalb des erwarteten Zeitfensters erreichbar.");
        RaiseStateChanged();
    }

    /// <summary>
    /// Sets the localization key describing the current install phase and notifies subscribers.
    /// </summary>
    /// <param name="phase">Localization key for the current phase, or <c>null</c>.</param>
    public void SetInstallPhase(string? phase)
    {
        InstallPhase = phase;
        RaiseStateChanged();
    }

    private void BeginBusy()
    {
        Busy = true;
        SetError(null, null);
        RaiseStateChanged();
    }

    private async Task RunBusyAsync(Func<CancellationToken, Task> operation, CancellationToken ct)
    {
        BeginBusy();
        try
        {
            await operation(ct);
        }
        catch (Exception ex)
        {
            HandleException(ex);
        }
        finally
        {
            Busy = false;
            RaiseStateChanged();
        }
    }

    private void HandleException(Exception ex)
    {
        _logger?.LogError(ex, "Update setup operation failed.");
        SetError(ApiClient.LastErrorCode, ApiClient.LastError ?? ex.Message);
    }

    private static bool IsDirty(UpdateSettingsDto? current, UpdateSettingsDto? original)
    {
        if (current is null || original is null)
        {
            return false;
        }

        return current.Enabled != original.Enabled
            || current.SourceCheckStartTime != original.SourceCheckStartTime
            || current.SourceCheckEndTime != original.SourceCheckEndTime
            || current.ScheduledInstallTime != original.ScheduledInstallTime
            || current.IncludePrereleases != original.IncludePrereleases
            || !string.Equals(NormalizeOptional(current.ServiceName), NormalizeOptional(original.ServiceName), StringComparison.Ordinal);
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <inheritdoc />
    protected override IReadOnlyList<UiRibbonRegister>? GetRibbonRegisterDefinition(IStringLocalizer localizer)
    {
        var actions = new List<UiRibbonAction>
        {
            new(
                "UpdateCheckNow",
                localizer["SetupUpdate_Btn_CheckNow"].Value,
                "<svg><use href='/icons/sprite.svg#refresh'/></svg>",
                UiRibbonItemSize.Small,
                Busy || Status is null,
                localizer["Hint_SetupUpdate_CheckNow"].Value,
                new Func<Task>(async () => await CheckAsync())),
            new(
                "UpdateInstall",
                localizer["SetupUpdate_Btn_Install"].Value,
                "<svg><use href='/icons/sprite.svg#download'/></svg>",
                UiRibbonItemSize.Small,
                Busy || Status is null || Status.Status != UpdateStatusKind.Ready || ConfirmInstallAsync is null,
                localizer["Hint_SetupUpdate_Install"].Value,
                new Func<Task>(async () => await StartInstallWithConfirmationAsync())),
            new(
                "UpdateResetLock",
                localizer["SetupUpdate_Btn_ResetLock"].Value,
                "<svg><use href='/icons/sprite.svg#undo'/></svg>",
                UiRibbonItemSize.Small,
                Status is null || !Status.IsLocked,
                localizer["Hint_SetupUpdate_ResetLock"].Value,
                new Func<Task>(async () => await ResetLockAsync()))
        };

        return new List<UiRibbonRegister>
        {
            new(UiRibbonRegisterKind.Actions, new List<UiRibbonTab>
            {
                new(localizer["Setup_Section_Update"].Value, actions, 50)
            })
        };
    }
}
