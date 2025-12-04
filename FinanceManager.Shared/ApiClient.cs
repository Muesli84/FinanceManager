using System.Net.Http.Json;
using System.Text.Json;
using FinanceManager.Shared.Dtos.SavingsPlans;


namespace FinanceManager.Shared;

public class ApiClient : IApiClient
{
    private readonly HttpClient _http;
    public string? LastError { get; private set; }

    public ApiClient(HttpClient http)
    {
        _http = http;
    }

    #region Accounts

    /// <inheritdoc />
    public async Task<IReadOnlyList<AccountDto>> GetAccountsAsync(int skip = 0, int take = 100, Guid? bankContactId = null, CancellationToken ct = default)
    {
        var url = $"/api/accounts?skip={skip}&take={take}";
        if (bankContactId.HasValue) url += $"&bankContactId={Uri.EscapeDataString(bankContactId.Value.ToString())}";
        var resp = await _http.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<IReadOnlyList<AccountDto>>(cancellationToken: ct) ?? Array.Empty<AccountDto>();
    }

    /// <inheritdoc />
    public async Task<AccountDto?> GetAccountAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"/api/accounts/{id}", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<AccountDto>(cancellationToken: ct);
    }

    /// <inheritdoc />
    public async Task<AccountDto> CreateAccountAsync(AccountCreateRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync("/api/accounts", request, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<AccountDto>(cancellationToken: ct))!;
    }

    /// <inheritdoc />
    public async Task<AccountDto?> UpdateAccountAsync(Guid id, AccountUpdateRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PutAsJsonAsync($"/api/accounts/{id}", request, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<AccountDto>(cancellationToken: ct);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAccountAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.DeleteAsync($"/api/accounts/{id}", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        resp.EnsureSuccessStatusCode();
        return true;
    }

    /// <inheritdoc />
    public async Task SetAccountSymbolAsync(Guid id, Guid attachmentId, CancellationToken ct = default)
    {
        var resp = await _http.PostAsync($"/api/accounts/{id}/symbol/{attachmentId}", content: null, ct);
        resp.EnsureSuccessStatusCode();
    }

    /// <inheritdoc />
    public async Task ClearAccountSymbolAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.DeleteAsync($"/api/accounts/{id}/symbol", ct);
        resp.EnsureSuccessStatusCode();
    }

    #endregion Accounts

    #region Auth

    /// <inheritdoc />
    public async Task<AuthOkResponse> Auth_LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync("/api/auth/login", request, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<AuthOkResponse>(cancellationToken: ct))!;
    }

    /// <inheritdoc />
    public async Task<AuthOkResponse> Auth_RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync("/api/auth/register", request, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<AuthOkResponse>(cancellationToken: ct))!;
    }

    /// <inheritdoc />
    public async Task<bool> Auth_LogoutAsync(CancellationToken ct = default)
    {
        var resp = await _http.PostAsync("/api/auth/logout", content: null, ct);
        resp.EnsureSuccessStatusCode();
        return true;
    }

    #endregion Auth

    #region Background tasks

    /// <inheritdoc />
    public async Task<BackgroundTaskInfo> BackgroundTasks_EnqueueAsync(BackgroundTaskType type, bool allowDuplicate = false, CancellationToken ct = default)
    {
        var url = $"/api/background-tasks/{type}?allowDuplicate={(allowDuplicate ? "true" : "false")}";
        var resp = await _http.PostAsync(url, content: null, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<BackgroundTaskInfo>(cancellationToken: ct))!;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BackgroundTaskInfo>> BackgroundTasks_GetActiveAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetAsync("/api/background-tasks/active", ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<IReadOnlyList<BackgroundTaskInfo>>(cancellationToken: ct) ?? Array.Empty<BackgroundTaskInfo>();
    }

    /// <inheritdoc />
    public async Task<BackgroundTaskInfo?> BackgroundTasks_GetDetailAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"/api/background-tasks/{id}", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<BackgroundTaskInfo>(cancellationToken: ct);
    }

    /// <inheritdoc />
    public async Task<bool> BackgroundTasks_CancelOrRemoveAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.DeleteAsync($"/api/background-tasks/{id}", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        if (resp.StatusCode == System.Net.HttpStatusCode.BadRequest) return false;
        resp.EnsureSuccessStatusCode();
        return true;
    }

    /// <inheritdoc />
    public async Task<AggregatesRebuildStatusDto> Aggregates_RebuildAsync(bool allowDuplicate = false, CancellationToken ct = default)
    {
        var url = $"/api/background-tasks/aggregates/rebuild?allowDuplicate={(allowDuplicate ? "true" : "false")}";
        var resp = await _http.PostAsync(url, content: null, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<AggregatesRebuildStatusDto>(cancellationToken: ct))!;
    }

    /// <inheritdoc />
    public async Task<AggregatesRebuildStatusDto> Aggregates_GetRebuildStatusAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetAsync("/api/background-tasks/aggregates/rebuild/status", ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<AggregatesRebuildStatusDto>(cancellationToken: ct))!;
    }

    #endregion Background tasks

    #region Admin - Users

    /// <summary>Lists all users.</summary>
    public async Task<IReadOnlyList<UserAdminDto>> Admin_ListUsersAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetAsync("/api/admin/users", ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<IReadOnlyList<UserAdminDto>>(cancellationToken: ct) ?? Array.Empty<UserAdminDto>();
    }

    /// <summary>Gets a user by id.</summary>
    public async Task<UserAdminDto?> Admin_GetUserAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"/api/admin/users/{id}", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<UserAdminDto>(cancellationToken: ct);
    }

    /// <summary>Creates a new user.</summary>
    public async Task<UserAdminDto> Admin_CreateUserAsync(CreateUserRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync("/api/admin/users", request, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<UserAdminDto>(cancellationToken: ct))!;
    }

    /// <summary>Updates a user.</summary>
    public async Task<UserAdminDto?> Admin_UpdateUserAsync(Guid id, UpdateUserRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PutAsJsonAsync($"/api/admin/users/{id}", request, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<UserAdminDto>(cancellationToken: ct);
    }

    /// <summary>Resets a user's password.</summary>
    public async Task<bool> Admin_ResetPasswordAsync(Guid id, ResetPasswordRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync($"/api/admin/users/{id}/reset-password", request, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        resp.EnsureSuccessStatusCode();
        return true;
    }

    /// <summary>Unlocks a user.</summary>
    public async Task<bool> Admin_UnlockUserAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.PostAsync($"/api/admin/users/{id}/unlock", content: null, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        resp.EnsureSuccessStatusCode();
        return true;
    }

    /// <summary>Deletes a user.</summary>
    public async Task<bool> Admin_DeleteUserAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.DeleteAsync($"/api/admin/users/{id}", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        resp.EnsureSuccessStatusCode();
        return true;
    }

    #endregion Admin - Users

    #region Admin - IP Blocks

    /// <summary>Lists IP blocks with optional filter.</summary>
    public async Task<IReadOnlyList<IpBlockDto>> Admin_ListIpBlocksAsync(bool? onlyBlocked = null, CancellationToken ct = default)
    {
        var url = "/api/admin/ip-blocks";
        if (onlyBlocked.HasValue)
        {
            url += onlyBlocked.Value ? "?onlyBlocked=true" : "?onlyBlocked=false";
        }
        var resp = await _http.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<IReadOnlyList<IpBlockDto>>(cancellationToken: ct) ?? Array.Empty<IpBlockDto>();
    }

    /// <summary>Creates a new IP block entry.</summary>
    public async Task<IpBlockDto> Admin_CreateIpBlockAsync(IpBlockCreateRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync("/api/admin/ip-blocks", request, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<IpBlockDto>(cancellationToken: ct))!;
    }

    /// <summary>Gets a single IP block entry.</summary>
    public async Task<IpBlockDto?> Admin_GetIpBlockAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"/api/admin/ip-blocks/{id}", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<IpBlockDto>(cancellationToken: ct);
    }

    /// <summary>Updates an IP block entry.</summary>
    public async Task<IpBlockDto?> Admin_UpdateIpBlockAsync(Guid id, IpBlockUpdateRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PutAsJsonAsync($"/api/admin/ip-blocks/{id}", request, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<IpBlockDto>(cancellationToken: ct);
    }

    /// <summary>Blocks an IP now.</summary>
    public async Task<bool> Admin_BlockIpAsync(Guid id, string? reason, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync($"/api/admin/ip-blocks/{id}/block", new IpBlockUpdateRequest(reason, null), ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        resp.EnsureSuccessStatusCode();
        return true;
    }

    /// <summary>Unblocks an IP.</summary>
    public async Task<bool> Admin_UnblockIpAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.PostAsync($"/api/admin/ip-blocks/{id}/unblock", content: null, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        resp.EnsureSuccessStatusCode();
        return true;
    }

    /// <summary>Resets attempt counters for an IP block entry.</summary>
    public async Task<bool> Admin_ResetCountersAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.PostAsync($"/api/admin/ip-blocks/{id}/reset-counters", content: null, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        resp.EnsureSuccessStatusCode();
        return true;
    }

    /// <summary>Deletes an IP block entry.</summary>
    public async Task<bool> Admin_DeleteIpBlockAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.DeleteAsync($"/api/admin/ip-blocks/{id}", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        resp.EnsureSuccessStatusCode();
        return true;
    }

    #endregion Admin - IP Blocks

    #region Attachments

    /// <summary>Lists attachments for an entity.</summary>
    public async Task<PageResult<AttachmentDto>> Attachments_ListAsync(short entityKind, Guid entityId, int skip = 0, int take = 50, Guid? categoryId = null, bool? isUrl = null, string? q = null, CancellationToken ct = default)
    {
        var url = $"/api/attachments/{entityKind}/{entityId}?skip={skip}&take={take}";
        if (categoryId.HasValue) url += $"&categoryId={categoryId}";
        if (isUrl.HasValue) url += isUrl.Value ? "&isUrl=true" : "&isUrl=false";
        if (!string.IsNullOrWhiteSpace(q)) url += $"&q={Uri.EscapeDataString(q)}";
        var resp = await _http.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<PageResult<AttachmentDto>>(cancellationToken: ct)) ?? new PageResult<AttachmentDto>();
    }

    /// <summary>Uploads a file as an attachment.</summary>
    public async Task<AttachmentDto> Attachments_UploadFileAsync(short entityKind, Guid entityId, Stream fileStream, string fileName, string contentType, Guid? categoryId = null, short? role = null, CancellationToken ct = default)
    {
        using var content = new MultipartFormDataContent();
        var part = new StreamContent(fileStream);
        part.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);
        content.Add(part, "file", fileName);
        if (categoryId.HasValue) content.Add(new StringContent(categoryId.Value.ToString()), "categoryId");
        var url = $"/api/attachments/{entityKind}/{entityId}";
        if (role.HasValue) url += $"?role={role.Value}";
        var resp = await _http.PostAsync(url, content, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<AttachmentDto>(cancellationToken: ct))!;
    }

    /// <summary>Creates a URL attachment.</summary>
    public async Task<AttachmentDto> Attachments_CreateUrlAsync(short entityKind, Guid entityId, string url, Guid? categoryId = null, CancellationToken ct = default)
    {
        var payload = new { file = (string?)null, categoryId, url };
        var resp = await _http.PostAsJsonAsync($"/api/attachments/{entityKind}/{entityId}", payload, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<AttachmentDto>(cancellationToken: ct))!;
    }

    /// <summary>Deletes an attachment.</summary>
    public async Task<bool> Attachments_DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.DeleteAsync($"/api/attachments/{id}", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        resp.EnsureSuccessStatusCode();
        return true;
    }

    /// <summary>Updates core properties of an attachment.</summary>
    public async Task<bool> Attachments_UpdateCoreAsync(Guid id, string? fileName, Guid? categoryId, CancellationToken ct = default)
    {
        var req = new AttachmentUpdateCoreRequest(fileName, categoryId);
        var resp = await _http.PutAsJsonAsync($"/api/attachments/{id}", req, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        resp.EnsureSuccessStatusCode();
        return true;
    }

    /// <summary>Updates the category of an attachment.</summary>
    public async Task<bool> Attachments_UpdateCategoryAsync(Guid id, Guid? categoryId, CancellationToken ct = default)
    {
        var req = new AttachmentUpdateCategoryRequest(categoryId);
        var resp = await _http.PutAsJsonAsync($"/api/attachments/{id}/category", req, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        resp.EnsureSuccessStatusCode();
        return true;
    }

    /// <summary>Lists all attachment categories.</summary>
    public async Task<IReadOnlyList<AttachmentCategoryDto>> Attachments_ListCategoriesAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetAsync("/api/attachments/categories", ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<IReadOnlyList<AttachmentCategoryDto>>(cancellationToken: ct) ?? Array.Empty<AttachmentCategoryDto>();
    }

    /// <summary>Creates a new attachment category.</summary>
    public async Task<AttachmentCategoryDto> Attachments_CreateCategoryAsync(string name, CancellationToken ct = default)
    {
        var req = new AttachmentCreateCategoryRequest(name);
        var resp = await _http.PostAsJsonAsync("/api/attachments/categories", req, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<AttachmentCategoryDto>(cancellationToken: ct))!;
    }

    /// <summary>Updates the name of an attachment category.</summary>
    public async Task<AttachmentCategoryDto?> Attachments_UpdateCategoryNameAsync(Guid id, string name, CancellationToken ct = default)
    {
        var req = new AttachmentUpdateCategoryNameRequest(name);
        var resp = await _http.PutAsJsonAsync($"/api/attachments/categories/{id}", req, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<AttachmentCategoryDto>(cancellationToken: ct);
    }

    /// <summary>Deletes an attachment category.</summary>
    public async Task<bool> Attachments_DeleteCategoryAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.DeleteAsync($"/api/attachments/categories/{id}", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        if (resp.StatusCode == System.Net.HttpStatusCode.Conflict) return false;
        resp.EnsureSuccessStatusCode();
        return true;
    }

    /// <summary>Creates a download token for an attachment.</summary>
    public async Task<AttachmentDownloadTokenDto?> Attachments_CreateDownloadTokenAsync(Guid id, int validSeconds = 60, CancellationToken ct = default)
    {
        var resp = await _http.PostAsync($"/api/attachments/{id}/download-token?validSeconds={validSeconds}", content: null, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<AttachmentDownloadTokenDto>(cancellationToken: ct);
    }

    #endregion Attachments

    #region Backups

    /// <inheritdoc />
    public async Task<IReadOnlyList<BackupDto>> Backups_ListAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetAsync("/api/setup/backups", ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<IReadOnlyList<BackupDto>>(cancellationToken: ct) ?? Array.Empty<BackupDto>();
    }

    /// <inheritdoc />
    public async Task<BackupDto> Backups_CreateAsync(CancellationToken ct = default)
    {
        var resp = await _http.PostAsync("/api/setup/backups", content: null, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<BackupDto>(cancellationToken: ct))!;
    }

    /// <inheritdoc />
    public async Task<BackupDto> Backups_UploadAsync(Stream fileStream, string fileName, CancellationToken ct = default)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StreamContent(fileStream), "file", fileName);
        var resp = await _http.PostAsync("/api/setup/backups/upload", content, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<BackupDto>(cancellationToken: ct))!;
    }

    /// <inheritdoc />
    public async Task<Stream?> Backups_DownloadAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"/api/setup/backups/{id}/download", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        var ms = new MemoryStream();
        await resp.Content.CopyToAsync(ms, ct);
        ms.Position = 0;
        return ms;
    }

    /// <inheritdoc />
    public async Task<BackupRestoreStatusDto> Backups_StartApplyAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.PostAsync($"/api/setup/backups/{id}/apply/start", content: null, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<BackupRestoreStatusDto>(cancellationToken: ct))!;
    }

    /// <inheritdoc />
    public async Task<BackupRestoreStatusDto> Backups_GetStatusAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetAsync("/api/setup/backups/restore/status", ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<BackupRestoreStatusDto>(cancellationToken: ct))!;
    }

    /// <inheritdoc />
    public async Task<bool> Backups_ApplyAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.PostAsync($"/api/setup/backups/{id}/apply", content: null, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        resp.EnsureSuccessStatusCode();
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> Backups_CancelAsync(CancellationToken ct = default)
    {
        var resp = await _http.PostAsync("/api/setup/backups/restore/cancel", content: null, ct);
        resp.EnsureSuccessStatusCode();
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> Backups_DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.DeleteAsync($"/api/setup/backups/{id}", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        resp.EnsureSuccessStatusCode();
        return true;
    }

    #endregion Backups

    #region Contact Categories

    /// <summary>Lists all contact categories.</summary>
    public async Task<IReadOnlyList<ContactCategoryDto>> ContactCategories_ListAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetAsync("/api/contact-categories", ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<IReadOnlyList<ContactCategoryDto>>(cancellationToken: ct) ?? Array.Empty<ContactCategoryDto>();
    }

    /// <summary>Gets a contact category by id.</summary>
    public async Task<ContactCategoryDto?> ContactCategories_GetAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"/api/contact-categories/{id}", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<ContactCategoryDto>(cancellationToken: ct);
    }

    /// <summary>Creates a new contact category.</summary>
    public async Task<ContactCategoryDto> ContactCategories_CreateAsync(ContactCategoryCreateRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync("/api/contact-categories", request, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<ContactCategoryDto>(cancellationToken: ct))!;
    }

    /// <summary>Updates a contact category.</summary>
    public async Task<bool> ContactCategories_UpdateAsync(Guid id, ContactCategoryUpdateRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PutAsJsonAsync($"/api/contact-categories/{id}", request, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        resp.EnsureSuccessStatusCode();
        return true;
    }

    /// <summary>Deletes a contact category.</summary>
    public async Task<bool> ContactCategories_DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.DeleteAsync($"/api/contact-categories/{id}", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        resp.EnsureSuccessStatusCode();
        return true;
    }

    /// <summary>Sets the symbol (attachment) for a contact category.</summary>
    public async Task<bool> ContactCategories_SetSymbolAsync(Guid id, Guid attachmentId, CancellationToken ct = default)
    {
        var resp = await _http.PostAsync($"/api/contact-categories/{id}/symbol/{attachmentId}", content: null, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        resp.EnsureSuccessStatusCode();
        return true;
    }

    /// <summary>Clears the symbol (attachment) from a contact category.</summary>
    public async Task<bool> ContactCategories_ClearSymbolAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.DeleteAsync($"/api/contact-categories/{id}/symbol", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        resp.EnsureSuccessStatusCode();
        return true;
    }

    #endregion Contact Categories

    #region Contacts

    /// <summary>Lists contacts with optional filters.</summary>
    public async Task<IReadOnlyList<ContactDto>> Contacts_ListAsync(int skip = 0, int take = 50, ContactType? type = null, bool all = false, string? nameFilter = null, CancellationToken ct = default)
    {
        var url = $"/api/contacts?skip={skip}&take={take}";
        if (type.HasValue) url += $"&type={type.Value}";
        if (all) url += "&all=true";
        if (!string.IsNullOrWhiteSpace(nameFilter)) url += $"&q={Uri.EscapeDataString(nameFilter)}";
        var resp = await _http.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<IReadOnlyList<ContactDto>>(cancellationToken: ct) ?? Array.Empty<ContactDto>();
    }

    /// <summary>Gets a contact by id.</summary>
    public async Task<ContactDto?> Contacts_GetAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"/api/contacts/{id}", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<ContactDto>(cancellationToken: ct);
    }

    /// <summary>Creates a new contact.</summary>
    public async Task<ContactDto> Contacts_CreateAsync(ContactCreateRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync("/api/contacts", request, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<ContactDto>(cancellationToken: ct))!;
    }

    /// <summary>Updates a contact.</summary>
    public async Task<ContactDto?> Contacts_UpdateAsync(Guid id, ContactUpdateRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PutAsJsonAsync($"/api/contacts/{id}", request, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<ContactDto>(cancellationToken: ct);
    }

    /// <summary>Deletes a contact.</summary>
    public async Task<bool> Contacts_DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.DeleteAsync($"/api/contacts/{id}", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        resp.EnsureSuccessStatusCode();
        return true;
    }

    /// <summary>Lists all aliases for a contact.</summary>
    public async Task<IReadOnlyList<AliasNameDto>> Contacts_GetAliasesAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"/api/contacts/{id}/aliases", ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<IReadOnlyList<AliasNameDto>>(cancellationToken: ct) ?? Array.Empty<AliasNameDto>();
    }

    /// <summary>Adds a new alias for a contact.</summary>
    public async Task<bool> Contacts_AddAliasAsync(Guid id, AliasCreateRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync($"/api/contacts/{id}/aliases", request, ct);
        resp.EnsureSuccessStatusCode();
        return true;
    }

    /// <summary>Deletes an alias from a contact.</summary>
    public async Task<bool> Contacts_DeleteAliasAsync(Guid id, Guid aliasId, CancellationToken ct = default)
    {
        var resp = await _http.DeleteAsync($"/api/contacts/{id}/aliases/{aliasId}", ct);
        resp.EnsureSuccessStatusCode();
        return true;
    }

    /// <summary>Merges a contact with another contact.</summary>
    public async Task<ContactDto> Contacts_MergeAsync(Guid sourceId, ContactMergeRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync($"/api/contacts/{sourceId}/merge", request, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<ContactDto>(cancellationToken: ct))!;
    }

    /// <summary>Counts all contacts.</summary>
    public async Task<int> Contacts_CountAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetAsync("/api/contacts/count", ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        if (json.TryGetProperty("count", out var countProp) && countProp.TryGetInt32(out var cnt))
        {
            return cnt;
        }
        return 0;
    }

    /// <summary>Sets the symbol (attachment) for a contact.</summary>
    public async Task<bool> Contacts_SetSymbolAsync(Guid id, Guid attachmentId, CancellationToken ct = default)
    {
        var resp = await _http.PostAsync($"/api/contacts/{id}/symbol/{attachmentId}", content: null, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        resp.EnsureSuccessStatusCode();
        return true;
    }

    /// <summary>Clears the symbol (attachment) from a contact.</summary>
    public async Task<bool> Contacts_ClearSymbolAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.DeleteAsync($"/api/contacts/{id}/symbol", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        resp.EnsureSuccessStatusCode();
        return true;
    }

    #endregion Contacts

    #region Home KPIs
    /// <inheritdoc />
    public async Task<IReadOnlyList<HomeKpiDto>> HomeKpis_ListAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetAsync("/api/home-kpis", ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<IReadOnlyList<HomeKpiDto>>(cancellationToken: ct) ?? Array.Empty<HomeKpiDto>();
    }
    /// <inheritdoc />
    public async Task<HomeKpiDto?> HomeKpis_GetAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"/api/home-kpis/{id}", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<HomeKpiDto>(cancellationToken: ct);
    }
    /// <inheritdoc />
    public async Task<HomeKpiDto> HomeKpis_CreateAsync(HomeKpiCreateRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync("/api/home-kpis", request, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<HomeKpiDto>(cancellationToken: ct))!;
    }
    /// <inheritdoc />
    public async Task<HomeKpiDto?> HomeKpis_UpdateAsync(Guid id, HomeKpiUpdateRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PutAsJsonAsync($"/api/home-kpis/{id}", request, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        if (resp.StatusCode == System.Net.HttpStatusCode.Conflict) throw new InvalidOperationException(await resp.Content.ReadAsStringAsync(ct));
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<HomeKpiDto>(cancellationToken: ct);
    }
    /// <inheritdoc />
    public async Task<bool> HomeKpis_DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.DeleteAsync($"/api/home-kpis/{id}", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        resp.EnsureSuccessStatusCode();
        return true;
    }

    #endregion Home KPIs

    #region Meta Holidays
    /// <inheritdoc />
    public async Task<string[]> Meta_GetHolidayProvidersAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetAsync("/api/meta/holiday-providers", ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<string[]>(cancellationToken: ct) ?? Array.Empty<string>();
    }
    /// <inheritdoc />
    public async Task<string[]> Meta_GetHolidayCountriesAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetAsync("/api/meta/holiday-countries", ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<string[]>(cancellationToken: ct) ?? Array.Empty<string>();
    }
    /// <inheritdoc />
    public async Task<string[]> Meta_GetHolidaySubdivisionsAsync(string provider, string country, CancellationToken ct = default)
    {
        var url = $"/api/meta/holiday-subdivisions?provider={Uri.EscapeDataString(provider ?? string.Empty)}&country={Uri.EscapeDataString(country ?? string.Empty)}";
        var resp = await _http.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<string[]>(cancellationToken: ct) ?? Array.Empty<string>();
    }

    #endregion Meta Holidays

    #region User Settings - Notifications

    /// <summary>
    /// Gets the notification settings for the current user.
    /// </summary>
    public async Task<NotificationSettingsDto?> User_GetNotificationSettingsAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetAsync("/api/user/settings/notifications", ct);
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<NotificationSettingsDto>(cancellationToken: ct);
    }

    /// <summary>
    /// Updates the notification settings for the current user.
    /// </summary>
    public async Task<bool> User_UpdateNotificationSettingsAsync(bool monthlyEnabled, int? hour, int? minute, string? provider, string? country, string? subdivision, CancellationToken ct = default)
    {
        var payload = new
        {
            MonthlyReminderEnabled = monthlyEnabled,
            MonthlyReminderHour = hour,
            MonthlyReminderMinute = minute,
            HolidayProvider = provider,
            HolidayCountryCode = country,
            HolidaySubdivisionCode = subdivision
        };
        var resp = await _http.PutAsJsonAsync("/api/user/settings/notifications", payload, ct);
        return resp.IsSuccessStatusCode;
    }

    #endregion User Settings - Notifications

    #region Notifications

    /// <inheritdoc />
    public async Task<IReadOnlyList<NotificationDto>> Notifications_ListAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetAsync("/api/notifications", ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<IReadOnlyList<NotificationDto>>(cancellationToken: ct) ?? Array.Empty<NotificationDto>();
    }

    /// <inheritdoc />
    public async Task<bool> Notifications_DismissAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.PostAsync($"/api/notifications/{id}/dismiss", content: null, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        resp.EnsureSuccessStatusCode();
        return true;
    }

    #endregion Notifications

    #region Postings

    public async Task<PostingServiceDto?> Postings_GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"/api/postings/{id}", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<PostingServiceDto>(cancellationToken: ct);
    }

    public async Task<IReadOnlyList<PostingServiceDto>> Postings_GetAccountAsync(Guid accountId, int skip = 0, int take = 50, string? q = null, DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        var url = $"/api/postings/account/{accountId}?skip={skip}&take={take}";
        if (!string.IsNullOrWhiteSpace(q)) url += $"&q={Uri.EscapeDataString(q)}";
        if (from.HasValue) url += $"&from={from.Value:O}";
        if (to.HasValue) url += $"&to={to.Value:O}";
        var resp = await _http.GetAsync(url, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return Array.Empty<PostingServiceDto>();
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<IReadOnlyList<PostingServiceDto>>(cancellationToken: ct) ?? Array.Empty<PostingServiceDto>();
    }

    public async Task<IReadOnlyList<PostingServiceDto>> Postings_GetContactAsync(Guid contactId, int skip = 0, int take = 50, string? q = null, DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        var url = $"/api/postings/contact/{contactId}?skip={skip}&take={take}";
        if (!string.IsNullOrWhiteSpace(q)) url += $"&q={Uri.EscapeDataString(q)}";
        if (from.HasValue) url += $"&from={from.Value:O}";
        if (to.HasValue) url += $"&to={to.Value:O}";
        var resp = await _http.GetAsync(url, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return Array.Empty<PostingServiceDto>();
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<IReadOnlyList<PostingServiceDto>>(cancellationToken: ct) ?? Array.Empty<PostingServiceDto>();
    }

    public async Task<IReadOnlyList<PostingServiceDto>> Postings_GetSavingsPlanAsync(Guid planId, int skip = 0, int take = 50, DateTime? from = null, DateTime? to = null, string? q = null, CancellationToken ct = default)
    {
        var url = $"/api/postings/savings-plan/{planId}?skip={skip}&take={take}";
        if (!string.IsNullOrWhiteSpace(q)) url += $"&q={Uri.EscapeDataString(q)}";
        if (from.HasValue) url += $"&from={from.Value:O}";
        if (to.HasValue) url += $"&to={to.Value:O}";
        var resp = await _http.GetAsync(url, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return Array.Empty<PostingServiceDto>();
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<IReadOnlyList<PostingServiceDto>>(cancellationToken: ct) ?? Array.Empty<PostingServiceDto>();
    }

    public async Task<IReadOnlyList<PostingServiceDto>> Postings_GetSecurityAsync(Guid securityId, int skip = 0, int take = 50, DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        var url = $"/api/postings/security/{securityId}?skip={skip}&take={take}";
        if (from.HasValue) url += $"&from={from.Value:O}";
        if (to.HasValue) url += $"&to={to.Value:O}";
        var resp = await _http.GetAsync(url, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return Array.Empty<PostingServiceDto>();
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<IReadOnlyList<PostingServiceDto>>(cancellationToken: ct) ?? Array.Empty<PostingServiceDto>();
    }

    public async Task<GroupLinksDto?> Postings_GetGroupLinksAsync(Guid groupId, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"/api/postings/group/{groupId}", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.BadRequest) return null;
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<GroupLinksDto>(cancellationToken: ct);
    }

    #endregion Postings

    #region Reports

    public async Task<ReportAggregationResult> Reports_QueryAggregatesAsync(ReportAggregatesQueryRequest req, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync("/api/report-aggregates", req, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<ReportAggregationResult>(cancellationToken: ct))!;
    }

    public async Task<IReadOnlyList<ReportFavoriteDto>> Reports_ListFavoritesAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetAsync("/api/report-favorites", ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<IReadOnlyList<ReportFavoriteDto>>(cancellationToken: ct) ?? Array.Empty<ReportFavoriteDto>();
    }

    public async Task<ReportFavoriteDto?> Reports_GetFavoriteAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"/api/report-favorites/{id}", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<ReportFavoriteDto>(cancellationToken: ct);
    }

    public async Task<ReportFavoriteDto> Reports_CreateFavoriteAsync(ReportFavoriteCreateApiRequest req, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync("/api/report-favorites", req, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            var err = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(err);
        }
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<ReportFavoriteDto>(cancellationToken: ct))!;
    }

    public async Task<ReportFavoriteDto?> Reports_UpdateFavoriteAsync(Guid id, ReportFavoriteUpdateApiRequest req, CancellationToken ct = default)
    {
        var resp = await _http.PutAsJsonAsync($"/api/report-favorites/{id}", req, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        if (resp.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            var err = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(err);
        }
        if (resp.StatusCode == System.Net.HttpStatusCode.BadRequest) { throw new ArgumentException(await resp.Content.ReadAsStringAsync(ct)); }
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<ReportFavoriteDto>(cancellationToken: ct);
    }

    public async Task<bool> Reports_DeleteFavoriteAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.DeleteAsync($"/api/report-favorites/{id}", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        resp.EnsureSuccessStatusCode();
        return true;
    }

    #endregion Reports

    #region Savings Plan Categories

    public async Task<IReadOnlyList<SavingsPlanCategoryDto>> SavingsPlanCategories_ListAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetAsync("/api/savings-plan-categories", ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<IReadOnlyList<SavingsPlanCategoryDto>>(cancellationToken: ct) ?? Array.Empty<SavingsPlanCategoryDto>();
    }

    public async Task<SavingsPlanCategoryDto?> SavingsPlanCategories_GetAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"/api/savings-plan-categories/{id}", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<SavingsPlanCategoryDto>(cancellationToken: ct);
    }

    public async Task<SavingsPlanCategoryDto?> SavingsPlanCategories_CreateAsync(SavingsPlanCategoryDto dto, CancellationToken ct = default)
    {
        LastError = null;
        var resp = await _http.PostAsJsonAsync("/api/savings-plan-categories", dto, ct);
        if (!resp.IsSuccessStatusCode)
        {
            LastError = await resp.Content.ReadAsStringAsync(ct);
            return null;
        }
        return await resp.Content.ReadFromJsonAsync<SavingsPlanCategoryDto>(cancellationToken: ct);
    }

    public async Task<SavingsPlanCategoryDto?> SavingsPlanCategories_UpdateAsync(Guid id, SavingsPlanCategoryDto dto, CancellationToken ct = default)
    {
        LastError = null;
        var resp = await _http.PutAsJsonAsync($"/api/savings-plan-categories/{id}", dto, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        if (!resp.IsSuccessStatusCode)
        {
            LastError = await resp.Content.ReadAsStringAsync(ct);
            return null;
        }
        return await resp.Content.ReadFromJsonAsync<SavingsPlanCategoryDto>(cancellationToken: ct);
    }

    public async Task<bool> SavingsPlanCategories_DeleteAsync(Guid id, CancellationToken ct = default)
    {
        LastError = null;
        var resp = await _http.DeleteAsync($"/api/savings-plan-categories/{id}", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
        if (resp.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            LastError = await resp.Content.ReadAsStringAsync(ct);
            return false;
        }
        resp.EnsureSuccessStatusCode();
        return true;
    }

    public async Task<bool> SavingsPlanCategories_SetSymbolAsync(Guid id, Guid attachmentId, CancellationToken ct = default)
    {
        LastError = null;
        var resp = await _http.PostAsync($"/api/savings-plan-categories/{id}/symbol/{attachmentId}", content: null, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        if (resp.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            LastError = await resp.Content.ReadAsStringAsync(ct);
            return false;
        }
        resp.EnsureSuccessStatusCode();
        return true;
    }

    public async Task<bool> SavingsPlanCategories_ClearSymbolAsync(Guid id, CancellationToken ct = default)
    {
        LastError = null;
        var resp = await _http.DeleteAsync($"/api/savings-plan-categories/{id}/symbol", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        if (resp.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            LastError = await resp.Content.ReadAsStringAsync(ct);
            return false;
        }
        resp.EnsureSuccessStatusCode();
        return true;
    }

    #endregion Savings Plan Categories

    #region Savings Plans

    public async Task<IReadOnlyList<SavingsPlanDto>> SavingsPlans_ListAsync(bool onlyActive = true, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"/api/savings-plans?onlyActive={(onlyActive ? "true" : "false")}", ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<IReadOnlyList<SavingsPlanDto>>(cancellationToken: ct) ?? Array.Empty<SavingsPlanDto>();
    }

    public async Task<int> SavingsPlans_CountAsync(bool onlyActive = true, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"/api/savings-plans/count?onlyActive={(onlyActive ? "true" : "false")}", ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        if (json.TryGetProperty("count", out var countProp) && countProp.TryGetInt32(out var cnt))
        {
            return cnt;
        }
        return 0;
    }

    public async Task<SavingsPlanDto?> SavingsPlans_GetAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"/api/savings-plans/{id}", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<SavingsPlanDto>(cancellationToken: ct);
    }

    public async Task<SavingsPlanDto> SavingsPlans_CreateAsync(SavingsPlanCreateRequest req, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync("/api/savings-plans", req, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<SavingsPlanDto>(cancellationToken: ct))!;
    }

    public async Task<SavingsPlanDto?> SavingsPlans_UpdateAsync(Guid id, SavingsPlanCreateRequest req, CancellationToken ct = default)
    {
        var resp = await _http.PutAsJsonAsync($"/api/savings-plans/{id}", req, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<SavingsPlanDto>(cancellationToken: ct);
    }

    public async Task<SavingsPlanAnalysisDto> SavingsPlans_AnalyzeAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"/api/savings-plans/{id}/analysis", ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<SavingsPlanAnalysisDto>(cancellationToken: ct))!;
    }

    public async Task<bool> SavingsPlans_ArchiveAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.PostAsync($"/api/savings-plans/{id}/archive", content: null, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        if (resp.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            LastError = await resp.Content.ReadAsStringAsync(ct);
            return false;
        }
        resp.EnsureSuccessStatusCode();
        return true;
    }

    public async Task<bool> SavingsPlans_DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.DeleteAsync($"/api/savings-plans/{id}", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        if (resp.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            LastError = await resp.Content.ReadAsStringAsync(ct);
            return false;
        }
        resp.EnsureSuccessStatusCode();
        return true;
    }

    public async Task<bool> SavingsPlans_SetSymbolAsync(Guid id, Guid attachmentId, CancellationToken ct = default)
    {
        var resp = await _http.PostAsync($"/api/savings-plans/{id}/symbol/{attachmentId}", content: null, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        if (resp.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            LastError = await resp.Content.ReadAsStringAsync(ct);
            return false;
        }
        resp.EnsureSuccessStatusCode();
        return true;
    }

    public async Task<bool> SavingsPlans_ClearSymbolAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.DeleteAsync($"/api/savings-plans/{id}/symbol", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        if (resp.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            LastError = await resp.Content.ReadAsStringAsync(ct);
            return false;
        }
        resp.EnsureSuccessStatusCode();
        return true;
    }

    #endregion Savings Plans

    #region Securities

    public async Task<IReadOnlyList<SecurityDto>> Securities_ListAsync(bool onlyActive = true, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"/api/securities?onlyActive={(onlyActive ? "true" : "false")}", ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<IReadOnlyList<SecurityDto>>(cancellationToken: ct) ?? Array.Empty<SecurityDto>();
    }

    public async Task<int> Securities_CountAsync(bool onlyActive = true, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"/api/securities/count?onlyActive={(onlyActive ? "true" : "false")}", ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        if (json.TryGetProperty("count", out var countProp) && countProp.TryGetInt32(out var cnt))
        {
            return cnt;
        }
        return 0;
    }

    public async Task<SecurityDto?> Securities_GetAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"/api/securities/{id}", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<SecurityDto>(cancellationToken: ct);
    }

    public async Task<SecurityDto> Securities_CreateAsync(SecurityRequest req, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync("/api/securities", req, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<SecurityDto>(cancellationToken: ct))!;
    }

    public async Task<SecurityDto?> Securities_UpdateAsync(Guid id, SecurityRequest req, CancellationToken ct = default)
    {
        var resp = await _http.PutAsJsonAsync($"/api/securities/{id}", req, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<SecurityDto>(cancellationToken: ct);
    }

    public async Task<bool> Securities_ArchiveAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.PostAsync($"/api/securities/{id}/archive", content: null, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        resp.EnsureSuccessStatusCode();
        return true;
    }

    public async Task<bool> Securities_DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.DeleteAsync($"/api/securities/{id}", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        resp.EnsureSuccessStatusCode();
        return true;
    }

    public async Task<bool> Securities_SetSymbolAsync(Guid id, Guid attachmentId, CancellationToken ct = default)
    {
        var resp = await _http.PostAsync($"/api/securities/{id}/symbol/{attachmentId}", content: null, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        resp.EnsureSuccessStatusCode();
        return true;
    }

    public async Task<bool> Securities_ClearSymbolAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.DeleteAsync($"/api/securities/{id}/symbol", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        resp.EnsureSuccessStatusCode();
        return true;
    }

    public async Task<AttachmentDto> Securities_UploadSymbolAsync(Guid id, Stream fileStream, string fileName, string? contentType = null, Guid? categoryId = null, CancellationToken ct = default)
    {
        using var content = new MultipartFormDataContent();
        var part = new StreamContent(fileStream);
        part.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);
        content.Add(part, "file", fileName);
        if (categoryId.HasValue) content.Add(new StringContent(categoryId.Value.ToString()), "categoryId");
        var resp = await _http.PostAsync($"/api/securities/{id}/symbol", content, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            LastError = await resp.Content.ReadAsStringAsync(ct);
        }
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<AttachmentDto>(cancellationToken: ct))!;
    }

    public async Task<IReadOnlyList<AggregatePointDto>?> Securities_GetAggregatesAsync(Guid securityId, string period = "Month", int take = 36, int? maxYearsBack = null, CancellationToken ct = default)
    {
        var url = $"/api/securities/{securityId}/aggregates?period={Uri.EscapeDataString(period)}&take={take}";
        if (maxYearsBack.HasValue) url += $"&maxYearsBack={maxYearsBack.Value}";
        var resp = await _http.GetAsync(url, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<IReadOnlyList<AggregatePointDto>>(cancellationToken: ct);
    }

    public async Task<IReadOnlyList<SecurityPriceDto>?> Securities_GetPricesAsync(Guid id, int skip = 0, int take = 50, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"/api/securities/{id}/prices?skip={skip}&take={take}", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<IReadOnlyList<SecurityPriceDto>>(cancellationToken: ct);
    }

    public async Task<BackgroundTaskInfo> Securities_EnqueueBackfillAsync(Guid? securityId, DateTime? fromDateUtc, DateTime? toDateUtc, CancellationToken ct = default)
    {
        var payload = new SecurityBackfillRequest(securityId, fromDateUtc, toDateUtc);
        var resp = await _http.PostAsJsonAsync("/api/securities/backfill", payload, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<BackgroundTaskInfo>(cancellationToken: ct))!;
    }

    public async Task<IReadOnlyList<AggregatePointDto>> Securities_GetDividendsAsync(string? period = null, int? take = null, CancellationToken ct = default)
    {
        var url = "/api/securities/dividends";
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(period)) query.Add($"period={Uri.EscapeDataString(period)}");
        if (take.HasValue) query.Add($"take={take.Value}");
        if (query.Count > 0) url += "?" + string.Join("&", query);
        var resp = await _http.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<IReadOnlyList<AggregatePointDto>>(cancellationToken: ct))!;
    }

    #endregion Securities

    #region Security Categories

    public async Task<IReadOnlyList<SecurityCategoryDto>> SecurityCategories_ListAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetAsync("/api/security-categories", ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<IReadOnlyList<SecurityCategoryDto>>(cancellationToken: ct) ?? Array.Empty<SecurityCategoryDto>();
    }

    public async Task<SecurityCategoryDto?> SecurityCategories_GetAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"/api/security-categories/{id}", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<SecurityCategoryDto>(cancellationToken: ct);
    }

    public async Task<SecurityCategoryDto> SecurityCategories_CreateAsync(SecurityCategoryRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync("/api/security-categories", request, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            var msg = await resp.Content.ReadAsStringAsync(ct);
            LastError = string.IsNullOrWhiteSpace(msg) ? "Bad Request" : msg;
            return null;
        }
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<SecurityCategoryDto>(cancellationToken: ct))!;
    }

    public async Task<SecurityCategoryDto?> SecurityCategories_UpdateAsync(Guid id, SecurityCategoryRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PutAsJsonAsync($"/api/security-categories/{id}", request, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            LastError = "Err_NotFound";
            return null;
        }
        if (resp.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            var msg = await resp.Content.ReadAsStringAsync(ct);
            LastError = string.IsNullOrWhiteSpace(msg) ? "Bad Request" : msg;
            return null;
        }
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<SecurityCategoryDto>(cancellationToken: ct);
    }

    public async Task<bool> SecurityCategories_DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.DeleteAsync($"/api/security-categories/{id}", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            LastError = "Err_NotFound";
            return false;
        }
        if (resp.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            var msg = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(msg) ? "Bad Request" : msg);
        }
        resp.EnsureSuccessStatusCode();
        return true;
    }

    public async Task<bool> SecurityCategories_SetSymbolAsync(Guid id, Guid attachmentId, CancellationToken ct = default)
    {
        var resp = await _http.PostAsync($"/api/security-categories/{id}/symbol/{attachmentId}", content: null, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        resp.EnsureSuccessStatusCode();
        return true;
    }

    public async Task<bool> SecurityCategories_ClearSymbolAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.DeleteAsync($"/api/security-categories/{id}/symbol", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        resp.EnsureSuccessStatusCode();
        return true;
    }

    #endregion Security Categories
}
