using FinanceManager.Application;
using FinanceManager.Application.Statements;
using FinanceManager.Application.Statements.Dtos;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net.Mime;

namespace FinanceManager.Web.Controllers
{
    /// <summary>
    /// Controller for operations on statement draft entries (batch updates for QuickEdit feature).
    /// </summary>
    [ApiController]
    [Route("api/statement-drafts/{draftId:guid}/entries")]
    [Produces(MediaTypeNames.Application.Json)]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public sealed class StatementDraftEntriesController : ControllerBase
    {
        private readonly IStatementDraftService _service;
        private readonly ILogger<StatementDraftEntriesController> _logger;
        private readonly ICurrentUserService _current;

        /// <summary>
        /// Initializes a new instance of <see cref="StatementDraftEntriesController"/>.
        /// </summary>
        /// <param name="service">The service.</param>
        /// <param name="current">The current.</param>
        /// <param name="logger">Logger instance.</param>
        public StatementDraftEntriesController(
            IStatementDraftService service,
            ICurrentUserService current,
            ILogger<StatementDraftEntriesController> logger)
        {
            _service = service;
            _current = current;
            _logger = logger;
        }

        /// <summary>
        /// Applies a batch update for entries belonging to the specified draft.
        /// Returns 200 on success or 400 with per-entry validation errors.
        /// </summary>
        /// <param name="draftId">Draft identifier.</param>
        /// <param name="req">Batch update request DTO.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Action result.</returns>
        [HttpPost("batch-update")]
        public async Task<IActionResult> BatchUpdate(Guid draftId, [FromBody] FinanceManager.Shared.Dtos.Statements.BatchUpdateRequestDto req, CancellationToken ct)
        {
            _logger?.LogInformation("BatchUpdate called for draft {DraftId} with {UpdateCount} updates, {DeleteCount} deletes and {CreateCount} creates", draftId, req?.Updates?.Count ?? 0, req?.Deletes?.Count ?? 0, req?.Creates?.Count ?? 0);
            // Basic validation
            if (req == null || ((req.Updates?.Count ?? 0) == 0 && (req.Deletes?.Count ?? 0) == 0 && (req.Creates?.Count ?? 0) == 0))
                return BadRequest(new { message = "No quick edit changes provided" });

            // Delegate to service - the service performs permission checks and validation
            try
            {
                var result = await _service.ApplyBatchEntryUpdatesAsync(draftId, _current.UserId, req, ct);
                if (!result.Success)
                {
                    return BadRequest(result.ErrorResponse);
                }
                return Ok(result.SuccessResponse);
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (Exception ex)
            {
                _logger?.Log(LogLevel.Error, ex, "Error applying batch updates for draft {DraftId}", draftId);
                return StatusCode(500, new { message = "Internal server error" });
            }
        }
    }
}
