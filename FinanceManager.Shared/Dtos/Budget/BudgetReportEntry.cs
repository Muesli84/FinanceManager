namespace FinanceManager.Shared.Dtos.Budget;

/// <summary>
/// Identifies the semantic kind of a row produced by <c>Budgetbericht.GetCurrentResult()</c>.
/// </summary>
public enum BudgetReportEntryRowKind
{
    /// <summary>
    /// A budget category row.
    /// </summary>
    Category = 0,

    /// <summary>
    /// A budget purpose row (or a category-level direct expectation row).
    /// </summary>
    Purpose = 1,

    /// <summary>
    /// A subtotal row for a category.
    /// </summary>
    Subtotal = 2,

    /// <summary>
    /// A row summarizing postings without any matching budget expectation.
    /// </summary>
    Unbudgeted = 3,

    /// <summary>
    /// A row summarizing cost-neutral mirror postings.
    /// </summary>
    CostNeutral = 4,

    /// <summary>
    /// The final total row.
    /// </summary>
    Total = 5
}

/// <summary>
/// A single row of the budget report detail table produced by <c>Budgetbericht.GetCurrentResult()</c>.
/// </summary>
public sealed record BudgetReportEntry
{
    /// <summary>
    /// Gets the semantic kind of this row.
    /// </summary>
    public BudgetReportEntryRowKind RowKind { get; init; }

    /// <summary>
    /// Gets the display name of this row.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets the budgeted (expected) amount for this row.
    /// </summary>
    public decimal BudgetedAmount { get; init; }

    /// <summary>
    /// Gets the actual amount for this row.
    /// </summary>
    public decimal ActualAmount { get; init; }

    /// <summary>
    /// Gets the deviation between actual and budgeted amount (ActualAmount - BudgetedAmount).
    /// </summary>
    public decimal Deviation { get; init; }

    /// <summary>
    /// Gets the deviation expressed as a percentage of the budgeted amount.
    /// </summary>
    public decimal DeviationPercentage { get; init; }

    /// <summary>
    /// Gets the postings contributing to this row.
    /// </summary>
    /// <returns>The result.</returns>
    public MonthlyBudgetRealization[] Postings { get; init; }
        = Array.Empty<MonthlyBudgetRealization>();

    /// <summary>
    /// Gets the id of the budget category this row belongs to (for <see cref="BudgetReportEntryRowKind.Category"/>,
    /// <see cref="BudgetReportEntryRowKind.Purpose"/> and <see cref="BudgetReportEntryRowKind.Subtotal"/> rows), or
    /// <c>null</c> for rows that are not scoped to a single category (Unbudgeted, CostNeutral, Total). Uses
    /// <see cref="Guid.Empty"/> for the virtual "Uncategorized" category.
    /// </summary>
    public Guid? BudgetCategoryId { get; init; }

    /// <summary>
    /// Gets the id of the budget purpose this row represents (for <see cref="BudgetReportEntryRowKind.Purpose"/>
    /// rows backed by an actual budget purpose), or <c>null</c> for category rows, subtotal rows, category-level
    /// direct expectation rows (a "Purpose" row without an underlying budget purpose) and the other row kinds.
    /// </summary>
    public Guid? BudgetPurposeId { get; init; }
}

/// <summary>
/// A single row of the interval summary table produced by <c>Budgetbericht.GetCumulativeResult()</c>.
/// </summary>
public sealed record BudgetReportCumulativeEntry
{
    /// <summary>
    /// Gets the inclusive start date of the interval bucket.
    /// </summary>
    public DateOnly IntervalStartDate { get; init; }

    /// <summary>
    /// Gets the display label of the interval bucket (e.g. "08/2026", "Q3/2026", "2026").
    /// </summary>
    public string IntervalLabel { get; init; } = string.Empty;

    /// <summary>
    /// Gets the budgeted (expected) amount for this interval bucket.
    /// </summary>
    public decimal BudgetedAmount { get; init; }

    /// <summary>
    /// Gets the actual amount for this interval bucket.
    /// </summary>
    public decimal ActualAmount { get; init; }

    /// <summary>
    /// Gets the deviation between actual and budgeted amount (ActualAmount - BudgetedAmount).
    /// </summary>
    public decimal Deviation { get; init; }

    /// <summary>
    /// Gets the deviation expressed as a percentage of the budgeted amount.
    /// </summary>
    public decimal DeviationPercentage { get; init; }
}
