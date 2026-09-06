using System.Net.Http.Json;
using System.Text.Json;

namespace FinanceManager.Shared;

public partial class ApiClient
{
    #region Contacts

    /// <summary>
    /// Lists contacts with optional paging and filtering.
    /// </summary>
    /// <param name="skip">The skip.</param>
    /// <param name="take">The take.</param>
    /// <param name="type">The type.</param>
    /// <param name="all">The all.</param>
    /// <param name="nameFilter">The name filter.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<IReadOnlyList<ContactDto>> Contacts_ListAsync(int skip = 0, int take = 50, ContactType? type = null, bool all = false, string? nameFilter = null, CancellationToken ct = default)
    {
        var url = $"/api/contacts?skip={skip}&take={take}";
        if (type.HasValue) url += $"&type={type.Value}";
        if (all) url += "&all=true";
        if (!string.IsNullOrWhiteSpace(nameFilter)) url += $"&q={Uri.EscapeDataString(nameFilter)}";
        var resp = await _http.GetAsync(url, ct);
        await EnsureSuccessOrSetErrorAsync(resp);
        return await resp.Content.ReadFromJsonAsync<IReadOnlyList<ContactDto>>(cancellationToken: ct) ?? Array.Empty<ContactDto>();
    }

    /// <summary>
    /// Gets a single contact by id or null when not found.
    /// </summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<ContactDto?> Contacts_GetAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"/api/contacts/{id}", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        await EnsureSuccessOrSetErrorAsync(resp);
        return await resp.Content.ReadFromJsonAsync<ContactDto>(cancellationToken: ct);
    }

    /// <summary>
    /// Creates a new contact.
    /// </summary>
    /// <param name="request">Request payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<ContactDto> Contacts_CreateAsync(ContactCreateRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync("/api/contacts", request, ct);
        await EnsureSuccessOrSetErrorAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<ContactDto>(cancellationToken: ct))!;
    }

    /// <summary>
    /// Updates an existing contact. Returns null when not found.
    /// </summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="request">Request payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<ContactDto?> Contacts_UpdateAsync(Guid id, ContactUpdateRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PutAsJsonAsync($"/api/contacts/{id}", request, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        await EnsureSuccessOrSetErrorAsync(resp);
        return await resp.Content.ReadFromJsonAsync<ContactDto>(cancellationToken: ct);
    }

    /// <summary>
    /// Deletes a contact. Returns false when not found.
    /// </summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<bool> Contacts_DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.DeleteAsync($"/api/contacts/{id}", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        await EnsureSuccessOrSetErrorAsync(resp);
        return true;
    }

    /// <summary>
    /// Lists alias patterns for a contact.
    /// </summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<IReadOnlyList<AliasNameDto>> Contacts_GetAliasesAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"/api/contacts/{id}/aliases", ct);
        await EnsureSuccessOrSetErrorAsync(resp);
        return await resp.Content.ReadFromJsonAsync<IReadOnlyList<AliasNameDto>>(cancellationToken: ct) ?? Array.Empty<AliasNameDto>();
    }

    /// <summary>
    /// Adds a new alias pattern to a contact.
    /// </summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="request">Request payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<bool> Contacts_AddAliasAsync(Guid id, AliasCreateRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync($"/api/contacts/{id}/aliases", request, ct);
        await EnsureSuccessOrSetErrorAsync(resp);
        return true;
    }

    /// <summary>
    /// Deletes an alias from a contact.
    /// </summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="aliasId">The alias id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<bool> Contacts_DeleteAliasAsync(Guid id, Guid aliasId, CancellationToken ct = default)
    {
        var resp = await _http.DeleteAsync($"/api/contacts/{id}/aliases/{aliasId}", ct);
        await EnsureSuccessOrSetErrorAsync(resp);
        return true;
    }

    /// <summary>
    /// Merges a source contact into a target and returns the updated target contact.
    /// </summary>
    /// <param name="sourceId">The source id.</param>
    /// <param name="request">Request payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<ContactDto> Contacts_MergeAsync(Guid sourceId, ContactMergeRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync($"/api/contacts/{sourceId}/merge", request, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<ContactDto>(cancellationToken: ct))!;
    }

    /// <summary>
    /// Returns the total number of contacts for the current user.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
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

    /// <summary>
    /// Assigns a symbol attachment to a contact. Returns false when not found.
    /// </summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="attachmentId">The attachment id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<bool> Contacts_SetSymbolAsync(Guid id, Guid attachmentId, CancellationToken ct = default)
    {
        var resp = await _http.PostAsync($"/api/contacts/{id}/symbol/{attachmentId}", content: null, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        resp.EnsureSuccessStatusCode();
        return true;
    }

    /// <summary>
    /// Clears the symbol attachment from a contact. Returns false when not found.
    /// </summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<bool> Contacts_ClearSymbolAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.DeleteAsync($"/api/contacts/{id}/symbol", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        resp.EnsureSuccessStatusCode();
        return true;
    }

    #endregion Contacts
}
