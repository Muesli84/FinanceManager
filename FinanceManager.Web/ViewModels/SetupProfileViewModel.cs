using FinanceManager.Shared;

namespace FinanceManager.Web.ViewModels;

public sealed class SetupProfileViewModel : ViewModelBase
{
    private readonly IApiClient _api;

    public SetupProfileViewModel(IServiceProvider sp) : base(sp)
    {
        _api = sp.GetRequiredService<IApiClient>();
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
            var dto = await _api.UserSettings_GetProfileAsync(ct);
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
            var request = new UserProfileSettingsUpdateRequest(
                PreferredLanguage: Model.PreferredLanguage,
                TimeZoneId: Model.TimeZoneId,
                AlphaVantageApiKey: string.IsNullOrWhiteSpace(KeyInput) ? null : KeyInput.Trim(),
                ClearAlphaVantageApiKey: _clearRequested ? true : null,
                ShareAlphaVantageApiKey: ShareKey
            );

            var ok = await _api.UserSettings_UpdateProfileAsync(request, ct);
            if (ok)
            {
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
                SaveError = _api.LastError ?? "Save failed";
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
