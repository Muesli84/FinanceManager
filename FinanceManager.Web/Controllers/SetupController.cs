using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using FinanceManager.Application;

[ApiController]
[Route("api/setup")]
[Authorize]
public sealed class SetupController : ControllerBase
{
    private readonly ISetupImportService _importService;
    private readonly ICurrentUserService _current;

    public SetupController(ISetupImportService importService, ICurrentUserService current)
    {
        _importService = importService;
        _current = current;
    }

    [HttpPost("import")]
    public async Task<IActionResult> ImportAsync([FromForm] IFormFile file, [FromForm] bool replaceExisting, CancellationToken ct)
    {
        if (file == null || file.Length == 0) return BadRequest("Keine Datei ausgewählt.");
        await _importService.ImportAsync(_current.UserId, file.OpenReadStream(), replaceExisting, ct);
        return Ok();
    }
}