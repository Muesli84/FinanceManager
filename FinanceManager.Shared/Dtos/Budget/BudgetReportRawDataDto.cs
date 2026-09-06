using FinanceManager.Shared.Dtos.Postings;

namespace FinanceManager.Shared.Dtos.Budget;

/// <summary>
/// Raw data structure for a budget report.
/// Designed for exports and advanced UI scenarios where aggregated and underlying data must be displayed together.
/// </summary>
public sealed record BudgetReportRawDataDto
{
    /// <summary>
    /// Gets the inclusive start of the report period.
    /// </summary>
    public DateTime PeriodStart { get; init; }

    /// <summary>
    /// Gets the inclusive end of the report period.
    /// </summary>
    public DateTime PeriodEnd { get; init; }

    /// <summary>
    /// Gets the categorized purposes.
    /// </summary>
    /// <returns>The result.</returns>
    public BudgetReportCategoryRawDataDto[] Categories { get; init; }
        = Array.Empty<BudgetReportCategoryRawDataDto>();

    /// <summary>
    /// Gets purposes that are not assigned to any budget category.
    /// </summary>
    /// <returns>The result.</returns>
    public BudgetReportPurposeRawDataDto[] UncategorizedPurposes { get; init; }
        = Array.Empty<BudgetReportPurposeRawDataDto>();
    /// <summary>
    /// Gets or sets the collection of postings that are not associated with any budget category.
    /// </summary>
    /// <returns>The result.</returns>
    public BudgetReportPostingRawDataDto[] UnbudgetedPostings { get; set; }
        = Array.Empty<BudgetReportPostingRawDataDto>();
}

/// <summary>
/// Raw category data within a budget report.
/// </summary>
public sealed record BudgetReportCategoryRawDataDto
{
    /// <summary>
    /// Gets the category id.
    /// </summary>
    public Guid CategoryId { get; init; }

    /// <summary>
    /// Gets the category name.
    /// </summary>
    public string CategoryName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the budgeted income amount for the category in the requested period.
    /// This value is derived from rules that are directly assigned to the category and contains only positive amounts.
    /// </summary>
    public decimal BudgetedIncome { get; init; }

    /// <summary>
    /// Gets the budgeted expense amount for the category in the requested period.
    /// This value is derived from rules that are directly assigned to the category and contains only negative amounts (negative value).
    /// </summary>
    public decimal BudgetedExpense { get; init; }

    /// <summary>
    /// Gets the net budget target for the category (income + expense).
    /// </summary>
    public decimal BudgetedTarget { get; init; }

    /// <summary>
    /// Backwards compatible: net budget amount (same as <see cref="BudgetedTarget"/>).
    /// </summary>
    public decimal BudgetedAmount { get => BudgetedTarget; init => BudgetedTarget = value; }

    /// <summary>
    /// Gets the purposes assigned to the category.
    /// </summary>
    /// <returns>The result.</returns>
    public BudgetReportPurposeRawDataDto[] Purposes { get; init; }
        = Array.Empty<BudgetReportPurposeRawDataDto>();
}

/// <summary>
/// Raw purpose data within a budget report.
/// </summary>
public sealed record BudgetReportPurposeRawDataDto
{
    /// <summary>
    /// Gets the purpose id.
    /// </summary>
    public Guid PurposeId { get; init; }

    /// <summary>
    /// Gets the purpose name.
    /// </summary>
    public string PurposeName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the budgeted income amount for the purpose in the requested period.
    /// This value contains only positive amounts derived from the purpose's rules.
    /// </summary>
    public decimal BudgetedIncome { get; init; }

    /// <summary>
    /// Gets the budgeted expense amount for the purpose in the requested period.
    /// This value contains only negative amounts derived from the purpose's rules (negative value).
    /// </summary>
    public decimal BudgetedExpense { get; init; }

    /// <summary>
    /// Gets the net budget target for the purpose (income + expense).
    /// </summary>
    public decimal BudgetedTarget { get; init; }

    /// <summary>
    /// Gets the purpose source type.
    /// </summary>
    public BudgetSourceType BudgetSourceType { get; init; }

    /// <summary>
    /// Gets the source id referenced by the purpose.
    /// </summary>
    public Guid SourceId { get; init; }

    /// <summary>
    /// Gets the source name for the referenced entity.
    /// </summary>
    public string SourceName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the budget valuation type used for this purpose.
    /// </summary>
    public BudgetValuationType ValuationType { get; init; } = BudgetValuationType.ExactPostings;

    /// <summary>
    /// Gets the contributing postings for the purpose in the requested period.
    /// </summary>
    /// <returns>The result.</returns>
    public BudgetReportPostingRawDataDto[] Postings { get; init; }
        = Array.Empty<BudgetReportPostingRawDataDto>();
}

/// <summary>
/// Raw posting row within a purpose.
/// </summary>
public sealed record BudgetReportPostingRawDataDto
{
    /// <summary>
    /// Gets the posting id.
    /// </summary>
    public Guid PostingId { get; init; }

    /// <summary>
    /// Gets the booking date.
    /// </summary>
    public DateTime BookingDate { get; init; }

    /// <summary>
    /// Gets the valuta date (optional).
    /// </summary>
    public DateTime? ValutaDate { get; init; }

    /// <summary>
    /// Gets the posting amount.
    /// </summary>
    public decimal Amount { get; init; }

    /// <summary>
    /// Gets the posting kind.
    /// </summary>
    public PostingKind PostingKind { get; init; }

    /// <summary>
    /// Gets the posting description.
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Gets the posting subject.
    /// </summary>
    public string Subject { get; init; } = string.Empty;

    /// <summary>
    /// Gets the related account id.
    /// </summary>
    public Guid? AccountId { get; init; }

    /// <summary>
    /// Gets the related account name.
    /// </summary>
    public string? AccountName { get; init; }

    /// <summary>
    /// Gets the related contact id.
    /// </summary>
    public Guid? ContactId { get; init; }

    /// <summary>
    /// Gets the related contact name.
    /// </summary>
    public string? ContactName { get; init; }

    /// <summary>
    /// Gets the related savings plan id.
    /// </summary>
    public Guid? SavingsPlanId { get; init; }

    /// <summary>
    /// Gets the related savings plan name.
    /// </summary>
    public string? SavingsPlanName { get; init; }

    /// <summary>
    /// Gets the related security id.
    /// </summary>
    public Guid? SecurityId { get; init; }

    /// <summary>
    /// Gets the related security name.
    /// </summary>
    public string? SecurityName { get; init; }

    // Budget metadata filled by the report service
    /// <summary>
    /// Gets the budget category id this posting belongs to (if any).
    /// </summary>
    public Guid? BudgetCategoryId { get; init; }

    /// <summary>
    /// Gets the budget category name this posting belongs to (if any).
    /// </summary>
    public string? BudgetCategoryName { get; init; }

    /// <summary>
    /// Gets the budget purpose id this posting belongs to (if any).
    /// </summary>
    public Guid? BudgetPurposeId { get; init; }

    /// <summary>
    /// Gets the budget purpose name this posting belongs to (if any).
    /// </summary>
    public string? BudgetPurposeName { get; init; }

    /// <summary>
    /// Gets whether this posting is valued in the actual amount of its budget purpose.
    /// </summary>
    public bool IsValuedForBudgetPurpose { get; init; } = true;

    /// <summary>
    /// Gets the group id for mirror postings (e.g., self-contact transfers).
    /// </summary>
    public Guid? GroupId { get; init; }
}
