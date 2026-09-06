namespace FinanceManager.Domain.Budget.ReportCalculation;

/// <summary>
/// Represents the budget expectation for a single budget purpose (or a category-level direct
/// expectation) within one month, consisting of one or more <see cref="MonthlyBudgetExpectationPosting"/>
/// occurrences derived from the applicable <see cref="BudgetRule"/> entries.
/// </summary>
public sealed class MonthlyBudgetExpectation
{
    private readonly List<MonthlyBudgetExpectationPosting> _postings = new();

    internal MonthlyBudgetExpectation(Guid? budgetPurposeId, string name)
    {
        BudgetPurposeId = budgetPurposeId;
        Name = name;
    }

    /// <summary>
    /// Gets the id of the budget purpose this expectation belongs to, or <c>null</c> for a
    /// category-level direct expectation.
    /// </summary>
    public Guid? BudgetPurposeId { get; }

    /// <summary>
    /// Gets the display name of this expectation.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the occurrences (expected postings) that make up this expectation.
    /// </summary>
    public IReadOnlyList<MonthlyBudgetExpectationPosting> Postings => _postings;

    /// <summary>
    /// Gets the sum of the expected amounts of all occurrences (see
    /// <see cref="MonthlyBudgetExpectationPosting.BudgetedDisplayAmount"/> - occurrences carried over from
    /// a period before the report's first month are excluded here to avoid double-counting).
    /// </summary>
    /// <returns>The result.</returns>
    public decimal SumExpectedAmount
    {
        get
        {
            return _postings.Sum(p => p.BudgetedDisplayAmount);
        }
    }

    /// <summary>
    /// Gets the sum of the actually assigned amounts of all occurrences.
    /// </summary>
    /// <returns>The result.</returns>
    public decimal SumActualAmount
    {
        get
        {
            return _postings.Sum(p => p.SumAssignedAmount);
        }
    }

    /// <summary>
    /// Gets the variance between actual and expected amount (SumActualAmount - SumExpectedAmount).
    /// </summary>
    public decimal Variance => SumActualAmount - SumExpectedAmount;

    internal void AddPosting(MonthlyBudgetExpectationPosting posting)
        => _postings.Add(posting);
}
