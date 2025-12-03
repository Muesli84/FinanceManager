using FinanceManager.Shared.Dtos.Accounts;
using FinanceManager.Shared.Dtos.Users;
using FinanceManager.Shared.Dtos.Security;
using FinanceManager.Shared.Dtos.Attachments;
using FinanceManager.Shared.Dtos.Common;
using FinanceManager.Shared.Dtos.Admin; // added for BackgroundTaskInfo/Type/Status
using FinanceManager.Shared.Dtos.Backups; // add backups dtos
using FinanceManager.Shared.Dtos.Contacts; // contact categories dtos
using FinanceManager.Shared.Dtos.HomeKpi; // home kpi dtos

namespace FinanceManager.Shared;

/// <summary>
/// Abstraction for the typed FinanceManager API client used via DI by Blazor view models and services.
/// Provides strongly-typed methods for calling backend endpoints.
/// </summary>
public interface IApiClient
{
    // Accounts
    /// <summary>Lists accounts for the current user. Optional filter by bank contact id.</summary>
    /// <param name="skip">Number of items to skip.</param>
    /// <param name="take">Max items to return.</param>
    /// <param name="bankContactId">Optional bank contact filter.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<AccountDto>> GetAccountsAsync(int skip = 0, int take = 100, Guid? bankContactId = null, CancellationToken ct = default);
    /// <summary>Gets a single account by id or null if not found.</summary>
    Task<AccountDto?> GetAccountAsync(Guid id, CancellationToken ct = default);
    /// <summary>Creates a new account.</summary>
    Task<AccountDto> CreateAccountAsync(AccountCreateRequest request, CancellationToken ct = default);
    /// <summary>Updates an existing account. Returns null when not found.</summary>
    Task<AccountDto?> UpdateAccountAsync(Guid id, AccountUpdateRequest request, CancellationToken ct = default);
    /// <summary>Deletes an account. Returns false when not found.</summary>
    Task<bool> DeleteAccountAsync(Guid id, CancellationToken ct = default);
    /// <summary>Assigns a symbol attachment to an account.</summary>
    Task SetAccountSymbolAsync(Guid id, Guid attachmentId, CancellationToken ct = default);
    /// <summary>Clears the symbol attachment from an account.</summary>
    Task ClearAccountSymbolAsync(Guid id, CancellationToken ct = default);

    // Auth
    /// <summary>Authenticates an existing user and sets the auth cookie.</summary>
    Task<AuthOkResponse> Auth_LoginAsync(LoginRequest request, CancellationToken ct = default);
    /// <summary>Registers a new user and sets the auth cookie.</summary>
    Task<AuthOkResponse> Auth_RegisterAsync(RegisterRequest request, CancellationToken ct = default);
    /// <summary>Logs out the current user and clears the auth cookie.</summary>
    Task<bool> Auth_LogoutAsync(CancellationToken ct = default);

    // Background tasks
    /// <summary>Enqueues a background task.</summary>
    Task<BackgroundTaskInfo> BackgroundTasks_EnqueueAsync(BackgroundTaskType type, bool allowDuplicate = false, CancellationToken ct = default);
    /// <summary>Gets active background tasks.</summary>
    Task<IReadOnlyList<BackgroundTaskInfo>> BackgroundTasks_GetActiveAsync(CancellationToken ct = default);
    /// <summary>Gets details for a background task or null if not found.</summary>
    Task<BackgroundTaskInfo?> BackgroundTasks_GetDetailAsync(Guid id, CancellationToken ct = default);
    /// <summary>Cancels or removes a background task. Returns false when not found or invalid.</summary>
    Task<bool> BackgroundTasks_CancelOrRemoveAsync(Guid id, CancellationToken ct = default);

