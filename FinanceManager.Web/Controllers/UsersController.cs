using FinanceManager.Application.Users;
using Microsoft.AspNetCore.Mvc;
using System.Net.Mime;

namespace FinanceManager.Web.Controllers;

/// <summary>
/// User management (read-only placeholder). Forwards calls to services and catches unexpected exceptions.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces(MediaTypeNames.Application.Json)]
public sealed class UsersController : ControllerBase
{
    private readonly IUserReadService _userReadService;
    private readonly ILogger<UsersController> _logger;

    public UsersController(IUserReadService userReadService, ILogger<UsersController> logger)
    {
        _userReadService = userReadService;
        _logger = logger;
    }

    /// <summary>
    /// Returns true if any user exists in the system.
    /// </summary>
    /// <remarks>
    /// This is a minimal endpoint; more administration endpoints (list, create, lock, etc.) will be added later.
    /// </remarks>
    [HttpGet("exists")]
    [ProducesResponseType(typeof(AnyUsersResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> HasAnyUsersAsync(CancellationToken ct)
    {
        try
        {
            bool any = await _userReadService.HasAnyUsersAsync(ct);
            return Ok(new AnyUsersResponse(any));
        }
        catch (OperationCanceledException)
        {
            // Let canceled requests bubble up as 499/Client Closed or generic cancellation.
            _logger.LogInformation("HasAnyUsersAsync cancelled");
            return Problem(statusCode: StatusCodes.Status499ClientClosedRequest, title: "Request cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while checking for existing users");
            return Problem(statusCode: StatusCodes.Status500InternalServerError, title: "Unexpected error", detail: ex.Message);
        }
    }
}
