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

namespace FinanceManager.Web.Controllers.BackgroundProcessings // moved from .Shared to Controllers for test compatibility
{
    /// <summary>
    /// Endpoints to enqueue and inspect background tasks for the current user.
    /// Thin controller delegating work to <see cref="IBackgroundTaskManager"/>.
    /// </summary>
    [ApiController]
    [Route("api/background-tasks")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public class BackgroundTasksController : ControllerBase
    {
        private readonly IBackgroundTaskManager _taskManager;
        private readonly ILogger<BackgroundTasksController> _logger;

        /// <summary>
        /// Creates a new instance of <see cref="BackgroundTasksController"/>.
        /// </summary>
        /// <param name="taskManager">Background task manager used to enqueue and manage tasks.</param>
        /// <param name="logger">Logger instance.</param>
        public BackgroundTasksController(IBackgroundTaskManager taskManager, ILogger<BackgroundTasksController> logger)
        {
            _taskManager = taskManager;
            _logger = logger;
        }

        private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        /// <summary>
        /// Enqueues a background task of the specified type for the current user.
        /// </summary>
        /// <param name="type">Background task type to enqueue.</param>
        /// <param name="allowDuplicate">If true, allow enqueueing a duplicate task even if one exists.</param>
        /// <returns>Information about the enqueued task.</returns>
        [HttpPost("{type}")]
        public ActionResult<BackgroundTaskInfo> Enqueue([FromRoute] BackgroundTaskType type, [FromQuery] bool allowDuplicate = false)
        {
            var userId = GetUserId();
            var info = _taskManager.Enqueue(type, userId, null, allowDuplicate);
            _logger.LogInformation("Enqueued background task {TaskId} of type {Type} for user {UserId}", info.Id, info.Type, userId);
            return Ok(info);
        }

        /// <summary>
        /// Returns active and queued background tasks for the current user.
        /// </summary>
        /// <returns>Enumerable of <see cref="BackgroundTaskInfo"/> representing running or queued tasks.</returns>
        [HttpGet("active")]
        public ActionResult<IEnumerable<BackgroundTaskInfo>> GetActiveAndQueued()
        {
            var userId = GetUserId();
            var all = _taskManager.GetAll().Where(x => x.UserId == userId && (x.Status == BackgroundTaskStatus.Running || x.Status == BackgroundTaskStatus.Queued));
            return Ok(all);
        }

        /// <summary>
        /// Returns details for a specific background task owned by the current user.
        /// </summary>
        /// <param name="id">Identifier of the background task.</param>
        /// <returns>Background task info or 404 when not found or not owned.</returns>
        [HttpGet("{id}")]
        public ActionResult<BackgroundTaskInfo> GetDetail([FromRoute] Guid id)
        {
            var userId = GetUserId();
            var info = _taskManager.Get(id);
            if (info == null || info.UserId != userId) return NotFound();
            return Ok(info);
        }

        /// <summary>
        /// Cancels a running task or removes a queued task for the current user.
        /// </summary>
        /// <param name="id">Identifier of the background task to cancel or remove.</param>
        /// <returns>NoContent on success, NotFound when missing, BadRequest on invalid state.</returns>
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

