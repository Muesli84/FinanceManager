using FinanceManager.Shared.Dtos.Budget;
using System.Net;
using System.Net.Http.Json;

namespace FinanceManager.Shared;

public partial class ApiClient
{
    #region Budget Categories

    /// <summary>
    /// Lists budget categories for the current user.
    /// </summary>
    /// <param name="from">The from.</param>
    /// <param name="to">The to.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<IReadOnlyList<BudgetCategoryOverviewDto>> Budgets_ListCategoriesAsync(DateOnly? from = null, DateOnly? to = null, CancellationToken ct = default)
    {
        var parts = new List<string>();
        if (from.HasValue)
        {
            parts.Add($"from={Uri.EscapeDataString(from.Value.ToString("yyyy-MM-dd"))}");
        }
        if (to.HasValue)
        {
            parts.Add($"to={Uri.EscapeDataString(to.Value.ToString("yyyy-MM-dd"))}");
        }

        var qs = parts.Count > 0 ? ("?" + string.Join('&', parts)) : string.Empty;
        var resp = await _http.GetAsync($"/api/budget/categories{qs}", ct);
        await EnsureSuccessOrSetErrorAsync(resp);
        return await resp.Content.ReadFromJsonAsync<IReadOnlyList<BudgetCategoryOverviewDto>>(cancellationToken: ct) ?? Array.Empty<BudgetCategoryOverviewDto>();
    }

    /// <summary>
    /// Gets a budget category by id or null when not found.
    /// </summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<BudgetCategoryDto?> Budgets_GetCategoryAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"/api/budget/categories/{id}", ct);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureSuccessOrSetErrorAsync(resp);
        return await resp.Content.ReadFromJsonAsync<BudgetCategoryDto>(cancellationToken: ct);
    }

    /// <summary>
    /// Creates a budget category.
    /// </summary>
    /// <param name="request">Request payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<BudgetCategoryDto> Budgets_CreateCategoryAsync(BudgetCategoryCreateRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync("/api/budget/categories", request, ct);
        await EnsureSuccessOrSetErrorAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<BudgetCategoryDto>(cancellationToken: ct))!;
    }

    /// <summary>
    /// Updates a budget category. Returns null when not found.
    /// </summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="request">Request payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<BudgetCategoryDto?> Budgets_UpdateCategoryAsync(Guid id, BudgetCategoryUpdateRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PutAsJsonAsync($"/api/budget/categories/{id}", request, ct);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureSuccessOrSetErrorAsync(resp);
        return await Budgets_GetCategoryAsync(id, ct);
    }

    /// <summary>
    /// Deletes a budget category. Returns false when not found.
    /// </summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<bool> Budgets_DeleteCategoryAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.DeleteAsync($"/api/budget/categories/{id}", ct);
        if (resp.StatusCode == HttpStatusCode.NotFound) return false;
        await EnsureSuccessOrSetErrorAsync(resp);
        return true;
    }

    #endregion Budget Categories
}
