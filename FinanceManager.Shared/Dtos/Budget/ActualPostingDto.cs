using System;

namespace FinanceManager.Shared.Dtos.Budget;

/// <summary>
/// Represents an actual contact posting with optional link to the originating budget rule/purpose.
/// </summary>
/// <param name="PostingId">The posting id.</param>
/// <param name="SourceId">The source id.</param>
/// <param name="BookingDate">The booking date.</param>
/// <param name="ValutaDate">The valuta date.</param>
/// <param name="Amount">The amount.</param>
/// <param name="AccountId">The account id.</param>
/// <param name="ContactId">The contact id.</param>
/// <param name="SavingsPlanId">The savings plan id.</param>
/// <param name="GroupId">The group id.</param>
/// <param name="Subject">The subject.</param>
/// <param name="RecipientName">The recipient name.</param>
/// <param name="Description">The description.</param>
/// <param name="BudgetRuleId">The budget rule id.</param>
/// <param name="BudgetPurposeId">The budget purpose id.</param>
/// <returns>The result.</returns>
public sealed record ActualPostingDto(
    Guid PostingId,
    Guid SourceId,
    DateTime BookingDate,
    DateTime ValutaDate,
    decimal Amount,
    Guid? AccountId,
    Guid? ContactId,
    Guid? SavingsPlanId,
    Guid? GroupId,
    string? Subject,
    string? RecipientName,
    string? Description,
    Guid? BudgetRuleId,
    Guid? BudgetPurposeId);
