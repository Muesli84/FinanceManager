using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Shared.Dtos;

public sealed record SavingsPlanCreateRequest(
    [property: Required, MinLength(2)] string Name,
    SavingsPlanType Type,
    decimal? TargetAmount,
    DateTime? TargetDate,
    SavingsPlanInterval? Interval,
    Guid? CategoryId,
    string? ContractNumber
);
