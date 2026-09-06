using FinanceManager.Shared;
using Microsoft.Extensions.Localization;

namespace FinanceManager.Web.ViewModels.Postings.Common
{
    /// <summary>
    /// Base list view model specialized for postings. Provides common column definitions, paging and export URL helpers
    /// that are shared across various postings list implementations.
    /// </summary>
    public abstract class BasePostingsListViewModel : BaseListViewModel<PostingServiceDto>
    {
        /// <summary>
        /// Initializes a new instance of <see cref="BasePostingsListViewModel"/> using the provided service provider.
        /// </summary>
        /// <param name="services">Service provider used to resolve dependencies such as the localizer and API client.</param>
        protected BasePostingsListViewModel(IServiceProvider services) : base(services)
        {
            var dateLabel = Localizer?["List_Th_Postings_Date"].Value ?? "Date";
            var valutaLabel = Localizer?["List_Th_Postings_Valuta"].Value ?? "Valuta";
            var amountLabel = Localizer?["List_Th_Postings_Amount"].Value ?? "Amount";
            var kindLabel = Localizer?["List_Th_Postings_Kind"].Value ?? "Kind";
            var recipientLabel = Localizer?["List_Th_Postings_Recipient"].Value ?? "Recipient";
            var subjectLabel = Localizer?["List_Th_Postings_Subject"].Value ?? "Subject";
            var descriptionLabel = Localizer?["List_Th_Postings_Description"].Value ?? "Description";
            var stornoLabel = Localizer?["List_Th_Postings_IsReversal"].Value ?? "Storno";
            var preliminaryLabel = Localizer?["List_Th_Postings_IsPreliminary"].Value ?? "Vorläufig";

            Columns = new[] {
                new ListColumn("date", dateLabel, Align: ListColumnAlign.Left, Width: "8rem"),
                new ListColumn("valuta", valutaLabel, Align: ListColumnAlign.Left, Width: "8rem"),
                new ListColumn("amount", amountLabel, Align: ListColumnAlign.Right, Width: "10rem"),
                new ListColumn("kind", kindLabel, Align: ListColumnAlign.Left, Width: "9rem"),
                new ListColumn("recipient", recipientLabel),
                new ListColumn("subject", subjectLabel, Width: "22%"),
                new ListColumn("description", descriptionLabel),
                new ListColumn("preliminary", preliminaryLabel, Align: ListColumnAlign.Left, Width: "5rem"),
                new ListColumn("storno", stornoLabel, Align: ListColumnAlign.Left, Width: "5rem")
            };
            _take = 50;
        }

        /// <summary>
        /// Number of items to request per page when querying postings. Initialized in the constructor.
        /// </summary>
        protected int _take;

        /// <summary>
        /// Current paging offset (number of items skipped).
        /// </summary>
        protected int _skip;

        /// <summary>
        /// Resets internal paging state so the next load will start from the beginning.
        /// </summary>
        protected void ResetPaging() { _skip = 0; CanLoadMore = true; }

        /// <summary>
        /// Loads a page of postings from the configured query and appends them to the item collection.
        /// </summary>
        /// <param name="resetPaging">When <c>true</c> the paging offset is reset and items cleared before loading.</param>
        /// <returns>A task that completes when the page load has finished.</returns>
        /// <exception cref="System.Exception">Propagates exceptions thrown by the underlying API client or query implementation.</exception>
        protected override async Task LoadPageAsync(bool resetPaging)
        {
            if (resetPaging) { ResetPaging(); Items.Clear(); }

            var api = ServiceProvider.GetRequiredService<IApiClient>();
            var list = await QueryPageAsync(api, _skip, _take, Search, RangeFrom, RangeTo);
            if (resetPaging)
            {
                Items.Clear();
            }
            if (list != null && list.Count > 0)
            {
                Items.AddRange(list);
                _skip += list.Count;
                CanLoadMore = list.Count >= _take;
            }
            else
            {
                CanLoadMore = false;
            }
        }

        /// <summary>
        /// Implemented by derived classes to query postings according to their filter criteria.
        /// </summary>
        /// <param name="api">API client to use for the query. Implementations should not dispose this instance.</param>
        /// <param name="skip">Number of items to skip for paging.</param>
        /// <param name="take">Number of items to request.</param>
        /// <param name="search">Search string to filter postings.</param>
        /// <param name="from">Optional start date for date range filtering.</param>
        /// <param name="to">Optional end date for date range filtering.</param>
        /// <returns>
        /// A task that resolves to a read-only list of <see cref="PostingServiceDto"/> when results are available;
        /// or <c>null</c> when the query failed or no data was returned.
        /// </returns>
        protected abstract Task<IReadOnlyList<PostingServiceDto>?> QueryPageAsync(IApiClient api, int skip, int take, string search, DateTime? from, DateTime? to);

