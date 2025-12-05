using FinanceManager.Shared.Dtos.Accounts;
using FinanceManager.Shared.Dtos.Users;
using FinanceManager.Shared.Dtos.Securities;
using FinanceManager.Shared.Dtos.Attachments;
using FinanceManager.Shared.Dtos.Common;
using FinanceManager.Shared.Dtos.Admin; // added for BackgroundTaskInfo/Type/Status
using FinanceManager.Shared.Dtos.Backups; // add backups dtos
using FinanceManager.Shared.Dtos.Contacts; // contact categories dtos
using FinanceManager.Shared.Dtos.HomeKpi; // home kpi dtos
using FinanceManager.Shared.Dtos.Postings; // postings dtos
using FinanceManager.Shared.Dtos.Reports; // reports dtos
using FinanceManager.Shared.Dtos.SavingsPlans; // savings plans dtos
using FinanceManager.Shared.Dtos.Statements; // statements dtos

namespace FinanceManager.Shared;

/// <summary>
/// Abstraction for the typed FinanceManager API client used via DI by Blazor view models and services.
/// Provides strongly-typed methods for calling backend endpoints.
/// </summary>
public interface IApiClient
{
    /// <summary>
    /// Holds the last error message returned by the server when a method returns false/null due to a non-success HTTP status.
    /// </summary>
    string? LastError { get; }

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

    // Security Categories
    /// <summary>Lists security categories for the current user.</summary>
    Task<IReadOnlyList<SecurityCategoryDto>> SecurityCategories_ListAsync(CancellationToken ct = default);
    /// <summary>Gets a single security category by id or null if not found.</summary>
    Task<SecurityCategoryDto?> SecurityCategories_GetAsync(Guid id, CancellationToken ct = default);
    /// <summary>Creates a new security category.</summary>
    Task<SecurityCategoryDto> SecurityCategories_CreateAsync(SecurityCategoryRequest request, CancellationToken ct = default);
    /// <summary>Updates the name of a security category. Returns null when not found.</summary>
    Task<SecurityCategoryDto?> SecurityCategories_UpdateAsync(Guid id, SecurityCategoryRequest request, CancellationToken ct = default);
    /// <summary>Deletes a security category. Returns false when not found.</summary>
    Task<bool> SecurityCategories_DeleteAsync(Guid id, CancellationToken ct = default);
    /// <summary>Assigns a symbol attachment to a security category. Returns false when not found.</summary>
    Task<bool> SecurityCategories_SetSymbolAsync(Guid id, Guid attachmentId, CancellationToken ct = default);
    /// <summary>Clears any symbol attachment from a security category. Returns false when not found.</summary>
    Task<bool> SecurityCategories_ClearSymbolAsync(Guid id, CancellationToken ct = default);

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

    // Meta Holidays
    /// <summary>
    /// Returns the list of available holiday provider kinds as strings.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Array of provider kind names.</returns>
    Task<string[]> Meta_GetHolidayProvidersAsync(CancellationToken ct = default);
    /// <summary>
    /// Returns supported country ISO codes for holiday data.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Array of country codes (ISO).</returns>
    Task<string[]> Meta_GetHolidayCountriesAsync(CancellationToken ct = default);
    /// <summary>
    /// Returns subdivision (state/region) codes for the given provider and country.
    /// </summary>
    /// <param name="provider">Holiday provider kind (enum name, case insensitive).</param>
    /// <param name="country">ISO country code.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Array of subdivision codes or empty when unsupported.</returns>
    Task<string[]> Meta_GetHolidaySubdivisionsAsync(string provider, string country, CancellationToken ct = default);

    // User Settings - Notifications
    /// <summary>Gets the current user's notification settings.</summary>
    Task<NotificationSettingsDto?> User_GetNotificationSettingsAsync(CancellationToken ct = default);
    /// <summary>Updates the current user's notification settings.</summary>
    /// <param name="monthlyEnabled">Monthly reminder enabled flag.</param>
    /// <param name="hour">Monthly reminder hour (0-23) or null.</param>
    /// <param name="minute">Monthly reminder minute (0-59) or null.</param>
    /// <param name="provider">Holiday provider name.</param>
    /// <param name="country">Holiday country ISO code.</param>
    /// <param name="subdivision">Holiday subdivision code.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True on success.</returns>
    Task<bool> User_UpdateNotificationSettingsAsync(bool monthlyEnabled, int? hour, int? minute, string? provider, string? country, string? subdivision, CancellationToken ct = default);

