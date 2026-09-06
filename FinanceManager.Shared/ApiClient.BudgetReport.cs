using FinanceManager.Shared.Dtos.Budget;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Linq;

namespace FinanceManager.Shared;

public partial class ApiClient
{
    #region Budget Report

    /// <summary>
    /// Generates a budget report for the current user.
    /// </summary>
    /// <param name="request">The report request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The generated <see cref="BudgetReportDto"/>.</returns>
    public async Task<BudgetReportDto> Budgets_GetReportAsync(BudgetReportRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync("/api/budget/report", request, ct);
        await EnsureSuccessOrSetErrorAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<BudgetReportDto>(cancellationToken: ct))!;
    }

    /// <summary>
    /// Gets raw budget report data for the current user.
    /// </summary>
    /// <param name="request">The report request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The raw budget report data.</returns>
    public async Task<BudgetReportRawDataDto> Budgets_GetReportRawAsync(BudgetReportRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync("/api/budget/report/raw", request, ct);
        await EnsureSuccessOrSetErrorAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<BudgetReportRawDataDto>(cancellationToken: ct))!;
    }

    /// <summary>
    /// Lists postings that are not covered by any budget purpose for the given range.
    /// </summary>
    /// <param name="from">Start date (optional).</param>
    /// <param name="to">End date (optional).</param>
    /// <param name="dateBasis">Date basis for filtering.</param>
    /// <param name="kind">Optional kind filter.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of unbudgeted postings.</returns>
    public async Task<IReadOnlyList<PostingServiceDto>> Budgets_GetUnbudgetedPostingsAsync(DateTime? from, DateTime? to, BudgetReportDateBasis dateBasis, string? kind = null, CancellationToken ct = default)
    {
        var url = $"/api/budget/report/unbudgeted?dateBasis={(int)dateBasis}";
        if (from.HasValue) url += $"&from={from.Value:O}";
        if (to.HasValue) url += $"&to={to.Value:O}";
        if (!string.IsNullOrWhiteSpace(kind)) url += $"&kind={Uri.EscapeDataString(kind)}";
        var resp = await _http.GetAsync(url, ct);
        await EnsureSuccessOrSetErrorAsync(resp);
        return await resp.Content.ReadFromJsonAsync<IReadOnlyList<PostingServiceDto>>(cancellationToken: ct) ?? Array.Empty<PostingServiceDto>();
    }

    /// <summary>
    /// Gets the Home Monthly Budget KPI values (planned/actual income and expenses).
    /// </summary>
    /// <param name="date">Optional date for the KPI values.</param>
    /// <param name="dateBasis">Date basis for filtering.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The <see cref="MonthlyBudgetKpiDto"/> for the current user/month.</returns>
    public async Task<MonthlyBudgetKpiDto> Budgets_GetMonthlyKpiAsync(DateOnly? date = null, BudgetReportDateBasis dateBasis = BudgetReportDateBasis.BookingDate, CancellationToken ct = default)
    {
        var url = "/api/budget/report/kpi-monthly";
        var query = new List<string> { $"dateBasis={(int)dateBasis}" };
        if (date.HasValue)
        {
            query.Add($"date={date.Value:yyyy-MM-dd}");
        }

        url += "?" + string.Join("&", query);

        var resp = await _http.GetAsync(url, ct);
        await EnsureSuccessOrSetErrorAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<MonthlyBudgetKpiDto>(cancellationToken: ct))!;
    }

    /// <summary>
    /// Downloads an XLSX export for the given budget report range.
    /// </summary>
    /// <param name="request">Request payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<(string ContentType, string FileName, byte[] Content)> Budgets_ExportAsync(BudgetReportExportRequest request, CancellationToken ct = default)
    {
        var url = $"/api/budget/report/export?asOf={request.AsOfDate:yyyy-MM-dd}&months={request.Months}&dateBasis={(int)request.DateBasis}";
        var resp = await _http.GetAsync(url, ct);
        await EnsureSuccessOrSetErrorAsync(resp);

        var content = await resp.Content.ReadAsByteArrayAsync(ct);
        var contentType = resp.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";

        string fileName = string.Empty;
        // Try standard Content-Disposition parsing
        if (resp.Content.Headers.ContentDisposition?.FileNameStar != null)
        {
            fileName = resp.Content.Headers.ContentDisposition.FileNameStar;
        }
        else if (resp.Content.Headers.ContentDisposition?.FileName != null)
        {
            fileName = resp.Content.Headers.ContentDisposition.FileName;
        }
        else if (resp.Headers.TryGetValues("Content-Disposition", out var vals))
        {
            var cd = vals.FirstOrDefault();
            if (!string.IsNullOrEmpty(cd))
            {
                var m = Regex.Match(cd, "filename\\*=UTF-8''(?<fn>.+)$");
                if (m.Success) fileName = Uri.UnescapeDataString(m.Groups["fn"].Value);
                else
                {
                    m = Regex.Match(cd, "filename=\"(?<fn>.+?)\"");
                    if (m.Success) fileName = m.Groups["fn"].Value;
                }
            }
        }

        return (contentType, fileName ?? string.Empty, content);
    }

    /// <summary>
    /// Clears cached report data for the current user.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task Budgets_ResetReportCacheAsync(CancellationToken ct = default)
    {
        var resp = await _http.PostAsync("/api/budget/report/cache/reset", content: null, ct);
        await EnsureSuccessOrSetErrorAsync(resp);
    }

    #endregion Budget Report
}