    // Aggregates (Background tasks specialized endpoints)
    /// <summary>Starts an aggregates rebuild background task.</summary>
    Task<AggregatesRebuildStatusDto> Aggregates_RebuildAsync(bool allowDuplicate = false, CancellationToken ct = default);
    /// <summary>Gets current status of the aggregates rebuild task.</summary>
    Task<AggregatesRebuildStatusDto> Aggregates_GetRebuildStatusAsync(CancellationToken ct = default);

    // Admin - Users
    /// <summary>Lists users (admin only).</summary>
    Task<IReadOnlyList<UserAdminDto>> Admin_ListUsersAsync(CancellationToken ct = default);
    /// <summary>Gets a user (admin only) or null if not found.</summary>
    Task<UserAdminDto?> Admin_GetUserAsync(Guid id, CancellationToken ct = default);
    /// <summary>Creates a new user (admin only).</summary>
    Task<UserAdminDto> Admin_CreateUserAsync(CreateUserRequest request, CancellationToken ct = default);
    /// <summary>Updates a user (admin only). Returns null when not found.</summary>
    Task<UserAdminDto?> Admin_UpdateUserAsync(Guid id, UpdateUserRequest request, CancellationToken ct = default);
    /// <summary>Resets a user's password (admin only). Returns false when not found.</summary>
    Task<bool> Admin_ResetPasswordAsync(Guid id, ResetPasswordRequest request, CancellationToken ct = default);
    /// <summary>Unlocks a user (admin only). Returns false when not found.</summary>
    Task<bool> Admin_UnlockUserAsync(Guid id, CancellationToken ct = default);
    /// <summary>Deletes a user (admin only). Returns false when not found.</summary>
    Task<bool> Admin_DeleteUserAsync(Guid id, CancellationToken ct = default);

    // Admin - IP Blocks
    /// <summary>Lists IP block entries with optional filter.</summary>
    Task<IReadOnlyList<IpBlockDto>> Admin_ListIpBlocksAsync(bool? onlyBlocked = null, CancellationToken ct = default);
    /// <summary>Creates a new IP block entry.</summary>
    Task<IpBlockDto> Admin_CreateIpBlockAsync(IpBlockCreateRequest request, CancellationToken ct = default);
    /// <summary>Gets a single IP block entry or null if not found.</summary>
    Task<IpBlockDto?> Admin_GetIpBlockAsync(Guid id, CancellationToken ct = default);
    /// <summary>Updates an IP block entry. Returns null when not found.</summary>
    Task<IpBlockDto?> Admin_UpdateIpBlockAsync(Guid id, IpBlockUpdateRequest request, CancellationToken ct = default);
    /// <summary>Blocks an IP. Returns false when not found.</summary>
    Task<bool> Admin_BlockIpAsync(Guid id, string? reason, CancellationToken ct = default);
    /// <summary>Unblocks an IP. Returns false when not found.</summary>
    Task<bool> Admin_UnblockIpAsync(Guid id, CancellationToken ct = default);
    /// <summary>Resets counters for an IP block entry. Returns false when not found.</summary>
    Task<bool> Admin_ResetCountersAsync(Guid id, CancellationToken ct = default);
    /// <summary>Deletes an IP block entry. Returns false when not found.</summary>
    Task<bool> Admin_DeleteIpBlockAsync(Guid id, CancellationToken ct = default);

