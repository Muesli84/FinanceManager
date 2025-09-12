namespace FinanceManager.Shared.Dtos;

public sealed record SavingsPlanAnalysisDto(
    Guid PlanId,
    bool TargetReachable,
    decimal? TargetAmount,
    DateTime? TargetDate,
    decimal AccumulatedAmount,
    decimal RequiredMonthly,
    int MonthsRemaining
);
