namespace FinanceManager.Shared.Dtos.Contacts;

/// <summary>
/// DTO representing an alias pattern associated with a contact.
/// </summary>
/// <param name="Id">Alias id.</param>
/// <param name="ContactId">Associated contact id.</param>
/// <param name="Pattern">Alias pattern text.</param>
/// <returns>The result.</returns>
public sealed record AliasNameDto(Guid Id, Guid ContactId, string Pattern);
