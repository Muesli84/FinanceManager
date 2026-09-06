using FinanceManager.Shared.Dtos.Budget;
using System;
using System.Net.Http;
using System.Threading;

namespace FinanceManager.Web.ViewModels.Budget
{
    /// <summary>
    /// ViewModel for the Monthly Budget KPI tile.
    /// </summary>
    public sealed class MonthlyBudgetKpiViewModel
    {
        /// <summary>
        /// Gets or sets a value indicating whether the data has been loaded.
        /// </summary>
        public bool DataLoaded { get; set; } = false;

        /// <summary>
        /// Gets the last user-visible error message when loading KPI data failed.
        /// </summary>
        public string? ErrorMessage { get; private set; }

        /// <summary>
        /// Gets the time at which the KPI data was loaded successfully.
        /// </summary>
        public DateTime? LoadedAtUtc { get; private set; }

        /// <summary>
        /// Gets the month for which the current values were loaded.
        /// </summary>
        public DateOnly? LoadedMonth { get; private set; }

        /// <summary>
        /// Monotonically increasing token used by components to detect invalidation.
        /// </summary>
        public int LoadGeneration { get; private set; }
        /// <summary>
        /// Planned income for the current month.
        /// </summary>
        public decimal PlannedIncome { get; set; }

        /// <summary>
        /// Planned expenses (absolute, positive value) for the current month.
        /// </summary>
        public decimal PlannedExpenseAbs { get; set; }

        /// <summary>
        /// Expected income for the current month (planned + unbudgeted actuals).
        /// </summary>
        public decimal ExpectedIncome { get; set; }

        /// <summary>
        /// Expected expenses (absolute, positive value) for the current month (planned + unbudgeted actuals).
        /// </summary>
        public decimal ExpectedExpenseAbs { get; set; }
        /// <summary>
        /// Unbudgeted actual income in the period.
        /// </summary>
        public decimal UnbudgetedIncome { get; set; }

        /// <summary>
        /// Unbudgeted actual expenses (absolute) in the period.
        /// </summary>
        public decimal UnbudgetedExpenseAbs { get; set; }

        /// <summary>
        /// Actual income for the current month.
        /// </summary>
        public decimal ActualIncome { get; set; }

        /// <summary>
        /// Actual expenses (absolute, positive value) for the current month.
        /// </summary>
        public decimal ActualExpenseAbs { get; set; }

        /// <summary>
        /// Target result (planned income minus planned expenses) for the current month.
        /// </summary>
        public decimal SollErgebnis { get; set; }

        /// <summary>
        /// The month for which the KPI is calculated.
        /// </summary>
        public DateTime Month { get; set; }

        /// <summary>
        /// Loads the monthly budget KPI data from the API and maps it to this ViewModel.
        /// </summary>
        /// <param name="api">The API client to use for data retrieval.</param>
        /// <param name="ct">Optional cancellation token.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public Task LoadAsync(FinanceManager.Shared.IApiClient api, CancellationToken ct = default)
        {
            return LoadAsync(api, timeProvider: null, ct);
        }

