using FinanceManager.Shared.Dtos.Statements;
using FinanceManager.Web.ViewModels.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using FinanceManager.Web.ViewModels;
using FinanceManager.Shared.Dtos.Admin;

namespace FinanceManager.Web.ViewModels.StatementDrafts;

/// <summary>
/// ViewModel that exposes the list of statement drafts for the current user and provides
/// operations for importing, deleting and triggering background tasks such as classification and booking.
/// </summary>
public sealed class StatementDraftsListViewModel : BaseListViewModel<StatementDraftsListViewModel.DraftItem>, FinanceManager.Web.ViewModels.Common.IUploadFilesViewModel
{
    private readonly FinanceManager.Shared.IApiClient _api;
    private readonly NavigationManager _nav;

    /// <summary>
    /// Background task types that should be visible on pages using this view model.
    /// </summary>
    public override BackgroundTaskType[]? VisibleBackgroundTaskTypes => new[] { BackgroundTaskType.ClassifyAllDrafts, BackgroundTaskType.BookAllDrafts };

    /// <summary>
    /// Initializes a new instance of <see cref="StatementDraftsListViewModel"/>.
    /// </summary>
    /// <param name="sp">Service provider used to resolve dependencies.</param>
    public StatementDraftsListViewModel(IServiceProvider sp) : base(sp)
    {
        _api = sp.GetRequiredService<FinanceManager.Shared.IApiClient>();
        _nav = sp.GetRequiredService<NavigationManager>();
        // disable generic range/search UI for drafts list
        AllowRangeFiltering = false;
        AllowSearchFiltering = false;
    }

    /// <summary>
    /// Represents a single draft entry shown in the list.
    /// </summary>
    public sealed class DraftItem : IListItemNavigation
    {
        /// <summary>Draft identifier.</summary>
        public Guid Id { get; set; }
        /// <summary>Original file name of the uploaded draft.</summary>
        public string FileName { get; set; } = string.Empty;
        /// <summary>Optional user-provided description.</summary>
        public string? Description { get; set; }
        /// <summary>Status of the draft.</summary>
        public StatementDraftStatus Status { get; set; }
        /// <summary>Number of entries still pending booking.</summary>
        public int PendingEntries { get; set; }
        /// <summary>Optional symbol attachment id used for list rendering.</summary>
        public Guid? AttachmentSymbolId { get; set; }

        /// <summary>
        /// Returns the navigate URL for the detail card of this draft.
        /// </summary>
        /// <returns>Relative URL to the draft card.</returns>
        public string GetNavigateUrl() => $"/card/statement-drafts/{Id}";
    }

    private int _skip;
    private const int PageSize = 3;

    /// <summary>
    /// Loads a page of drafts. This implementation requests open drafts from the API and appends them to <see cref="FinanceManager.Web.ViewModels.Common.BaseListViewModel{TItem}.Items"/>.
    /// </summary>
    /// <param name="resetPaging">When true the paging state is reset and items cleared.</param>
    protected override async Task LoadPageAsync(bool resetPaging)
    {
        if (!IsAuthenticated) return;
        if (resetPaging)
        {
            _skip = 0;
            Items.Clear();
        }

        try
        {
            var batch = await _api.StatementDrafts_ListOpenAsync(_skip, PageSize, CancellationToken.None);
            var list = batch?.ToList() ?? new List<FinanceManager.Shared.Dtos.Statements.StatementDraftDto>();
            if (list.Count < PageSize) CanLoadMore = false; else CanLoadMore = true;

            foreach (var d in list)
            {
                var pending = d.Entries.Count(e => e.Status != StatementDraftEntryStatus.AlreadyBooked);
                Items.Add(new DraftItem
                {
                    Id = d.DraftId,
                    FileName = d.OriginalFileName,
                    Description = d.Description,
                    Status = d.Status,
                    PendingEntries = pending,
                    AttachmentSymbolId = d.AttachmentSymbolId,
                });
            }
            _skip += list.Count;
        }
        catch
        {
            // swallow � UI shows empty state
            Items.Clear();
            CanLoadMore = false;
        }
    }