        /// <summary>
        /// Builds <see cref="FinanceManager.Web.ViewModels.Common.BaseListViewModel{TItem}.Records"/> from the current <see cref="FinanceManager.Web.ViewModels.Common.BaseListViewModel{TItem}.Items"/>. The default implementation maps the Posting fields
        /// into table cells and constructs navigation wrapper items that implement <see cref="IListItemNavigation"/>.
        /// </summary>
        protected override void BuildRecords()
        {
            Columns = Columns ?? Array.Empty<ListColumn>();
            Records = Items.Select(i =>
            {
                // create a navigation wrapper item that implements IListItemNavigation
                var navItem = new PostingListItem(i);
                return new ListRecord(new[] {
                    new ListCell(ListCellKind.Text, Text: i.BookingDate.ToString("d")),
                    new ListCell(ListCellKind.Text, Text: i.ValutaDate.ToString("d")),
                    new ListCell(ListCellKind.Currency, Amount: i.Amount),
                    new ListCell(ListCellKind.Text, Text: (i.Kind == PostingKind.Security && i.SecuritySubType.HasValue) ? $"Security-{i.SecuritySubType}" : i.Kind.ToString()),
                    new ListCell(ListCellKind.Text, Text: i.RecipientName ?? string.Empty),
                    new ListCell(ListCellKind.Text, Text: i.Subject ?? string.Empty),
                    new ListCell(ListCellKind.Text, Text: i.Description ?? string.Empty),
                    new ListCell(ListCellKind.Text, Text: i.IsPreliminary ? "✓" : string.Empty),
                    new ListCell(ListCellKind.Text, Text: i.IsReversal ? "✓" : (i.IsReversed ? "—" : string.Empty))
                }, navItem);
            }).ToList();
        }

        // navigation wrapper record for postings
        /// <summary>
        /// Small wrapper record used to provide a navigation URL for posting items displayed in the list.
        /// Accessible to derived classes so they can reuse the same navigation logic when overriding
        /// <see cref="BuildRecords"/>.
        /// </summary>
        /// <param name="Posting">Posting DTO wrapped by this navigation item.</param>
        /// <returns>The result.</returns>
        protected sealed record PostingListItem(PostingServiceDto Posting) : IListItemNavigation
        {
            /// <summary>
            /// Returns the target URL for navigating to the posting card.
            /// </summary>
            /// <returns>Relative URL for the posting card page.</returns>
            public string GetNavigateUrl()
            {
                // navigate to card page for posting
                var kind = "postings";
                return $"/card/{kind}/{Posting.Id}";
            }
        }

        /// <summary>
        /// Optional override point for navigation URL used by the view model (default empty).
        /// </summary>
        /// <returns>A string containing a relative navigation URL or an empty string when none is provided.</returns>
        public virtual string GetNavigateUrl()
        {
            return string.Empty;
        }

        /// <summary>
        /// Builds an export URL including common query parameters (format, search, date range).
        /// Derived classes may call this helper and append additional parameters to the returned base path.
        /// </summary>
        /// <param name="basePath">Base endpoint path used for the export (e.g. "/api/postings/account/{id}/export").</param>
        /// <param name="format">Export format identifier (e.g. "csv" or "xlsx").</param>
        /// <returns>A relative URL that can be used to download the export.</returns>
        protected string BuildExportUrl(string basePath, string format)
        {
            var q = new List<string>();
            if (!string.IsNullOrWhiteSpace(format)) q.Add($"format={Uri.EscapeDataString(format)}");
            if (!string.IsNullOrWhiteSpace(Search)) q.Add($"q={Uri.EscapeDataString(Search)}");
            if (RangeFrom.HasValue) q.Add($"from={RangeFrom.Value:yyyy-MM-dd}");
            if (RangeTo.HasValue) q.Add($"to={RangeTo.Value:yyyy-MM-dd}");
            return q.Count == 0 ? basePath : basePath + "?" + string.Join('&', q);
        }

        /// <summary>
        /// Returns the export URL for the requested format. Derived classes should override to provide a concrete export path.
        /// </summary>
        /// <param name="format">Export format identifier (e.g. "csv", "xlsx").</param>
        /// <returns>Relative export URL or an empty string when not supported.</returns>
        public virtual string GetExportUrl(string format) => string.Empty;

        /// <summary>
        /// Default Ribbon for postings lists: Navigation (Back) and Export (CSV/XLSX).
        /// Derived classes may override to customize available actions.
        /// </summary>
        /// <param name="localizer">Localizer used to resolve labels for the ribbon actions.</param>
        /// <returns>A list of UI ribbon registers describing tabs and actions, or <c>null</c> when no ribbon is available.</returns>
        protected override IReadOnlyList<UiRibbonRegister>? GetRibbonRegisterDefinition(IStringLocalizer localizer)
        {
            var tabs = new List<UiRibbonTab>
            {
                new UiRibbonTab(localizer["Ribbon_Group_Navigation"].Value, new List<UiRibbonAction>
                {
                    new UiRibbonAction("Back", localizer["Ribbon_Back"].Value, "<svg><use href='/icons/sprite.svg#back'/></svg>", UiRibbonItemSize.Large, false, null, new Func<Task>(()=>{ RaiseUiActionRequested("Back"); return Task.CompletedTask; })) { MobileShortcut = true },
                }),
                new UiRibbonTab(localizer["Ribbon_Group_Export"].Value, new List<UiRibbonAction>
                {
                    new UiRibbonAction("ExportCsv", localizer["Ribbon_ExportCsv"].Value, "<svg><use href='/icons/sprite.svg#download'/></svg>", UiRibbonItemSize.Small, Loading, null, new Func<Task>(()=>{ RaiseUiActionRequested("ExportCsv"); return Task.CompletedTask; })),
                    new UiRibbonAction("ExportXlsx", localizer["Ribbon_ExportExcel"].Value, "<svg><use href='/icons/sprite.svg#download'/></svg>", UiRibbonItemSize.Small, Loading, null, new Func<Task>(()=>{ RaiseUiActionRequested("ExportXlsx"); return Task.CompletedTask; }))
                })
            };

            var registers = new List<UiRibbonRegister> { new UiRibbonRegister(UiRibbonRegisterKind.Actions, tabs) };
            return registers.Count == 0 ? null : registers;
        }
    }
}
