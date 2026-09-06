namespace FinanceManager.Shared.Dtos.Postings;

/// <summary>
/// Links discovered for a posting group across different entity kinds.
/// </summary>
/// <param name="AccountId">The account id.</param>
/// <param name="ContactId">The contact id.</param>
/// <param name="SavingsPlanId">The savings plan id.</param>
/// <param name="SecurityId">The security id.</param>
/// <returns>The result.</returns>
public sealed record GroupLinksDto(
    Guid? AccountId,
    Guid? ContactId,
    Guid? SavingsPlanId,
    Guid? SecurityId
);
