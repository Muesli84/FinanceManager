using FinanceManager.Application;
using FinanceManager.Application.Accounts;
using FinanceManager.Application.Contacts;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net.Mime;

namespace FinanceManager.Web.Controllers;

/// <summary>
/// Provides CRUD operations for bank accounts owned by the current user, including linking to a bank contact and symbol management.
/// </summary>
[ApiController]
[Route("api/accounts")]
[Produces(MediaTypeNames.Application.Json)]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
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

    /// <summary>
    /// Returns a (paged) list of accounts owned by the current user. Optional filter by bank contact id.
    /// </summary>
    /// <param name="skip">Number of items to skip.</param>
    /// <param name="take">Max items to return (1..200).</param>
    /// <param name="bankContactId">Optional bank contact filter.</param>
    /// <param name="ct">Cancellation token.</param>
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

    /// <summary>
    /// Gets a single account by id (must be owned by current user).
    /// </summary>
    /// <param name="id">Account id.</param>
    /// <param name="ct">Cancellation token.</param>
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

    /// <summary>
    /// Creates a new account. Either an existing bank contact id or a new bank contact name must be provided.
    /// </summary>
    /// <param name="req">Account creation payload.</param>
    /// <param name="ct">Cancellation token.</param>
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
                var createdContact = await _contacts.CreateAsync(_current.UserId, req.NewBankContactName.Trim(), ContactType.Bank, null, null, null, ct);
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

            var account = await _accounts.CreateAsync(_current.UserId, req.Name.Trim(), req.Type, req.Iban?.Trim(), bankContactId, req.SavingsPlanExpectation, ct);
            if (req.SymbolAttachmentId.HasValue)
            {
                await _accounts.SetSymbolAttachmentAsync(account.Id, _current.UserId, req.SymbolAttachmentId.Value, ct);
                account = await _accounts.GetAsync(account.Id, _current.UserId, ct) ?? account;
            }
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

    /// <summary>
    /// Updates an existing account (including optional bank contact change and symbol assignment).
    /// </summary>
    /// <param name="id">Account id.</param>
    /// <param name="req">Update payload.</param>
    /// <param name="ct">Cancellation token.</param>
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
                var createdContact = await _contacts.CreateAsync(_current.UserId, req.NewBankContactName.Trim(), ContactType.Bank, null, null, null, ct);
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
            var updated = await _accounts.UpdateAsync(id, _current.UserId, req.Name.Trim(), req.Iban?.Trim(), bankContactId, req.SavingsPlanExpectation, ct);
            if (updated is null) return NotFound();
            if (req.SymbolAttachmentId.HasValue)
            {
                await _accounts.SetSymbolAttachmentAsync(id, _current.UserId, req.SymbolAttachmentId.Value, ct);
                updated = await _accounts.GetAsync(id, _current.UserId, ct) ?? updated;
            }
            return Ok(updated);
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

    /// <summary>
    /// Deletes an account owned by the current user.
    /// </summary>
    /// <param name="id">Account id.</param>
    /// <param name="ct">Cancellation token.</param>
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

    /// <summary>
    /// Assigns an existing attachment as symbol for the account.
    /// </summary>
    /// <param name="id">Account id.</param>
    /// <param name="attachmentId">Attachment id.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("{id:guid}/symbol/{attachmentId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetSymbolAsync(Guid id, Guid attachmentId, CancellationToken ct)
    {
        try
        {
            await _accounts.SetSymbolAttachmentAsync(id, _current.UserId, attachmentId, ct);
            return NoContent();
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "SetSymbol failed for account {AccountId}", id);
            return NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SetSymbol failed for account {AccountId}", id);
            return Problem("Unexpected error", statusCode: 500);
        }
    }

    /// <summary>
    /// Clears any symbol attachment from the account.
    /// </summary>
    /// <param name="id">Account id.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpDelete("{id:guid}/symbol")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ClearSymbolAsync(Guid id, CancellationToken ct)
    {
        try
        {
            await _accounts.SetSymbolAttachmentAsync(id, _current.UserId, null, ct);
            return NoContent();
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "ClearSymbol failed for account {AccountId}", id);
            return NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ClearSymbol failed for account {AccountId}", id);
            return Problem("Unexpected error", statusCode: 500);
        }
    }
}
