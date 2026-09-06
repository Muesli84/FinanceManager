using FinanceManager.Shared;
using Microsoft.Extensions.Localization;

namespace FinanceManager.Web.ViewModels.Accounts
{
    /// <summary>
    /// Load state for the account statistics tile, independent from the account list.
    /// </summary>
    public enum AccountStatisticsLoadState
    {
        /// <summary>Statistics loading has not started.</summary>
        NotStarted,
        /// <summary>Statistics are currently loading.</summary>
        Loading,
        /// <summary>Statistics are loaded and contain at least one account.</summary>
        Loaded,
        /// <summary>Statistics loaded successfully for an empty account scope.</summary>
        Empty,
        /// <summary>Statistics loading failed.</summary>
        Error
    }

    /// <summary>
    /// List view model providing paging and rendering logic for bank accounts.
    /// </summary>
    public sealed class BankAccountListViewModel : BaseListViewModel<AccountListItem>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="BankAccountListViewModel"/> class.
        /// </summary>
        /// <param name="sp">Service provider used to resolve required services (API client, localizer, etc.).</param>
        public BankAccountListViewModel(IServiceProvider sp) : base(sp)
        {
        }

        /// <summary>
        /// Indicates whether range/date filtering is supported by this list. Accounts list does not allow range filtering.
        /// </summary>
        public override bool AllowRangeFiltering => false;

        private int _skip;
        private long _statisticsGeneration;
        private CancellationTokenSource? _statisticsCts;
        private const int PageSize = 50;

        /// <summary>
        /// Current account statistics payload.
        /// </summary>
        public AccountStatisticsDto? Statistics { get; private set; }

        /// <summary>
        /// Current statistics load state.
        /// </summary>
        public AccountStatisticsLoadState StatisticsState { get; private set; } = AccountStatisticsLoadState.NotStarted;

        /// <summary>
        /// Statistics-specific error message. List errors remain independent.
        /// </summary>
        public string? StatisticsErrorMessage { get; private set; }

        /// <summary>
        /// Loads a page of account items from the API and appends them to the internal item collection.
        /// </summary>
        /// <param name="resetPaging">When <c>true</c> the paging offset is reset and the list will be rebuilt from the first page.</param>
        /// <returns>A task that completes when the page load has finished. Errors from the API are captured via <see cref="FinanceManager.Web.ViewModels.Common.BaseViewModel.SetError(string?, string?)"/> and do not propagate.</returns>
        protected override async Task LoadPageAsync(bool resetPaging)
        {
            var api = ServiceProvider.GetRequiredService<IApiClient>();
            var q = NormalizeSearchForRequest(Search);
            try
            {
                if (resetPaging)
                {
                    _skip = 0;
                    StartStatisticsLoad(q);
                }

                var list = await api.GetAccountsAsync(_skip, PageSize, null, q);
                var items = (list ?? Array.Empty<AccountDto>())
                    .Select(a => new AccountListItem(a.Id, a.Name ?? string.Empty, a.Type, a.Iban, a.CurrentBalance, a.SymbolAttachmentId));
                if (resetPaging) Items.Clear();
                Items.AddRange(items);
                _skip += PageSize;
                CanLoadMore = list != null && list.Count >= PageSize;
            }
            catch (Exception ex)
            {
                SetError(api.LastErrorCode ?? null, api.LastError ?? ex.Message);
                CanLoadMore = false;
            }
        }

        /// <summary>
        /// Retries loading statistics for the currently active search without reloading the list.
        /// </summary>
        public Task RetryStatisticsAsync()
        {
            StartStatisticsLoad(NormalizeSearchForRequest(Search));
            return Task.CompletedTask;
        }

