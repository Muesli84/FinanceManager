using System.Net.Http;
using System.Net.Http.Json;
using System.IO;
using FinanceManager.Web.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace FinanceManager.Web.ViewModels;

public sealed class StatementDraftsViewModel : ViewModelBase
{
    private readonly HttpClient _http;

    public StatementDraftsViewModel(IServiceProvider sp, IHttpClientFactory httpFactory) : base(sp)
    {
        _http = httpFactory.CreateClient("Api");
    }

    public sealed class StatementDraftEntryDto
    {
        public FinanceManager.Domain.Statements.StatementDraftEntryStatus Status { get; set; }
    }
    public sealed class StatementDraftDto
    {
        public Guid DraftId { get; set; }
        public string OriginalFileName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public FinanceManager.Domain.StatementDraftStatus Status { get; set; }
        public List<StatementDraftEntryDto> Entries { get; set; } = new();
    }

    public sealed class DraftItem
    {
        public Guid Id { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public FinanceManager.Domain.StatementDraftStatus Status { get; set; }
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
    private CancellationTokenSource? _classifyCts;

    // Booking state
    public bool IsBooking { get; private set; }
    public int BookingProcessed { get; private set; }
    public int BookingFailed { get; private set; }
    public int BookingTotal { get; private set; }
    public string? BookingMessage { get; private set; }
    public int BookingErrors { get; private set; }
    public int BookingWarnings { get; private set; }
    public List<BookIssue> BookingIssues { get; private set; } = new();
    private CancellationTokenSource? _bookingCts;

    public override async ValueTask InitializeAsync(CancellationToken ct = default)
    {
        await LoadMoreAsync(ct);
    }

    public async Task LoadMoreAsync(CancellationToken ct = default)
    {
        if (Loading || !CanLoadMore) { return; }
        Loading = true; RaiseStateChanged();
        try
        {
            var url = $"api/statement-drafts?skip={_skip}&take={PageSize}";
            var batch = await _http.GetFromJsonAsync<List<StatementDraftDto>>(url, ct) ?? new();
            if (batch.Count < PageSize)
            {
                CanLoadMore = false;
            }
            foreach (var d in batch)
            {
                var pending = d.Entries.Count(e => e.Status != FinanceManager.Domain.Statements.StatementDraftEntryStatus.AlreadyBooked);
                Items.Add(new DraftItem
                {
                    Id = d.DraftId,
                    FileName = d.OriginalFileName,
                    Description = d.Description,
                    Status = d.Status,
                    PendingEntries = pending
                });
            }
            _skip += batch.Count;
        }
        finally
        {
            Loading = false; RaiseStateChanged();
        }
    }

    public async Task<bool> DeleteAllAsync(CancellationToken ct = default)
    {
        var resp = await _http.DeleteAsync("api/statement-drafts/all", ct);
        if (!resp.IsSuccessStatusCode)
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
        // Management
        groups.Add(new UiRibbonGroup(
            localizer["Ribbon_Group_Management"],
            new List<UiRibbonItem>
            {
                new UiRibbonItem(localizer["Ribbon_DeleteAll"], "<svg><use href='/icons/sprite.svg#delete'/></svg>", UiRibbonItemSize.Large, false, "DeleteAll")
            }));

        // Classification
        groups.Add(new UiRibbonGroup(
            localizer["Ribbon_Group_Classification"],
            new List<UiRibbonItem>
            {
                new UiRibbonItem(localizer["Ribbon_Reclassify"], "<svg><use href='/icons/sprite.svg#refresh'/></svg>", UiRibbonItemSize.Large, IsClassifying, "Reclassify")
            }));

        // Booking
        groups.Add(new UiRibbonGroup(
            localizer["Ribbon_Group_Booking"],
            new List<UiRibbonItem>
            {
                new UiRibbonItem(localizer["Ribbon_MassBooking"], "<svg><use href='/icons/sprite.svg#save'/></svg>", UiRibbonItemSize.Large, IsBooking, "MassBooking")
            }));

        // Import
        groups.Add(new UiRibbonGroup(
            localizer["Ribbon_Group_Import"],
            new List<UiRibbonItem>
            {
                new UiRibbonItem(localizer["Ribbon_Import"], "<svg><use href='/icons/sprite.svg#upload'/></svg>", UiRibbonItemSize.Large, false, "Import")
            }));

        return groups;
    }

    // Upload handling
    private sealed class UploadResponse { public StatementDraftDto? FirstDraft { get; set; } public object? SplitInfo { get; set; } }
    public async Task<Guid?> UploadAsync(Stream stream, string fileName, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var content = new MultipartFormDataContent();
        content.Add(new StreamContent(stream), "file", fileName);
        using var resp = await _http.PostAsync("/api/statement-drafts/upload", content, ct);
        if (!resp.IsSuccessStatusCode)
        {
            return null;
        }
        var result = await resp.Content.ReadFromJsonAsync<UploadResponse>(cancellationToken: ct);
        return result?.FirstDraft?.DraftId;
    }

    // Classification
    private sealed class TempStatus { public bool running { get; set; } public int processed { get; set; } public int total { get; set; } public string? message { get; set; } }
    public async Task StartClassifyAsync(CancellationToken ct = default)
    {
        _classifyCts ??= new CancellationTokenSource();
        using var resp = await _http.PostAsync("api/statement-drafts/classify", content: null, _classifyCts.Token);
        if (resp.StatusCode == System.Net.HttpStatusCode.Accepted)
        {
            IsClassifying = true;
            var payload = await resp.Content.ReadFromJsonAsync<TempStatus>(_classifyCts.Token);
            UpdateClassifyUi(payload);
            _ = PollClassifyUntilFinishedAsync();
        }
        else if (resp.IsSuccessStatusCode)
        {
            IsClassifying = false; ClassifyMessage = null; await ReloadAfterActionAsync();
        }
        else
        {
            ClassifyMessage = await resp.Content.ReadAsStringAsync(_classifyCts.Token);
            RaiseStateChanged();
        }
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
        var s = await _http.GetFromJsonAsync<TempStatus>("api/statement-drafts/classify/status", ct);
        if (s != null)
        {
            var wasRunning = IsClassifying;
            UpdateClassifyUi(s);
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
    private void UpdateClassifyUi(TempStatus? s)
    {
        if (s == null) { return; }
        IsClassifying = s.running;
        ClassifyProcessed = s.processed;
        ClassifyTotal = s.total;
        ClassifyMessage = s.message ?? (IsClassifying ? "Working..." : null);
        RaiseStateChanged();
    }

    // Booking
    public sealed class BookIssue { public Guid draftId { get; set; } public Guid? entryId { get; set; } public string code { get; set; } = string.Empty; public string message { get; set; } = string.Empty; }
    private sealed class BookStatus { public bool running { get; set; } public int processed { get; set; } public int failed { get; set; } public int total { get; set; } public int warnings { get; set; } public int errors { get; set; } public string? message { get; set; } public List<BookIssue>? issues { get; set; } }
    public async Task StartBookAllAsync(bool ignoreWarnings, bool abortOnFirstIssue, bool bookEntriesIndividually, CancellationToken ct = default)
    {
        _bookingCts ??= new CancellationTokenSource();
        var payload = new { ignoreWarnings, abortOnFirstIssue, bookEntriesIndividually };
        using var resp = await _http.PostAsJsonAsync("api/statement-drafts/book-all", payload, _bookingCts.Token);
        if (resp.StatusCode == System.Net.HttpStatusCode.Accepted)
        {
            IsBooking = true;
            var s = await resp.Content.ReadFromJsonAsync<BookStatus>(_bookingCts.Token);
            UpdateBookingUi(s);
            _ = PollBookingUntilFinishedAsync();
        }
        else if (resp.IsSuccessStatusCode)
        {
            IsBooking = false; BookingMessage = null; await ReloadAfterActionAsync(); await RefreshBookStatusAsync();
        }
        else
        {
            BookingMessage = await resp.Content.ReadAsStringAsync(_bookingCts.Token);
            RaiseStateChanged();
        }
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
        var s = await _http.GetFromJsonAsync<BookStatus>("api/statement-drafts/book-all/status", ct);
        if (s != null)
        {
            var wasRunning = IsBooking;
            UpdateBookingUi(s);
            if (!wasRunning && IsBooking)
            {
                _ = PollBookingUntilFinishedAsync();
            }
            if (!s.running && s.total > 0 && reloadOnFinish)
            {
                await ReloadAfterActionAsync(ct);
            }
        }
    }
    private void UpdateBookingUi(BookStatus? s)
    {
        if (s == null) { return; }
        IsBooking = s.running;
        BookingProcessed = s.processed;
        BookingFailed = s.failed;
        BookingTotal = s.total;
        BookingMessage = s.message ?? (IsBooking ? "Working..." : null);
        BookingErrors = s.errors;
        BookingWarnings = s.warnings;
        BookingIssues = s.issues ?? new();
        RaiseStateChanged();
    }
    public async Task CancelBookingAsync(CancellationToken ct = default)
    {
        try { await _http.PostAsync("api/statement-drafts/book-all/cancel", content: null, ct); } catch { }
    }

    private async Task ReloadAfterActionAsync(CancellationToken ct = default)
    {
        Reset();
        await LoadMoreAsync(ct);
    }
}
