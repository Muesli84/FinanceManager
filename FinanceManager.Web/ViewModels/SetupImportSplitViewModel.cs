namespace FinanceManager.Web.ViewModels;

public sealed class SetupImportSplitViewModel : ViewModelBase
{
    private readonly HttpClient _http;

    public SetupImportSplitViewModel(IServiceProvider sp, IHttpClientFactory httpFactory) : base(sp)
    {
        _http = httpFactory.CreateClient("Api");
    }

    public ImportSplitSettingsDto? Model { get; private set; }
    private ImportSplitSettingsDto? _original;

    public bool Loading { get; private set; }
    public bool Saving { get; private set; }
    public bool SavedOk { get; private set; }
    public string? Error { get; private set; }
    public string? SaveError { get; private set; }
    public string? ValidationMessage { get; private set; }
    public bool HasValidationError { get; private set; }
    public bool Dirty { get; private set; }

    public override async ValueTask InitializeAsync(CancellationToken ct = default)
    {
        await LoadAsync(ct);
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        Loading = true; Error = null; SaveError = null; SavedOk = false; RaiseStateChanged();
        try
        {
            var dto = await _http.GetFromJsonAsync<ImportSplitSettingsDto>("/api/user/settings/import-split", ct);
            Model = dto ?? new ImportSplitSettingsDto();
            _original = Clone(Model);
            RecomputeDirty();
            Validate();
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally { Loading = false; RaiseStateChanged(); }
    }

    public async Task SaveAsync(CancellationToken ct = default)
    {
        if (Model is null) { return; }
        Validate(); if (HasValidationError) { return; }
        Saving = true; SavedOk = false; SaveError = null; RaiseStateChanged();
        try
        {
            using var resp = await _http.PutAsJsonAsync("/api/user/settings/import-split", Model, ct);
            if (resp.IsSuccessStatusCode)
            {
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
        if (Model is null || _original is null) { return; }
        Model.Mode = _original.Mode;
        Model.MaxEntriesPerDraft = _original.MaxEntriesPerDraft;
        Model.MonthlySplitThreshold = _original.MonthlySplitThreshold;
        Model.MinEntriesPerDraft = _original.MinEntriesPerDraft;
        SavedOk = false; SaveError = null;
        Validate();
        RecomputeDirty();
        RaiseStateChanged();
    }

    public void OnModeChanged()
    {
        if (Model is not null && Model.Mode == ImportSplitMode.MonthlyOrFixed)
        {
            if (!Model.MonthlySplitThreshold.HasValue || Model.MonthlySplitThreshold.Value < Model.MaxEntriesPerDraft)
            {
                Model.MonthlySplitThreshold = Model.MaxEntriesPerDraft;
            }
        }
        Validate();
        RecomputeDirty();
        RaiseStateChanged();
    }

    public void Validate()
    {
        ValidationMessage = null; HasValidationError = false;
        if (Model is null) { return; }
        if (Model.MaxEntriesPerDraft < 20)
        {
            ValidationMessage = "ImportSplit_InvalidMaxMin20"; HasValidationError = true; return;
        }
        if (Model.Mode != ImportSplitMode.FixedSize)
        {
            if (Model.MinEntriesPerDraft < 1)
            {
                ValidationMessage = "ImportSplit_InvalidMinEntries"; HasValidationError = true; return;
            }
            if (Model.MinEntriesPerDraft > Model.MaxEntriesPerDraft)
            {
                ValidationMessage = "ImportSplit_InvalidMinGreaterMax"; HasValidationError = true; return;
            }
        }
        if (Model.Mode == ImportSplitMode.MonthlyOrFixed)
        {
            var thr = Model.MonthlySplitThreshold ?? 0;
            if (thr < Model.MaxEntriesPerDraft)
            {
                ValidationMessage = "ImportSplit_InvalidThreshold"; HasValidationError = true; return;
            }
        }
        RecomputeDirty();
    }

    private void RecomputeDirty()
    {
        if (Model is null || _original is null) { Dirty = false; return; }
        Dirty = Model.Mode != _original.Mode
             || Model.MaxEntriesPerDraft != _original.MaxEntriesPerDraft
             || (Model.MonthlySplitThreshold ?? 0) != (_original.MonthlySplitThreshold ?? 0)
             || Model.MinEntriesPerDraft != _original.MinEntriesPerDraft;
    }

    private static ImportSplitSettingsDto Clone(ImportSplitSettingsDto src) => new()
    {
        Mode = src.Mode,
        MaxEntriesPerDraft = src.MaxEntriesPerDraft,
        MonthlySplitThreshold = src.MonthlySplitThreshold,
        MinEntriesPerDraft = src.MinEntriesPerDraft
    };
}
