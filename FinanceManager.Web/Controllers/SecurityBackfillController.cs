using System.Text.Json;
using FinanceManager.Application;
using FinanceManager.Shared.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceManager.Web.Controllers;

[ApiController]
[Route("api/securities/backfill")]
[Authorize]
public sealed class SecurityBackfillController : ControllerBase
{
    private readonly IBackgroundTaskManager _tasks;
    private readonly ICurrentUserService _current;

    public SecurityBackfillController(IBackgroundTaskManager tasks, ICurrentUserService current)
    {
        _tasks = tasks; _current = current;
    }

    public sealed record BackfillRequest(Guid? SecurityId, DateTime? FromDateUtc, DateTime? ToDateUtc);

    [HttpPost]
    public ActionResult<BackgroundTaskInfo> Enqueue([FromBody] BackfillRequest req)
    {
        var payload = new { SecurityId = req.SecurityId?.ToString(), FromDateUtc = req.FromDateUtc?.ToString("o"), ToDateUtc = req.ToDateUtc?.ToString("o") };
        var info = _tasks.Enqueue(BackgroundTaskType.SecurityPricesBackfill, _current.UserId, payload, allowDuplicate: false);
        return Ok(info);
    }
}
