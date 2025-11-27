using FinanceManager.Application;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceManager.Web.Controllers
{
    [ApiController]
    [Route("api/aggregates")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public sealed class AggregatesController : ControllerBase
    {
        private readonly IBackgroundTaskManager _tasks;
        private readonly ICurrentUserService _current;
        private readonly ILogger<AggregatesController> _logger;

        public AggregatesController(IBackgroundTaskManager tasks, ICurrentUserService current, ILogger<AggregatesController> logger)
        {
            _tasks = tasks;
            _current = current;
            _logger = logger;
        }

        [HttpPost("rebuild")]
        [ProducesResponseType(typeof(AggregatesRebuildStatusDto), StatusCodes.Status202Accepted)]
        public IActionResult RebuildAsync([FromQuery] bool allowDuplicate = false)
        {
            var existing = _tasks.GetAll()
                .FirstOrDefault(t => t.UserId == _current.UserId && t.Type == BackgroundTaskType.RebuildAggregates && (t.Status == BackgroundTaskStatus.Running || t.Status == BackgroundTaskStatus.Queued));
            if (existing != null && !allowDuplicate)
            {
                return Accepted(new AggregatesRebuildStatusDto(true, existing.Processed ?? 0, existing.Total ?? 0, existing.Message));
            }

            var info = _tasks.Enqueue(BackgroundTaskType.RebuildAggregates, _current.UserId, payload: null, allowDuplicate: allowDuplicate);
            _logger.LogInformation("Enqueued rebuild aggregates task {TaskId} for user {UserId}", info.Id, _current.UserId);
            return Accepted(new AggregatesRebuildStatusDto(true, 0, 0, "Queued"));
        }

        [HttpGet("rebuild/status")]
        [ProducesResponseType(typeof(AggregatesRebuildStatusDto), StatusCodes.Status200OK)]
        public IActionResult GetRebuildStatus()
        {
            var task = _tasks.GetAll()
                .Where(t => t.UserId == _current.UserId && t.Type == BackgroundTaskType.RebuildAggregates)
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