        /// <summary>
        /// Loads the monthly budget KPI data from the API and maps it to this ViewModel.
        /// </summary>
        /// <param name="api">The API client to use for data retrieval.</param>
        /// <param name="timeProvider">The time provider used to timestamp the loaded snapshot.</param>
        /// <param name="ct">Optional cancellation token.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task LoadAsync(FinanceManager.Shared.IApiClient api, TimeProvider? timeProvider = null, CancellationToken ct = default)
        {
            // Do not reset DataLoaded so cached values stay visible during a background refresh.
            ErrorMessage = null;
            LoadedAtUtc = null;
            LoadedMonth = null;

            try
            {
                var kpiDto = await api.Budgets_GetMonthlyKpiAsync(date: null, dateBasis: FinanceManager.Shared.Dtos.Budget.BudgetReportDateBasis.BookingDate, ct);
                if (kpiDto == null)
                {
                    MarkLoadedWithDefaults(timeProvider);
                    return;
                }

                PlannedIncome = kpiDto.PlannedIncome;
                PlannedExpenseAbs = kpiDto.PlannedExpenseAbs;
                ActualIncome = kpiDto.ActualIncome;
                ActualExpenseAbs = kpiDto.ActualExpenseAbs;
                SollErgebnis = kpiDto.PlannedResult;
                ExpectedIncome = kpiDto.ExpectedIncome;
                ExpectedExpenseAbs = kpiDto.ExpectedExpenseAbs;
                UnbudgetedIncome = kpiDto.UnbudgetedIncome;
                UnbudgetedExpenseAbs = kpiDto.UnbudgetedExpenseAbs;
                var localNow = GetLocalNow(timeProvider);
                var utcNow = GetUtcNow(timeProvider);
                Month = new DateTime(localNow.Year, localNow.Month, 1);
                LoadedAtUtc = utcNow;
                LoadedMonth = new DateOnly(localNow.Year, localNow.Month, 1);
                DataLoaded = true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (HttpRequestException)
            {
                MarkLoadFailed(api.LastError);
            }
        }

        /// <summary>
        /// Marks the KPI as failed after the owning component has observed and logged an unexpected load error.
        /// </summary>
        /// <param name="errorMessage">Optional user-visible error message.</param>
        public void MarkLoadFailed(string? errorMessage = null)
        {
            DataLoaded = false;
            ErrorMessage = string.IsNullOrWhiteSpace(errorMessage) ? "Error_Unhandled" : errorMessage;
            LoadedAtUtc = null;
            LoadedMonth = null;
        }

        private void MarkLoadedWithDefaults(TimeProvider? timeProvider)
        {
            PlannedIncome = 0m;
            PlannedExpenseAbs = 0m;
            ActualIncome = 0m;
            ActualExpenseAbs = 0m;
            SollErgebnis = 0m;
            ExpectedIncome = 0m;
            ExpectedExpenseAbs = 0m;
            UnbudgetedIncome = 0m;
            UnbudgetedExpenseAbs = 0m;

            var localNow = GetLocalNow(timeProvider);
            Month = new DateTime(localNow.Year, localNow.Month, 1);
            LoadedAtUtc = GetUtcNow(timeProvider);
            LoadedMonth = new DateOnly(localNow.Year, localNow.Month, 1);
            DataLoaded = true;
        }

        private static DateTime GetUtcNow(TimeProvider? timeProvider)
        {
            return (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        }

        private static DateTime GetLocalNow(TimeProvider? timeProvider)
        {
            return TimeZoneInfo.ConvertTime((timeProvider ?? TimeProvider.System).GetUtcNow(), TimeZoneInfo.Local).DateTime;
        }

        /// <summary>
        /// Invalidates the loaded KPI data so the owning component can start a fresh asynchronous load.
        /// </summary>
        public void Invalidate()
        {
            DataLoaded = false;
            ErrorMessage = null;
            LoadedAtUtc = null;
            LoadedMonth = null;
            LoadGeneration++;
        }

        /// <summary>
        /// Invalidates successfully loaded data when it is older than the allowed age or belongs to an earlier month.
        /// </summary>
        /// <param name="utcNow">Current UTC timestamp.</param>
        /// <param name="localNow">Current local timestamp used for month-boundary detection.</param>
        /// <param name="maxAge">Maximum age for a loaded KPI snapshot.</param>
        /// <returns><c>true</c> when the data was invalidated.</returns>
        public bool InvalidateIfRefreshDue(DateTime utcNow, DateTime localNow, TimeSpan maxAge)
        {
            if (!DataLoaded || !LoadedAtUtc.HasValue || !LoadedMonth.HasValue)
            {
                return false;
            }

            var currentMonth = new DateOnly(localNow.Year, localNow.Month, 1);
            if (LoadedMonth.Value != currentMonth || utcNow - LoadedAtUtc.Value >= maxAge)
            {
                Invalidate();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Restores the view model from a previously cached snapshot without calling the API.
        /// </summary>
        /// <param name="snapshot">The cached snapshot to restore from.</param>
        public void LoadFromSnapshot(MonthlyBudgetKpiViewModelSnapshot snapshot)
        {
            if (snapshot?.Dto is null)
            {
                return;
            }

            PlannedIncome = snapshot.Dto.PlannedIncome;
            PlannedExpenseAbs = snapshot.Dto.PlannedExpenseAbs;
            ActualIncome = snapshot.Dto.ActualIncome;
            ActualExpenseAbs = snapshot.Dto.ActualExpenseAbs;
            SollErgebnis = snapshot.Dto.PlannedResult;
            ExpectedIncome = snapshot.Dto.ExpectedIncome;
            ExpectedExpenseAbs = snapshot.Dto.ExpectedExpenseAbs;
            UnbudgetedIncome = snapshot.Dto.UnbudgetedIncome;
            UnbudgetedExpenseAbs = snapshot.Dto.UnbudgetedExpenseAbs;
            Month = snapshot.Month;
            LoadedAtUtc = snapshot.LoadedAtUtc;
            LoadedMonth = snapshot.LoadedMonth;
            DataLoaded = true;
            ErrorMessage = null;
        }

        /// <summary>
        /// Creates a serializable snapshot of the currently loaded data.
        /// </summary>
        /// <returns>A snapshot containing the current values.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the view model has no loaded data.</exception>
        public MonthlyBudgetKpiViewModelSnapshot CreateSnapshot()
        {
            if (!DataLoaded)
            {
                throw new InvalidOperationException("Cannot create a snapshot before data is loaded.");
            }

            var dto = new MonthlyBudgetKpiDto
            {
                PlannedIncome = PlannedIncome,
                PlannedExpenseAbs = PlannedExpenseAbs,
                ActualIncome = ActualIncome,
                ActualExpenseAbs = ActualExpenseAbs,
                ActualResult = ActualIncome - ActualExpenseAbs,
                PlannedResult = SollErgebnis,
                ExpectedIncome = ExpectedIncome,
                ExpectedExpenseAbs = ExpectedExpenseAbs,
                UnbudgetedIncome = UnbudgetedIncome,
                UnbudgetedExpenseAbs = UnbudgetedExpenseAbs,
            };

            return new MonthlyBudgetKpiViewModelSnapshot(
                dto,
                Month,
                LoadedAtUtc ?? DateTime.UtcNow,
                LoadedMonth);
        }
    }

    /// <summary>
    /// Snapshot of a <see cref="MonthlyBudgetKpiViewModel"/> used for browser local storage caching.
    /// </summary>
    /// <param name="Dto">The dto.</param>
    /// <param name="Month">The month.</param>
    /// <param name="LoadedAtUtc">The loaded at utc.</param>
    /// <param name="LoadedMonth">The loaded month.</param>
    /// <returns>The result.</returns>
    public sealed record MonthlyBudgetKpiViewModelSnapshot(
        MonthlyBudgetKpiDto Dto,
        DateTime Month,
        DateTime LoadedAtUtc,
        DateOnly? LoadedMonth);
}
