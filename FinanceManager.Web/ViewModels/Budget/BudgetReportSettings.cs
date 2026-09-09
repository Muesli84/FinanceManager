namespace FinanceManager.Web.ViewModels.Budget;

/// <summary>
/// Determines which date field is used for postings aggregation.
/// </summary>
public enum BudgetReportDateBasis
{
    /// <summary>
    /// Use the booking date.
    /// </summary>
    BookingDate = 0,

    /// <summary>
    /// Use the valuta/value date.
    /// </summary>
    ValutaDate = 1
}

/// <summary>
/// Settings controlling the budget report rendering.
/// </summary>
/// <param name="AsOfDate">The as of date.</param>
/// <param name="Months">The months.</param>
/// <param name="Interval">The interval.</param>
/// <param name="ShowTitle">The show title.</param>
/// <param name="ShowLineChart">The show line chart.</param>
/// <param name="ShowMonthlyTable">The show monthly table.</param>
/// <param name="ShowDetailsTable">The show details table.</param>
/// <param name="ShowPurposeRows">The show purpose rows.</param>
/// <param name="ShowPeriodSumRow">The show period sum row.</param>
/// <param name="ShowCategorySumRow">The show category sum row.</param>
/// <param name="CategoryValueScope">The category value scope.</param>
/// <param name="DateBasis">The date basis.</param>
/// <returns>The result.</returns>
public sealed record BudgetReportSettings(
    DateOnly AsOfDate,
    int Months,
    BudgetReportInterval Interval,
    bool ShowTitle,
    bool ShowLineChart,
    bool ShowMonthlyTable,
    bool ShowDetailsTable,
    bool ShowPurposeRows,
    bool ShowPeriodSumRow,
    bool ShowCategorySumRow,
    BudgetReportValueScope CategoryValueScope,
    BudgetReportDateBasis DateBasis)
{
    /// <summary>
    /// Default settings: 12 months ending at end of current month.
    /// </summary>
    /// <returns>The result.</returns>
    public static BudgetReportSettings Default { get; }
        = new(
        AsOfDate: new DateOnly(DateTime.Today.Year, DateTime.Today.Month, DateTime.DaysInMonth(DateTime.Today.Year, DateTime.Today.Month)),
        Months: 12,
        Interval: BudgetReportInterval.Month,
        ShowTitle: true,
        ShowLineChart: true,
        ShowMonthlyTable: true,
        ShowDetailsTable: true,
        ShowPurposeRows: true,
        ShowPeriodSumRow: true,
        ShowCategorySumRow: true,
        CategoryValueScope: BudgetReportValueScope.LastInterval,
        DateBasis: BudgetReportDateBasis.BookingDate);
}
