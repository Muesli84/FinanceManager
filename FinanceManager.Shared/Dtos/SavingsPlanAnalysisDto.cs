namespace FinanceManager.Shared.Dtos;

/// <summary>
/// DTO summarizing analysis values for a savings plan.
/// </summary>
public sealed record SavingsPlanAnalysisDto(
    Guid PlanId,
    bool TargetReachable,
    decimal? TargetAmount,
    DateTime? TargetDate,
    decimal AccumulatedAmount,
    decimal RequiredMonthly,
    int MonthsRemaining
);
