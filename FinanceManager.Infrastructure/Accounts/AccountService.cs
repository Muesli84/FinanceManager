using FinanceManager.Application.Accounts;
using FinanceManager.Domain.Attachments; // added
using Microsoft.EntityFrameworkCore;

namespace FinanceManager.Infrastructure.Accounts;

/// <summary>
/// Service providing CRUD operations for user bank accounts.
/// Implements <see cref="IAccountService"/> and performs validation against related contacts and uniqueness constraints.
/// </summary>
public sealed class AccountService : IAccountService
{
    private const string UnknownBankContactKey = "unknown";
    private readonly AppDbContext _db;
    private readonly IAccountStatisticsPeriodProvider _periodProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="AccountService"/> class.
    /// </summary>
    /// <param name="db">Database context used to persist accounts and related entities.</param>
    public AccountService(AppDbContext db)
        : this(db, new UtcAccountStatisticsPeriodProvider())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AccountService"/> class.
    /// </summary>
    /// <param name="db">Database context used to persist accounts and related entities.</param>
    /// <param name="periodProvider">Provider for local period boundaries used in statistics.</param>
    public AccountService(AppDbContext db, IAccountStatisticsPeriodProvider periodProvider)
    {
        _db = db;
        _periodProvider = periodProvider;
    }

    /// <summary>
    /// Creates a new account for the specified owner with default savings plan expectation (<see cref="SavingsPlanExpectation.Optional"/>).
    /// </summary>
    /// <param name="ownerUserId">Owner user identifier.</param>
    /// <param name="name">Display name of the account. Must not be null or whitespace.</param>
    /// <param name="type">Type of the account.</param>
    /// <param name="iban">Optional IBAN; when provided it must be unique for the user.</param>
    /// <param name="bankContactId">Contact id of the bank (must exist and be of type <see cref="ContactType.Bank"/>).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created <see cref="AccountDto"/> representing the persisted account.</returns>
    /// <exception cref="ArgumentException">Thrown when the bank contact is invalid, the name is missing or already exists, or the IBAN already exists for the user.</exception>
    private async Task<AccountDto> CreateAsync(Guid ownerUserId, string name, AccountType type, string? iban, Guid bankContactId, CancellationToken ct)
    {
        // default expectation for older callers
        return await CreateAsync(ownerUserId, name, type, iban, bankContactId, SavingsPlanExpectation.Optional, securityProcessingEnabled: true, isCollectionAccount: false, ct);
    }

    /// <summary>
    /// Creates a new account for the specified owner.
    /// </summary>
    /// <param name="ownerUserId">Owner user identifier.</param>
    /// <param name="name">Display name of the account. Must not be null or whitespace.</param>
    /// <param name="type">Type of the account.</param>
    /// <param name="iban">Optional IBAN; when provided it must be unique for the user.</param>
    /// <param name="bankContactId">Contact id of the bank (must exist and be of type <see cref="ContactType.Bank"/>).</param>
    /// <param name="expectation">Savings plan expectation for the account.</param>
    /// <param name="securityProcessingEnabled">Whether postings on this account are eligible for automatic security detection/processing.</param>
    /// <param name="isCollectionAccount">Whether this account acts as a collection account (aggregating postings from linked IBANs).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created <see cref="AccountDto"/> representing the persisted account.</returns>
    /// <exception cref="ArgumentException">Thrown when the bank contact is invalid, the name is missing or already exists, or the IBAN already exists for the user.</exception>
    public async Task<AccountDto> CreateAsync(Guid ownerUserId, string name, AccountType type, string? iban, Guid bankContactId, SavingsPlanExpectation expectation, bool securityProcessingEnabled, bool isCollectionAccount, CancellationToken ct)
    {
        if (!await _db.Contacts.AsNoTracking().AnyAsync(c => c.Id == bankContactId && c.OwnerUserId == ownerUserId && c.Type == ContactType.Bank, ct))
        {
            throw new ArgumentException("Bank contact invalid", nameof(bankContactId));
        }

        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name required", nameof(name));
        name = name.Trim();

        var nameExists = await _db.Accounts.AsNoTracking().AnyAsync(a => a.OwnerUserId == ownerUserId && a.Name.ToLower() == name.ToLower(), ct);
        if (nameExists)
        {
            throw new ArgumentException("Account name already exists", nameof(name));
        }

        if (!string.IsNullOrWhiteSpace(iban))
        {
            var norm = iban.Trim();
            bool exists = await _db.Accounts.AsNoTracking().AnyAsync(a => a.OwnerUserId == ownerUserId && a.Iban == norm, ct);
            if (exists)
            {
                throw new ArgumentException("IBAN already exists for user", nameof(iban));
            }
            iban = norm;
        }
        var account = new Domain.Accounts.Account(ownerUserId, type, name, iban, bankContactId);
        account.SetSavingsPlanExpectation(expectation);
        account.SetSecurityProcessingEnabled(securityProcessingEnabled);
        account.SetIsCollectionAccount(isCollectionAccount);
        _db.Accounts.Add(account);
        await _db.SaveChangesAsync(ct);
        return new AccountDto(account.Id, account.Name, account.Type, account.Iban, account.CurrentBalance, account.BankContactId, account.SymbolAttachmentId, account.SavingsPlanExpectation, account.SecurityProcessingEnabled)
        {
            IsCollectionAccount = account.IsCollectionAccount,
            LinkedIbans = Array.Empty<string>()
        };
    }

