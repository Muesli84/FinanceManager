using FinanceManager.Shared.Dtos.Accounts;
using FinanceManager.Shared.Dtos.Users;
using FinanceManager.Shared.Dtos.Security;
using FinanceManager.Shared.Dtos.Attachments;
using FinanceManager.Shared.Dtos.Common;

namespace FinanceManager.Shared;

/// <summary>
/// Abstraction for a typed API client used via DI by Blazor view models and services.
/// Only Accounts and Admin endpoints are defined for now; extend as needed.
/// </summary>
public interface IApiClient
{
    // Accounts
    Task<IReadOnlyList<AccountDto>> GetAccountsAsync(int skip = 0, int take = 100, Guid? bankContactId = null, CancellationToken ct = default);
    Task<AccountDto?> GetAccountAsync(Guid id, CancellationToken ct = default);
    Task<AccountDto> CreateAccountAsync(AccountCreateRequest request, CancellationToken ct = default);
    Task<AccountDto?> UpdateAccountAsync(Guid id, AccountUpdateRequest request, CancellationToken ct = default);
    Task<bool> DeleteAccountAsync(Guid id, CancellationToken ct = default);
    Task SetAccountSymbolAsync(Guid id, Guid attachmentId, CancellationToken ct = default);
    Task ClearAccountSymbolAsync(Guid id, CancellationToken ct = default);

    // Admin - Users
    Task<IReadOnlyList<UserAdminDto>> Admin_ListUsersAsync(CancellationToken ct = default);
    Task<UserAdminDto?> Admin_GetUserAsync(Guid id, CancellationToken ct = default);
    Task<UserAdminDto> Admin_CreateUserAsync(CreateUserRequest request, CancellationToken ct = default);
    Task<UserAdminDto?> Admin_UpdateUserAsync(Guid id, UpdateUserRequest request, CancellationToken ct = default);
    Task<bool> Admin_ResetPasswordAsync(Guid id, ResetPasswordRequest request, CancellationToken ct = default);
    Task<bool> Admin_UnlockUserAsync(Guid id, CancellationToken ct = default);
    Task<bool> Admin_DeleteUserAsync(Guid id, CancellationToken ct = default);

    // Admin - IP Blocks
    Task<IReadOnlyList<IpBlockDto>> Admin_ListIpBlocksAsync(bool? onlyBlocked = null, CancellationToken ct = default);
    Task<IpBlockDto> Admin_CreateIpBlockAsync(IpBlockCreateRequest request, CancellationToken ct = default);
    Task<IpBlockDto?> Admin_GetIpBlockAsync(Guid id, CancellationToken ct = default);
    Task<IpBlockDto?> Admin_UpdateIpBlockAsync(Guid id, IpBlockUpdateRequest request, CancellationToken ct = default);
    Task<bool> Admin_BlockIpAsync(Guid id, string? reason, CancellationToken ct = default);
    Task<bool> Admin_UnblockIpAsync(Guid id, CancellationToken ct = default);
    Task<bool> Admin_ResetCountersAsync(Guid id, CancellationToken ct = default);
    Task<bool> Admin_DeleteIpBlockAsync(Guid id, CancellationToken ct = default);

    // Attachments
    Task<PageResult<AttachmentDto>> Attachments_ListAsync(short entityKind, Guid entityId, int skip = 0, int take = 50, Guid? categoryId = null, bool? isUrl = null, string? q = null, CancellationToken ct = default);
    Task<AttachmentDto> Attachments_UploadFileAsync(short entityKind, Guid entityId, Stream fileStream, string fileName, string contentType, Guid? categoryId = null, short? role = null, CancellationToken ct = default);
    Task<AttachmentDto> Attachments_CreateUrlAsync(short entityKind, Guid entityId, string url, Guid? categoryId = null, CancellationToken ct = default);
    Task<bool> Attachments_DeleteAsync(Guid id, CancellationToken ct = default);
    Task<bool> Attachments_UpdateCoreAsync(Guid id, string? fileName, Guid? categoryId, CancellationToken ct = default);
    Task<bool> Attachments_UpdateCategoryAsync(Guid id, Guid? categoryId, CancellationToken ct = default);
    Task<IReadOnlyList<AttachmentCategoryDto>> Attachments_ListCategoriesAsync(CancellationToken ct = default);
    Task<AttachmentCategoryDto> Attachments_CreateCategoryAsync(string name, CancellationToken ct = default);
    Task<AttachmentCategoryDto?> Attachments_UpdateCategoryNameAsync(Guid id, string name, CancellationToken ct = default);
    Task<bool> Attachments_DeleteCategoryAsync(Guid id, CancellationToken ct = default);
    Task<AttachmentDownloadTokenDto?> Attachments_CreateDownloadTokenAsync(Guid id, int validSeconds = 60, CancellationToken ct = default);
}
