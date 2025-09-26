using System.ComponentModel.DataAnnotations;
using FinanceManager.Application;
using FinanceManager.Application.Reports;
using FinanceManager.Domain.Reports; // added for ReportInterval
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceManager.Web.Controllers;

[ApiController]
[Route("api/report-favorites")]
[Authorize]
public sealed class ReportFavoritesController : ControllerBase
{
    private readonly IReportFavoriteService _favorites;
    private readonly ICurrentUserService _current;
    private readonly ILogger<ReportFavoritesController> _logger;

    public ReportFavoritesController(IReportFavoriteService favorites, ICurrentUserService current, ILogger<ReportFavoritesController> logger)
    {
        _favorites = favorites;
        _current = current;
        _logger = logger;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ReportFavoriteDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAsync(CancellationToken ct)
    {
        var list = await _favorites.ListAsync(_current.UserId, ct);
        return Ok(list);
    }

    [HttpGet("{id:guid}", Name = "GetReportFavorite")]
    [ProducesResponseType(typeof(ReportFavoriteDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken ct)
    {
        var dto = await _favorites.GetAsync(id, _current.UserId, ct);
        return dto == null ? NotFound() : Ok(dto);
    }

    public sealed class CreateRequest
    {
        [Required, MinLength(2), MaxLength(120)] public string Name { get; set; } = string.Empty;
        [Range(0, 10)] public int PostingKind { get; set; }
        public bool IncludeCategory { get; set; }
        [Required] public ReportInterval Interval { get; set; }
        public bool ComparePrevious { get; set; }
        public bool CompareYear { get; set; }
        public bool ShowChart { get; set; }
        public bool Expandable { get; set; } = true;
    }

    [HttpPost]
    [ProducesResponseType(typeof(ReportFavoriteDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateAsync([FromBody] CreateRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        try
        {
            var dto = await _favorites.CreateAsync(_current.UserId, new ReportFavoriteCreateRequest(req.Name.Trim(), req.PostingKind, req.IncludeCategory, req.Interval, req.ComparePrevious, req.CompareYear, req.ShowChart, req.Expandable), ct);
            return CreatedAtRoute("GetReportFavorite", new { id = dto.Id }, dto);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Create report favorite failed");
            return Problem("Unexpected error", statusCode: 500);
        }
    }

    public sealed class UpdateRequest
    {
        [Required, MinLength(2), MaxLength(120)] public string Name { get; set; } = string.Empty;
        [Range(0, 10)] public int PostingKind { get; set; }
        public bool IncludeCategory { get; set; }
        [Required] public ReportInterval Interval { get; set; }
        public bool ComparePrevious { get; set; }
        public bool CompareYear { get; set; }
        public bool ShowChart { get; set; }
        public bool Expandable { get; set; } = true;
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ReportFavoriteDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateAsync(Guid id, [FromBody] UpdateRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        try
        {
            var dto = await _favorites.UpdateAsync(id, _current.UserId, new ReportFavoriteUpdateRequest(req.Name.Trim(), req.PostingKind, req.IncludeCategory, req.Interval, req.ComparePrevious, req.CompareYear, req.ShowChart, req.Expandable), ct);
            return dto == null ? NotFound() : Ok(dto);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update report favorite {FavoriteId} failed", id);
            return Problem("Unexpected error", statusCode: 500);
        }
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken ct)
    {
        try
        {
            var ok = await _favorites.DeleteAsync(id, _current.UserId, ct);
            return ok ? NoContent() : NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Delete report favorite {FavoriteId} failed", id);
            return Problem("Unexpected error", statusCode: 500);
        }
    }
}