    /// <summary>
    /// Updates an existing account.
    /// </summary>
    /// <param name="id">Identifier of the account to update.</param>
    /// <param name="ownerUserId">Owner user identifier.</param>
    /// <param name="name">New display name. Must not be null or whitespace.</param>
    /// <param name="iban">Optional new IBAN; when provided it must be unique for the user (excluding this account).</param>
    /// <param name="bankContactId">Contact id of the bank (must exist and be of type <see cref="ContactType.Bank"/>).</param>
    /// <param name="expectation">Savings plan expectation.</param>
    /// <param name="securityProcessingEnabled">Whether postings on this account are eligible for automatic security detection/processing.</param>
    /// <param name="isCollectionAccount">Whether this account acts as a collection account (aggregating postings from linked IBANs).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated <see cref="AccountDto"/>, or <c>null</c> when the account was not found.</returns>
    /// <exception cref="ArgumentException">Thrown when the bank contact is invalid, the name is missing or already exists, or the IBAN already exists for the user.</exception>
    public async Task<AccountDto?> UpdateAsync(Guid id, Guid ownerUserId, string name, string? iban, Guid bankContactId, SavingsPlanExpectation expectation, bool securityProcessingEnabled, bool isCollectionAccount, CancellationToken ct)
    {
        var account = await _db.Accounts.FirstOrDefaultAsync(a => a.Id == id && a.OwnerUserId == ownerUserId, ct);
        if (account == null) return null;
        if (!await _db.Contacts.AsNoTracking().AnyAsync(c => c.Id == bankContactId && c.OwnerUserId == ownerUserId && c.Type == ContactType.Bank, ct))
        {
            throw new ArgumentException("Bank contact invalid", nameof(bankContactId));
        }

        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name required", nameof(name));
        name = name.Trim();

        var duplicateName = await _db.Accounts.AsNoTracking().AnyAsync(a => a.OwnerUserId == ownerUserId && a.Id != id && a.Name.ToLower() == name.ToLower(), ct);
        if (duplicateName)
        {
            throw new ArgumentException("Account name already exists", nameof(name));
        }

        if (!string.IsNullOrWhiteSpace(iban))
        {
            iban = iban.Trim();
            bool exists = await _db.Accounts.AsNoTracking().AnyAsync(a => a.OwnerUserId == ownerUserId && a.Iban == iban && a.Id != id, ct);
            if (exists) throw new ArgumentException("IBAN already exists for user", nameof(iban));
        }
        account.Rename(name);
        account.SetIban(iban);
        account.SetBankContact(bankContactId);
        account.SetSavingsPlanExpectation(expectation);
        account.SetSecurityProcessingEnabled(securityProcessingEnabled);
        account.SetIsCollectionAccount(isCollectionAccount);
        await _db.SaveChangesAsync(ct);

        var linkedIbans = await _db.AccountLinkedIbans
            .AsNoTracking()
            .Where(li => li.AccountId == id)
            .Select(li => li.Iban)
            .ToListAsync(ct);

        return new AccountDto(account.Id, account.Name, account.Type, account.Iban, account.CurrentBalance, account.BankContactId, account.SymbolAttachmentId, account.SavingsPlanExpectation, account.SecurityProcessingEnabled)
        {
            IsCollectionAccount = account.IsCollectionAccount,
            LinkedIbans = linkedIbans
        };
    }

