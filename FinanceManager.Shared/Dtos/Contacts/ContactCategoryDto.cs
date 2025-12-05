namespace FinanceManager.Shared.Dtos.Contacts;

/// <summary>
/// DTO describing a contact category used to group contacts.
/// </summary>
/// <param name="Id">Unique identifier of the category.</param>
/// <param name="Name">Display name of the category.</param>
/// <param name="SymbolAttachmentId">Optional attachment id of the category symbol.</param>
public sealed record ContactCategoryDto(Guid Id, string Name, Guid? SymbolAttachmentId = null);