    // Attachments
    /// <summary>Lists attachments for an entity with optional filters.</summary>
    Task<PageResult<AttachmentDto>> Attachments_ListAsync(short entityKind, Guid entityId, int skip = 0, int take = 50, Guid? categoryId = null, bool? isUrl = null, string? q = null, CancellationToken ct = default);
    /// <summary>Uploads a file as an attachment.</summary>
    Task<AttachmentDto> Attachments_UploadFileAsync(short entityKind, Guid entityId, Stream fileStream, string fileName, string contentType, Guid? categoryId = null, short? role = null, CancellationToken ct = default);
    /// <summary>Creates a URL attachment.</summary>
    Task<AttachmentDto> Attachments_CreateUrlAsync(short entityKind, Guid entityId, string url, Guid? categoryId = null, CancellationToken ct = default);
    /// <summary>Deletes an attachment. Returns false when not found.</summary>
    Task<bool> Attachments_DeleteAsync(Guid id, CancellationToken ct = default);
    /// <summary>Updates core properties of an attachment. Returns false when not found.</summary>
    Task<bool> Attachments_UpdateCoreAsync(Guid id, string? fileName, Guid? categoryId, CancellationToken ct = default);
    /// <summary>Updates the category of an attachment. Returns false when not found.</summary>
    Task<bool> Attachments_UpdateCategoryAsync(Guid id, Guid? categoryId, CancellationToken ct = default);
    /// <summary>Lists all attachment categories.</summary>
    Task<IReadOnlyList<AttachmentCategoryDto>> Attachments_ListCategoriesAsync(CancellationToken ct = default);
    /// <summary>Creates a new attachment category.</summary>
    Task<AttachmentCategoryDto> Attachments_CreateCategoryAsync(string name, CancellationToken ct = default);
    /// <summary>Updates the name of an attachment category. Returns null when not found.</summary>
    Task<AttachmentCategoryDto?> Attachments_UpdateCategoryNameAsync(Guid id, string name, CancellationToken ct = default);
    /// <summary>Deletes an attachment category. Returns false on not found or when conflicting.</summary>
    Task<bool> Attachments_DeleteCategoryAsync(Guid id, CancellationToken ct = default);
    /// <summary>Creates a download token for an attachment or null if not found.</summary>
    Task<AttachmentDownloadTokenDto?> Attachments_CreateDownloadTokenAsync(Guid id, int validSeconds = 60, CancellationToken ct = default);

    // Setup - Backups
    /// <summary>Lists backups owned by the current user.</summary>
    Task<IReadOnlyList<BackupDto>> Backups_ListAsync(CancellationToken ct = default);
    /// <summary>Creates a new backup for the current user.</summary>
    Task<BackupDto> Backups_CreateAsync(CancellationToken ct = default);
    /// <summary>Uploads a backup file and returns its metadata.</summary>
    Task<BackupDto> Backups_UploadAsync(Stream fileStream, string fileName, CancellationToken ct = default);
    /// <summary>Downloads a backup file stream or null if not found.</summary>
    Task<Stream?> Backups_DownloadAsync(Guid id, CancellationToken ct = default);
    /// <summary>Immediately applies the specified backup. Returns false when not found.</summary>
    Task<bool> Backups_ApplyAsync(Guid id, CancellationToken ct = default);
    /// <summary>Starts a background restore task for a backup and returns status.</summary>
    Task<BackupRestoreStatusDto> Backups_StartApplyAsync(Guid id, CancellationToken ct = default);
    /// <summary>Gets the status of the current or last backup restore task.</summary>
    Task<BackupRestoreStatusDto> Backups_GetStatusAsync(CancellationToken ct = default);
    /// <summary>Cancels the currently running backup restore task.</summary>
    Task<bool> Backups_CancelAsync(CancellationToken ct = default);
    /// <summary>Deletes a backup entry. Returns false when not found.</summary>
    Task<bool> Backups_DeleteAsync(Guid id, CancellationToken ct = default);

    // Contact Categories
    /// <summary>Lists contact categories for the current user.</summary>
    Task<IReadOnlyList<ContactCategoryDto>> ContactCategories_ListAsync(CancellationToken ct = default);
    /// <summary>Gets a single contact category by id or null if not found.</summary>
    Task<ContactCategoryDto?> ContactCategories_GetAsync(Guid id, CancellationToken ct = default);
    /// <summary>Creates a new contact category.</summary>
    Task<ContactCategoryDto> ContactCategories_CreateAsync(ContactCategoryCreateRequest request, CancellationToken ct = default);
    /// <summary>Updates a contact category name. Returns false when not found.</summary>
    Task<bool> ContactCategories_UpdateAsync(Guid id, ContactCategoryUpdateRequest request, CancellationToken ct = default);
    /// <summary>Deletes a contact category. Returns false when not found.</summary>
    Task<bool> ContactCategories_DeleteAsync(Guid id, CancellationToken ct = default);
    /// <summary>Assigns a symbol attachment to a contact category. Returns false when not found.</summary>
    Task<bool> ContactCategories_SetSymbolAsync(Guid id, Guid attachmentId, CancellationToken ct = default);
    /// <summary>Clears any symbol attachment from a contact category. Returns false when not found.</summary>
    Task<bool> ContactCategories_ClearSymbolAsync(Guid id, CancellationToken ct = default);

