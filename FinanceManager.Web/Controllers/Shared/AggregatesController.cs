using FinanceManager.Application;
using FinanceManager.Shared.Dtos;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FinanceManager.Web.Controllers.Shared
{
    /// <summary>
    /// Endpoints to manage and inspect background aggregate rebuild tasks for the current user.
    /// Provides operations to enqueue a rebuild and to query the current rebuild status.
    /// </summary>
    [ApiController]
    [Route("api/aggregates")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public sealed class AggregatesController : ControllerBase
    {
        private readonly IBackgroundTaskManager _tasks;
        private readonly ICurrentUserService _current;
        private readonly ILogger<AggregatesController> _logger;

        /// <summary>
        /// Creates a new instance of <see cref="AggregatesController"/>.
        /// </summary>
        /// <param name="tasks">Background task manager used to enqueue and inspect tasks.</param>
        /// <param name="current">Current user context service.</param>
        /// <param name="logger">Logger instance.</param>
        public AggregatesController(IBackgroundTaskManager tasks, ICurrentUserService current, ILogger<AggregatesController> logger)
        {
            _tasks = tasks;
            _current = current;
            _logger = logger;
        }

        /// <summary>
        /// Enqueues a background task to rebuild aggregates for the current user.
        /// If a rebuild task is already running or queued, returns the existing task status unless <paramref name="allowDuplicate"/> is true.
        /// </summary>
        /// <param name="allowDuplicate">If true, allow enqueueing a duplicate rebuild task even if one is running or queued.</param>
        /// <returns>Accepted (202) with task status payload indicating queued or running state.</returns>
        [HttpPost("rebuild")] 
        public IActionResult RebuildAsync([FromQuery] bool allowDuplicate = false)
        {
            var existing = _tasks.GetAll()
                .FirstOrDefault(t => t.UserId == _current.UserId && t.Type == BackgroundTaskType.RebuildAggregates && (t.Status == BackgroundTaskStatus.Running || t.Status == BackgroundTaskStatus.Queued));
            if (existing != null && !allowDuplicate)
            {
                return Accepted(new { running = true, processed = existing.Processed ?? 0, total = existing.Total ?? 0, message = existing.Message });
            }

            var info = _tasks.Enqueue(BackgroundTaskType.RebuildAggregates, _current.UserId, payload: null, allowDuplicate: allowDuplicate);
            _logger.LogInformation("Enqueued rebuild aggregates task {TaskId} for user {UserId}", info.Id, _current.UserId);
            return Accepted(new { running = true, processed = 0, total = 0, message = "Queued" });
        }

        /// <summary>
        /// Returns status information about the latest rebuild aggregates task for the current user.
        /// </summary>
        /// <returns>200 OK with running flag and progress counters.</returns>
        [HttpGet("rebuild/status")] 
        public IActionResult GetRebuildStatus()
        {
            var task = _tasks.GetAll()
                .Where(t => t.UserId == _current.UserId && t.Type == BackgroundTaskType.RebuildAggregates)
                .OrderByDescending(t => t.EnqueuedUtc)
                .FirstOrDefault(t => t.Status == BackgroundTaskStatus.Running || t.Status == BackgroundTaskStatus.Queued);

            if (task == null)
            {
                return Ok(new { running = false, processed = 0, total = 0, message = (string?)null });
            }
            return Ok(new { running = true, processed = task.Processed ?? 0, total = task.Total ?? 0, message = task.Message });
        }
    }
}

