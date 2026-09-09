namespace FinanceManager.Shared.Dtos.SavingsPlans;

/// <summary>
/// DTO summarizing analysis values for a savings plan.
/// </summary>
/// <param name="PlanId">The plan id.</param>
/// <param name="TargetReachable">The target reachable.</param>
/// <param name="TargetAmount">The target amount.</param>
/// <param name="TargetDate">The target date.</param>
/// <param name="AccumulatedAmount">The accumulated amount.</param>
/// <param name="RequiredMonthly">The required monthly.</param>
/// <param name="MonthsRemaining">The months remaining.</param>
/// <returns>The result.</returns>
public sealed record SavingsPlanAnalysisDto(
    Guid PlanId,
    bool TargetReachable,
    decimal? TargetAmount,
    DateTime? TargetDate,
    decimal AccumulatedAmount,
    decimal RequiredMonthly,
    int MonthsRemaining
);
