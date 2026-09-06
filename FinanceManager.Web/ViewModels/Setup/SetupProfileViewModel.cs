using FinanceManager.Shared;
using Microsoft.Extensions.Localization;
using FinanceManager.Web.Services;
using FinanceManager.Web.ViewModels.Common;

namespace FinanceManager.Web.ViewModels.Setup;

/// <summary>
/// View model for the user profile settings editor in the setup area.
/// Provides loading, editing and persistence of <see cref="UserProfileSettingsDto"/>, API key handling and ribbon actions.
/// </summary>
public sealed class SetupProfileViewModel : BaseViewModel
{
    private readonly IKpiLocalStorageCache _kpiCache;

    /// <summary>
    /// Initializes a new instance of <see cref="SetupProfileViewModel"/>.
    /// </summary>
    /// <param name="sp">Service provider used to resolve dependencies (API client, localizer, navigation, etc.).</param>
    public SetupProfileViewModel(IServiceProvider sp) : base(sp)
    {
        _kpiCache = sp.GetRequiredService<IKpiLocalStorageCache>();
    }

    /// <summary>
    /// Current editable model representing the user's profile settings.
    /// </summary>
    /// <returns>The result.</returns>
    public UserProfileSettingsDto Model { get; private set; } = new();
    private UserProfileSettingsDto _original = new();

    // Loading is inherited from BaseViewModel (protected set) - no local redeclaration needed.

    /// <summary>
    /// Indicates whether a save operation is in progress.
    /// </summary>
    public bool Saving { get; private set; }

    /// <summary>
    /// True when the last save operation completed successfully.
    /// </summary>
    public bool SavedOk { get; private set; }

    /// <summary>
    /// Error message produced during load operations, if any.
    /// </summary>
    public string? Error { get; private set; }

    /// <summary>
    /// Error message produced during save operations, if any.
    /// </summary>
    public string? SaveError { get; private set; }

    /// <summary>
    /// True when the model contains unsaved changes compared to the last loaded snapshot.
    /// </summary>
    public bool Dirty { get; private set; }

    /// <summary>
    /// Indicates whether an AlphaVantage API key is present for the user.
    /// </summary>
    public bool HasKey { get; private set; }

    /// <summary>
    /// Indicates whether the user chose to share their AlphaVantage API key.
    /// </summary>
    public bool ShareKey { get; set; }

    /// <summary>
    /// Temporary input field for entering an AlphaVantage API key.
    /// </summary>
    public string KeyInput { get; set; } = string.Empty;
    private bool _clearRequested;

    /// <summary>
    /// Loads the profile settings for the current user from the API and prepares the editable model.
    /// </summary>
    /// <param name="ct">Cancellation token used to cancel the operation.</param>
    /// <returns>A task that completes when the load operation has finished.</returns>
    /// <exception cref="OperationCanceledException">Thrown when the provided cancellation token is canceled.</exception>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        Loading = true; Error = null; SaveError = null; SavedOk = false; RaiseStateChanged();
        try
        {
            var dto = await ApiClient.UserSettings_GetProfileAsync(ct);
            Model = dto ?? new();
            _original = Clone(Model);

            HasKey = dto?.HasAlphaVantageApiKey ?? false;
            ShareKey = dto?.ShareAlphaVantageApiKey ?? false;
            KeyInput = string.Empty;
            _clearRequested = false;

            RecomputeDirty();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally { Loading = false; RaiseStateChanged(); }
    }

