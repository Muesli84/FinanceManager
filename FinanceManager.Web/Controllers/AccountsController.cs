using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using FinanceManager.Application;
using FinanceManager.Application.Accounts;
using FinanceManager.Application.Contacts;
using FinanceManager.Domain;
using FinanceManager.Domain.Contacts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceManager.Web.Controllers;

[ApiController]
[Route("api/accounts")]
[Produces(MediaTypeNames.Application.Json)]
[Authorize]
public sealed class AccountsController : ControllerBase
{
    private readonly IAccountService _accounts;
    private readonly IContactService _contacts;
    private readonly ICurrentUserService _current;
    private readonly ILogger<AccountsController> _logger;

    public AccountsController(IAccountService accounts, IContactService contacts, ICurrentUserService current, ILogger<AccountsController> logger)
    {
        _accounts = accounts;
        _contacts = contacts;
        _current = current;
        _logger = logger;
    }

    public sealed record AccountCreateRequest([
        Required, MinLength(2)] string Name,
        AccountType Type,
        string? Iban,
        Guid? BankContactId,
        string? NewBankContactName);

    public sealed record AccountUpdateRequest([
        Required, MinLength(2)] string Name,
        string? Iban,
        Guid? BankContactId,
        string? NewBankContactName);

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AccountDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAsync([FromQuery] int skip = 0, [FromQuery] int take = 100, [FromQuery] Guid? bankContactId = null, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 200);
        try
        {
            var list = await _accounts.ListAsync(_current.UserId, skip, take, ct);
            if (bankContactId.HasValue)
            {
                list = list.Where(a => a.BankContactId == bankContactId.Value).ToList();
            }
            return Ok(list);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "List accounts failed");
            return Problem("Unexpected error", statusCode: 500);
        }
    }

    [HttpGet("{id:guid}", Name = "GetAccount")]
    [ProducesResponseType(typeof(AccountDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken ct)
    {
        try
        {
            var dto = await _accounts.GetAsync(id, _current.UserId, ct);
            return dto is null ? NotFound() : Ok(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Get account {AccountId} failed", id);
            return Problem("Unexpected error", statusCode: 500);
        }
    }

    [HttpPost]
    [ProducesResponseType(typeof(AccountDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateAsync([FromBody] AccountCreateRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        try
        {
            Guid bankContactId;
            if (!string.IsNullOrWhiteSpace(req.NewBankContactName))
            {
                var createdContact = await _contacts.CreateAsync(_current.UserId, req.NewBankContactName.Trim(), ContactType.Bank, null, null, ct);
                bankContactId = createdContact.Id;
            }
            else if (req.BankContactId.HasValue)
            {
                bankContactId = req.BankContactId.Value;
            }
            else
            {
                return BadRequest(new { error = "Bank contact required (existing or new)" });
            }

            var account = await _accounts.CreateAsync(_current.UserId, req.Name.Trim(), req.Type, req.Iban?.Trim(), bankContactId, ct);
            return CreatedAtRoute("GetAccount", new { id = account.Id }, account);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Create account failed");
            return Problem("Unexpected error", statusCode: 500);
        }
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(AccountDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateAsync(Guid id, [FromBody] AccountUpdateRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        try
        {
            Guid bankContactId;
            if (!string.IsNullOrWhiteSpace(req.NewBankContactName))
            {
                var createdContact = await _contacts.CreateAsync(_current.UserId, req.NewBankContactName.Trim(), ContactType.Bank, null, null, ct);
                bankContactId = createdContact.Id;
            }
            else if (req.BankContactId.HasValue)
            {
                bankContactId = req.BankContactId.Value;
            }
            else
            {
                return BadRequest(new { error = "Bank contact required (existing or new)" });
            }
            var updated = await _accounts.UpdateAsync(id, _current.UserId, req.Name.Trim(), req.Iban?.Trim(), bankContactId, ct);
            return updated is null ? NotFound() : Ok(updated);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update account {AccountId} failed", id);
            return Problem("Unexpected error", statusCode: 500);
        }
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken ct)
    {
        try
        {
            var ok = await _accounts.DeleteAsync(id, _current.UserId, ct);
            return ok ? NoContent() : NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Delete account {AccountId} failed", id);
            return Problem("Unexpected error", statusCode: 500);
        }
    }
}