    // Notifications
    /// <summary>
    /// Lists currently active notifications for the signed-in user (filtered server-side by current UTC time).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Active notifications.</returns>
    Task<IReadOnlyList<NotificationDto>> Notifications_ListAsync(CancellationToken ct = default);

    /// <summary>
    /// Dismisses a notification by its id for the current user.
    /// </summary>
    /// <param name="id">Notification id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the notification existed and was dismissed.</returns>
    Task<bool> Notifications_DismissAsync(Guid id, CancellationToken ct = default);

    // Postings
    /// <summary>Gets a single posting by id or null if not found or not owned by the current user.</summary>
    Task<PostingServiceDto?> Postings_GetByIdAsync(Guid id, CancellationToken ct = default);
    /// <summary>Lists postings for an account. Returns empty on not found or unauthorized.</summary>
    Task<IReadOnlyList<PostingServiceDto>> Postings_GetAccountAsync(Guid accountId, int skip = 0, int take = 50, string? q = null, DateTime? from = null, DateTime? to = null, CancellationToken ct = default);
    /// <summary>Lists postings for a contact. Returns empty on not found or unauthorized.</summary>
    Task<IReadOnlyList<PostingServiceDto>> Postings_GetContactAsync(Guid contactId, int skip = 0, int take = 50, string? q = null, DateTime? from = null, DateTime? to = null, CancellationToken ct = default);
    /// <summary>Lists postings for a savings plan. Returns empty on not found or unauthorized.</summary>
    Task<IReadOnlyList<PostingServiceDto>> Postings_GetSavingsPlanAsync(Guid planId, int skip = 0, int take = 50, DateTime? from = null, DateTime? to = null, string? q = null, CancellationToken ct = default);
    /// <summary>Lists postings for a security. Returns empty on not found or unauthorized.</summary>
    Task<IReadOnlyList<PostingServiceDto>> Postings_GetSecurityAsync(Guid securityId, int skip = 0, int take = 50, DateTime? from = null, DateTime? to = null, CancellationToken ct = default);
    /// <summary>Returns first entity links for a posting group or null when not found or unauthorized.</summary>
    Task<GroupLinksDto?> Postings_GetGroupLinksAsync(Guid groupId, CancellationToken ct = default);

    // Reports
    /// <summary>Executes a report aggregation query.</summary>
    Task<ReportAggregationResult> Reports_QueryAggregatesAsync(ReportAggregatesQueryRequest req, CancellationToken ct = default);
    /// <summary>Lists all report favorites for the current user.</summary>
    Task<IReadOnlyList<ReportFavoriteDto>> Reports_ListFavoritesAsync(CancellationToken ct = default);
    /// <summary>Gets a single report favorite by id or null if not found.</summary>
    Task<ReportFavoriteDto?> Reports_GetFavoriteAsync(Guid id, CancellationToken ct = default);
    /// <summary>Creates a new report favorite.</summary>
    Task<ReportFavoriteDto> Reports_CreateFavoriteAsync(ReportFavoriteCreateApiRequest req, CancellationToken ct = default);
    /// <summary>Updates an existing report favorite. Returns null if not found.</summary>
    Task<ReportFavoriteDto?> Reports_UpdateFavoriteAsync(Guid id, ReportFavoriteUpdateApiRequest req, CancellationToken ct = default);
    /// <summary>Deletes a report favorite. Returns false when not found.</summary>
    Task<bool> Reports_DeleteFavoriteAsync(Guid id, CancellationToken ct = default);

