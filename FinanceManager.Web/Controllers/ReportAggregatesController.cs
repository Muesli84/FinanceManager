using FinanceManager.Application;
using FinanceManager.Application.Reports;
using FinanceManager.Domain.Reports;
using FinanceManager.Infrastructure; // DbContext
using FinanceManager.Infrastructure.Setup; // SchemaPatcher
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;

namespace FinanceManager.Web.Controllers;

[ApiController]
[Route("api/report-aggregates")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class ReportAggregatesController : ControllerBase
{
    private readonly IReportAggregationService _agg;
    private readonly ICurrentUserService _current;
    private readonly ILogger<ReportAggregatesController> _logger;
    private readonly AppDbContext _db;
    private readonly ILoggerFactory _loggerFactory;

    public ReportAggregatesController(IReportAggregationService agg, ICurrentUserService current, ILogger<ReportAggregatesController> logger, AppDbContext db, ILoggerFactory loggerFactory)
    {
        _agg = agg; _current = current; _logger = logger; _db = db; _loggerFactory = loggerFactory;
    }

    public sealed record FiltersRequest(
        IReadOnlyCollection<Guid>? AccountIds,
        IReadOnlyCollection<Guid>? ContactIds,
        IReadOnlyCollection<Guid>? SavingsPlanIds,
        IReadOnlyCollection<Guid>? SecurityIds,
        IReadOnlyCollection<Guid>? ContactCategoryIds,
        IReadOnlyCollection<Guid>? SavingsPlanCategoryIds,
        IReadOnlyCollection<Guid>? SecurityCategoryIds,
        IReadOnlyCollection<int>? SecuritySubTypes, // new
        bool? IncludeDividendRelated // new
    )
    {
        public ReportAggregationFilters ToModel() => new(
            AccountIds,
            ContactIds,
            SavingsPlanIds,
            SecurityIds,
            ContactCategoryIds,
            SavingsPlanCategoryIds,
            SecurityCategoryIds,
            SecuritySubTypes,
            IncludeDividendRelated
        );
    }

    public sealed record QueryRequest(
        int PostingKind,
        ReportInterval Interval,
        int Take = 24,
        bool IncludeCategory = false,
        bool ComparePrevious = false,
        bool CompareYear = false,
        IReadOnlyCollection<int>? PostingKinds = null, // neu: Multi aus Body
        DateTime? AnalysisDate = null, // neu: optionales Analysedatum (Monatsgenau)
        FiltersRequest? Filters = null // neu: optionale Entitäts-/Kategorie-Filter
    );

    [HttpPost]
    [ProducesResponseType(typeof(ReportAggregationResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> QueryAsync([FromBody] QueryRequest req, CancellationToken ct)
    {
        try
        {
            if (req.Take < 1 || req.Take > 200)
            {
                return BadRequest(new { error = "Take must be 1..200" });
            }

            // Fallback: Query-String (?postingKinds=0,1,2) nur nutzen, wenn im Body nichts gesendet wurde
            IReadOnlyCollection<int>? multi = req.PostingKinds;
            if ((multi == null || multi.Count == 0))
            {
                var kindsParam = Request.Query["postingKinds"].FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(kindsParam))
                {
                    multi = kindsParam
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(s => int.TryParse(s, out var v) ? v : (int?)null)
                        .Where(v => v.HasValue)
                        .Select(v => v!.Value)
                        .Distinct()
                        .ToArray();
                }
            }

            // AnalysisDate optional aus Query lesen, falls nicht gesetzt
            DateTime? analysisDate = req.AnalysisDate;
            if (!analysisDate.HasValue)
            {
                var adStr = Request.Query["analysisDate"].FirstOrDefault();
                if (DateTime.TryParse(adStr, out var ad))
                {
                    analysisDate = ad;
                }
            }

            if (analysisDate.HasValue)
            {
                var d = analysisDate.Value.Date;
                // auf Monatsersten normalisieren
                analysisDate = new DateTime(d.Year, d.Month, 1);
            }

            var filters = req.Filters?.ToModel();

            var query = new ReportAggregationQuery(
                _current.UserId,
                req.PostingKind,
                req.Interval,
                req.Take,
                req.IncludeCategory,
                req.ComparePrevious,
                req.CompareYear,
                multi,
                analysisDate,
                filters);

            var result = await _agg.QueryAsync(query, ct);
            return Ok(result);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 1 && ex.Message.Contains("SecuritySubType", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(ex, "Aggregation failed due to missing column. Attempting schema patch and retry.");
            try
            {
                var query = new ReportAggregationQuery(
                    _current.UserId,
                    req.PostingKind,
                    req.Interval,
                    req.Take,
                    req.IncludeCategory,
                    req.ComparePrevious,
                    req.CompareYear,
                    req.PostingKinds,
                    req.AnalysisDate,
                    req.Filters?.ToModel());
                var result = await _agg.QueryAsync(query, ct);
                return Ok(result);
            }
            catch (Exception ex2)
            {
                _logger.LogError(ex2, "Aggregation retry after schema patch failed");
                return Problem("Unexpected error", statusCode: 500);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Report aggregation failed");
            return Problem("Unexpected error", statusCode: 500);
        }
    }
}