    /// <summary>
    /// Persists pending profile changes to the API, including API key updates or clears.
    /// When the preferred language changes the method triggers a full page reload after a successful save
    /// so that the updated auth cookie (which contains the new <c>pref_lang</c> JWT claim issued by the
    /// server) is sent with the next HTTP request and the ASP.NET request localisation middleware picks
    /// up the new culture for the refreshed Blazor circuit.
    /// </summary>
    /// <param name="ct">Cancellation token used to cancel the operation.</param>
    /// <returns>A task that completes when the save operation has finished. The <see cref="SavedOk"/>, <see cref="SaveError"/> and <see cref="Dirty"/> state will be updated accordingly.</returns>
    /// <exception cref="OperationCanceledException">Thrown when the provided cancellation token is canceled during the network call.</exception>
    public async Task SaveAsync(CancellationToken ct = default)
    {
        Saving = true; SavedOk = false; SaveError = null; RaiseStateChanged();
        try
        {
            var languageChanged = Model.PreferredLanguage != _original.PreferredLanguage;

            var request = new UserProfileSettingsUpdateRequest(
                PreferredLanguage: Model.PreferredLanguage,
                TimeZoneId: Model.TimeZoneId,
                AlphaVantageApiKey: string.IsNullOrWhiteSpace(KeyInput) ? null : KeyInput.Trim(),
                ClearAlphaVantageApiKey: _clearRequested ? true : null,
                ShareAlphaVantageApiKey: ShareKey,
                CacheKpisInLocalStorage: Model.CacheKpisInLocalStorage
            );

            var ok = await ApiClient.UserSettings_UpdateProfileAsync(request, ct);
            if (ok)
            {
                Model.ShareAlphaVantageApiKey = ShareKey;
                _original = Clone(Model);
                HasKey = !_clearRequested && (HasKey || !string.IsNullOrWhiteSpace(KeyInput));
                KeyInput = string.Empty;
                _clearRequested = false;
                SavedOk = true;
                RecomputeDirty();

                if (!Model.CacheKpisInLocalStorage)
                {
                    try { await _kpiCache.RemoveAllAsync(); } catch { /* ignored */ }
                }

                // A full page reload is required when the language changed: the server re-issues the auth
                // cookie with an updated JWT (containing the new pref_lang claim), but the running Blazor
                // Server circuit keeps the culture that was resolved at circuit creation time.  A force-
                // reload sends the new cookie as a regular HTTP request so the RequestLocalizationMiddleware
                // can pick up the correct culture for the new circuit.
                if (languageChanged)
                {
                    Navigation.Refresh(forceReload: true);
                    return;
                }
            }
            else
            {
                SaveError = ApiClient.LastError ?? "Save failed";
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            SaveError = ex.Message;
        }
        finally { Saving = false; RaiseStateChanged(); }
    }

    /// <summary>
    /// Clears the API key input and marks the model to perform a key clear on save.
    /// </summary>
    public void ClearKey()
    {
        KeyInput = string.Empty;
        _clearRequested = true;
        OnChanged();
    }

    /// <summary>
    /// Resets the editable model to the most recently loaded values and clears pending key input.
    /// </summary>
    public void Reset()
    {
        Model = Clone(_original);
        SavedOk = false; SaveError = null;
        KeyInput = string.Empty;
        _clearRequested = false;
        ShareKey = _original.ShareAlphaVantageApiKey;
        RecomputeDirty();
        RaiseStateChanged();
    }

    /// <summary>
    /// Called when any editable field changed. Clears save state and recomputes dirty flag.
    /// </summary>
    public void OnChanged()
    {
        SavedOk = false;
        SaveError = null;
        RecomputeDirty();
        RaiseStateChanged();
    }

    /// <summary>
    /// Sets the detected language and timezone values (used by timezone detection UI) and applies length limits.
    /// </summary>
    /// <param name="lang">Detected language code (may be truncated).</param>
    /// <param name="tz">Detected timezone identifier (may be truncated).</param>
    public void SetDetectedTimezone(string? lang, string? tz)
    {
        if (!string.IsNullOrWhiteSpace(lang)) { Model.PreferredLanguage = lang[..Math.Min(lang.Length, 10)]; }
        if (!string.IsNullOrWhiteSpace(tz)) { Model.TimeZoneId = tz[..Math.Min(tz.Length, 100)]; }
        OnChanged();
    }

    private void RecomputeDirty()
    {
        var baseDirty = Model.PreferredLanguage != _original.PreferredLanguage || Model.TimeZoneId != _original.TimeZoneId ||
                        Model.CacheKpisInLocalStorage != _original.CacheKpisInLocalStorage;
        var keyDirty = !string.IsNullOrWhiteSpace(KeyInput) || _clearRequested || ShareKey != _original.ShareAlphaVantageApiKey;
        Dirty = baseDirty || keyDirty;
    }

    private static UserProfileSettingsDto Clone(UserProfileSettingsDto src) => new()
    {
        PreferredLanguage = src.PreferredLanguage,
        TimeZoneId = src.TimeZoneId,
        HasAlphaVantageApiKey = src.HasAlphaVantageApiKey,
        ShareAlphaVantageApiKey = src.ShareAlphaVantageApiKey,
        CacheKpisInLocalStorage = src.CacheKpisInLocalStorage
    };

    /// <summary>
    /// Builds ribbon register definitions for the profile editor including Save, Reset and Detect timezone actions.
    /// </summary>
    /// <param name="localizer">Localizer used to resolve UI labels and hints.</param>
    /// <returns>Collection of ribbon registers describing available tabs and actions for the UI.</returns>
    protected override IReadOnlyList<UiRibbonRegister>? GetRibbonRegisterDefinition(Microsoft.Extensions.Localization.IStringLocalizer localizer)
    {
        var lblProfileGroup = localizer["SetupProfile_Title"].Value;
        var lblDetect = localizer["Ribbon_Detect_Timezone"].Value;

        var detectAction = new UiRibbonAction(
            "DetectTimezone",
            lblDetect,
            "<svg><use href='/icons/sprite.svg#refresh'/></svg>",
            UiRibbonItemSize.Small,
            false,
            null,
            new Func<Task>(() => { RaiseUiActionRequested("DetectTimezone"); return Task.CompletedTask; })
        );
        var tabs = new List<UiRibbonTab>
        {
            new UiRibbonTab(lblProfileGroup, new List<UiRibbonAction> { detectAction })
        };

        return new List<UiRibbonRegister> { new UiRibbonRegister(UiRibbonRegisterKind.Actions, tabs) };
    }
}