    // Savings Plan Categories
    /// <summary>Lists saving plan categories for the current user.</summary>
    Task<IReadOnlyList<SavingsPlanCategoryDto>> SavingsPlanCategories_ListAsync(CancellationToken ct = default);
    /// <summary>Gets a single saving plan category by id or null if not found.</summary>
    Task<SavingsPlanCategoryDto?> SavingsPlanCategories_GetAsync(Guid id, CancellationToken ct = default);
    /// <summary>Creates a new saving plan category.</summary>
    Task<SavingsPlanCategoryDto?> SavingsPlanCategories_CreateAsync(SavingsPlanCategoryDto dto, CancellationToken ct = default);
    /// <summary>Updates an existing saving plan category. Returns null when not found.</summary>
    Task<SavingsPlanCategoryDto?> SavingsPlanCategories_UpdateAsync(Guid id, SavingsPlanCategoryDto dto, CancellationToken ct = default);
    /// <summary>Deletes a saving plan category. Returns false when not found.</summary>
    Task<bool> SavingsPlanCategories_DeleteAsync(Guid id, CancellationToken ct = default);
    /// <summary>Assigns a symbol attachment to a saving plan category. Returns false when not found.</summary>
    Task<bool> SavingsPlanCategories_SetSymbolAsync(Guid id, Guid attachmentId, CancellationToken ct = default);
    /// <summary>Clears the symbol attachment from a saving plan category. Returns false when not found.</summary>
    Task<bool> SavingsPlanCategories_ClearSymbolAsync(Guid id, CancellationToken ct = default);

    // Savings Plans
    Task<IReadOnlyList<SavingsPlanDto>> SavingsPlans_ListAsync(bool onlyActive = true, CancellationToken ct = default);
    Task<int> SavingsPlans_CountAsync(bool onlyActive = true, CancellationToken ct = default);
    Task<SavingsPlanDto?> SavingsPlans_GetAsync(Guid id, CancellationToken ct = default);
    Task<SavingsPlanDto> SavingsPlans_CreateAsync(SavingsPlanCreateRequest req, CancellationToken ct = default);
    Task<SavingsPlanDto?> SavingsPlans_UpdateAsync(Guid id, SavingsPlanCreateRequest req, CancellationToken ct = default);
    Task<SavingsPlanAnalysisDto> SavingsPlans_AnalyzeAsync(Guid id, CancellationToken ct = default);
    Task<bool> SavingsPlans_ArchiveAsync(Guid id, CancellationToken ct = default);
    Task<bool> SavingsPlans_DeleteAsync(Guid id, CancellationToken ct = default);
    Task<bool> SavingsPlans_SetSymbolAsync(Guid id, Guid attachmentId, CancellationToken ct = default);
    Task<bool> SavingsPlans_ClearSymbolAsync(Guid id, CancellationToken ct = default);

    // Securities
    /// <summary>Lists securities for the current user.</summary>
    Task<IReadOnlyList<SecurityDto>> Securities_ListAsync(bool onlyActive = true, CancellationToken ct = default);
    /// <summary>Counts all or active securities.</summary>
    Task<int> Securities_CountAsync(bool onlyActive = true, CancellationToken ct = default);
    /// <summary>Gets a single security by id or null if not found.</summary>
    Task<SecurityDto?> Securities_GetAsync(Guid id, CancellationToken ct = default);
    /// <summary>Creates a new security.</summary>
    Task<SecurityDto> Securities_CreateAsync(SecurityRequest req, CancellationToken ct = default);
    /// <summary>Updates an existing security. Returns null when not found.</summary>
    Task<SecurityDto?> Securities_UpdateAsync(Guid id, SecurityRequest req, CancellationToken ct = default);
    /// <summary>Archives a security. Returns false when not found.</summary>
    Task<bool> Securities_ArchiveAsync(Guid id, CancellationToken ct = default);
    /// <summary>Deletes a security. Returns false when not found.</summary>
    Task<bool> Securities_DeleteAsync(Guid id, CancellationToken ct = default);
    /// <summary>Assigns a symbol attachment to a security. Returns false when not found.</summary>
    Task<bool> Securities_SetSymbolAsync(Guid id, Guid attachmentId, CancellationToken ct = default);
    /// <summary>Clears the symbol attachment from a security. Returns false when not found.</summary>
    Task<bool> Securities_ClearSymbolAsync(Guid id, CancellationToken ct = default);
    /// <summary>Uploads a new symbol file for a security.</summary>
    Task<AttachmentDto> Securities_UploadSymbolAsync(Guid id, Stream fileStream, string fileName, string? contentType = null, Guid? categoryId = null, CancellationToken ct = default);
    /// <summary>Gets historical aggregate data for a security.</summary>
    Task<IReadOnlyList<AggregatePointDto>?> Securities_GetAggregatesAsync(Guid securityId, string period = "Month", int take = 36, int? maxYearsBack = null, CancellationToken ct = default);
    /// <summary>Gets historical price data for a security.</summary>
    Task<IReadOnlyList<SecurityPriceDto>?> Securities_GetPricesAsync(Guid id, int skip = 0, int take = 50, CancellationToken ct = default);
    /// <summary>Enqueues a background task to backfill missing security data.</summary>
    Task<BackgroundTaskInfo> Securities_EnqueueBackfillAsync(Guid? securityId, DateTime? fromDateUtc, DateTime? toDateUtc, CancellationToken ct = default);
    /// <summary>Lists upcoming or past dividends for a security.</summary>
    Task<IReadOnlyList<AggregatePointDto>> Securities_GetDividendsAsync(string? period = null, int? take = null, CancellationToken ct = default);

