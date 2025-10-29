using FinanceManager.Application;
using FinanceManager.Application.Contacts;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using System.Net.Mime;

namespace FinanceManager.Web.Controllers;

[ApiController]
[Route("api/contact-categories")]
[Produces(MediaTypeNames.Application.Json)]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class ContactCategoriesController : ControllerBase
{
    private readonly IContactCategoryService _svc;
    private readonly ICurrentUserService _current;
    private readonly ILogger<ContactCategoriesController> _logger;

    public ContactCategoriesController(IContactCategoryService svc, ICurrentUserService current, ILogger<ContactCategoriesController> logger)
    {
        _svc = svc;
        _current = current;
        _logger = logger;
    }

    public sealed record CreateCategoryRequest([Required, MinLength(2)] string Name);

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ContactCategoryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAsync(CancellationToken ct)
    {
        try
        {
            var list = await _svc.ListAsync(_current.UserId, ct);
            return Ok(list);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "List categories failed");
            return Problem("Unexpected error", statusCode: 500);
        }
    }

    [HttpPost]
    [ProducesResponseType(typeof(ContactCategoryDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateAsync([FromBody] CreateCategoryRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        try
        {
            var created = await _svc.CreateAsync(_current.UserId, req.Name, ct);
            return Created($"/api/contact-categories/{created.Id}", created);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Create category failed");
            return Problem("Unexpected error", statusCode: 500);
        }
    }
}