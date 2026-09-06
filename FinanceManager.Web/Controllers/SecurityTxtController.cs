using FinanceManager.Application.Security;
using FinanceManager.Shared.Dtos.Admin;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net.Mime;

namespace FinanceManager.Web.Controllers;

/// <summary>
/// Exposes public security.txt endpoints and admin configuration endpoints.
/// </summary>
[ApiController]
public sealed class SecurityTxtController : ControllerBase
{
    private readonly ISecurityTxtSettingsService _service;

    /// <summary>Creates a new controller instance.</summary>
    /// <param name="service">The service.</param>
    public SecurityTxtController(ISecurityTxtSettingsService service)
    {
        _service = service;
    }

    /// <summary>Returns the RFC 9116 plain text document.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    [HttpGet("/security.txt")]
    [HttpGet("/.well-known/security.txt")]
    [AllowAnonymous]
    public async Task<IActionResult> GetSecurityTxtAsync(CancellationToken ct)
        => await RenderAsync(SecurityTxtFormat.PlainText, "text/plain; charset=utf-8", ct);

    /// <summary>Returns the Markdown representation.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    [HttpGet("/.well-known/security.md")]
    [AllowAnonymous]
    public async Task<IActionResult> GetSecurityMdAsync(CancellationToken ct)
        => await RenderAsync(SecurityTxtFormat.Markdown, "text/markdown; charset=utf-8", ct);

    /// <summary>Returns the HTML representation.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    [HttpGet("/.well-known/security.html")]
    [AllowAnonymous]
    public async Task<IActionResult> GetSecurityHtmlAsync(CancellationToken ct)
        => await RenderAsync(SecurityTxtFormat.Html, "text/html; charset=utf-8", ct);

    /// <summary>Returns the current admin settings.</summary>
    /// <returns>The result.</returns>
    [HttpGet("api/admin/security-txt")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Admin")]
    public async Task<IActionResult> GetSettingsAsync(CancellationToken ct) => Ok(await _service.GetAsync(ct));

    /// <summary>Updates the current admin settings.</summary>
    /// <param name="request">Request payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    [HttpPut("api/admin/security-txt")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Admin")]
    public async Task<IActionResult> UpdateSettingsAsync([FromBody] SecurityTxtSettingsUpdateRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        await _service.UpdateAsync(request, ct);
        return NoContent();
    }

    private async Task<IActionResult> RenderAsync(SecurityTxtFormat format, string contentType, CancellationToken ct)
    {
        var content = await _service.BuildContentAsync(format, ct);
        if (content is null) return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "security.txt is not configured yet." });
        return Content(content, contentType);
    }
}