    /// <summary>
    /// Deletes the account and related attachments. If the associated bank contact becomes unused it is removed as well.
    /// </summary>
    /// <param name="id">Identifier of the account to delete.</param>
    /// <param name="ownerUserId">Owner user identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> when the account existed and was removed; otherwise <c>false</c>.</returns>
    public async Task<bool> DeleteAsync(Guid id, Guid ownerUserId, CancellationToken ct)
    {
        var account = await _db.Accounts.FirstOrDefaultAsync(a => a.Id == id && a.OwnerUserId == ownerUserId, ct);
        if (account == null) return false;
        var bankContactId = account.BankContactId;

        // Delete attachments linked to this account
        var accountAttQuery = _db.Attachments
            .Where(a => a.OwnerUserId == ownerUserId && a.EntityKind == AttachmentEntityKind.Account && a.EntityId == account.Id);
        if (_db.Database.IsRelational())
        {
            await accountAttQuery.ExecuteDeleteAsync(ct);
        }
        else
        {
            var atts = await accountAttQuery.ToListAsync(ct);
            if (atts.Count > 0) _db.Attachments.RemoveRange(atts);
        }

        _db.Accounts.Remove(account);
        await _db.SaveChangesAsync(ct);

        bool anyOther = await _db.Accounts.AsNoTracking().AnyAsync(a => a.BankContactId == bankContactId, ct);
        if (!anyOther)
        {
            var bankContact = await _db.Contacts.FirstOrDefaultAsync(c => c.Id == bankContactId && c.Type == ContactType.Bank, ct);
            if (bankContact != null)
            {
                // Delete attachments linked to the bank contact being auto-removed
                var contactAttQuery = _db.Attachments
                    .Where(a => a.OwnerUserId == bankContact.OwnerUserId && a.EntityKind == AttachmentEntityKind.Contact && a.EntityId == bankContact.Id);
                if (_db.Database.IsRelational())
                {
                    await contactAttQuery.ExecuteDeleteAsync(ct);
                }
                else
                {
                    var catts = await contactAttQuery.ToListAsync(ct);
                    if (catts.Count > 0) _db.Attachments.RemoveRange(catts);
                }

                _db.Contacts.Remove(bankContact);
                await _db.SaveChangesAsync(ct);
            }
        }
        return true;
    }

    /// <summary>
    /// Lists accounts for the specified owner with optional paging. The method returns symbol resolution that falls back from account -> contact -> contact category.
    /// </summary>
    /// <param name="ownerUserId">Owner user identifier.</param>
    /// <param name="skip">Items to skip for paging.</param>
    /// <param name="take">Items to take for paging.</param>
    /// <param name="bankContactId">Optional bank contact id filter.</param>
    /// <param name="q">Optional search text for account name or normalized IBAN.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of <see cref="AccountDto"/> instances.</returns>
    public async Task<IReadOnlyList<AccountDto>> ListAsync(Guid ownerUserId, int skip, int take, Guid? bankContactId, string? q, CancellationToken ct)
    {
        var scopedAccounts = await LoadAccountScopeAsync(ownerUserId, bankContactId, q, ct);
        var dtos = scopedAccounts
            .OrderBy(a => a.Name, StringComparer.InvariantCultureIgnoreCase)
            .Skip(skip)
            .Take(take)
            .Select(a => a.ToDto())
            .ToList();

        // Load linked IBANs for all returned accounts in a single query
        var accountIds = dtos.Select(d => d.Id).ToList();
        if (accountIds.Count > 0)
        {
            var allLinkedIbans = await _db.AccountLinkedIbans
                .AsNoTracking()
                .Where(li => accountIds.Contains(li.AccountId))
                .Select(li => new { li.AccountId, li.Iban })
                .ToListAsync(ct);

            var ibansByAccount = allLinkedIbans
                .GroupBy(li => li.AccountId)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(li => li.Iban).ToList());

            return dtos.Select(d => ibansByAccount.TryGetValue(d.Id, out var ibans)
                ? d with { LinkedIbans = ibans }
                : d).ToList();
        }