        /// <summary>
        /// Builds the list column definitions and list records used by the UI to render the accounts table.
        /// </summary>
        protected override void BuildRecords()
        {
            var L = ServiceProvider.GetRequiredService<IStringLocalizer<Pages>>();
            Columns = new List<ListColumn>
            {
                new ListColumn("symbol", string.Empty, "48px", ListColumnAlign.Left),
                new ListColumn("name", L["List_Th_Account_Name"], "18%", ListColumnAlign.Left),
                new ListColumn("type", L["List_Th_Account_Type"], "14%", ListColumnAlign.Left),
                new ListColumn("iban", L["List_Th_Account_Iban"], "26%", ListColumnAlign.Left),
                new ListColumn("balance", L["List_Th_Account_Balance"], "12%", ListColumnAlign.Right),
                new ListColumn("placeholder", string.Empty, ""),
            };
            Records = Items.Select(i => new ListRecord(new List<ListCell>
            {
                new ListCell(ListCellKind.Symbol, SymbolId: i.SymbolId),
                new ListCell(ListCellKind.Text, Text: i.Name),
                new ListCell(ListCellKind.Text, Text: L[$"EnumType_AccountType_{i.Type}"].Value),
                new ListCell(ListCellKind.Text, Text: string.IsNullOrWhiteSpace(i.Iban) ? "-" : i.Iban ?? string.Empty),
                new ListCell(ListCellKind.Currency, Amount: i.CurrentBalance),
                new ListCell(ListCellKind.Text),
            }, i)).ToList();
        }

        /// <summary>
        /// Returns the ribbon/action definitions shown for this list view. The provided <paramref name="localizer"/> is used to localize labels.
        /// </summary>
        /// <param name="localizer">Localizer used to resolve UI labels for the ribbon actions.</param>
        /// <returns>A list of <see cref="UiRibbonRegister"/> describing available ribbon tabs and actions.</returns>
        protected override IReadOnlyList<UiRibbonRegister>? GetRibbonRegisterDefinition(IStringLocalizer localizer)
        {
            var actions = new List<UiRibbonAction>
            {
                new UiRibbonAction("Back", localizer["Ribbon_Back"].Value, "<svg><use href='/icons/sprite.svg#back'/></svg>", UiRibbonItemSize.Small, false, null, () => { RaiseUiActionRequested("Back"); return Task.CompletedTask; }) { MobileShortcut = true },
                new UiRibbonAction("New", localizer["Ribbon_New"].Value, "<svg><use href='/icons/sprite.svg#plus'/></svg>", UiRibbonItemSize.Small, false, null, () => { RaiseUiActionRequested("New"); return Task.CompletedTask; }) { MobileShortcut = true },
                new UiRibbonAction("ClearSearch", localizer["Ribbon_ClearSearch"].Value, "<svg><use href='/icons/sprite.svg#clear'/></svg>", UiRibbonItemSize.Small, false, null, () => { RaiseUiActionRequested("ClearSearch"); return Task.CompletedTask; }) { MobileShortcut = true }
            };
            var tabs = new List<UiRibbonTab> { new UiRibbonTab(localizer["Ribbon_Group_Navigation"].Value, actions) };
            return new List<UiRibbonRegister> { new UiRibbonRegister(UiRibbonRegisterKind.Actions, tabs) };
        }

        private void StartStatisticsLoad(string? q)
        {
            _statisticsCts?.Cancel();
            _statisticsCts?.Dispose();
            _statisticsCts = new CancellationTokenSource();
            var generation = Interlocked.Increment(ref _statisticsGeneration);

            Statistics = null;
            StatisticsErrorMessage = null;
            StatisticsState = AccountStatisticsLoadState.Loading;
            RaiseStateChanged();

            _ = LoadStatisticsAsync(generation, q, _statisticsCts.Token);
        }

        private async Task LoadStatisticsAsync(long generation, string? q, CancellationToken ct)
        {
            var api = ServiceProvider.GetRequiredService<IApiClient>();
            try
            {
                var statistics = await api.GetAccountStatisticsAsync(q, ct);
                if (generation != Volatile.Read(ref _statisticsGeneration) || ct.IsCancellationRequested)
                {
                    return;
                }

                Statistics = statistics;
                StatisticsState = statistics.AccountCount == 0 ? AccountStatisticsLoadState.Empty : AccountStatisticsLoadState.Loaded;
                StatisticsErrorMessage = null;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                if (generation != Volatile.Read(ref _statisticsGeneration))
                {
                    return;
                }

                Statistics = null;
                StatisticsState = AccountStatisticsLoadState.Error;
                StatisticsErrorMessage = api.LastError ?? ex.Message;
            }
            finally
            {
                if (generation == Volatile.Read(ref _statisticsGeneration))
                {
                    RaiseStateChanged();
                }
            }
        }

        private static string? NormalizeSearchForRequest(string? search)
        {
            if (string.IsNullOrWhiteSpace(search))
            {
                return null;
            }

            return search.Trim();
        }
    }
}
