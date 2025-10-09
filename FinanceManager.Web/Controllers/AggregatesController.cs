using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FinanceManager.Application;
using FinanceManager.Shared.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace FinanceManager.Web.Controllers
{
    [ApiController]
    [Route("api/aggregates")] 
    [Authorize]
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
