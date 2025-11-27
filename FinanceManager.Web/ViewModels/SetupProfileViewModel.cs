namespace FinanceManager.Web.ViewModels;

public sealed class SetupProfileViewModel : ViewModelBase
{
    private readonly HttpClient _http;

    public SetupProfileViewModel(IServiceProvider sp, IHttpClientFactory httpFactory) : base(sp)
    {
        _http = httpFactory.CreateClient("Api");
    }

    public UserProfileSettingsDto Model { get; private set; } = new();
    private UserProfileSettingsDto _original = new();

    public bool Loading { get; private set; }
    public bool Saving { get; private set; }
    public bool SavedOk { get; private set; }
    public string? Error { get; private set; }
    public string? SaveError { get; private set; }
    public bool Dirty { get; private set; }

    public bool HasKey { get; private set; }
    public bool ShareKey { get; set; }
    public string KeyInput { get; set; } = string.Empty;
    private bool _clearRequested;

    public override async ValueTask InitializeAsync(CancellationToken ct = default)
    {
        await LoadAsync(ct);
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        Loading = true; Error = null; SaveError = null; SavedOk = false; RaiseStateChanged();
        try
        {
            var dto = await _http.GetFromJsonAsync<UserProfileSettingsDto>("/api/user/profile-settings", ct);
            Model = dto ?? new();
            _original = Clone(Model);

            HasKey = dto?.HasAlphaVantageApiKey ?? false;
            ShareKey = dto?.ShareAlphaVantageApiKey ?? false;
            KeyInput = string.Empty;
            _clearRequested = false;

            RecomputeDirty();
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally { Loading = false; RaiseStateChanged(); }
    }

    public async Task SaveAsync(CancellationToken ct = default)
    {
        Saving = true; SavedOk = false; SaveError = null; RaiseStateChanged();
        try
        {
            var payload = new Dictionary<string, object?>
            {
                ["PreferredLanguage"] = Model.PreferredLanguage,
                ["TimeZoneId"] = Model.TimeZoneId,
                ["ShareAlphaVantageApiKey"] = ShareKey
            };
            if (!string.IsNullOrWhiteSpace(KeyInput))
            {
                payload["AlphaVantageApiKey"] = KeyInput.Trim();
            }
            if (_clearRequested)
            {
                payload["ClearAlphaVantageApiKey"] = true;
            }

            using var resp = await _http.PutAsJsonAsync("/api/user/profile-settings", payload, ct);
            if (resp.IsSuccessStatusCode)
            {
                // Reflect saved ShareKey into the model before cloning as original
                Model.ShareAlphaVantageApiKey = ShareKey;
                _original = Clone(Model);
                HasKey = !_clearRequested && (HasKey || !string.IsNullOrWhiteSpace(KeyInput));
                KeyInput = string.Empty;
                _clearRequested = false;
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

    public void ClearKey()
    {
        KeyInput = string.Empty;
        _clearRequested = true;
        OnChanged();
    }

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

    public void OnChanged()
    {
        SavedOk = false;
        SaveError = null;
        RecomputeDirty();
        RaiseStateChanged();
    }

    public void SetDetected(string? lang, string? tz)
    {
        if (!string.IsNullOrWhiteSpace(lang)) { Model.PreferredLanguage = lang[..Math.Min(lang.Length, 10)]; }
        if (!string.IsNullOrWhiteSpace(tz)) { Model.TimeZoneId = tz[..Math.Min(tz.Length, 100)]; }
        OnChanged();
    }

    private void RecomputeDirty()
    {
        var baseDirty = Model.PreferredLanguage != _original.PreferredLanguage || Model.TimeZoneId != _original.TimeZoneId;
        var keyDirty = !string.IsNullOrWhiteSpace(KeyInput) || _clearRequested || ShareKey != _original.ShareAlphaVantageApiKey;
        Dirty = baseDirty || keyDirty;
    }

    private static UserProfileSettingsDto Clone(UserProfileSettingsDto src) => new()
    {
        PreferredLanguage = src.PreferredLanguage,
        TimeZoneId = src.TimeZoneId,
        HasAlphaVantageApiKey = src.HasAlphaVantageApiKey,
        ShareAlphaVantageApiKey = src.ShareAlphaVantageApiKey
    };
}
