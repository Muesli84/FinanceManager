namespace FinanceManager.Shared.Dtos.Postings;

/// <summary>
/// Links discovered for a posting group across different entity kinds.
/// </summary>
public sealed record GroupLinksDto(
    Guid? AccountId,
    Guid? ContactId,
    Guid? SavingsPlanId,
    Guid? SecurityId
);
