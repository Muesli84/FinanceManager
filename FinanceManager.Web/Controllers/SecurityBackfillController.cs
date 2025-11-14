using FinanceManager.Application;
using FinanceManager.Shared.Dtos;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace FinanceManager.Web.Controllers;

/// <summary>
/// Enqueues background backfill tasks for securities (price history backfill).
/// </summary>
[ApiController]
[Route("api/securities/backfill")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class SecurityBackfillController : ControllerBase
{
    private readonly IBackgroundTaskManager _tasks;
    private readonly ICurrentUserService _current;

    /// <summary>
    /// Creates a new instance of <see cref="SecurityBackfillController"/>.
    /// </summary>
    public SecurityBackfillController(IBackgroundTaskManager tasks, ICurrentUserService current)
    {
        _tasks = tasks; _current = current;
    }

    /// <summary>
    /// Backfill request payload parameters.
    /// </summary>
    public sealed record BackfillRequest(Guid? SecurityId, DateTime? FromDateUtc, DateTime? ToDateUtc);

    /// <summary>
    /// Enqueues a background task to backfill security prices for given security or all securities when null.
    /// </summary>
    [HttpPost]
    public ActionResult<BackgroundTaskInfo> Enqueue([FromBody] BackfillRequest req)
    {
        var payload = new { SecurityId = req.SecurityId?.ToString(), FromDateUtc = req.FromDateUtc?.ToString("o"), ToDateUtc = req.ToDateUtc?.ToString("o") };
        var info = _tasks.Enqueue(BackgroundTaskType.SecurityPricesBackfill, _current.UserId, payload, allowDuplicate: false);
        return Ok(info);
    }
}