    /// <summary>
    /// Builds the list columns and records used by the list renderer.
    /// </summary>
    protected override void BuildRecords()
    {
        var L = ServiceProvider.GetRequiredService<IStringLocalizer<Pages>>();
        Columns = new List<ListColumn>
        {
            new ListColumn("", string.Empty, "48px", ListColumnAlign.Left),
            new ListColumn("file", L["List_Th_StatementDrafts_File"].Value, "", ListColumnAlign.Left),
            new ListColumn("description", L["List_Th_StatementDrafts_Description"].Value, "", ListColumnAlign.Left),
            new ListColumn("status", L["List_Th_StatementDrafts_Status"].Value, "160px", ListColumnAlign.Left),
            new ListColumn("entries", L["List_Th_StatementDrafts_Entries"].Value, "110px", ListColumnAlign.Right)
        };

        Records = Items.Select(i => new ListRecord(new List<ListCell>
        {
            new ListCell(ListCellKind.Symbol, SymbolId:i.AttachmentSymbolId, Text: string.Empty),
            new ListCell(ListCellKind.Text, Text: i.FileName),
            new ListCell(ListCellKind.Text, Text: i.Description ?? string.Empty),
            new ListCell(ListCellKind.Text, Text: i.Status.ToString()),
            new ListCell(ListCellKind.Text, Text: i.PendingEntries.ToString())
        }, i as object)).ToList();
    }

    /// <summary>
    /// Builds ribbon register definitions for the list view providing actions such as New, DeleteAll, Reclassify, MassBooking and Import.
    /// </summary>
    /// <param name="localizer">Localizer used to resolve action labels.</param>
    /// <returns>Collection of ribbon registers to render for this view.</returns>
    protected override IReadOnlyList<UiRibbonRegister>? GetRibbonRegisterDefinition(IStringLocalizer localizer)
    {
        var tabs = new List<UiRibbonTab>
        {
            new UiRibbonTab(localizer["Ribbon_Group_Manage"].Value, new List<UiRibbonAction>
            {
                new UiRibbonAction(
                    "New",
                    localizer["Ribbon_New"].Value,
                    "<svg><use href='/icons/sprite.svg#new'/></svg>",
                    UiRibbonItemSize.Large,
                    false,
                    null,
                    new Func<Task>(() => { _nav.NavigateTo($"/card/statement-drafts/new"); return Task.CompletedTask; }))
                { MobileShortcut = true },
                new UiRibbonAction(
                    "DeleteAll",
                    localizer["Ribbon_DeleteAll"].Value,
                    "<svg><use href='/icons/sprite.svg#delete'/></svg>",
                    UiRibbonItemSize.Large,
                    false,
                    null,
                    new Func<Task>(async () => { await DeleteAllAsync(); })),
                new UiRibbonAction(
                    "Reclassify",
                    localizer["Ribbon_Reclassify"].Value,
                    "<svg><use href='/icons/sprite.svg#refresh'/></svg>",
                    UiRibbonItemSize.Large,
                    false,
                    null,
                    new Func<Task>(async () => { await StartClassifyAsync(); })),
                new UiRibbonAction(
                    "MassBooking",
                    localizer["Ribbon_MassBooking"].Value,
                    "<svg><use href='/icons/sprite.svg#save'/></svg>",
                    UiRibbonItemSize.Large,
                    false,
                    null,
                    // Show options dialog before starting mass booking
                    new Func<Task>(() => {
                        // Build overlay spec with callback to start booking
                        var overlayType = typeof(FinanceManager.Web.Components.Shared.MassBookingOptionsPanel);
                        var parameters = new Dictionary<string, object?>
                        {
                            ["StartCallback"] = new Func<bool, bool, bool, Task>(async (ignoreWarnings, abortOnFirstIssue, bookEntriesIndividually) =>
                            {
                                await StartBookAllAsync(ignoreWarnings, abortOnFirstIssue, bookEntriesIndividually);
                            }),
                            ["OverlayTitle"] = localizer["MassBooking_Title"].Value
                        };

                        var spec = new FinanceManager.Web.ViewModels.Common.BaseViewModel.UiOverlaySpec(overlayType, parameters);
                        RaiseUiActionRequested(null, payloadObject: spec);
                        return Task.CompletedTask;
                    }))
                { MobileShortcut = true },
                new UiRibbonAction(
                    "Import",
                    localizer["Ribbon_Import"].Value,
                    "<svg><use href='/icons/sprite.svg#upload'/></svg>",
                    UiRibbonItemSize.Large,
                    false,
                    null,
                    null)
                {
                    FileCallback = async (InputFileChangeEventArgs e) =>
                    {
                        var files = e.GetMultipleFiles();
                        if (files == null || files.Count == 0) return;

                        var streams = new List<(Stream Stream, string FileName)>();
                        try
                        {
                            foreach (var f in files)
                            {
                                streams.Add((f.OpenReadStream(10_000_000), f.Name));
                            }

                            var uploadResult = await ((FinanceManager.Web.ViewModels.Common.IUploadFilesViewModel)this).UploadFilesAsync("statementdraft", streams, CancellationToken.None);
                            if (uploadResult?.StatementDraftResult?.FirstDraft != null && FirstDraftId == null)
                            {
                                FirstDraftId = uploadResult.StatementDraftResult.FirstDraft.DraftId;
                            }
                        }
                        finally
                        {
                            foreach (var (s, _) in streams)
                            {
                                try { s.Dispose(); } catch { }
                            }
                        }

                        // refresh list after upload
                        ResetAndSearch();
                        await LoadAsync();
                    },
                    MobileShortcut = true
                }
            })
        };

        return new List<UiRibbonRegister> { new UiRibbonRegister(UiRibbonRegisterKind.Actions, tabs) };
    }

