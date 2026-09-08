using FinanceManager.Shared.Dtos.Budget;
using System.Net;
using System.Net.Http.Json;

namespace FinanceManager.Shared;

public partial class ApiClient
{
    #region Budgets

    /// <summary>
    /// Lists budget purposes for the current user.
    /// When <paramref name="from"/> and <paramref name="to"/> are provided, the server returns an overview that
    /// includes rule count and computed budget sum for the given period.
    /// </summary>
    /// <param name="skip">The skip.</param>
    /// <param name="take">The take.</param>
    /// <param name="sourceType">The source type.</param>
    /// <param name="q">The q.</param>
    /// <param name="from">The from.</param>
    /// <param name="to">The to.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<IReadOnlyList<BudgetPurposeOverviewDto>> Budgets_ListPurposesAsync(
        int skip = 0,
        int take = 200,
        BudgetSourceType? sourceType = null,
        string? q = null,
        DateOnly? from = null,
        DateOnly? to = null,
        CancellationToken ct = default)
    {
        var parts = new List<string> { $"skip={skip}", $"take={take}" };
        if (sourceType.HasValue)
        {
            parts.Add($"sourceType={(int)sourceType.Value}");
        }
        if (!string.IsNullOrWhiteSpace(q))
        {
            parts.Add($"q={Uri.EscapeDataString(q)}");
        }
        if (from.HasValue)
        {
            parts.Add($"from={Uri.EscapeDataString(from.Value.ToString("yyyy-MM-dd"))}");
        }
        if (to.HasValue)
        {
            parts.Add($"to={Uri.EscapeDataString(to.Value.ToString("yyyy-MM-dd"))}");
        }

        var qs = parts.Count > 0 ? ("?" + string.Join('&', parts)) : string.Empty;
        var resp = await _http.GetAsync($"/api/budget/purposes{qs}", ct);
        await EnsureSuccessOrSetErrorAsync(resp);
        return await resp.Content.ReadFromJsonAsync<IReadOnlyList<BudgetPurposeOverviewDto>>(cancellationToken: ct) ?? Array.Empty<BudgetPurposeOverviewDto>();
    }

    /// <summary>
    /// Gets a budget purpose by id or null when not found.
    /// </summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<BudgetPurposeDto?> Budgets_GetPurposeAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"/api/budget/purposes/{id}", ct);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureSuccessOrSetErrorAsync(resp);
        return await resp.Content.ReadFromJsonAsync<BudgetPurposeDto>(cancellationToken: ct);
    }

    /// <summary>
    /// Creates a budget purpose.
    /// </summary>
    /// <param name="request">Request payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<BudgetPurposeDto> Budgets_CreatePurposeAsync(BudgetPurposeCreateRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync("/api/budget/purposes", request, ct);
        await EnsureSuccessOrSetErrorAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<BudgetPurposeDto>(cancellationToken: ct))!;
    }

    /// <summary>
    /// Updates a budget purpose. Returns null when not found.
    /// </summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="request">Request payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<BudgetPurposeDto?> Budgets_UpdatePurposeAsync(Guid id, BudgetPurposeUpdateRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PutAsJsonAsync($"/api/budget/purposes/{id}", request, ct);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureSuccessOrSetErrorAsync(resp);
        return await Budgets_GetPurposeAsync(id, ct);
    }

    /// <summary>
    /// Deletes a budget purpose. Returns false when not found.
    /// </summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<bool> Budgets_DeletePurposeAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.DeleteAsync($"/api/budget/purposes/{id}", ct);
        if (resp.StatusCode == HttpStatusCode.NotFound) return false;
        await EnsureSuccessOrSetErrorAsync(resp);
        return true;
    }

    /// <summary>
    /// Lists rules for a budget purpose.
    /// </summary>
    /// <param name="budgetPurposeId">The budget purpose id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<IReadOnlyList<BudgetRuleDto>> Budgets_ListRulesByPurposeAsync(Guid budgetPurposeId, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"/api/budget/rules/by-purpose/{budgetPurposeId}", ct);
        await EnsureSuccessOrSetErrorAsync(resp);
        return await resp.Content.ReadFromJsonAsync<IReadOnlyList<BudgetRuleDto>>(cancellationToken: ct) ?? Array.Empty<BudgetRuleDto>();
    }

    /// <summary>
    /// Creates a budget rule.
    /// </summary>
    /// <param name="request">Request payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<BudgetRuleDto> Budgets_CreateRuleAsync(BudgetRuleCreateRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync("/api/budget/rules", request, ct);
        await EnsureSuccessOrSetErrorAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<BudgetRuleDto>(cancellationToken: ct))!;
    }

    /// <summary>
    /// Updates a budget rule. Returns null when not found.
    /// </summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="request">Request payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<BudgetRuleDto?> Budgets_UpdateRuleAsync(Guid id, BudgetRuleUpdateRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PutAsJsonAsync($"/api/budget/rules/{id}", request, ct);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureSuccessOrSetErrorAsync(resp);
        // controller returns NoContent; re-fetch
        return await Budgets_GetRuleAsync(id, ct);
    }

    /// <summary>
    /// Gets a budget rule by id or null when not found.
    /// </summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<BudgetRuleDto?> Budgets_GetRuleAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"/api/budget/rules/{id}", ct);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureSuccessOrSetErrorAsync(resp);
        return await resp.Content.ReadFromJsonAsync<BudgetRuleDto>(cancellationToken: ct);
    }

    /// <summary>
    /// Deletes a budget rule. Returns false when not found.
    /// </summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<bool> Budgets_DeleteRuleAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.DeleteAsync($"/api/budget/rules/{id}", ct);
        if (resp.StatusCode == HttpStatusCode.NotFound) return false;
        await EnsureSuccessOrSetErrorAsync(resp);
        return true;
    }

    /// <summary>
    /// Lists overrides for a budget purpose.
    /// </summary>
    /// <param name="budgetPurposeId">The budget purpose id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<IReadOnlyList<BudgetOverrideDto>> Budgets_ListOverridesByPurposeAsync(Guid budgetPurposeId, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"/api/budget/overrides/by-purpose/{budgetPurposeId}", ct);
        await EnsureSuccessOrSetErrorAsync(resp);
        return await resp.Content.ReadFromJsonAsync<IReadOnlyList<BudgetOverrideDto>>(cancellationToken: ct) ?? Array.Empty<BudgetOverrideDto>();
    }

    /// <summary>
    /// Creates a budget override.
    /// </summary>
    /// <param name="request">Request payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<BudgetOverrideDto> Budgets_CreateOverrideAsync(BudgetOverrideCreateRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync("/api/budget/overrides", request, ct);
        await EnsureSuccessOrSetErrorAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<BudgetOverrideDto>(cancellationToken: ct))!;
    }

    /// <summary>
    /// Gets a budget override by id or null when not found.
    /// </summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<BudgetOverrideDto?> Budgets_GetOverrideAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"/api/budget/overrides/{id}", ct);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureSuccessOrSetErrorAsync(resp);
        return await resp.Content.ReadFromJsonAsync<BudgetOverrideDto>(cancellationToken: ct);
    }

    /// <summary>
    /// Updates a budget override. Returns null when not found.
    /// </summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="request">Request payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<BudgetOverrideDto?> Budgets_UpdateOverrideAsync(Guid id, BudgetOverrideUpdateRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PutAsJsonAsync($"/api/budget/overrides/{id}", request, ct);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureSuccessOrSetErrorAsync(resp);
        return await Budgets_GetOverrideAsync(id, ct);
    }

    /// <summary>
    /// Deletes a budget override. Returns false when not found.
    /// </summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<bool> Budgets_DeleteOverrideAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.DeleteAsync($"/api/budget/overrides/{id}", ct);
        if (resp.StatusCode == HttpStatusCode.NotFound) return false;
        await EnsureSuccessOrSetErrorAsync(resp);
        return true;
    }

    #endregion Budgets
}