        return dtos;
    }

    /// <inheritdoc/>
    public async Task<AccountStatisticsDto> GetStatisticsAsync(Guid ownerUserId, string? q, CancellationToken ct)
    {
        var scopedAccounts = await LoadAccountScopeAsync(ownerUserId, bankContactId: null, q, ct);

        if (scopedAccounts.Count == 0)
        {
            return AccountStatisticsDto.Empty;
        }

        var accountIds = scopedAccounts.Select(a => a.Id).ToList();
        var period = await _periodProvider.GetPeriodAsync(ownerUserId, ct);

        var yearToDate = await _db.Postings
            .AsNoTracking()
            .Where(p => p.Kind == PostingKind.Bank
                && p.AccountId.HasValue
                && accountIds.Contains(p.AccountId.Value)
                && p.BookingDate >= period.YearStart
                && p.BookingDate < period.TomorrowExclusive)
            .SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;

        var monthToDate = await _db.Postings
            .AsNoTracking()
            .Where(p => p.Kind == PostingKind.Bank
                && p.AccountId.HasValue
                && accountIds.Contains(p.AccountId.Value)
                && p.BookingDate >= period.MonthStart
                && p.BookingDate < period.TomorrowExclusive)
            .SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;

        var byType = BuildGroups(
            scopedAccounts,
            a => a.Type.ToString(),
            a => null);

        var byContact = BuildGroups(
            scopedAccounts,
            a => a.StatisticsBankContactId?.ToString("D") ?? UnknownBankContactKey,
            a => a.StatisticsBankContactName);

        var totalBalance = scopedAccounts.Sum(a => a.CurrentBalance);
        var totalGrossMagnitude = byType.Sum(g => g.GrossMagnitude);

        return new AccountStatisticsDto(
            scopedAccounts.Count,
            totalBalance,
            yearToDate,
            monthToDate,
            totalGrossMagnitude,
            byType,
            byContact);
    }

    /// <summary>
    /// Retrieves a single account with resolved symbol attachment fallback.
    /// </summary>
    /// <param name="id">Account identifier.</param>
    /// <param name="ownerUserId">Owner user identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The mapped <see cref="AccountDto"/>, or <c>null</c> when not found.</returns>
    public async Task<AccountDto?> GetAsync(Guid id, Guid ownerUserId, CancellationToken ct)
    {
        var query = from a in _db.Accounts.AsNoTracking()
                    where a.OwnerUserId == ownerUserId && a.Id == id
                    join c in _db.Contacts.AsNoTracking() on a.BankContactId equals c.Id into cj
                    from c in cj.DefaultIfEmpty()
                    join cat in _db.ContactCategories.AsNoTracking() on c.CategoryId equals cat.Id into catj
                    from cat in catj.DefaultIfEmpty()
                    orderby a.Name
                    select new AccountDto(
                        a.Id,
                        a.Name,
                        a.Type,
                        a.Iban,
                        a.CurrentBalance,
                        a.BankContactId,
                        (a.SymbolAttachmentId.HasValue && a.SymbolAttachmentId!.Value != Guid.Empty) ? a.SymbolAttachmentId
                            : (c != null && c.SymbolAttachmentId.HasValue && c.SymbolAttachmentId.Value != Guid.Empty) ? c.SymbolAttachmentId
                            : cat.SymbolAttachmentId,
                        a.SavingsPlanExpectation,
                        a.SecurityProcessingEnabled
                    )
                    {
                        IsCollectionAccount = a.IsCollectionAccount
                    };

        var dto = await query.FirstOrDefaultAsync(ct);
        if (dto == null) return null;

        var linkedIbans = await _db.AccountLinkedIbans
            .AsNoTracking()
            .Where(li => li.AccountId == id)
            .Select(li => li.Iban)
            .ToListAsync(ct);

        return dto with { LinkedIbans = linkedIbans };
    }

    /// <summary>
    /// Synchronous variant of <see cref="GetAsync(Guid, Guid, CancellationToken)"/> for read-only scenarios.
    /// </summary>
    /// <param name="id">Account identifier.</param>
    /// <param name="ownerUserId">Owner user identifier.</param>
    /// <returns>The mapped <see cref="AccountDto"/>, or <c>null</c> when not found.</returns>
    public AccountDto? Get(Guid id, Guid ownerUserId)
    {
        var query = from a in _db.Accounts.AsNoTracking()
                    where a.OwnerUserId == ownerUserId && a.Id == id
                    join c in _db.Contacts.AsNoTracking() on a.BankContactId equals c.Id into cj
                    from c in cj.DefaultIfEmpty()
                    join cat in _db.ContactCategories.AsNoTracking() on c.CategoryId equals cat.Id into catj
                    from cat in catj.DefaultIfEmpty()
                    orderby a.Name
                    select new AccountDto(
                        a.Id,
                        a.Name,
                        a.Type,
                        a.Iban,
                        a.CurrentBalance,
                        a.BankContactId,
                        (a.SymbolAttachmentId.HasValue && a.SymbolAttachmentId!.Value != Guid.Empty) ? a.SymbolAttachmentId
                            : (c != null && c.SymbolAttachmentId.HasValue && c.SymbolAttachmentId.Value != Guid.Empty) ? c.SymbolAttachmentId
                            : cat.SymbolAttachmentId,
                        a.SavingsPlanExpectation,
                        a.SecurityProcessingEnabled
                    )
                    {
                        IsCollectionAccount = a.IsCollectionAccount
                    };

        var dto = query.FirstOrDefault();
        if (dto == null) return null;

        var linkedIbans = _db.AccountLinkedIbans
            .AsNoTracking()
            .Where(li => li.AccountId == id)
            .Select(li => li.Iban)
            .ToList();

        return dto with { LinkedIbans = linkedIbans };
    }

    /// <summary>
    /// Sets or clears the symbol attachment for an account.
    /// </summary>
    /// <param name="id">Account identifier.</param>
    /// <param name="ownerUserId">Owner user identifier.</param>
    /// <param name="attachmentId">Attachment identifier to set, or <c>null</c> to clear.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the operation has finished.</returns>
    /// <exception cref="ArgumentException">Thrown when the account was not found for the specified id and owner.</exception>
    public async Task SetSymbolAttachmentAsync(Guid id, Guid ownerUserId, Guid? attachmentId, CancellationToken ct)
    {
        var account = await _db.Accounts.FirstOrDefaultAsync(a => a.Id == id && a.OwnerUserId == ownerUserId, ct);
        if (account == null) throw new ArgumentException("Account not found", nameof(id));
        account.SetSymbolAttachment(attachmentId);
        await _db.SaveChangesAsync(ct);
    }

    /// <inheritdoc/>
    public async Task AddLinkedIbanAsync(Guid accountId, Guid ownerUserId, string iban, CancellationToken ct)
    {
        var account = await _db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId && a.OwnerUserId == ownerUserId, ct);
        if (account == null) throw new ArgumentException("Account not found", nameof(accountId));

        if (string.IsNullOrWhiteSpace(iban)) throw new ArgumentException("IBAN required", nameof(iban));
        iban = iban.Trim();

        bool exists = await _db.AccountLinkedIbans
            .AsNoTracking()
            .AnyAsync(li => li.AccountId == accountId && li.Iban == iban, ct);
        if (exists) throw new InvalidOperationException("IBAN already linked to this account");

        var entry = new Domain.Accounts.AccountLinkedIban(accountId, iban);
        _db.AccountLinkedIbans.Add(entry);
        await _db.SaveChangesAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<bool> RemoveLinkedIbanAsync(Guid accountId, Guid ownerUserId, string iban, CancellationToken ct)
    {
        var account = await _db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == accountId && a.OwnerUserId == ownerUserId, ct);
        if (account == null) return false;

        var entry = await _db.AccountLinkedIbans
            .FirstOrDefaultAsync(li => li.AccountId == accountId && li.Iban == iban, ct);
        if (entry == null) return false;

        _db.AccountLinkedIbans.Remove(entry);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>?> GetLinkedIbansAsync(Guid accountId, Guid ownerUserId, CancellationToken ct)
    {
        var exists = await _db.Accounts.AsNoTracking().AnyAsync(a => a.Id == accountId && a.OwnerUserId == ownerUserId, ct);
        if (!exists) return null;

        return await _db.AccountLinkedIbans
            .AsNoTracking()
            .Where(li => li.AccountId == accountId)
            .Select(li => li.Iban)
            .ToListAsync(ct);
    }

    private async Task<List<AccountScopeRow>> LoadAccountScopeAsync(Guid ownerUserId, Guid? bankContactId, string? q, CancellationToken ct)
    {
        var accountQuery = ApplyOwnerAndBankContactFilters(_db.Accounts.AsNoTracking(), ownerUserId, bankContactId);
        var search = AccountSearchText.Normalize(q);

        // Owner and bank-contact predicates stay database-side. The search contract is applied once here
        // in the server process to keep Unicode name matching and normalized IBAN matching provider-stable.
        var rows = await (
            from a in accountQuery
            join c in _db.Contacts.AsNoTracking() on a.BankContactId equals c.Id into contacts
            from c in contacts.DefaultIfEmpty()
            join cat in _db.ContactCategories.AsNoTracking() on c.CategoryId equals cat.Id into categories
            from cat in categories.DefaultIfEmpty()
            select new AccountScopeRow(
                a.Id,
                a.Name,
                a.Iban,
                a.Type,
                a.CurrentBalance,
                a.BankContactId,
                (a.SymbolAttachmentId.HasValue && a.SymbolAttachmentId.Value != Guid.Empty) ? a.SymbolAttachmentId
                    : (c != null && c.SymbolAttachmentId.HasValue && c.SymbolAttachmentId.Value != Guid.Empty) ? c.SymbolAttachmentId
                    : cat.SymbolAttachmentId,
                a.SavingsPlanExpectation,
                a.SecurityProcessingEnabled,
                a.IsCollectionAccount,
                c != null && c.OwnerUserId == ownerUserId && c.Type == ContactType.Bank ? a.BankContactId : null,
                c != null && c.OwnerUserId == ownerUserId && c.Type == ContactType.Bank ? c.Name : null))
            .ToListAsync(ct);

        return search == null
            ? rows
            : rows.Where(a => search.Matches(a.Name, a.Iban)).ToList();
    }

    private static IQueryable<Domain.Accounts.Account> ApplyOwnerAndBankContactFilters(IQueryable<Domain.Accounts.Account> query, Guid ownerUserId, Guid? bankContactId)
    {
        query = query.Where(a => a.OwnerUserId == ownerUserId);
        if (bankContactId.HasValue)
        {
            query = query.Where(a => a.BankContactId == bankContactId.Value);
        }

        return query;
    }

    private static IReadOnlyList<AccountBalanceGroupDto> BuildGroups(
        IReadOnlyList<AccountScopeRow> accounts,
        Func<AccountScopeRow, string> keySelector,
        Func<AccountScopeRow, string?> displayNameSelector)
    {
        return accounts
            .GroupBy(keySelector)
            .Select(g =>
            {
                var positive = g.Where(a => a.CurrentBalance > 0m).Sum(a => a.CurrentBalance);
                var negative = g.Where(a => a.CurrentBalance < 0m).Sum(a => Math.Abs(a.CurrentBalance));
                var gross = positive + negative;
                return new AccountBalanceGroupDto(
                    g.Key,
                    g.Select(displayNameSelector).FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)),
                    positive - negative,
                    positive,
                    negative,
                    gross,
                    g.Count(),
                    g.Count(a => a.CurrentBalance == 0m));
            })
            .OrderByDescending(g => g.GrossMagnitude)
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .ToList();
    }

    private sealed record AccountScopeRow(
        Guid Id,
        string Name,
        string? Iban,
        AccountType Type,
        decimal CurrentBalance,
        Guid BankContactId,
        Guid? SymbolAttachmentId,
        SavingsPlanExpectation SavingsPlanExpectation,
        bool SecurityProcessingEnabled,
        bool IsCollectionAccount,
        Guid? StatisticsBankContactId,
        string? StatisticsBankContactName)
    {
        public AccountDto ToDto()
            => new(
                Id,
                Name,
                Type,
                Iban,
                CurrentBalance,
                BankContactId,
                SymbolAttachmentId,
                SavingsPlanExpectation,
                SecurityProcessingEnabled)
            {
                IsCollectionAccount = IsCollectionAccount
            };
    }

    private sealed class UtcAccountStatisticsPeriodProvider : IAccountStatisticsPeriodProvider
    {
        public Task<AccountStatisticsPeriod> GetPeriodAsync(Guid ownerUserId, CancellationToken ct)
        {
            var today = DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Unspecified);
            return Task.FromResult(new AccountStatisticsPeriod(today, new DateTime(today.Year, 1, 1), new DateTime(today.Year, today.Month, 1), today.AddDays(1)));
        }
    }
}
