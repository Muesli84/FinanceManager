using System.Net.Http.Json;

namespace FinanceManager.Shared;

public partial class ApiClient
{
    #region Home KPIs

    /// <summary>
    /// Lists home KPI definitions for the current user.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of home KPI DTOs.</returns>
    public async Task<IReadOnlyList<HomeKpiDto>> HomeKpis_ListAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetAsync("/api/home-kpis", ct);
        await EnsureSuccessOrSetErrorAsync(resp);
        return await resp.Content.ReadFromJsonAsync<IReadOnlyList<HomeKpiDto>>(cancellationToken: ct) ?? Array.Empty<HomeKpiDto>();
    }

    /// <summary>
    /// Gets a single home KPI by id or null when not found.
    /// </summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<HomeKpiDto?> HomeKpis_GetAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"/api/home-kpis/{id}", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        await EnsureSuccessOrSetErrorAsync(resp);
        return await resp.Content.ReadFromJsonAsync<HomeKpiDto>(cancellationToken: ct);
    }

    /// <summary>
    /// Creates a new home KPI.
    /// </summary>
    /// <param name="request">Request payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<HomeKpiDto> HomeKpis_CreateAsync(HomeKpiCreateRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync("/api/home-kpis", request, ct);
        await EnsureSuccessOrSetErrorAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<HomeKpiDto>(cancellationToken: ct))!;
    }

    /// <summary>
    /// Updates an existing home KPI. Returns null when not found.
    /// </summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="request">Request payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<HomeKpiDto?> HomeKpis_UpdateAsync(Guid id, HomeKpiUpdateRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PutAsJsonAsync($"/api/home-kpis/{id}", request, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        if (resp.StatusCode == System.Net.HttpStatusCode.Conflict) throw new InvalidOperationException(await resp.Content.ReadAsStringAsync(ct));
        await EnsureSuccessOrSetErrorAsync(resp);
        return await resp.Content.ReadFromJsonAsync<HomeKpiDto>(cancellationToken: ct);
    }

    /// <summary>
    /// Deletes a home KPI. Returns false when not found.
    /// </summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<bool> HomeKpis_DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.DeleteAsync($"/api/home-kpis/{id}", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        await EnsureSuccessOrSetErrorAsync(resp);
        return true;
    }

    #endregion Home KPIs
}