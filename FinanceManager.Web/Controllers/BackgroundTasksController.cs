using FinanceManager.Application;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace FinanceManager.Web.Controllers
{
    [ApiController]
    [Route("api/background-tasks")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public class BackgroundTasksController : ControllerBase
    {
        private readonly IBackgroundTaskManager _taskManager;
        private readonly ILogger<BackgroundTasksController> _logger;

        public BackgroundTasksController(IBackgroundTaskManager taskManager, ILogger<BackgroundTasksController> logger)
        {
            _taskManager = taskManager;
            _logger = logger;
        }

        private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        [HttpPost("{type}")]
        [ProducesResponseType(typeof(BackgroundTaskInfo), StatusCodes.Status200OK)]
        public ActionResult<BackgroundTaskInfo> Enqueue([FromRoute] BackgroundTaskType type, [FromQuery] bool allowDuplicate = false)
        {
            var userId = GetUserId();
            var info = _taskManager.Enqueue(type, userId, null, allowDuplicate);
            _logger.LogInformation("Enqueued background task {TaskId} of type {Type} for user {UserId}", info.Id, info.Type, userId);
            return Ok(info);
        }

        [HttpGet("active")]
        [ProducesResponseType(typeof(IEnumerable<BackgroundTaskInfo>), StatusCodes.Status200OK)]
        public ActionResult<IEnumerable<BackgroundTaskInfo>> GetActiveAndQueued()
        {
            var userId = GetUserId();
            var all = _taskManager.GetAll().Where(x => x.UserId == userId && (x.Status == BackgroundTaskStatus.Running || x.Status == BackgroundTaskStatus.Queued));
            return Ok(all);
        }

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

        [HttpDelete("{id}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(ApiErrorDto), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult CancelOrRemove([FromRoute] Guid id)
        {
            var userId = GetUserId();
            var info = _taskManager.Get(id);
            if (info == null || info.UserId != userId) return NotFound();
            if (info.Status == BackgroundTaskStatus.Running)
            {
                var cancelled = _taskManager.TryCancel(id);
                return cancelled ? NoContent() : BadRequest(new ApiErrorDto("Could not cancel running task."));
            }
            if (info.Status == BackgroundTaskStatus.Queued)
            {
                var removed = _taskManager.TryRemoveQueued(id);
                return removed ? NoContent() : BadRequest(new ApiErrorDto("Could not remove queued task."));
            }
            return BadRequest(new ApiErrorDto("Only queued or running tasks can be cancelled or removed."));
        }

        // New: Aggregates rebuild endpoints under background-tasks, keeping original routes in AggregatesController
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
