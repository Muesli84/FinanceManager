using FinanceManager.Shared.Dtos.Securities; // use shared DTO
namespace FinanceManager.Web.ViewModels;

public sealed class SecurityPricesViewModel : ViewModelBase
{
    private readonly HttpClient _http;
    private readonly FinanceManager.Shared.IApiClient _api;

    public SecurityPricesViewModel(IServiceProvider sp, IHttpClientFactory httpFactory) : base(sp)
    {
        _http = httpFactory.CreateClient("Api");
        _api = sp.GetService<FinanceManager.Shared.IApiClient>() ?? new FinanceManager.Shared.ApiClient(_http);
    }

    public Guid SecurityId { get; private set; }

    private bool _loading;
    public bool Loading
    {
        get => _loading;
        private set { if (_loading != value) { _loading = value; RaiseStateChanged(); } }
    }

    private bool _canLoadMore = true;
    public bool CanLoadMore
    {
        get => _canLoadMore;
        private set { if (_canLoadMore != value) { _canLoadMore = value; RaiseStateChanged(); } }
    }

    public int Skip { get; private set; }
    public List<SecurityPriceDto> Items { get; } = new(); // switched to shared DTO

    // Backfill dialog state
    private bool _showBackfillDialog;
    public bool ShowBackfillDialog
    {
        get => _showBackfillDialog;
        set { if (_showBackfillDialog != value) { _showBackfillDialog = value; RaiseStateChanged(); } }
    }

    private DateTime? _fromDate;
    public DateTime? FromDate
    {
        get => _fromDate;
        set { if (_fromDate != value) { _fromDate = value; RaiseStateChanged(); } }
    }

    private DateTime? _toDate;
    public DateTime? ToDate
    {
        get => _toDate;
        set { if (_toDate != value) { _toDate = value; RaiseStateChanged(); } }
    }

    private bool _submitting;
    public bool Submitting
    {
        get => _submitting;
        private set { if (_submitting != value) { _submitting = value; RaiseStateChanged(); } }
    }

    // UI soll lokalisiert rendern: Key statt Text zurückgeben
    public string? DialogErrorKey { get; private set; }

    public void ForSecurity(Guid securityId) => SecurityId = securityId;

    public override async ValueTask InitializeAsync(CancellationToken ct = default)
    {
        if (Items.Count == 0)
        {
            await LoadMoreAsync(ct);
        }
    }

    public async Task LoadMoreAsync(CancellationToken ct = default)
    {
        if (Loading || !CanLoadMore) { return; }
        Loading = true;
        try
        {
            var chunk = await _api.Securities_GetPricesAsync(SecurityId, skip: Skip, take: 100, ct) ?? new List<SecurityPriceDto>();
            Items.AddRange(chunk);
            Skip += chunk.Count;
            if (chunk.Count < 100) { CanLoadMore = false; }
        }
        finally
        {
            Loading = false;
        }
    }

    public void OpenBackfillDialogDefaultPeriod()
    {
        var end = DateTime.UtcNow.Date.AddDays(-1);
        var start = end.AddYears(-2);
        FromDate = start;
        ToDate = end;
        DialogErrorKey = null;
        Submitting = false;
        ShowBackfillDialog = true;
    }

    public void CloseBackfillDialog()
    {
        ShowBackfillDialog = false;
    }

    public async Task ConfirmBackfillAsync(CancellationToken ct = default)
    {
        if (Submitting) { return; }
        DialogErrorKey = null;

        if (!FromDate.HasValue || !ToDate.HasValue)
        {
            DialogErrorKey = "Dlg_InvalidDates";
            return;
        }
        var from = FromDate.Value.Date;
        var to = ToDate.Value.Date;

        if (from > to)
        {
            DialogErrorKey = "Dlg_FromAfterTo";
            return;
        }
        if (to > DateTime.UtcNow.Date)
        {
            DialogErrorKey = "Dlg_ToInFuture";
            return;
        }

        Submitting = true;
        try
        {
            var info = await _api.Securities_EnqueueBackfillAsync(SecurityId, from, to, ct);
            if (info == null)
            {
                DialogErrorKey = "Dlg_EnqueueFailed";
                return;
            }
            ShowBackfillDialog = false;
        }
        catch
        {
            DialogErrorKey = "Dlg_EnqueueFailed";
        }
        finally
        {
            Submitting = false;
        }
    }

    // Ribbon-Struktur der Seite (wird mit Sub-VMs gemerged)
    public override IReadOnlyList<UiRibbonGroup> GetRibbon(Microsoft.Extensions.Localization.IStringLocalizer localizer)
    {
        var groups = new List<UiRibbonGroup>
        {
            new UiRibbonGroup(
                localizer["Ribbon_Group_Navigation"],
                new List<UiRibbonItem>
                {
                    new UiRibbonItem(
                        localizer["Ribbon_Back"],
                        "<svg><use href='/icons/sprite.svg#back'/></svg>",
                        UiRibbonItemSize.Large,
                        false,
                        "Back")
                }),
            new UiRibbonGroup(
                localizer["Ribbon_Group_Actions"],
                new List<UiRibbonItem>
                {
                    new UiRibbonItem(
                        localizer["Ribbon_Backfill"],
                        "<svg><use href='/icons/sprite.svg#postings'/></svg>",
                        UiRibbonItemSize.Large,
                        Loading,
                        "Backfill")
                })
        };

        var merged = base.GetRibbon(localizer);
        if (merged.Count > 0) { groups.AddRange(merged); }

        return groups;
    }
}