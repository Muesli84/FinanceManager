using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Shared.Dtos.Accounts;

/// <summary>
/// Request payload for adding a linked sub-IBAN to a collection account.
/// </summary>
/// <param name="Iban">The iban.</param>
/// <returns>The result.</returns>
public sealed record AccountLinkedIbanUpsertRequest(
    [Required, MaxLength(34)] string Iban);
