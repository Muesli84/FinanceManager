using Microsoft.Extensions.Localization;
using System.Collections.Generic;

namespace FinanceManager.Web.ViewModels.Setup;

/// <summary>
/// View model for managing user notification settings in the setup area.
/// Handles loading, editing and saving notification preferences such as monthly reminders
/// and holiday provider selection.
/// </summary>
public sealed class SetupNotificationsViewModel : BaseViewModel
{
    private readonly FinanceManager.Shared.IApiClient _api;

    /// <summary>
    /// Initializes a new instance of <see cref="SetupNotificationsViewModel"/>.
    /// </summary>
    /// <param name="sp">Service provider used to resolve required services (API client, localization etc.).</param>
    public SetupNotificationsViewModel(IServiceProvider sp) : base(sp)
    {
        _api = sp.GetRequiredService<FinanceManager.Shared.IApiClient>();
    }

    /// <summary>
    /// Current notification settings model used by the UI.
    /// </summary>
    /// <returns>The result.</returns>
    public NotificationSettingsDto Model { get; private set; } = new();

    private NotificationSettingsDto _original = new();

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
    /// Last non-save related error message or <c>null</c> when none.
    /// </summary>
    public string? Error { get; private set; }

    /// <summary>
    /// Last save error message or <c>null</c> when none.
    /// </summary>
    public string? SaveError { get; private set; }

    /// <summary>
    /// True when the current model differs from the last loaded state.
    /// </summary>
    public bool Dirty { get; private set; }

    /// <summary>
    /// Hour component for monthly reminder (0-23) or <c>null</c> when unset.
    /// </summary>
    public int? Hour { get; set; }

    /// <summary>
    /// Minute component for monthly reminder (0-59) or <c>null</c> when unset.
    /// </summary>
    public int? Minute { get; set; }

    /// <summary>
    /// Available subdivisions returned for the configured holiday provider and country code.
    /// </summary>
    public string[]? Subdivisions { get; private set; }

    /// <summary>
    /// Loads the notification settings from the API and prepares UI state.
    /// Any exceptions are caught and surfaced via the <see cref="Error"/> property.
    /// </summary>
    /// <param name="ct">Cancellation token used to cancel the load operation.</param>
    /// <returns>A task that completes when the operation has finished.</returns>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        Loading = true; Error = null; SaveError = null; SavedOk = false; RaiseStateChanged();
        try
        {
            var dto = await _api.User_GetNotificationSettingsAsync(ct);
            Model = dto ?? new NotificationSettingsDto();
            if (string.IsNullOrEmpty(Model.HolidayProvider))
            {
                Model.HolidayProvider = "Memory";
            }
            _original = Clone(Model);
            Hour = Model.MonthlyReminderHour ?? 9;
            Minute = Model.MonthlyReminderMinute ?? 0;
            await LoadSubdivisionsAsync(ct);
            RecomputeDirty();
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally { Loading = false; RaiseStateChanged(); }
    }

    /// <summary>
    /// Loads available holiday subdivisions from the API based on the currently selected
    /// holiday provider and country code. Failures fall back to an empty list.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when subdivisions have been loaded.</returns>
    public async Task LoadSubdivisionsAsync(CancellationToken ct = default)
    {
        Subdivisions = null;
        if (Model.HolidayProvider == "NagerDate" && !string.IsNullOrWhiteSpace(Model.HolidayCountryCode))
        {
            try
            {
                var list = await _api.Meta_GetHolidaySubdivisionsAsync(Model.HolidayProvider!, Model.HolidayCountryCode!, ct);
                Subdivisions = list ?? Array.Empty<string>();
            }
            catch
            {
                Subdivisions = Array.Empty<string>();
            }
        }
        RaiseStateChanged();
    }

