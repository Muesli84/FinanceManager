using System.Net.Http.Json;

namespace FinanceManager.Shared;

public partial class ApiClient
{
    #region Postings

    /// <summary>
    /// Gets a single posting by id or null if not found or not accessible.
    /// </summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<PostingServiceDto?> Postings_GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"/api/postings/{id}", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        await EnsureSuccessOrSetErrorAsync(resp);
        return await resp.Content.ReadFromJsonAsync<PostingServiceDto>(cancellationToken: ct);
    }

    /// <summary>
    /// Lists postings for a specific account with optional paging and filters.
    /// </summary>
    /// <param name="accountId">The account id.</param>
    /// <param name="skip">The skip.</param>
    /// <param name="take">The take.</param>
    /// <param name="q">The q.</param>
    /// <param name="from">The from.</param>
    /// <param name="to">The to.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<IReadOnlyList<PostingServiceDto>> Postings_GetAccountAsync(Guid accountId, int skip = 0, int take = 50, string? q = null, DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        var url = $"/api/postings/account/{accountId}?skip={skip}&take={take}";
        if (!string.IsNullOrWhiteSpace(q)) url += $"&q={Uri.EscapeDataString(q)}";
        if (from.HasValue) url += $"&from={from.Value:O}";
        if (to.HasValue) url += $"&to={to.Value:O}";
        var resp = await _http.GetAsync(url, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return Array.Empty<PostingServiceDto>();
        await EnsureSuccessOrSetErrorAsync(resp);
        return await resp.Content.ReadFromJsonAsync<IReadOnlyList<PostingServiceDto>>(cancellationToken: ct) ?? Array.Empty<PostingServiceDto>();
    }

    /// <summary>
    /// Lists postings for a specific contact with optional paging and filters.
    /// </summary>
    /// <param name="contactId">The contact id.</param>
    /// <param name="skip">The skip.</param>
    /// <param name="take">The take.</param>
    /// <param name="q">The q.</param>
    /// <param name="from">The from.</param>
    /// <param name="to">The to.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<IReadOnlyList<PostingServiceDto>> Postings_GetContactAsync(Guid contactId, int skip = 0, int take = 50, string? q = null, DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        var url = $"/api/postings/contact/{contactId}?skip={skip}&take={take}";
        if (!string.IsNullOrWhiteSpace(q)) url += $"&q={Uri.EscapeDataString(q)}";
        if (from.HasValue) url += $"&from={from.Value:O}";
        if (to.HasValue) url += $"&to={to.Value:O}";
        var resp = await _http.GetAsync(url, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return Array.Empty<PostingServiceDto>();
        await EnsureSuccessOrSetErrorAsync(resp);
        return await resp.Content.ReadFromJsonAsync<IReadOnlyList<PostingServiceDto>>(cancellationToken: ct) ?? Array.Empty<PostingServiceDto>();
    }

    /// <summary>
    /// Lists postings for a savings plan with optional paging and filters.
    /// </summary>
    /// <param name="planId">The plan id.</param>
    /// <param name="skip">The skip.</param>
    /// <param name="take">The take.</param>
    /// <param name="from">The from.</param>
    /// <param name="to">The to.</param>
    /// <param name="q">The q.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<IReadOnlyList<PostingServiceDto>> Postings_GetSavingsPlanAsync(Guid planId, int skip = 0, int take = 50, DateTime? from = null, DateTime? to = null, string? q = null, CancellationToken ct = default)
    {
        var url = $"/api/postings/savings-plan/{planId}?skip={skip}&take={take}";
        if (!string.IsNullOrWhiteSpace(q)) url += $"&q={Uri.EscapeDataString(q)}";
        if (from.HasValue) url += $"&from={from.Value:O}";
        if (to.HasValue) url += $"&to={to.Value:O}";
        var resp = await _http.GetAsync(url, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return Array.Empty<PostingServiceDto>();
        await EnsureSuccessOrSetErrorAsync(resp);
        return await resp.Content.ReadFromJsonAsync<IReadOnlyList<PostingServiceDto>>(cancellationToken: ct) ?? Array.Empty<PostingServiceDto>();
    }

    /// <summary>
    /// Lists postings for a security with optional paging and date range.
    /// </summary>
    /// <param name="securityId">The security id.</param>
    /// <param name="skip">The skip.</param>
    /// <param name="take">The take.</param>
    /// <param name="from">The from.</param>
    /// <param name="to">The to.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<IReadOnlyList<PostingServiceDto>> Postings_GetSecurityAsync(Guid securityId, int skip = 0, int take = 50, DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        var url = $"/api/postings/security/{securityId}?skip={skip}&take={take}";
        if (from.HasValue) url += $"&from={from.Value:O}";
        if (to.HasValue) url += $"&to={to.Value:O}";
        var resp = await _http.GetAsync(url, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return Array.Empty<PostingServiceDto>();
        await EnsureSuccessOrSetErrorAsync(resp);
        return await resp.Content.ReadFromJsonAsync<IReadOnlyList<PostingServiceDto>>(cancellationToken: ct) ?? Array.Empty<PostingServiceDto>();
    }

    /// <summary>
    /// Gets the first entity links for a posting group or null when not found.
    /// </summary>
    /// <param name="groupId">The group id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<GroupLinksDto?> Postings_GetGroupLinksAsync(Guid groupId, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"/api/postings/group/{groupId}", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.BadRequest) return null;
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<GroupLinksDto>(cancellationToken: ct);
    }

    /// <summary>
    /// Reverses a posting by creating a counter-posting with negated amount.
    /// </summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<ReversalResultDto?> Postings_ReverseAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.PostAsync($"/api/postings/{id}/reverse", null, ct);
        if (!resp.IsSuccessStatusCode)
        {
            try { await EnsureSuccessOrSetErrorAsync(resp); } catch { }
            return null;
        }
        return await resp.Content.ReadFromJsonAsync<ReversalResultDto>(cancellationToken: ct);
    }

    /// <summary>
    /// Validates whether a posting can be reversed.
    /// </summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<ReversalValidationDto?> Postings_ValidateReversalAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"/api/postings/{id}/validate-reversal", ct);
        await EnsureSuccessOrSetErrorAsync(resp);
        return await resp.Content.ReadFromJsonAsync<ReversalValidationDto>(cancellationToken: ct);
    }

    #endregion Postings
}