    // Statement Drafts
    /// <summary>Lists open statement drafts for the current user.</summary>
    Task<IReadOnlyList<StatementDraftDto>> StatementDrafts_ListOpenAsync(int skip = 0, int take = 3, CancellationToken ct = default);
    /// <summary>Gets the count of open statement drafts.</summary>
    Task<int> StatementDrafts_GetOpenCountAsync(CancellationToken ct = default);
    /// <summary>Deletes all statement drafts. Caution: irreversible!</summary>
    Task<bool> StatementDrafts_DeleteAllAsync(CancellationToken ct = default);
    /// <summary>Uploads a statement file for processing.</summary>
    Task<StatementDraftUploadResult?> StatementDrafts_UploadAsync(Stream stream, string fileName, CancellationToken ct = default);
    /// <summary>Gets the detail of a statement draft by id.</summary>
    Task<StatementDraftDetailDto?> StatementDrafts_GetAsync(Guid draftId, bool headerOnly = false, string? src = null, Guid? fromEntryDraftId = null, Guid? fromEntryId = null, CancellationToken ct = default);
    /// <summary>Gets the detail of a specific entry in a statement draft.</summary>
    Task<StatementDraftEntryDetailDto?> StatementDrafts_GetEntryAsync(Guid draftId, Guid entryId, CancellationToken ct = default);
    /// <summary>Adds a new entry to a statement draft.</summary>
    Task<StatementDraftDetailDto?> StatementDrafts_AddEntryAsync(Guid draftId, StatementDraftAddEntryRequest req, CancellationToken ct = default);
    /// <summary>Classifies a statement draft (automatic processing).</summary>
    Task<StatementDraftDetailDto?> StatementDrafts_ClassifyAsync(Guid draftId, CancellationToken ct = default);
    /// <summary>Sets the account for a statement draft.</summary>
    Task<StatementDraftDetailDto?> StatementDrafts_SetAccountAsync(Guid draftId, Guid accountId, CancellationToken ct = default);
    /// <summary>Commits (books) a statement draft, creating postings.</summary>
    Task<object?> StatementDrafts_CommitAsync(Guid draftId, StatementDraftCommitRequest req, CancellationToken ct = default);
    /// <summary>Sets the contact for an entry in a statement draft.</summary>
    Task<StatementDraftEntryDto?> StatementDrafts_SetEntryContactAsync(Guid draftId, Guid entryId, StatementDraftSetContactRequest req, CancellationToken ct = default);
    /// <summary>Sets a cost-neutral flag for an entry in a statement draft.</summary>
    Task<StatementDraftEntryDto?> StatementDrafts_SetEntryCostNeutralAsync(Guid draftId, Guid entryId, StatementDraftSetCostNeutralRequest req, CancellationToken ct = default);
    /// <summary>Sets the savings plan for an entry in a statement draft.</summary>
    Task<StatementDraftEntryDto?> StatementDrafts_SetEntrySavingsPlanAsync(Guid draftId, Guid entryId, StatementDraftSetSavingsPlanRequest req, CancellationToken ct = default);
    /// <summary>Sets the security for an entry in a statement draft.</summary>
    Task<StatementDraftEntryDto?> StatementDrafts_SetEntrySecurityAsync(Guid draftId, Guid entryId, StatementDraftSetEntrySecurityRequest req, CancellationToken ct = default);
    /// <summary>Sets an entry in a statement draft to archive on booking.</summary>
    Task<StatementDraftEntryDto?> StatementDrafts_SetEntryArchiveOnBookingAsync(Guid draftId, Guid entryId, StatementDraftSetArchiveSavingsPlanOnBookingRequest req, CancellationToken ct = default);
    /// <summary>Validates a statement draft (checks for errors).</summary>
    Task<DraftValidationResultDto?> StatementDrafts_ValidateAsync(Guid draftId, CancellationToken ct = default);
    /// <summary>Validates an entry in a statement draft.</summary>
    Task<DraftValidationResultDto?> StatementDrafts_ValidateEntryAsync(Guid draftId, Guid entryId, CancellationToken ct = default);
    /// <summary>Books (commits) a statement draft, creating postings.</summary>
    Task<BookingResult?> StatementDrafts_BookAsync(Guid draftId, bool forceWarnings = false, CancellationToken ct = default);
    /// <summary>Books (commits) an entry in a statement draft.</summary>
    Task<BookingResult?> StatementDrafts_BookEntryAsync(Guid draftId, Guid entryId, bool forceWarnings = false, CancellationToken ct = default);
    /// <summary>Saves all details of an entry in a statement draft.</summary>
    Task<object?> StatementDrafts_SaveEntryAllAsync(Guid draftId, Guid entryId, StatementDraftSaveEntryAllRequest req, CancellationToken ct = default);
    /// <summary>Deletes an entry from a statement draft.</summary>
    Task<bool> StatementDrafts_DeleteEntryAsync(Guid draftId, Guid entryId, CancellationToken ct = default);
    /// <summary>Resets a duplicate entry in a statement draft.</summary>
    Task<object?> StatementDrafts_ResetDuplicateEntryAsync(Guid draftId, Guid entryId, CancellationToken ct = default);
    /// <summary>Classifies a specific entry in a statement draft.</summary>
    Task<StatementDraftDetailDto?> StatementDrafts_ClassifyEntryAsync(Guid draftId, Guid entryId, CancellationToken ct = default);
    /// <summary>Downloads the original statement file for a draft.</summary>
    Task<Stream?> StatementDrafts_DownloadOriginalAsync(Guid draftId, CancellationToken ct = default);
    /// <summary>Starts the classification of statement drafts as a background task.</summary>
    Task<ApiClient.StatementDraftsClassifyStatus?> StatementDrafts_StartClassifyAsync(CancellationToken ct = default);
    /// <summary>Gets the status of the ongoing or last classification task for statement drafts.</summary>
    Task<ApiClient.StatementDraftsClassifyStatus?> StatementDrafts_GetClassifyStatusAsync(CancellationToken ct = default);
    /// <summary>Starts the booking of all statement drafts as a background task.</summary>
    Task<StatementDraftMassBookStatusDto?> StatementDrafts_StartBookAllAsync(bool ignoreWarnings, bool abortOnFirstIssue, bool bookEntriesIndividually, CancellationToken ct = default);
    /// <summary>Gets the status of the booking all task for statement drafts.</summary>
    Task<StatementDraftMassBookStatusDto?> StatementDrafts_GetBookAllStatusAsync(CancellationToken ct = default);
    /// <summary>Cancels the booking all task for statement drafts.</summary>
    Task<bool> StatementDrafts_CancelBookAllAsync(CancellationToken ct = default);
    /// <summary>Deletes a statement draft. Returns false when not found.</summary>
    Task<bool> StatementDrafts_DeleteAsync(Guid draftId, CancellationToken ct = default);

    // Users
    /// <summary>Checks if any users exist in the system.</summary>
    Task<bool> Users_HasAnyAsync(CancellationToken ct = default);

    // User Settings
    /// <summary>Gets the profile settings for the current user.</summary>
    Task<UserProfileSettingsDto?> UserSettings_GetProfileAsync(CancellationToken ct = default);
    /// <summary>Updates the profile settings for the current user.</summary>
    Task<bool> UserSettings_UpdateProfileAsync(UserProfileSettingsUpdateRequest request, CancellationToken ct = default);
    /// <summary>Gets the import split settings for the current user.</summary>
    Task<ImportSplitSettingsDto?> UserSettings_GetImportSplitAsync(CancellationToken ct = default);
    /// <summary>Updates the import split settings for the current user.</summary>
    Task<bool> UserSettings_UpdateImportSplitAsync(ImportSplitSettingsUpdateRequest request, CancellationToken ct = default);
}