    /// <summary>
    /// Saves the current notification settings via the API. Save errors are captured in <see cref="SaveError"/>.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the save operation finishes.</returns>
    public async Task SaveAsync(CancellationToken ct = default)
    {
        Saving = true; SavedOk = false; SaveError = null; RaiseStateChanged();
        try
        {
            var ok = await _api.User_UpdateNotificationSettingsAsync(Model.MonthlyReminderEnabled, Hour, Minute, Model.HolidayProvider, Model.HolidayCountryCode, Model.HolidaySubdivisionCode, ct);
            if (ok)
            {
                Model.MonthlyReminderEnabled = Model.MonthlyReminderEnabled;
                Model.MonthlyReminderHour = Hour;
                Model.MonthlyReminderMinute = Minute;
                _original = Clone(Model);
                SavedOk = true;
                RecomputeDirty();
            }
            else
            {
                SaveError = "SaveFailed";
            }
        }
        catch (Exception ex)
        {
            SaveError = ex.Message;
        }
        finally { Saving = false; RaiseStateChanged(); }
    }

    /// <summary>
    /// Resets edits to the last loaded state.
    /// </summary>
    public void Reset()
    {
        Model = Clone(_original);
        Hour = _original.MonthlyReminderHour ?? 9;
        Minute = _original.MonthlyReminderMinute ?? 0;
        SavedOk = false; SaveError = null;
        RecomputeDirty();
        RaiseStateChanged();
    }

    /// <summary>
    /// Handler to call when the selected country changes. Reloads subdivisions and marks model as changed.
    /// </summary>
    public async Task OnCountryChanged()
    {
        await LoadSubdivisionsAsync();
        OnChanged();
    }

    /// <summary>
    /// Marks the view model state as changed and resets save indicators.
    /// </summary>
    public void OnChanged()
    {
        SavedOk = false;
        SaveError = null;
        RecomputeDirty();
        RaiseStateChanged();
    }

    /// <summary>
    /// Handler executed when the selected holiday provider changes. Clears subdivision selection for the memory provider,
    /// reloads subdivisions and marks state as changed.
    /// </summary>
    public async Task OnProviderChanged()
    {
        if (Model.HolidayProvider == "Memory")
        {
            Model.HolidaySubdivisionCode = null;
        }
        await LoadSubdivisionsAsync();
        OnChanged();
    }

    /// <summary>
    /// Validates and normalizes hour/minute values and marks state as changed.
    /// </summary>
    public void OnTimeChanged()
    {
        if (Hour is < 0 or > 23)
        {
            Hour = 9;
        }
        if (Minute is < 0 or > 59)
        {
            Minute = 0;
        }
        SavedOk = false;
        SaveError = null;
        RecomputeDirty();
        RaiseStateChanged();
    }

    private void RecomputeDirty()
    {
        Dirty = Model.MonthlyReminderEnabled != _original.MonthlyReminderEnabled
             || (Hour ?? 9) != (_original.MonthlyReminderHour ?? 9)
             || (Minute ?? 0) != (_original.MonthlyReminderMinute ?? 0)
             || Model.HolidayProvider != _original.HolidayProvider
             || Model.HolidayCountryCode != _original.HolidayCountryCode
             || Model.HolidaySubdivisionCode != _original.HolidaySubdivisionCode;
    }

    private static NotificationSettingsDto Clone(NotificationSettingsDto src) => new()
    {
        MonthlyReminderEnabled = src.MonthlyReminderEnabled,
        MonthlyReminderHour = src.MonthlyReminderHour,
        MonthlyReminderMinute = src.MonthlyReminderMinute,
        HolidayProvider = src.HolidayProvider,
        HolidayCountryCode = src.HolidayCountryCode,
        HolidaySubdivisionCode = src.HolidaySubdivisionCode
    };

    /// <summary>
    /// Builds ribbon register definitions for the setup notifications view to allow save/reset actions.
    /// </summary>
    /// <param name="localizer">Localizer used to resolve UI labels.</param>
    /// <returns>Collection of ribbon registers describing available tabs and actions.</returns>
    protected override IReadOnlyList<UiRibbonRegister>? GetRibbonRegisterDefinition(IStringLocalizer localizer)
    {
        return null;
    }
}
