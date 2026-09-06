using System.Net.Http.Json;

namespace FinanceManager.Shared;

public partial class ApiClient
{
    #region Reports

    /// <summary>
    /// Executes an aggregates report query and returns the aggregated result.
    /// </summary>
    /// <param name="req">Query request describing aggregation parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Aggregation result.</returns>
    public async Task<ReportAggregationResult> Reports_QueryAggregatesAsync(ReportAggregatesQueryRequest req, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync("/api/report-aggregates", req, ct);
        await EnsureSuccessOrSetErrorAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<ReportAggregationResult>(cancellationToken: ct))!;
    }

    /// <summary>
    /// Lists saved report favorites for the current user.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<IReadOnlyList<ReportFavoriteDto>> Reports_ListFavoritesAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetAsync("/api/report-favorites", ct);
        await EnsureSuccessOrSetErrorAsync(resp);
        return await resp.Content.ReadFromJsonAsync<IReadOnlyList<ReportFavoriteDto>>(cancellationToken: ct) ?? Array.Empty<ReportFavoriteDto>();
    }

    /// <summary>
    /// Gets a single report favorite by id or null when not found.
    /// </summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<ReportFavoriteDto?> Reports_GetFavoriteAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"/api/report-favorites/{id}", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        await EnsureSuccessOrSetErrorAsync(resp);
        return await resp.Content.ReadFromJsonAsync<ReportFavoriteDto>(cancellationToken: ct);
    }

    /// <summary>
    /// Creates a new report favorite. Throws InvalidOperationException on conflict.
    /// </summary>
    /// <param name="req">Request payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<ReportFavoriteDto> Reports_CreateFavoriteAsync(ReportFavoriteCreateApiRequest req, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync("/api/report-favorites", req, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            var err = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(err);
        }
        await EnsureSuccessOrSetErrorAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<ReportFavoriteDto>(cancellationToken: ct))!;
    }

    /// <summary>
    /// Updates an existing report favorite or returns null when not found. May throw on conflict or bad request.
    /// </summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="req">Request payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<ReportFavoriteDto?> Reports_UpdateFavoriteAsync(Guid id, ReportFavoriteUpdateApiRequest req, CancellationToken ct = default)
    {
        var resp = await _http.PutAsJsonAsync($"/api/report-favorites/{id}", req, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        if (resp.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            var err = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(err);
        }
        if (resp.StatusCode == System.Net.HttpStatusCode.BadRequest) { throw new ArgumentException(await resp.Content.ReadAsStringAsync(ct)); }
        await EnsureSuccessOrSetErrorAsync(resp);
        return await resp.Content.ReadFromJsonAsync<ReportFavoriteDto>(cancellationToken: ct);
    }

    /// <summary>
    /// Deletes a report favorite. Returns false when not found.
    /// </summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<bool> Reports_DeleteFavoriteAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.DeleteAsync($"/api/report-favorites/{id}", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        resp.EnsureSuccessStatusCode();
        return true;
    }

    #endregion Reports
}
