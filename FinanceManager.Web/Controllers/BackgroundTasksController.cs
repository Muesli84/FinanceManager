using FinanceManager.Application;
using FinanceManager.Application.Common;
using FinanceManager.Shared.Dtos.Common;
using FinanceManager.Web.Infrastructure.ApiErrors;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using System.Net.Mime;
using System.Security.Claims;

namespace FinanceManager.Web.Controllers
{
    /// <summary>
    /// Provides endpoints to enqueue and manage background tasks for the current user,
    /// including generic task enqueue, detail retrieval, cancellation/removal, and
    /// specialized aggregate rebuild operations.
    /// </summary>
    [ApiController]
    [Route("api/background-tasks")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public class BackgroundTasksController : ControllerBase
    {
        private const string Origin = "API_BackgroundTask";

        private readonly IBackgroundTaskManager _taskManager;
        private readonly ILogger<BackgroundTasksController> _logger;
        private readonly IStringLocalizer<Controller> _localizer;

        /// <summary>
        /// Initializes a new instance of <see cref="BackgroundTasksController"/>.
        /// </summary>
        /// <param name="taskManager">Background task manager used to enqueue and query tasks.</param>
        /// <param name="logger">Logger instance for controller diagnostics.</param>
        /// <param name="localizer">Localizer for accessing localized strings.</param>
        public BackgroundTasksController(
            IBackgroundTaskManager taskManager,
            ILogger<BackgroundTasksController> logger,
            IStringLocalizer<Controller> localizer)
        {
            _taskManager = taskManager;
            _logger = logger;
            _localizer = localizer;
        }

        private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        /// <summary>
        /// Enqueues a new background task of the specified type for the current user.
        /// </summary>
        /// <param name="type">Type of background task to enqueue.</param>
        /// <param name="allowDuplicate">When <c>true</c>, allows enqueueing even if a task of the same type is already running or queued for the user.</param>
        /// <returns>HTTP 200 with a <see cref="BackgroundTaskInfo"/> describing the enqueued task.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="type"/> is not supported.</exception>
        /// <response code="200">The HTTP 200 response.</response>
        [HttpPost("{type}")]
        [ProducesResponseType(typeof(BackgroundTaskInfo), StatusCodes.Status200OK)]
        public ActionResult<BackgroundTaskInfo> Enqueue([FromRoute] BackgroundTaskType type, [FromQuery] bool allowDuplicate = false)
        {
            var userId = GetUserId();
            var info = _taskManager.Enqueue(type, userId, null, allowDuplicate);
            _logger.LogInformation("Enqueued background task {TaskId} of type {Type} for user {UserId}", info.Id, info.Type, userId);
            return Ok(info);
        }

        /// <summary>
        /// Returns active or queued background tasks for the current user.
        /// </summary>
        /// <returns>HTTP 200 with an enumerable of <see cref="BackgroundTaskInfo"/> representing running or queued tasks.</returns>
        /// <response code="200">The HTTP 200 response.</response>
        [HttpGet("active")]
        [ProducesResponseType(typeof(IEnumerable<BackgroundTaskInfo>), StatusCodes.Status200OK)]
        public ActionResult<IEnumerable<BackgroundTaskInfo>> GetActiveAndQueued()
        {
            var userId = GetUserId();
            var all = _taskManager.GetAll().Where(x => x.UserId == userId && (x.Status == BackgroundTaskStatus.Running || x.Status == BackgroundTaskStatus.Queued));
            return Ok(all);
        }

        /// <summary>
        /// Gets detailed information about a single background task if it is owned by the current user.
        /// </summary>
        /// <param name="id">Identifier of the background task to retrieve.</param>
        /// <returns>
        /// HTTP 200 with <see cref="BackgroundTaskInfo"/> when the task is found and owned by the current user;
        /// HTTP 404 when the task does not exist or is not owned by the user.
        /// </returns>
        /// <response code="200">The HTTP 200 response.</response>
        /// <response code="404">The HTTP 404 response.</response>
        [HttpGet("{id}")]
        [ProducesResponseType(typeof(BackgroundTaskInfo), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public ActionResult<BackgroundTaskInfo> GetDetail([FromRoute] Guid id)
        {
            var userId = GetUserId();
            var info = _taskManager.Get(id);
            if (info == null || info.UserId != userId) return NotFound();
            return Ok(info);
        }

        /// <summary>
        /// Cancels a running task or removes a queued task owned by the current user. Only tasks in Running or Queued status are affected.
        /// </summary>
        /// <param name="id">Identifier of the task to cancel or remove.</param>
        /// <returns>
        /// HTTP 204 when cancellation/removal succeeded.
        /// HTTP 400 with an <see cref="ApiErrorDto"/> when the operation could not be performed.
        /// HTTP 404 when the task was not found or not owned by the user.
        /// </returns>
        /// <response code="204">The HTTP 204 response.</response>
        /// <response code="400">The HTTP 400 response.</response>
        /// <response code="404">The HTTP 404 response.</response>
        [HttpDelete("{id}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(ApiErrorDto), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult CancelOrRemove([FromRoute] Guid id)
        {
            var userId = GetUserId();
            var info = _taskManager.Get(id);
            if (info == null || info.UserId != userId)
            {
                return NotFound();
            }

            if (info.Status == BackgroundTaskStatus.Running)
            {
                var cancelled = _taskManager.TryCancel(id);
                if (cancelled)
                {
                    return NoContent();
                }

                const string code = "Err_CancelFailed";
                var entry = _localizer[$"{Origin}_{code}"];
                var message = entry.ResourceNotFound ? "Could not cancel running task." : entry.Value;
                return BadRequest(ApiErrorDto.Create(Origin, code, message));
            }

            if (info.Status == BackgroundTaskStatus.Queued)
            {
                var removed = _taskManager.TryRemoveQueued(id);
                if (removed)
                {
                    return NoContent();
                }

                const string code = "Err_RemoveFailed";
                var entry = _localizer[$"{Origin}_{code}"];
                var message = entry.ResourceNotFound ? "Could not remove queued task." : entry.Value;
                return BadRequest(ApiErrorDto.Create(Origin, code, message));
            }

            {
                const string code = "Err_InvalidState";
                var entry = _localizer[$"{Origin}_{code}"];
                var message = entry.ResourceNotFound ? "Only queued or running tasks can be cancelled or removed." : entry.Value;
                return BadRequest(ApiErrorDto.Create(Origin, code, message));
            }
        }

        /// <summary>
        /// Enqueues an aggregates rebuild task or returns the existing running/queued task status for the user.
        /// </summary>
        /// <param name="allowDuplicate">If <c>true</c>, allows enqueueing even when an aggregates rebuild task is already running or queued for the user.</param>
        /// <returns>HTTP 202 with an <see cref="AggregatesRebuildStatusDto"/> describing the queued or existing rebuild task.</returns>
        /// <response code="202">The HTTP 202 response.</response>
        [HttpPost("aggregates/rebuild")]
        [ProducesResponseType(typeof(AggregatesRebuildStatusDto), StatusCodes.Status202Accepted)]
        public IActionResult RebuildAggregates([FromQuery] bool allowDuplicate = false)
        {
            var userId = GetUserId();
            var existing = _taskManager.GetAll()
                .FirstOrDefault(t => t.UserId == userId && t.Type == BackgroundTaskType.RebuildAggregates && (t.Status == BackgroundTaskStatus.Running || t.Status == BackgroundTaskStatus.Queued));
            if (existing != null && !allowDuplicate)
            {
                return Accepted(new AggregatesRebuildStatusDto(true, existing.Processed ?? 0, existing.Total ?? 0, existing.Message));
            }

            var info = _taskManager.Enqueue(BackgroundTaskType.RebuildAggregates, userId, payload: null, allowDuplicate: allowDuplicate);
            _logger.LogInformation("Enqueued rebuild aggregates task {TaskId} for user {UserId}", info.Id, userId);
            return Accepted(new AggregatesRebuildStatusDto(true, 0, 0, "Queued"));
        }

        /// <summary>
        /// Returns the status of the most recent running or queued aggregates rebuild task for the current user.
        /// </summary>
        /// <returns>HTTP 200 with an <see cref="AggregatesRebuildStatusDto"/> indicating whether a rebuild is active and progress values when available.</returns>
        /// <response code="200">The HTTP 200 response.</response>
        [HttpGet("aggregates/rebuild/status")]
        [ProducesResponseType(typeof(AggregatesRebuildStatusDto), StatusCodes.Status200OK)]
        public IActionResult GetRebuildAggregatesStatus()
        {
            var userId = GetUserId();
            var task = _taskManager.GetAll()
                .Where(t => t.UserId == userId && t.Type == BackgroundTaskType.RebuildAggregates)
                .OrderByDescending(t => t.EnqueuedUtc)
                .FirstOrDefault(t => t.Status == BackgroundTaskStatus.Running || t.Status == BackgroundTaskStatus.Queued);

            if (task == null)
            {
                return Ok(new AggregatesRebuildStatusDto(false, 0, 0, null));
            }
            return Ok(new AggregatesRebuildStatusDto(true, task.Processed ?? 0, task.Total ?? 0, task.Message));
        }
    }
}
