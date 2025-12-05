using FinanceManager.Shared; // use ApiClient abstraction
using Microsoft.Extensions.Localization;

namespace FinanceManager.Web.ViewModels;

public sealed class StatementDraftsViewModel : ViewModelBase
{
    private readonly IApiClient _api;

    public StatementDraftsViewModel(IServiceProvider sp, IApiClient api) : base(sp)
    {
        _api = api;
    }

    public sealed class DraftItem
    {
        public Guid Id { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public StatementDraftStatus Status { get; set; }
        public int PendingEntries { get; set; }
    }

    public List<DraftItem> Items { get; } = new();
    public bool Loading { get; private set; }
    public bool CanLoadMore { get; private set; } = true;
    private int _skip;
    private const int PageSize = 3;

    // Classification state
    public bool IsClassifying { get; private set; }
    public int ClassifyProcessed { get; private set; }
    public int ClassifyTotal { get; private set; }
    public string? ClassifyMessage { get; private set; }

    // Booking state
    public bool IsBooking { get; private set; }
    public int BookingProcessed { get; private set; }
    public int BookingFailed { get; private set; }
    public int BookingTotal { get; private set; }
    public string? BookingMessage { get; private set; }
    public int BookingErrors { get; private set; }
    public int BookingWarnings { get; private set; }
    public List<StatementDraftMassBookIssueDto> BookingIssues { get; private set; } = new();

    public override async ValueTask InitializeAsync(CancellationToken ct = default)
    {
        await LoadMoreAsync(ct);
        await RefreshClassifyStatusAsync(false, ct);
        await RefreshBookStatusAsync(false, ct);
    }

    public async Task LoadMoreAsync(CancellationToken ct = default)
    {
        if (Loading || !CanLoadMore) { return; }
        Loading = true; RaiseStateChanged();
        try
        {
            var batch = await _api.StatementDrafts_ListOpenAsync(_skip, PageSize, ct);
            var list = batch?.ToList() ?? new List<StatementDraftDto>();
            if (list.Count < PageSize)
            {
                CanLoadMore = false;
            }
            foreach (var d in list)
            {
                var pending = d.Entries.Count(e => e.Status != StatementDraftEntryStatus.AlreadyBooked);
                Items.Add(new DraftItem
                {
                    Id = d.DraftId,
                    FileName = d.OriginalFileName,
                    Description = d.Description,
                    Status = d.Status,
                    PendingEntries = pending
                });
            }
            _skip += list.Count;
        }
        finally
        {
            Loading = false; RaiseStateChanged();
        }
    }

    public async Task<bool> DeleteAllAsync(CancellationToken ct = default)
    {
        var ok = await _api.StatementDrafts_DeleteAllAsync(ct);
        if (!ok)
        {
            return false;
        }
        Items.Clear();
        _skip = 0;
        CanLoadMore = true;
        RaiseStateChanged();
        return true;
    }

    public void Reset()
    {
        Items.Clear();
        _skip = 0;
        CanLoadMore = true;
        RaiseStateChanged();
    }

    public override IReadOnlyList<UiRibbonGroup> GetRibbon(IStringLocalizer localizer)
    {
        var groups = new List<UiRibbonGroup>();
        groups.Add(new UiRibbonGroup(
            localizer["Ribbon_Group_Management"],
            new List<UiRibbonItem>
            {
                new UiRibbonItem(localizer["Ribbon_DeleteAll"], "<svg><use href='/icons/sprite.svg#delete'/></svg>", UiRibbonItemSize.Large, false, "DeleteAll")
            }));
        groups.Add(new UiRibbonGroup(
            localizer["Ribbon_Group_Classification"],
            new List<UiRibbonItem>
            {
                new UiRibbonItem(localizer["Ribbon_Reclassify"], "<svg><use href='/icons/sprite.svg#refresh'/></svg>", UiRibbonItemSize.Large, IsClassifying, "Reclassify")
            }));
        groups.Add(new UiRibbonGroup(
            localizer["Ribbon_Group_Booking"],
            new List<UiRibbonItem>
            {
                new UiRibbonItem(localizer["Ribbon_MassBooking"], "<svg><use href='/icons/sprite.svg#save'/></svg>", UiRibbonItemSize.Large, IsBooking, "MassBooking")
            }));
        groups.Add(new UiRibbonGroup(
            localizer["Ribbon_Group_Import"],
            new List<UiRibbonItem>
            {
                new UiRibbonItem(localizer["Ribbon_Import"], "<svg><use href='/icons/sprite.svg#upload'/></svg>", UiRibbonItemSize.Large, false, "Import")
            }));
        return groups;
    }

