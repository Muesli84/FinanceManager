using System.Net.Http.Json;


namespace FinanceManager.Shared;

/// <summary>
/// Typed API client for the FinanceManager backend. Encapsulates HTTP calls to controller endpoints
/// and returns strongly-typed request/response records from the FinanceManager.Shared project.
///
/// Configure an <see cref="HttpClient"/> with the API base address and pass it to the constructor.
/// </summary>
public sealed class ApiClient : IApiClient
{
    private readonly HttpClient _http;

    /// <summary>
    /// Creates a new instance of the API client.
    /// </summary>
    /// <param name="http">Pre-configured <see cref="HttpClient"/> pointing at the FinanceManager API base address.</param>
    public ApiClient(HttpClient http) => _http = http ?? throw new ArgumentNullException(nameof(http));

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
}
