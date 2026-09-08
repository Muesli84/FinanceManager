namespace FinanceManager.Application.Contacts;

/// <summary>
/// Service for managing contacts and related aliases.
/// </summary>
public interface IContactService
{
    /// <summary>
    /// Creates a new contact.
    /// </summary>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="name">The name.</param>
    /// <param name="type">The type.</param>
    /// <param name="categoryId">The category id.</param>
    /// <param name="description">The description.</param>
    /// <param name="isPaymentIntermediary">The is payment intermediary.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    Task<ContactDto> CreateAsync(Guid ownerUserId, string name, ContactType type, Guid? categoryId, string? description, bool? isPaymentIntermediary, CancellationToken ct);

    /// <summary>
    /// Updates an existing contact and returns the updated DTO or null when not found.
    /// </summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="name">The name.</param>
    /// <param name="type">The type.</param>
    /// <param name="categoryId">The category id.</param>
    /// <param name="description">The description.</param>
    /// <param name="isPaymentIntermediary">The is payment intermediary.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    Task<ContactDto?> UpdateAsync(Guid id, Guid ownerUserId, string name, ContactType type, Guid? categoryId, string? description, bool? isPaymentIntermediary, CancellationToken ct);

    /// <summary>
    /// Deletes a contact.
    /// </summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    Task<bool> DeleteAsync(Guid id, Guid ownerUserId, CancellationToken ct);

    /// <summary>
    /// Lists contacts with optional filtering by type and name.
    /// </summary>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="skip">The skip.</param>
    /// <param name="take">The take.</param>
    /// <param name="type">The type.</param>
    /// <param name="nameFilter">The name filter.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    Task<IReadOnlyList<ContactDto>> ListAsync(Guid ownerUserId, int skip, int take, ContactType? type, string? nameFilter, CancellationToken ct);

    /// <summary>
    /// Gets a contact by id or null when not found.
    /// </summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    Task<ContactDto?> GetAsync(Guid id, Guid ownerUserId, CancellationToken ct);

    /// <summary>
    /// Adds an alias pattern to a contact.
    /// </summary>
    /// <param name="contactId">The contact id.</param>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="pattern">The pattern.</param>
    /// <param name="ct">Cancellation token.</param>
    Task AddAliasAsync(Guid contactId, Guid ownerUserId, string pattern, CancellationToken ct);

    /// <summary>
    /// Deletes an alias by id for the given contact.
    /// </summary>
    /// <param name="contactId">The contact id.</param>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="aliasId">The alias id.</param>
    /// <param name="ct">Cancellation token.</param>
    Task DeleteAliasAsync(Guid contactId, Guid ownerUserId, Guid aliasId, CancellationToken ct);

    /// <summary>
    /// Lists aliases for the given contact.
    /// </summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="userId">The user id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    Task<IReadOnlyList<AliasNameDto>> ListAliases(Guid id, Guid userId, CancellationToken ct);

    /// <summary>
    /// Merges two contacts into a single target contact according to the preference strategy.
    /// </summary>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="sourceContactId">The source contact id.</param>
    /// <param name="targetContactId">The target contact id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="preference">The preference.</param>
    /// <returns>The result.</returns>
    Task<ContactDto> MergeAsync(Guid ownerUserId, Guid sourceContactId, Guid targetContactId, CancellationToken ct, FinanceManager.Shared.Dtos.Contacts.MergePreference preference = FinanceManager.Shared.Dtos.Contacts.MergePreference.DestinationFirst);

    /// <summary>
    /// Returns the total number of contacts for the owner.
    /// </summary>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    Task<int> CountAsync(Guid ownerUserId, CancellationToken ct);

    /// <summary>
    /// Assigns or clears a symbol attachment for a contact.
    /// </summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="attachmentId">The attachment id.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SetSymbolAttachmentAsync(Guid id, Guid ownerUserId, Guid? attachmentId, CancellationToken ct);
}
