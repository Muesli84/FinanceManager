using System.Net.Http.Json;

namespace FinanceManager.Shared;

public partial class ApiClient
{
    #region Accounts

    /// <summary>
    /// Lists accounts for the current user with optional pagination and bank contact filter.
    /// </summary>
    public async Task<IReadOnlyList<AccountDto>> GetAccountsAsync(int skip = 0, int take = 100, Guid? bankContactId = null, string? q = null, CancellationToken ct = default)
    {
        var query = new List<string>
        {
            $"skip={skip}",
            $"take={take}"
        };
        if (bankContactId.HasValue) query.Add($"bankContactId={Uri.EscapeDataString(bankContactId.Value.ToString())}");
        if (!string.IsNullOrWhiteSpace(q)) query.Add($"q={Uri.EscapeDataString(q.Trim())}");
        var url = $"/api/accounts?{string.Join('&', query)}";
        var resp = await _http.GetAsync(url, ct);
        await EnsureSuccessOrSetErrorAsync(resp);
        return await resp.Content.ReadFromJsonAsync<IReadOnlyList<AccountDto>>(cancellationToken: ct) ?? Array.Empty<AccountDto>();
    }

    /// <summary>
    /// Gets account statistics for the current user and optional account search text.
    /// </summary>
    public async Task<AccountStatisticsDto> GetAccountStatisticsAsync(string? q = null, CancellationToken ct = default)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(q)) query.Add($"q={Uri.EscapeDataString(q.Trim())}");
        var url = query.Count == 0 ? "/api/accounts/statistics" : $"/api/accounts/statistics?{string.Join('&', query)}";
        var resp = await _http.GetAsync(url, ct);
        await EnsureSuccessOrSetErrorAsync(resp);
        return await resp.Content.ReadFromJsonAsync<AccountStatisticsDto>(cancellationToken: ct) ?? AccountStatisticsDto.Empty;
    }

    /// <summary>
    /// Gets a single account by id or null when not found.
    /// </summary>
    public async Task<AccountDto?> GetAccountAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"/api/accounts/{id}", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        await EnsureSuccessOrSetErrorAsync(resp);
        return await resp.Content.ReadFromJsonAsync<AccountDto>(cancellationToken: ct);
    }

    /// <summary>
    /// Creates a new account.
    /// </summary>
    public async Task<AccountDto> CreateAccountAsync(AccountCreateRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync("/api/accounts", request, ct);
        await EnsureSuccessOrSetErrorAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<AccountDto>(cancellationToken: ct))!;
    }

    /// <summary>
    /// Updates an existing account. Returns null when not found.
    /// </summary>
    public async Task<AccountDto?> UpdateAccountAsync(Guid id, AccountUpdateRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PutAsJsonAsync($"/api/accounts/{id}", request, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        await EnsureSuccessOrSetErrorAsync(resp);
        return await resp.Content.ReadFromJsonAsync<AccountDto>(cancellationToken: ct);
    }

    /// <summary>
    /// Deletes an account. Returns false when not found.
    /// </summary>
    public async Task<bool> DeleteAccountAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.DeleteAsync($"/api/accounts/{id}", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        await EnsureSuccessOrSetErrorAsync(resp);
        return true;
    }

    /// <summary>
    /// Assigns a symbol attachment to an account.
    /// </summary>
    public async Task SetAccountSymbolAsync(Guid id, Guid attachmentId, CancellationToken ct = default)
    {
        var resp = await _http.PostAsync($"/api/accounts/{id}/symbol/{attachmentId}", content: null, ct);
        await EnsureSuccessOrSetErrorAsync(resp);
    }

    /// <summary>
    /// Clears the symbol attachment from an account.
    /// </summary>
    public async Task ClearAccountSymbolAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.DeleteAsync($"/api/accounts/{id}/symbol", ct);
        await EnsureSuccessOrSetErrorAsync(resp);
    }

    /// <summary>
    /// Returns the list of linked sub-IBANs for a collection account.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetLinkedIbansAsync(Guid accountId, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"/api/accounts/{accountId}/linked-ibans", ct);
        await EnsureSuccessOrSetErrorAsync(resp);
        return await resp.Content.ReadFromJsonAsync<IReadOnlyList<string>>(cancellationToken: ct) ?? Array.Empty<string>();
    }

    /// <summary>
    /// Adds a linked sub-IBAN to a collection account.
    /// </summary>
    public async Task AddLinkedIbanAsync(Guid accountId, AccountLinkedIbanUpsertRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync($"/api/accounts/{accountId}/linked-ibans", request, ct);
        await EnsureSuccessOrSetErrorAsync(resp);
    }

    /// <summary>
    /// Removes a linked sub-IBAN from a collection account.
    /// </summary>
    public async Task RemoveLinkedIbanAsync(Guid accountId, string iban, CancellationToken ct = default)
    {
        var resp = await _http.DeleteAsync($"/api/accounts/{accountId}/linked-ibans/{Uri.EscapeDataString(iban)}", ct);
        await EnsureSuccessOrSetErrorAsync(resp);
    }

    #endregion Accounts
}
