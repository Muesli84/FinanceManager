using FinanceManager.Application;
using FinanceManager.Shared.Dtos;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

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
        public ActionResult<BackgroundTaskInfo> Enqueue([FromRoute] BackgroundTaskType type, [FromQuery] bool allowDuplicate = false)
        {
            var userId = GetUserId();
            var info = _taskManager.Enqueue(type, userId, null, allowDuplicate);
            _logger.LogInformation("Enqueued background task {TaskId} of type {Type} for user {UserId}", info.Id, info.Type, userId);
            return Ok(info);
        }

        [HttpGet("active")]
        public ActionResult<IEnumerable<BackgroundTaskInfo>> GetActiveAndQueued()
        {
            var userId = GetUserId();
            var all = _taskManager.GetAll().Where(x => x.UserId == userId && (x.Status == BackgroundTaskStatus.Running || x.Status == BackgroundTaskStatus.Queued));
            return Ok(all);
        }

        [HttpGet("{id}")]
        public ActionResult<BackgroundTaskInfo> GetDetail([FromRoute] Guid id)
        {
            var userId = GetUserId();
            var info = _taskManager.Get(id);
            if (info == null || info.UserId != userId) return NotFound();
            return Ok(info);
        }

        [HttpDelete("{id}")]
        public IActionResult CancelOrRemove([FromRoute] Guid id)
        {
            var userId = GetUserId();
            var info = _taskManager.Get(id);
            if (info == null || info.UserId != userId) return NotFound();
            if (info.Status == BackgroundTaskStatus.Running)
            {
                var cancelled = _taskManager.TryCancel(id);
                return cancelled ? NoContent() : BadRequest();
            }
            if (info.Status == BackgroundTaskStatus.Queued)
            {
                var removed = _taskManager.TryRemoveQueued(id);
                return removed ? NoContent() : BadRequest();
            }
            return BadRequest();
        }
    }
}