    // Upload state (for statement-drafts import)

    /// <summary>Indicates whether an import upload operation is in progress.</summary>
    public bool UploadInProgress { get; private set; }
    /// <summary>Total number of files to upload in current batch.</summary>
    public int UploadTotal { get; private set; }
    /// <summary>Number of files already uploaded in current batch.</summary>
    public int UploadDone { get; private set; }
    /// <summary>Name of the file currently being uploaded (or <c>null</c> when none).</summary>
    public string? CurrentFileName { get; private set; }
    /// <summary>Indicates whether the last import was considered successful.</summary>
    public bool ImportSuccess { get; private set; }
    /// <summary>Id of the first draft created during the last import batch (if any).</summary>
    public Guid? FirstDraftId { get; private set; }
    /// <summary>Optional split info returned by the import API when a file contained multiple drafts.</summary>
    public ImportSplitInfoDto? SplitInfo { get; private set; }

    /// <summary>
    /// Progress percentage computed from upload totals and progress.
    /// </summary>
    /// <returns>The result.</returns>
    public int UploadPercent => UploadTotal == 0 ? 0 : (int)Math.Round((double)(UploadDone * 100m / UploadTotal));

    // Classification background task state
    /// <summary>Indicates whether a classification background task is running.</summary>
    public bool IsClassifying { get; private set; }
    /// <summary>Indicates whether a booking (mass) background task is running.</summary>
    public bool IsBooking { get; private set; }
    // Polling cancellation for book-all status
    private CancellationTokenSource? _bookPollingCts;

    private void StartUpload(int total)
    {
        UploadInProgress = true;
        UploadTotal = total;
        UploadDone = 0;
        CurrentFileName = null;
        ImportSuccess = false;
        FirstDraftId = null;
        SplitInfo = null;
        RaiseStateChanged();
    }

    /// <summary>
    /// Uploads a single file stream to the statement-drafts API endpoint and updates local upload state.
    /// </summary>
    /// <param name="stream">Stream containing file content. The caller remains responsible for disposing the stream.</param>
    /// <param name="fileName">Original filename.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Upload result DTO when upload succeeded; otherwise <c>null</c>.</returns>
    private async Task<StatementDraftUploadResult?> UploadFileAsync(Stream stream, string fileName, CancellationToken ct = default)
    {
        CurrentFileName = fileName;
        RaiseStateChanged();
        try
        {
            var result = await _api.StatementDrafts_UploadAsync(stream, fileName, ct);
            if (result?.FirstDraft != null && FirstDraftId == null)
            {
                FirstDraftId = result.FirstDraft.DraftId;
            }
            if (result?.SplitInfo != null)
            {
                SplitInfo = result.SplitInfo;
            }
            if (FirstDraftId.HasValue)
            {
                ImportSuccess = true;
            }
            return result;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return null;
        }
        finally
        {
            UploadDone++;
            if (UploadDone >= UploadTotal)
            {
                UploadInProgress = false;
                CurrentFileName = null;
            }
            RaiseStateChanged();
        }
    }