    // Contacts
    /// <summary>Lists contacts with optional paging/filtering, or all when all=true.</summary>
    Task<IReadOnlyList<ContactDto>> Contacts_ListAsync(int skip = 0, int take = 50, ContactType? type = null, bool all = false, string? nameFilter = null, CancellationToken ct = default);
    /// <summary>Gets a single contact by id or null when not found.</summary>
    Task<ContactDto?> Contacts_GetAsync(Guid id, CancellationToken ct = default);
    /// <summary>Creates a new contact.</summary>
    Task<ContactDto> Contacts_CreateAsync(ContactCreateRequest request, CancellationToken ct = default);
    /// <summary>Updates an existing contact. Returns null if not found.</summary>
    Task<ContactDto?> Contacts_UpdateAsync(Guid id, ContactUpdateRequest request, CancellationToken ct = default);
    /// <summary>Deletes a contact. Returns false when not found.</summary>
    Task<bool> Contacts_DeleteAsync(Guid id, CancellationToken ct = default);
    /// <summary>Lists alias patterns for a contact.</summary>
    Task<IReadOnlyList<AliasNameDto>> Contacts_GetAliasesAsync(Guid id, CancellationToken ct = default);
    /// <summary>Adds a new alias pattern to a contact.</summary>
    Task<bool> Contacts_AddAliasAsync(Guid id, AliasCreateRequest request, CancellationToken ct = default);
    /// <summary>Deletes an alias from a contact.</summary>
    Task<bool> Contacts_DeleteAliasAsync(Guid id, Guid aliasId, CancellationToken ct = default);
    /// <summary>Merges a source contact into a target contact and returns the updated target.</summary>
    Task<ContactDto> Contacts_MergeAsync(Guid sourceId, ContactMergeRequest request, CancellationToken ct = default);
    /// <summary>Returns the total number of contacts for the current user.</summary>
    Task<int> Contacts_CountAsync(CancellationToken ct = default);
    /// <summary>Assigns a symbol attachment to a contact. Returns false when not found.</summary>
    Task<bool> Contacts_SetSymbolAsync(Guid id, Guid attachmentId, CancellationToken ct = default);
    /// <summary>Clears the symbol attachment from a contact. Returns false when not found.</summary>
    Task<bool> Contacts_ClearSymbolAsync(Guid id, CancellationToken ct = default);

    // Home KPIs
    /// <summary>Lists home KPIs for the current user.</summary>
    Task<IReadOnlyList<HomeKpiDto>> HomeKpis_ListAsync(CancellationToken ct = default);
    /// <summary>Gets a single home KPI by id or null if not found.</summary>
    Task<HomeKpiDto?> HomeKpis_GetAsync(Guid id, CancellationToken ct = default);
    /// <summary>Creates a new home KPI.</summary>
    Task<HomeKpiDto> HomeKpis_CreateAsync(HomeKpiCreateRequest request, CancellationToken ct = default);
    /// <summary>Updates an existing home KPI. Returns null when not found.</summary>
    Task<HomeKpiDto?> HomeKpis_UpdateAsync(Guid id, HomeKpiUpdateRequest request, CancellationToken ct = default);
    /// <summary>Deletes a home KPI. Returns false when not found.</summary>
    Task<bool> HomeKpis_DeleteAsync(Guid id, CancellationToken ct = default);
}
