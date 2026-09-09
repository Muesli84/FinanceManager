using System.Net.Http.Json;

namespace FinanceManager.Shared;

public partial class ApiClient
{
    #region Admin - Users

    /// <summary>Lists users (admin only).</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<IReadOnlyList<UserAdminDto>> Admin_ListUsersAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetAsync("/api/admin/users", ct);
        await EnsureSuccessOrSetErrorAsync(resp);
        return await resp.Content.ReadFromJsonAsync<IReadOnlyList<UserAdminDto>>(cancellationToken: ct) ?? Array.Empty<UserAdminDto>();
    }

    /// <summary>Gets a user (admin only) or null if not found.</summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<UserAdminDto?> Admin_GetUserAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"/api/admin/users/{id}", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        await EnsureSuccessOrSetErrorAsync(resp);
        return await resp.Content.ReadFromJsonAsync<UserAdminDto>(cancellationToken: ct);
    }

    /// <summary>Creates a new user (admin only).</summary>
    /// <param name="request">Request payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<UserAdminDto> Admin_CreateUserAsync(CreateUserRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync("/api/admin/users", request, ct);
        await EnsureSuccessOrSetErrorAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<UserAdminDto>(cancellationToken: ct))!;
    }

    /// <summary>Updates a user (admin only). Returns null when not found.</summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="request">Request payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<UserAdminDto?> Admin_UpdateUserAsync(Guid id, UpdateUserRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PutAsJsonAsync($"/api/admin/users/{id}", request, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        await EnsureSuccessOrSetErrorAsync(resp);
        return await resp.Content.ReadFromJsonAsync<UserAdminDto>(cancellationToken: ct);
    }

    /// <summary>Resets a user's password (admin only). Returns false when not found.</summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="request">Request payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<bool> Admin_ResetPasswordAsync(Guid id, ResetPasswordRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync($"/api/admin/users/{id}/reset-password", request, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        await EnsureSuccessOrSetErrorAsync(resp);
        return true;
    }

    /// <summary>Unlocks a user (admin only). Returns false when not found.</summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<bool> Admin_UnlockUserAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.PostAsync($"/api/admin/users/{id}/unlock", content: null, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        await EnsureSuccessOrSetErrorAsync(resp);
        return true;
    }

    /// <summary>Deletes a user (admin only). Returns false when not found.</summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<bool> Admin_DeleteUserAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.DeleteAsync($"/api/admin/users/{id}", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        await EnsureSuccessOrSetErrorAsync(resp);
        return true;
    }

    #endregion Admin - Users

    #region Admin - IP Blocks

    /// <summary>Lists IP block entries with optional filter.</summary>
    /// <param name="onlyBlocked">The only blocked.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<IReadOnlyList<IpBlockDto>> Admin_ListIpBlocksAsync(bool? onlyBlocked = null, CancellationToken ct = default)
    {
        var url = "/api/admin/ip-blocks";
        if (onlyBlocked.HasValue)
        {
            url += onlyBlocked.Value ? "?onlyBlocked=true" : "?onlyBlocked=false";
        }
        var resp = await _http.GetAsync(url, ct);
        await EnsureSuccessOrSetErrorAsync(resp);
        return await resp.Content.ReadFromJsonAsync<IReadOnlyList<IpBlockDto>>(cancellationToken: ct) ?? Array.Empty<IpBlockDto>();
    }

    /// <summary>Creates a new IP block entry.</summary>
    /// <param name="request">Request payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<IpBlockDto> Admin_CreateIpBlockAsync(IpBlockCreateRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync("/api/admin/ip-blocks", request, ct);
        await EnsureSuccessOrSetErrorAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<IpBlockDto>(cancellationToken: ct))!;
    }

    /// <summary>Gets a single IP block entry or null if not found.</summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<IpBlockDto?> Admin_GetIpBlockAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"/api/admin/ip-blocks/{id}", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        await EnsureSuccessOrSetErrorAsync(resp);
        return await resp.Content.ReadFromJsonAsync<IpBlockDto>(cancellationToken: ct);
    }

    /// <summary>Updates an IP block entry. Returns null when not found.</summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="request">Request payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<IpBlockDto?> Admin_UpdateIpBlockAsync(Guid id, IpBlockUpdateRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PutAsJsonAsync($"/api/admin/ip-blocks/{id}", request, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        await EnsureSuccessOrSetErrorAsync(resp);
        return await resp.Content.ReadFromJsonAsync<IpBlockDto>(cancellationToken: ct);
    }

    /// <summary>Blocks an IP. Returns false when not found.</summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="reason">The reason.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<bool> Admin_BlockIpAsync(Guid id, string? reason, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync($"/api/admin/ip-blocks/{id}/block", new IpBlockUpdateRequest(reason, null), ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        await EnsureSuccessOrSetErrorAsync(resp);
        return true;
    }

    /// <summary>Unblocks an IP. Returns false when not found.</summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<bool> Admin_UnblockIpAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.PostAsync($"/api/admin/ip-blocks/{id}/unblock", content: null, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        await EnsureSuccessOrSetErrorAsync(resp);
        return true;
    }

    /// <summary>Resets counters for an IP block entry. Returns false when not found.</summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<bool> Admin_ResetCountersAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.PostAsync($"/api/admin/ip-blocks/{id}/reset-counters", content: null, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        await EnsureSuccessOrSetErrorAsync(resp);
        return true;
    }

    /// <summary>Deletes an IP block entry. Returns false when not found.</summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<bool> Admin_DeleteIpBlockAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.DeleteAsync($"/api/admin/ip-blocks/{id}", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        await EnsureSuccessOrSetErrorAsync(resp);
        return true;
    }

    #endregion Admin - IP Blocks
}