    /// <summary>
    /// Implementation of <see cref="IUploadFilesViewModel.UploadFilesAsync"/> for statement-draft imports.
    /// </summary>
    /// <param name="payload">Payload identifier (must be "statementdraft").</param>
    /// <param name="files">Enumerable of streams and filenames to upload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Upload result containing first draft result and created count, or <c>null</c> when payload is unsupported or no files provided.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="payload"/> is null or whitespace.</exception>
    async Task<FinanceManager.Web.ViewModels.Common.UploadResult?> FinanceManager.Web.ViewModels.Common.IUploadFilesViewModel.UploadFilesAsync(string payload, IEnumerable<(Stream Stream, string FileName)> files, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(payload)) throw new ArgumentNullException(nameof(payload));
        // Only 'statementdraft' payload supported here. Future payloads can be routed accordingly.
        if (!string.Equals(payload, "statementdraft", StringComparison.OrdinalIgnoreCase))
            return null;

        var list = files.ToList();
        if (!list.Any()) return null;

        StartUpload(list.Count);
        int createdCount = 0;
        StatementDraftUploadResult? firstResult = null;
        foreach (var (stream, fileName) in list)
        {
            if (ct.IsCancellationRequested) break;
            var res = await UploadFileAsync(stream, fileName, ct).ConfigureAwait(false);
            if (res != null && res.FirstDraft != null)
            {
                createdCount++;
                if (firstResult == null) firstResult = res;
            }
        }