    // Upload handling
    public async Task<Guid?> UploadAsync(Stream stream, string fileName, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var result = await _api.StatementDrafts_UploadAsync(stream, fileName, ct);
        if (result?.FirstDraft != null) return result.FirstDraft.DraftId;
        return null;
    }

    // Classification
    public async Task StartClassifyAsync(CancellationToken ct = default)
    {
        var status = await _api.StatementDrafts_StartClassifyAsync(ct);
        if (status == null)
        {
            ClassifyMessage = _api.LastError;
            RaiseStateChanged();
            return;
        }
        IsClassifying = status.running;
        ClassifyProcessed = status.processed;
        ClassifyTotal = status.total;
        ClassifyMessage = status.message ?? (IsClassifying ? "Working..." : null);
        RaiseStateChanged();
        if (IsClassifying) { _ = PollClassifyUntilFinishedAsync(); }
    }
    private async Task PollClassifyUntilFinishedAsync()
    {
        while (IsClassifying)
        {
            await Task.Delay(1000);
            await RefreshClassifyStatusAsync();
        }
    }
    public async Task RefreshClassifyStatusAsync(bool reloadOnFinish = true, CancellationToken ct = default)
    {
        var s = await _api.StatementDrafts_GetClassifyStatusAsync(ct);
        if (s != null)
        {
            var wasRunning = IsClassifying;
            IsClassifying = s.running;
            ClassifyProcessed = s.processed;
            ClassifyTotal = s.total;
            ClassifyMessage = s.message ?? (IsClassifying ? "Working..." : null);
            RaiseStateChanged();
            if (!wasRunning && IsClassifying)
            {
                _ = PollClassifyUntilFinishedAsync();
            }
            if (!s.running && s.total > 0 && reloadOnFinish)
            {
                await ReloadAfterActionAsync(ct);
            }
        }
    }

    // Booking
    public async Task StartBookAllAsync(bool ignoreWarnings, bool abortOnFirstIssue, bool bookEntriesIndividually, CancellationToken ct = default)
    {
        var s = await _api.StatementDrafts_StartBookAllAsync(ignoreWarnings, abortOnFirstIssue, bookEntriesIndividually, ct);
        if (s == null)
        {
            BookingMessage = _api.LastError;
            RaiseStateChanged();
            return;
        }
        UpdateBookingUi(s);
        if (IsBooking) { _ = PollBookingUntilFinishedAsync(); }
    }
    private async Task PollBookingUntilFinishedAsync()
    {
        while (IsBooking)
        {
            await Task.Delay(1000);
            await RefreshBookStatusAsync();
        }
    }
    public async Task RefreshBookStatusAsync(bool reloadOnFinish = true, CancellationToken ct = default)
    {
        var s = await _api.StatementDrafts_GetBookAllStatusAsync(ct);
        if (s != null)
        {
            var wasRunning = IsBooking;
            UpdateBookingUi(s);
            if (!wasRunning && IsBooking)
            {
                _ = PollBookingUntilFinishedAsync();
            }
            if (!s.Running && s.Total > 0 && reloadOnFinish)
            {
                await ReloadAfterActionAsync(ct);
            }
        }
    }
    private void UpdateBookingUi(StatementDraftMassBookStatusDto? s)
    {
        if (s == null) { return; }
        IsBooking = s.Running;
        BookingProcessed = s.Processed;
        BookingFailed = s.Failed;
        BookingTotal = s.Total;
        BookingMessage = s.Message ?? (IsBooking ? "Working..." : null);
        BookingErrors = s.Errors;
        BookingWarnings = s.Warnings;
        BookingIssues = s.Issues?.ToList() ?? new();
        RaiseStateChanged();
    }
    public async Task CancelBookingAsync(CancellationToken ct = default)
    {
        try { await _api.StatementDrafts_CancelBookAllAsync(ct); } catch { }
    }

    private async Task ReloadAfterActionAsync(CancellationToken ct = default)
    {
        Reset();
        await LoadMoreAsync(ct);
    }
}
