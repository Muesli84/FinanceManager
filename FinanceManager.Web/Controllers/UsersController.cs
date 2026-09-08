using FinanceManager.Application;
using FinanceManager.Application.Users;
using FinanceManager.Infrastructure.Auth;
using FinanceManager.Shared.Dtos.Common;
using FinanceManager.Web.Infrastructure.ApiErrors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using System.Net.Mime;
using FinanceManager.Application.Demo;
using FinanceManager.Web.Controllers;

namespace FinanceManager.Web.Controllers;

/// <summary>
/// User management (read-only placeholder). Forwards calls to services and catches unexpected exceptions.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces(MediaTypeNames.Application.Json)]
public sealed class UsersController : ControllerBase
{
    private const string Origin = "API_Users";

    private readonly IUserReadService _userReadService;
    private readonly IDemoDataService _demoDataService;
    private readonly ILogger<UsersController> _logger;
    private readonly IStringLocalizer<Controller> _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="UsersController"/> class.
    /// </summary>
    /// <param name="userReadService">Service used to read user information.</param>
    /// <param name="demoDataService">Service responsible for creating demo data for a user.</param>
    /// <param name="logger">Logger instance for this controller.</param>
    /// <param name="localizer">Localizer for error messages.</param>
    public UsersController(IUserReadService userReadService, IDemoDataService demoDataService, ILogger<UsersController> logger, IStringLocalizer<Controller> localizer)
    {
        _userReadService = userReadService;
        _demoDataService = demoDataService;
        _logger = logger;
        _localizer = localizer;
    }

    /// <summary>
    /// Returns true if any user exists in the system.
    /// </summary>
    /// <param name="ct">Cancellation token used to cancel the operation.</param>
    /// <returns>
    /// An <see cref="IActionResult"/> which contains a 200 OK response with an <see cref="AnyUsersResponse"/> value when successful.
    /// In case of cancellation a ProblemDetails response with status code 499 (Client Closed Request) is returned.
    /// For unexpected errors a 500 Internal Server Error ProblemDetails response is returned.
    /// </returns>
    /// <remarks>
    /// This is a minimal endpoint; more administration endpoints (list, create, lock, etc.) will be added later.
    /// </remarks>
    /// <exception cref="OperationCanceledException">When the provided cancellation token is triggered. This is handled and mapped to a 499 response.</exception>
    /// <exception cref="Exception">An unexpected exception that is logged and results in a 500 response.</exception>
    /// <response code="200">The HTTP 200 response.</response>
    /// <response code="429">The HTTP 429 response.</response>
    /// <response code="500">The HTTP 500 response.</response>
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
            var err = ApiErrorDto.Create(Origin, "Err_Cancelled", "Request cancelled");
            return StatusCode(StatusCodes.Status499ClientClosedRequest, err);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while checking for existing users");
            return StatusCode(StatusCodes.Status500InternalServerError, ApiErrorFactory.Unexpected(Origin, _localizer));
        }
    }

    /// <summary>
    /// Creates demo data for the specified user. This is intended for development and demo scenarios only.
    /// The method will schedule or execute creation of various domain entities under the provided user account.
    /// </summary>
    /// <param name="userId">Identifier of the user for whom demo data should be created.</param>
    /// <param name="req">Request payload; when its <c>createPostings</c> flag is true, statement imports and entries (postings) will also be created as part of the demo data.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// 202 Accepted when the request was accepted for processing.
    /// 400 Bad Request when the <paramref name="userId"/> is empty.
    /// 500 Internal Server Error for unexpected errors.
    /// </returns>
    /// <response code="202">The HTTP 202 response.</response>
    /// <response code="400">The HTTP 400 response.</response>
    /// <response code="500">The HTTP 500 response.</response>
    [HttpPost("demo/{userId:guid}")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> CreateDemoDataAsync(Guid userId, [FromBody] DemoRequest req, CancellationToken ct)
    {
        if (userId == Guid.Empty)
        {
            var ex = new ArgumentException("userId required", nameof(userId));
            return BadRequest(ApiErrorFactory.FromArgumentException(Origin, ex, _localizer));
        }

        try
        {
            await _demoDataService.CreateDemoDataAsync(userId, req.createPostings, ct);
            return Accepted();
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("CreateDemoDataAsync cancelled for {UserId}", userId);
            var err = ApiErrorDto.Create(Origin, "Err_Cancelled", "Request cancelled");
            return StatusCode(StatusCodes.Status499ClientClosedRequest, err);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while creating demo data for user {UserId}", userId);
            return StatusCode(StatusCodes.Status500InternalServerError, ApiErrorFactory.Unexpected(Origin, _localizer));
        }
    }
}