        return new FinanceManager.Web.ViewModels.Common.UploadResult { StatementDraftResult = firstResult, CreatedCount = createdCount };
    }

    /// <summary>
    /// Deletes all open statement drafts for the current user and resets list state.
    /// </summary>
    /// <returns>True when deletion succeeded; otherwise false.</returns>
    public async Task<bool> DeleteAllAsync()
    {
        try
        {
            if (!await ConfirmationService.ConfirmAsync(new(
                TitleResourceKey: "Confirmation_Delete_Title",
                MessageResourceKey: "Confirmation_Delete_Message",
                Severity: ConfirmationSeverity.Critical)))
            {
                return false;
            }

            var ok = await _api.StatementDrafts_DeleteAllAsync(CancellationToken.None);
            if (!ok)
            {
                SetError(_api.LastErrorCode ?? null, _api.LastError ?? "Delete failed");
                return false;
            }

            // Clear current items and reset paging so UI can reload
            Items.Clear();
            _skip = 0;
            CanLoadMore = true;
            RaiseStateChanged();
            return true;
        }
        catch (Exception ex)
        {
            SetError(null, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Starts a background classification task (classify all drafts) if not already running.
    /// </summary>
    public async Task StartClassifyAsync()
    {
        try
        {
            SetError(null, null);
            var status = await _api.StatementDrafts_StartClassifyAsync(CancellationToken.None);
            // If API returned an explicit status, honor it; otherwise assume queued/running
            IsClassifying = status?.running ?? true;
            RaiseStateChanged();
        }
        catch (Exception ex)
        {
            SetError(null, ex.Message);
            IsClassifying = false;
            RaiseStateChanged();
        }
    }

    /// <summary>
    /// Refreshes classification status; when completed reloads the list.
    /// </summary>
    public async Task RefreshClassifyStatusAsync()
    {
        try
        {
            SetError(null, null);
            var status = await _api.StatementDrafts_GetClassifyStatusAsync(CancellationToken.None);
            IsClassifying = status?.running ?? false;
            // if classification finished, refresh list
            if (!IsClassifying)
            {
                await InitializeAsync();
            }
            RaiseStateChanged();
        }
        catch (Exception ex)
        {
            SetError(null, ex.Message);
            IsClassifying = false;
            RaiseStateChanged();
        }
    }

    /// <summary>
    /// Starts mass booking (book-all) background task.
    /// </summary>
    /// <param name="ignoreWarnings">When true ignore non-fatal warnings and continue.</param>
    /// <param name="abortOnFirstIssue">When true abort on first detected issue.</param>
    /// <param name="bookEntriesIndividually">When true book entries individually instead of batch.</param>
    public async Task StartBookAllAsync(bool ignoreWarnings, bool abortOnFirstIssue, bool bookEntriesIndividually)
    {
        try
        {
            if (!await ConfirmationService.ConfirmAsync(new(
                TitleResourceKey: "Confirmation_Book_Title",
                MessageResourceKey: "Confirmation_Book_Message",
                Severity: ConfirmationSeverity.Warning)))
            {
                return;
            }

            SetError(null, null);
            var status = await _api.StatementDrafts_StartBookAllAsync(ignoreWarnings, abortOnFirstIssue, bookEntriesIndividually, CancellationToken.None);
            IsBooking = status?.Running ?? true;
            RaiseStateChanged();

            // If booking is running on server, start polling status every second until finished
            if (IsBooking)
            {
                StartBookStatusPolling();
            }
            else
            {
                // If booking already finished synchronously, reload list
                await InitializeAsync();
            }
        }
        catch (Exception ex)
        {
            SetError(null, ex.Message);
            IsBooking = false;
            RaiseStateChanged();
        }
    }

    /// <summary>
    /// Refreshes book-all status; when completed reloads the list.
    /// </summary>
    public async Task RefreshBookStatusAsync()
    {
        try
        {
            SetError(null, null);
            var status = await _api.StatementDrafts_GetBookAllStatusAsync(CancellationToken.None);
            IsBooking = status?.Running ?? false;
            if (!IsBooking)
            {
                // Stop polling if active and refresh list
                StopBookStatusPolling();
                await InitializeAsync();
            }
            RaiseStateChanged();
        }
        catch (Exception ex)
        {
            SetError(null, ex.Message);
            IsBooking = false;
            RaiseStateChanged();
        }
    }

    private void StartBookStatusPolling()
    {
        // cancel any existing polling
        StopBookStatusPolling();
        _bookPollingCts = new CancellationTokenSource();
        var token = _bookPollingCts.Token;
        // fire-and-forget polling loop; catch exceptions to avoid unobserved exceptions
        _ = Task.Run(async () =>
        {
            try
            {
                while (!token.IsCancellationRequested && IsBooking)
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(1), token);
                    }
                    catch (TaskCanceledException) { break; }

                    if (token.IsCancellationRequested) break;
                    try
                    {
                        await RefreshBookStatusAsync();
                    }
                    catch
                    {
                        // swallow per-refresh exceptions; RefreshBookStatusAsync already sets state and errors
                    }
                }
            }
            finally
            {
                // ensure polling CTS is disposed when loop exits
                StopBookStatusPolling();
            }
        }, CancellationToken.None);
    }

    private void StopBookStatusPolling()
    {
        try
        {
            if (_bookPollingCts != null)
            {
                if (!_bookPollingCts.IsCancellationRequested) _bookPollingCts.Cancel();
                _bookPollingCts.Dispose();
                _bookPollingCts = null;
            }
        }
        catch { }
    }

    /// <summary>
    /// Attempts to cancel running book-all background task.
    /// </summary>
    public async Task CancelBookingAsync()
    {
        try
        {
            if (!await ConfirmationService.ConfirmAsync(new(
                TitleResourceKey: "Confirmation_Finalize_Title",
                MessageResourceKey: "Confirmation_Finalize_Message",
                Severity: ConfirmationSeverity.Warning)))
            {
                return;
            }

            SetError(null, null);
            // Stop local polling first to avoid races
            StopBookStatusPolling();
            await _api.StatementDrafts_CancelBookAllAsync(CancellationToken.None);
            // after cancel, refresh status (this will also reload list when finished)
            await RefreshBookStatusAsync();
        }
        catch (Exception ex)
        {
            SetError(null, ex.Message);
            RaiseStateChanged();
        }
    }
}
