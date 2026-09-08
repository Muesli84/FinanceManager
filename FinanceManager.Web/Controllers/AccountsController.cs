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

    /// <summary>
    /// Creates a new instance of <see cref="AccountsController"/>.
    /// </summary>
    /// <param name="accounts">Service for account operations.</param>
    /// <param name="contacts">Service for contact operations used to create or lookup bank contacts.</param>
    /// <param name="current">Service providing information about the current user.</param>
    /// <param name="logger">Logger instance for the controller.</param>
    public AccountsController(IAccountService accounts, IContactService contacts, ICurrentUserService current, ILogger<AccountsController> logger)
    {
        _accounts = accounts;
        _contacts = contacts;
        _current = current;
        _logger = logger;
    }

    /// <summary>
    /// Returns a (paged) list of accounts owned by the current user. Optional filter by bank contact id and search text.
    /// </summary>
    /// <param name="skip">Number of items to skip for paging.</param>
    /// <param name="take">Maximum number of items to return (clamped to 1..200).</param>
    /// <param name="bankContactId">Optional bank contact identifier to filter the accounts.</param>
    /// <param name="q">Optional search text for account name or normalized IBAN.</param>
    /// <param name="ct">Cancellation token to cancel the operation.</param>
    /// <returns>HTTP 200 with a list of <see cref="AccountDto"/> matching the criteria.</returns>
    /// <exception cref="Exception">May throw on unexpected server errors which are translated to HTTP 500.</exception>
    /// <response code="200">The HTTP 200 response.</response>
    /// <response code="400">The HTTP 400 response.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AccountDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListAsync([FromQuery] int skip = 0, [FromQuery] int take = 100, [FromQuery] Guid? bankContactId = null, [FromQuery] string? q = null, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 200);
        try
        {
            var list = await _accounts.ListAsync(_current.UserId, skip, take, bankContactId, q, ct);
            return Ok(list);
        }
        catch (ArgumentOutOfRangeException ex) when (ex.ParamName == "q")
        {
            return BadRequest(new { error = "Err_Invalid_q", message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "List accounts failed");
            return Problem("Unexpected error", statusCode: 500);
        }
    }

    /// <summary>
    /// Returns unpaged account statistics for the current user and optional search text.
    /// </summary>
    /// <param name="q">Optional search text for account name or normalized IBAN.</param>
    /// <param name="ct">Cancellation token to cancel the operation.</param>
    /// <returns>HTTP 200 with statistics for the filtered account scope.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    /// <response code="400">The HTTP 400 response.</response>
    [HttpGet("statistics")]
    [ProducesResponseType(typeof(AccountStatisticsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> StatisticsAsync([FromQuery] string? q = null, CancellationToken ct = default)
    {
        try
        {
            return Ok(await _accounts.GetStatisticsAsync(_current.UserId, q, ct));
        }
        catch (ArgumentOutOfRangeException ex) when (ex.ParamName == "q")
        {
            return BadRequest(new { error = "Err_Invalid_q", message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Get account statistics failed");
            return Problem("Unexpected error", statusCode: 500);
        }
    }

    /// <summary>
    /// Gets a single account by id. The account must belong to the current user.
    /// </summary>
    /// <param name="id">Account identifier.</param>
    /// <param name="ct">Cancellation token to cancel the operation.</param>
    /// <returns>
    /// HTTP 200 with <see cref="AccountDto"/> when found; HTTP 404 when the account does not exist or is not owned by the current user.
    /// </returns>
    /// <exception cref="Exception">May throw on unexpected server errors which are translated to HTTP 500.</exception>
    /// <response code="200">The HTTP 200 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
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
    /// Creates a new account for the current user. Either an existing bank contact id or a new bank contact name must be provided; when none are provided a bank contact will be auto-created.
    /// </summary>
    /// <param name="req">Account creation payload containing name, type, optional IBAN, bank contact info and optional symbol attachment id.</param>
    /// <param name="ct">Cancellation token to cancel the operation.</param>
    /// <returns>
    /// HTTP 201 with the created <see cref="AccountDto"/> on success.
    /// HTTP 400 when provided arguments are invalid.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when provided input values are invalid; translated to HTTP 400 with error details.</exception>
    /// <exception cref="Exception">Unexpected server errors are translated to HTTP 500.</exception>
    /// <response code="201">The HTTP 201 response.</response>
    /// <response code="400">The HTTP 400 response.</response>
    [HttpPost]
    [ProducesResponseType(typeof(AccountDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateAsync([FromBody] AccountCreateRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        try
        {
            var bankContactId = await ResolveBankContactAsync(req.NewBankContactName, req.BankContactId, req.Name, req.Iban, allowAutoCreate: true, ct);

            var account = await _accounts.CreateAsync(_current.UserId, req.Name.Trim(), req.Type, req.Iban?.Trim(), bankContactId!.Value, req.SavingsPlanExpectation, req.SecurityProcessingEnabled, req.IsCollectionAccount, ct);
            if (req.SymbolAttachmentId.HasValue)
            {
                await _accounts.SetSymbolAttachmentAsync(account.Id, _current.UserId, req.SymbolAttachmentId.Value, ct);
                account = await _accounts.GetAsync(account.Id, _current.UserId, ct) ?? account;
            }
            return CreatedAtRoute("GetAccount", new { id = account.Id }, account);
        }
        catch (ArgumentException ex)
        {
            var code = !string.IsNullOrWhiteSpace(ex.ParamName) ? $"Err_Invalid_{ex.ParamName}" : "Err_InvalidArgument";
            return BadRequest(new { error = code, message = ex.Message });
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
    /// <param name="id">Identifier of the account to update.</param>
    /// <param name="req">Update payload containing new values.</param>
    /// <param name="ct">Cancellation token to cancel the operation.</param>
    /// <returns>
    /// HTTP 200 with the updated <see cref="AccountDto"/> when successful; HTTP 404 when the account does not exist; HTTP 400 when input is invalid.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when provided input values are invalid; translated to HTTP 400.</exception>
    /// <exception cref="Exception">Unexpected server errors are translated to HTTP 500.</exception>
    /// <response code="200">The HTTP 200 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(AccountDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateAsync(Guid id, [FromBody] AccountUpdateRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        try
        {
            var bankContactId = await ResolveBankContactAsync(req.NewBankContactName, req.BankContactId, req.Name, null, allowAutoCreate: false, ct);
            if (bankContactId is null)
                return BadRequest(new { error = "Bank contact required (existing or new)" });

            var updated = await _accounts.UpdateAsync(id, _current.UserId, req.Name.Trim(), req.Iban?.Trim(), bankContactId.Value, req.SavingsPlanExpectation, req.SecurityProcessingEnabled, req.IsCollectionAccount, ct);
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
            var code = !string.IsNullOrWhiteSpace(ex.ParamName) ? $"Err_Invalid_{ex.ParamName}" : "Err_InvalidArgument";
            return BadRequest(new { error = code, message = ex.Message });
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
    /// <param name="id">Identifier of the account to delete.</param>
    /// <param name="ct">Cancellation token to cancel the operation.</param>
    /// <returns>HTTP 204 when deletion succeeded; HTTP 404 when the account does not exist.</returns>
    /// <exception cref="Exception">Thrown on unexpected server errors which are translated to HTTP 500.</exception>
    /// <response code="204">The HTTP 204 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
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
    /// <param name="id">Account identifier.</param>
    /// <param name="attachmentId">Attachment identifier to assign as symbol.</param>
    /// <param name="ct">Cancellation token to cancel the operation.</param>
    /// <returns>HTTP 204 when symbol assignment succeeds; HTTP 404 when account or attachment not found; HTTP 400 when input invalid.</returns>
    /// <exception cref="ArgumentException">Thrown when provided identifiers are invalid; translated to HTTP 404/400 as used in the controller.</exception>
    /// <response code="204">The HTTP 204 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
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
            var code = !string.IsNullOrWhiteSpace(ex.ParamName) ? $"Err_Invalid_{ex.ParamName}" : "Err_NotFound";
            return NotFound(new { error = code, message = ex.Message });
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
    /// <param name="id">Account identifier.</param>
    /// <param name="ct">Cancellation token to cancel the operation.</param>
    /// <returns>HTTP 204 when the symbol was cleared; HTTP 404 when the account or resource was not found.</returns>
    /// <exception cref="ArgumentException">Thrown when provided identifiers are invalid; translated to HTTP 404/400 as used in the controller.</exception>
    /// <response code="204">The HTTP 204 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
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
            var code = !string.IsNullOrWhiteSpace(ex.ParamName) ? $"Err_Invalid_{ex.ParamName}" : "Err_NotFound";
            return NotFound(new { error = code, message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ClearSymbol failed for account {AccountId}", id);
            return Problem("Unexpected error", statusCode: 500);
        }
    }

    /// <summary>
    /// Returns the list of linked sub-IBANs for a collection account.
    /// </summary>
    /// <param name="id">Account identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>HTTP 200 with a list of IBAN strings; HTTP 404 when account not found.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    [HttpGet("{id:guid}/linked-ibans")]
    [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetLinkedIbansAsync(Guid id, CancellationToken ct)
    {
        try
        {
            var ibans = await _accounts.GetLinkedIbansAsync(id, _current.UserId, ct);
            if (ibans is null) return NotFound();
            return Ok(ibans);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetLinkedIbans failed for account {AccountId}", id);
            return Problem("Unexpected error", statusCode: 500);
        }
    }

    /// <summary>
    /// Adds a linked sub-IBAN to a collection account.
    /// </summary>
    /// <param name="id">Account identifier.</param>
    /// <param name="req">Request payload containing the IBAN to add.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>HTTP 204 when added; HTTP 400 when invalid; HTTP 404 when account not found.</returns>
    /// <response code="204">The HTTP 204 response.</response>
    /// <response code="400">The HTTP 400 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    [HttpPost("{id:guid}/linked-ibans")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddLinkedIbanAsync(Guid id, [FromBody] AccountLinkedIbanUpsertRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        try
        {
            await _accounts.AddLinkedIbanAsync(id, _current.UserId, req.Iban, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = "Err_Iban_Duplicate", message = ex.Message });
        }
        catch (ArgumentException ex) when (ex.ParamName == nameof(req.Iban))
        {
            return BadRequest(new { error = "Err_Invalid_Iban", message = ex.Message });
        }
        catch (ArgumentException ex) when (ex.ParamName == "accountId")
        {
            return NotFound(new { error = "Err_NotFound", message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = "Err_InvalidArgument", message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AddLinkedIban failed for account {AccountId}", id);
            return Problem("Unexpected error", statusCode: 500);
        }
    }

    /// <summary>
    /// Removes a linked sub-IBAN from a collection account.
    /// </summary>
    /// <param name="id">Account identifier.</param>
    /// <param name="iban">The IBAN to remove.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>HTTP 204 when removed; HTTP 404 when not found.</returns>
    /// <response code="204">The HTTP 204 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    [HttpDelete("{id:guid}/linked-ibans/{iban}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveLinkedIbanAsync(Guid id, string iban, CancellationToken ct)
    {
        try
        {
            var ok = await _accounts.RemoveLinkedIbanAsync(id, _current.UserId, iban, ct);
            return ok ? NoContent() : NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RemoveLinkedIban failed for account {AccountId}", id);
            return Problem("Unexpected error", statusCode: 500);
        }
    }

    private async Task<Guid?> ResolveBankContactAsync(string? newBankContactName, Guid? bankContactId, string? accountName, string? iban, bool allowAutoCreate, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(newBankContactName))
        {
            var created = await _contacts.CreateAsync(_current.UserId, newBankContactName.Trim(), ContactType.Bank, null, null, null, ct);
            return created.Id;
        }

        if (bankContactId.HasValue)
            return bankContactId.Value;

        if (allowAutoCreate)
        {
            var contactName = !string.IsNullOrWhiteSpace(accountName) ? accountName.Trim()
                : (!string.IsNullOrWhiteSpace(iban) ? iban!.Trim() : "Bankkonto");
            var created = await _contacts.CreateAsync(_current.UserId, contactName, ContactType.Bank, null, null, null, ct);
            return created.Id;
        }

        return null;
    }
}
