using FinanceManager.Application;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceManager.Web.Controllers;

[ApiController]
[Route("api/securities/backfill")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class SecurityBackfillController : ControllerBase
{
    private readonly IBackgroundTaskManager _tasks;
    private readonly ICurrentUserService _current;

    public SecurityBackfillController(IBackgroundTaskManager tasks, ICurrentUserService current)
    {
        _tasks = tasks; _current = current;
    }

    [HttpPost]
    [ProducesResponseType(typeof(BackgroundTaskInfo), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<BackgroundTaskInfo> Enqueue([FromBody] SecurityBackfillRequest req)
    {
        var payload = new { SecurityId = req.SecurityId?.ToString(), FromDateUtc = req.FromDateUtc?.ToString("o"), ToDateUtc = req.ToDateUtc?.ToString("o") };
        var info = _tasks.Enqueue(BackgroundTaskType.SecurityPricesBackfill, _current.UserId, payload, allowDuplicate: false);
        return Ok(info);
    }
}
