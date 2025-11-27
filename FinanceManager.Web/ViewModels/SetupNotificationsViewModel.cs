namespace FinanceManager.Web.ViewModels;

public sealed class SetupNotificationsViewModel : ViewModelBase
{
    private readonly HttpClient _http;

    public SetupNotificationsViewModel(IServiceProvider sp, IHttpClientFactory httpFactory) : base(sp)
    {
        _http = httpFactory.CreateClient("Api");
    }

    public NotificationSettingsDto Model { get; private set; } = new();
    private NotificationSettingsDto _original = new();

    public bool Loading { get; private set; }
    public bool Saving { get; private set; }
    public bool SavedOk { get; private set; }
    public string? Error { get; private set; }
    public string? SaveError { get; private set; }
    public bool Dirty { get; private set; }

    public int? Hour { get; set; }
    public int? Minute { get; set; }

    public string[]? Subdivisions { get; private set; }

    public override async ValueTask InitializeAsync(CancellationToken ct = default)
    {
        await LoadAsync(ct);
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        Loading = true; Error = null; SaveError = null; SavedOk = false; RaiseStateChanged();
        try
        {
            var dto = await _http.GetFromJsonAsync<NotificationSettingsDto>("/api/user/notification-settings", ct);
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

    public async Task LoadSubdivisionsAsync(CancellationToken ct = default)
    {
        Subdivisions = null;
        if (Model.HolidayProvider == "NagerDate" && !string.IsNullOrWhiteSpace(Model.HolidayCountryCode))
        {
            try
            {
                var list = await _http.GetFromJsonAsync<string[]>($"/api/meta/holiday-subdivisions?provider={Model.HolidayProvider}&country={Model.HolidayCountryCode}", ct);
                Subdivisions = list ?? Array.Empty<string>();
            }
            catch
            {
                Subdivisions = Array.Empty<string>();
            }
        }
        RaiseStateChanged();
    }

    public async Task SaveAsync(CancellationToken ct = default)
    {
        Saving = true; SavedOk = false; SaveError = null; RaiseStateChanged();
        try
        {
            var payload = new
            {
                Model.MonthlyReminderEnabled,
                MonthlyReminderHour = Hour,
                MonthlyReminderMinute = Minute,
                Model.HolidayProvider,
                Model.HolidayCountryCode,
                Model.HolidaySubdivisionCode
            };
            using var resp = await _http.PutAsJsonAsync("/api/user/notification-settings", payload, ct);
            if (resp.IsSuccessStatusCode)
            {
                Model.MonthlyReminderEnabled = payload.MonthlyReminderEnabled;
                Model.MonthlyReminderHour = payload.MonthlyReminderHour;
                Model.MonthlyReminderMinute = payload.MonthlyReminderMinute;
                _original = Clone(Model);
                SavedOk = true;
                RecomputeDirty();
            }
            else
            {
                SaveError = await resp.Content.ReadAsStringAsync(ct);
            }
        }
        catch (Exception ex)
        {
            SaveError = ex.Message;
        }
        finally { Saving = false; RaiseStateChanged(); }
    }

    public void Reset()
    {
        Model = Clone(_original);
        Hour = _original.MonthlyReminderHour ?? 9;
        Minute = _original.MonthlyReminderMinute ?? 0;
        SavedOk = false; SaveError = null;
        RecomputeDirty();
        RaiseStateChanged();
    }

    public async Task OnCountryChanged()
    {
        await LoadSubdivisionsAsync();
        OnChanged();
    }

    public void OnChanged()
    {
        SavedOk = false;
        SaveError = null;
        RecomputeDirty();
        RaiseStateChanged();
    }

    public async Task OnProviderChanged()
    {
        if (Model.HolidayProvider == "Memory")
        {
            Model.HolidaySubdivisionCode = null;
        }
        await LoadSubdivisionsAsync();
        OnChanged();
    }

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
}
