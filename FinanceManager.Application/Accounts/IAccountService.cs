namespace FinanceManager.Application.Accounts;

/// <summary>
/// Service interface for managing accounts (create, update, delete, list and symbol assignment).
/// </summary>
public interface IAccountService
{
    /// <summary>
    /// Creates a new account for the specified owner.
    /// </summary>
    /// <param name="ownerUserId">Owner user identifier.</param>
    /// <param name="name">Account display name.</param>
    /// <param name="type">Account type.</param>
    /// <param name="iban">Optional IBAN string.</param>
    /// <param name="bankContactId">Bank contact identifier associated with the account.</param>
    /// <param name="expectation">Savings plan expectation for the account.</param>
    /// <param name="securityProcessingEnabled">Whether security processing is enabled.</param>
    /// <param name="isCollectionAccount">Whether the account is a collection account.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created <see cref="AccountDto"/>.</returns>
    Task<AccountDto> CreateAsync(Guid ownerUserId, string name, AccountType type, string? iban, Guid bankContactId, SavingsPlanExpectation expectation, bool securityProcessingEnabled, bool isCollectionAccount, CancellationToken ct);

    /// <summary>
    /// Updates an existing account. Returns null when not found.
    /// </summary>
    /// <param name="id">Account identifier.</param>
    /// <param name="ownerUserId">Owner user identifier.</param>
    /// <param name="name">New display name.</param>
    /// <param name="iban">New IBAN or null to clear.</param>
    /// <param name="bankContactId">Bank contact identifier.</param>
    /// <param name="expectation">Savings plan expectation.</param>
    /// <param name="securityProcessingEnabled">Whether security processing is enabled.</param>
    /// <param name="isCollectionAccount">Whether the account is a collection account.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Updated <see cref="AccountDto"/> or null when not found.</returns>
    Task<AccountDto?> UpdateAsync(Guid id, Guid ownerUserId, string name, string? iban, Guid bankContactId, SavingsPlanExpectation expectation, bool securityProcessingEnabled, bool isCollectionAccount, CancellationToken ct);

    /// <summary>
    /// Deletes an account. Returns true when deleted.
    /// </summary>
    /// <param name="id">Account id to delete.</param>
    /// <param name="ownerUserId">Owner user id performing the deletion.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True when deletion succeeded; otherwise false.</returns>
    Task<bool> DeleteAsync(Guid id, Guid ownerUserId, CancellationToken ct);

    /// <summary>
    /// Lists accounts for the owner with paging.
    /// </summary>
    /// <param name="ownerUserId">Owner user id.</param>
    /// <param name="skip">Number of items to skip.</param>
    /// <param name="take">Number of items to take.</param>
    /// <param name="bankContactId">Optional bank contact id filter.</param>
    /// <param name="q">Optional account search text.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Read-only list of <see cref="AccountDto"/>.</returns>
    Task<IReadOnlyList<AccountDto>> ListAsync(Guid ownerUserId, int skip, int take, Guid? bankContactId, string? q, CancellationToken ct);

    /// <summary>
    /// Gets unpaged account statistics for the owner and optional search scope.
    /// </summary>
    /// <param name="ownerUserId">Owner user id.</param>
    /// <param name="q">Optional account search text.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Statistics for the filtered account scope.</returns>
    Task<AccountStatisticsDto> GetStatisticsAsync(Guid ownerUserId, string? q, CancellationToken ct);

    /// <summary>
    /// Gets a single account by id for the owner or null when not found.
    /// </summary>
    /// <param name="id">Account id.</param>
    /// <param name="ownerUserId">Owner user id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The matching <see cref="AccountDto"/> or null.</returns>
    Task<AccountDto?> GetAsync(Guid id, Guid ownerUserId, CancellationToken ct);

    /// <summary>
    /// Synchronous get helper that returns the account dto or null when not found.
    /// </summary>
    /// <param name="id">Account id.</param>
    /// <param name="ownerUserId">Owner user id.</param>
    /// <returns>Account DTO or null.</returns>
    AccountDto? Get(Guid id, Guid ownerUserId);

    /// <summary>
    /// Assigns or clears a symbol attachment on the account.
    /// </summary>
    /// <param name="id">Account id.</param>
    /// <param name="ownerUserId">Owner user id.</param>
    /// <param name="attachmentId">Attachment id to set as symbol or null to clear.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SetSymbolAttachmentAsync(Guid id, Guid ownerUserId, Guid? attachmentId, CancellationToken ct);

    /// <summary>
    /// Adds a linked sub-IBAN to a collection account.
    /// </summary>
    /// <param name="accountId">Account identifier.</param>
    /// <param name="ownerUserId">Owner user identifier.</param>
    /// <param name="iban">IBAN to add.</param>
    /// <param name="ct">Cancellation token.</param>
    Task AddLinkedIbanAsync(Guid accountId, Guid ownerUserId, string iban, CancellationToken ct);

    /// <summary>
    /// Removes a linked sub-IBAN from a collection account.
    /// </summary>
    /// <param name="accountId">Account identifier.</param>
    /// <param name="ownerUserId">Owner user identifier.</param>
    /// <param name="iban">IBAN to remove.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True when removed; false when not found.</returns>
    Task<bool> RemoveLinkedIbanAsync(Guid accountId, Guid ownerUserId, string iban, CancellationToken ct);

    /// <summary>
    /// Returns the list of linked sub-IBANs for the specified collection account.
    /// </summary>
    /// <param name="accountId">Account identifier.</param>
    /// <param name="ownerUserId">Owner user identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Read-only list of IBAN strings, or <see langword="null"/> when the account was not found.</returns>
    Task<IReadOnlyList<string>?> GetLinkedIbansAsync(Guid accountId, Guid ownerUserId, CancellationToken ct);
}
