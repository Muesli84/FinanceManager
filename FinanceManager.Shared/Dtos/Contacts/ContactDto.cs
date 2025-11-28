namespace FinanceManager.Shared.Dtos.Contacts;

/// <summary>
/// DTO describing a contact entity.
/// </summary>
/// <param name="Id">Unique contact identifier.</param>
/// <param name="Name">Display name of the contact.</param>
/// <param name="Type">Type of the contact.</param>
/// <param name="CategoryId">Optional category id the contact belongs to.</param>
/// <param name="Description">Optional description.</param>
/// <param name="IsPaymentIntermediary">True when the contact is a payment intermediary.</param>
/// <param name="SymbolAttachmentId">Optional attachment id for the contact's symbol.</param>
public sealed record ContactDto(Guid Id, string Name, ContactType Type, Guid? CategoryId, string? Description, bool IsPaymentIntermediary, Guid? SymbolAttachmentId);
