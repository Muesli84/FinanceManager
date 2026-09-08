using System.Net.Http.Json;

namespace FinanceManager.Shared;

public partial class ApiClient
{
    #region Contact Categories

    /// <summary>
    /// Lists contact categories available to the current user.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of contact categories.</returns>
    public async Task<IReadOnlyList<ContactCategoryDto>> ContactCategories_ListAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetAsync("/api/contact-categories", ct);
        await EnsureSuccessOrSetErrorAsync(resp);
        return await resp.Content.ReadFromJsonAsync<IReadOnlyList<ContactCategoryDto>>(cancellationToken: ct) ?? Array.Empty<ContactCategoryDto>();
    }

    /// <summary>
    /// Gets a single contact category by id or null when not found.
    /// </summary>
    /// <param name="id">Contact category id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Contact category or null when not found.</returns>
    public async Task<ContactCategoryDto?> ContactCategories_GetAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"/api/contact-categories/{id}", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        await EnsureSuccessOrSetErrorAsync(resp);
        return await resp.Content.ReadFromJsonAsync<ContactCategoryDto>(cancellationToken: ct);
    }

    /// <summary>
    /// Creates a new contact category.
    /// </summary>
    /// <param name="request">Create request containing category details.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created contact category.</returns>
    public async Task<ContactCategoryDto> ContactCategories_CreateAsync(ContactCategoryCreateRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync("/api/contact-categories", request, ct);
        await EnsureSuccessOrSetErrorAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<ContactCategoryDto>(cancellationToken: ct))!;
    }

    /// <summary>
    /// Updates an existing contact category. Returns false when not found.
    /// </summary>
    /// <param name="id">Contact category id.</param>
    /// <param name="request">Update request with new values.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True when update succeeded, false when not found.</returns>
    public async Task<bool> ContactCategories_UpdateAsync(Guid id, ContactCategoryUpdateRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PutAsJsonAsync($"/api/contact-categories/{id}", request, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        await EnsureSuccessOrSetErrorAsync(resp);
        return true;
    }

    /// <summary>
    /// Deletes a contact category. Returns false when not found.
    /// </summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<bool> ContactCategories_DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.DeleteAsync($"/api/contact-categories/{id}", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        await EnsureSuccessOrSetErrorAsync(resp);
        return true;
    }

    /// <summary>
    /// Assigns a symbol attachment to a contact category. Returns false when not found.
    /// </summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="attachmentId">The attachment id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<bool> ContactCategories_SetSymbolAsync(Guid id, Guid attachmentId, CancellationToken ct = default)
    {
        var resp = await _http.PostAsync($"/api/contact-categories/{id}/symbol/{attachmentId}", content: null, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        resp.EnsureSuccessStatusCode();
        return true;
    }

    /// <summary>
    /// Clears the symbol attachment from a contact category. Returns false when not found.
    /// </summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<bool> ContactCategories_ClearSymbolAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.DeleteAsync($"/api/contact-categories/{id}/symbol", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        resp.EnsureSuccessStatusCode();
        return true;
    }

    #endregion Contact Categories
}