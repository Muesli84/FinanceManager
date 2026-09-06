using System.Net.Http.Json;

namespace FinanceManager.Shared;

public partial class ApiClient
{

    #region Security Categories

    /// <summary>
    /// Lists security categories for the current user.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<IReadOnlyList<SecurityCategoryDto>> SecurityCategories_ListAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetAsync("/api/security-categories", ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<IReadOnlyList<SecurityCategoryDto>>(cancellationToken: ct) ?? Array.Empty<SecurityCategoryDto>();
    }

    /// <summary>
    /// Gets a single security category by id or null if not found.
    /// </summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<SecurityCategoryDto?> SecurityCategories_GetAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"/api/security-categories/{id}", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<SecurityCategoryDto>(cancellationToken: ct);
    }

    /// <summary>
    /// Creates a new security category. Sets LastError on bad request and returns null in that case.
    /// </summary>
    /// <param name="request">Request payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<SecurityCategoryDto?> SecurityCategories_CreateAsync(SecurityCategoryRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync("/api/security-categories", request, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            var msg = await resp.Content.ReadAsStringAsync(ct);
            LastError = string.IsNullOrWhiteSpace(msg) ? "Bad Request" : msg;
            return null;
        }
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<SecurityCategoryDto>(cancellationToken: ct);
    }

    /// <summary>
    /// Updates a security category. Returns null when not found or when request is invalid.
    /// </summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="request">Request payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
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

    /// <summary>
    /// Deletes a security category. Returns false when not found. Throws on bad request with message.
    /// </summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
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

    /// <summary>
    /// Assigns a symbol attachment to a security category. Returns false when not found.
    /// </summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="attachmentId">The attachment id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<bool> SecurityCategories_SetSymbolAsync(Guid id, Guid attachmentId, CancellationToken ct = default)
    {
        var resp = await _http.PostAsync($"/api/security-categories/{id}/symbol/{attachmentId}", content: null, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        resp.EnsureSuccessStatusCode();
        return true;
    }

    /// <summary>
    /// Clears the symbol attachment from a security category. Returns false when not found.
    /// </summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<bool> SecurityCategories_ClearSymbolAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.DeleteAsync($"/api/security-categories/{id}/symbol", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        resp.EnsureSuccessStatusCode();
        return true;
    }

    #endregion Security Categories
}