using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Shared.Dtos.SavingsPlans;

/// <summary>
/// Request payload to create a new savings plan.
/// </summary>
public sealed record SavingsPlanCreateRequest(
    [param: Required, MinLength(2)] string Name,
    SavingsPlanType Type,
    decimal? TargetAmount,
    DateTime? TargetDate,
    SavingsPlanInterval? Interval,
    Guid? CategoryId,
    string? ContractNumber
);
