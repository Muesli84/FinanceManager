using FinanceManager.Application;
using FinanceManager.Application.Reports;
using FinanceManager.Domain.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceManager.Web.Controllers;

[ApiController]
[Route("api/report-aggregates")]
[Authorize]
public sealed class ReportAggregatesController : ControllerBase
{
    private readonly IReportAggregationService _agg;
    private readonly ICurrentUserService _current;
    private readonly ILogger<ReportAggregatesController> _logger;

    public ReportAggregatesController(IReportAggregationService agg, ICurrentUserService current, ILogger<ReportAggregatesController> logger)
    {
        _agg = agg; _current = current; _logger = logger;
    }

    public sealed record QueryRequest(
        int PostingKind,
        ReportInterval Interval,
        int Take = 24,
        bool IncludeCategory = false,
        bool ComparePrevious = false,
        bool CompareYear = false);

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
            var query = new ReportAggregationQuery(_current.UserId, req.PostingKind, req.Interval, req.Take, req.IncludeCategory, req.ComparePrevious, req.CompareYear);
            var result = await _agg.QueryAsync(query, ct);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Report aggregation failed");
            return Problem("Unexpected error", statusCode: 500);
        }
    }
}
