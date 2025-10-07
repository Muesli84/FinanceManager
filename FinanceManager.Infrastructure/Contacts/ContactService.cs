using FinanceManager.Application.Contacts;
using FinanceManager.Domain;
using FinanceManager.Domain.Contacts;
using FinanceManager.Shared.Dtos;
using Microsoft.EntityFrameworkCore;
using FinanceManager.Domain.Attachments; // added

namespace FinanceManager.Infrastructure.Contacts;

public sealed class ContactService : IContactService
{
    private readonly AppDbContext _db;
    public ContactService(AppDbContext db) { _db = db; }

    public async Task<ContactDto> CreateAsync(Guid ownerUserId, string name, ContactType type, Guid? categoryId, string? description, bool? isPaymentIntermediary, CancellationToken ct)
    {
        if (type == ContactType.Self)
        {
            throw new ArgumentException("Creating a contact of type 'Self' is not allowed.");
        }
        var contact = new Contact(ownerUserId, name, type, categoryId, description, isPaymentIntermediary);
        _db.Contacts.Add(contact);
        await _db.SaveChangesAsync(ct);
        return new ContactDto(contact.Id, contact.Name, contact.Type, contact.CategoryId, contact.Description, contact.IsPaymentIntermediary);
    }

    public async Task<ContactDto?> UpdateAsync(Guid id, Guid ownerUserId, string name, ContactType type, Guid? categoryId, string? description, bool? isPaymentIntermediary, CancellationToken ct)
    {
        var contact = await _db.Contacts.FirstOrDefaultAsync(c => c.Id == id && c.OwnerUserId == ownerUserId, ct);
        if (contact == null) return null;

        bool isSelf = contact.Type == ContactType.Self;
        if (isSelf && type != ContactType.Self)
        {
            throw new ArgumentException("Changing the type of a 'Self' contact is not allowed.");
        }
        if (!isSelf && type == ContactType.Self)
        {
            throw new ArgumentException("Changing a contact's type to 'Self' is not allowed.");
        }

        contact.Rename(name);
        if (!isSelf)
        {
            contact.ChangeType(type);
        }
        contact.SetCategory(categoryId);
        contact.SetDescription(description);
        contact.SetPaymentIntermediary(isPaymentIntermediary ?? false);
        await _db.SaveChangesAsync(ct);
        return new ContactDto(contact.Id, contact.Name, contact.Type, contact.CategoryId, contact.Description, contact.IsPaymentIntermediary);
    }

    public async Task<bool> DeleteAsync(Guid id, Guid ownerUserId, CancellationToken ct)
    {
        var contact = await _db.Contacts.FirstOrDefaultAsync(c => c.Id == id && c.OwnerUserId == ownerUserId, ct);
        if (contact == null) return false;
        if (contact.Type == ContactType.Self)
        {
            throw new ArgumentException("Deleting the 'Self' contact is not allowed.");
        }

        // Delete attachments for this contact
        var attQuery = _db.Attachments
            .Where(a => a.OwnerUserId == ownerUserId && a.EntityKind == AttachmentEntityKind.Contact && a.EntityId == contact.Id);
        if (_db.Database.IsRelational())
        {
            await attQuery.ExecuteDeleteAsync(ct);
        }
        else
        {
            var atts = await attQuery.ToListAsync(ct);
            if (atts.Count > 0)
            {
                _db.Attachments.RemoveRange(atts);
            }
        }

        _db.Contacts.Remove(contact);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<ContactDto>> ListAsync(Guid ownerUserId, int skip, int take, ContactType? type, string? nameFilter, CancellationToken ct)
    {
        var query = _db.Contacts.AsNoTracking()
            .Where(c => c.OwnerUserId == ownerUserId);

        if (type.HasValue)
        {
            query = query.Where(c => c.Type == type.Value);
        }

        if (!string.IsNullOrWhiteSpace(nameFilter))
        {
            var pattern = $"%{nameFilter.Trim()}%";
            query = query.Where(c => EF.Functions.Like(c.Name, pattern));
        }

        return await query
            .OrderBy(c => c.Name)
            .Skip(skip)
            .Take(take)
            .Select(c => new ContactDto(c.Id, c.Name, c.Type, c.CategoryId, c.Description, c.IsPaymentIntermediary))
            .ToListAsync(ct);
    }

    public async Task<ContactDto?> GetAsync(Guid id, Guid ownerUserId, CancellationToken ct)
    {
        return await _db.Contacts.AsNoTracking()
            .Where(c => c.Id == id && c.OwnerUserId == ownerUserId)
            .Select(c => new ContactDto(c.Id, c.Name, c.Type, c.CategoryId, c.Description, c.IsPaymentIntermediary))
            .FirstOrDefaultAsync(ct);
    }

    public async Task AddAliasAsync(Guid contactId, Guid ownerUserId, string pattern, CancellationToken ct)
    {
        var contact = await _db.Contacts.FirstOrDefaultAsync(c => c.Id == contactId && c.OwnerUserId == ownerUserId, ct);
        if (contact == null) throw new ArgumentException("Contact not found.");
        if (string.IsNullOrWhiteSpace(pattern)) throw new ArgumentException("Pattern must not be empty.");
        var trimmed = pattern.Trim();

        // prevent duplicates (case-insensitive)
        var exists = await _db.AliasNames
            .AnyAsync(a => a.ContactId == contactId && a.Pattern.ToLower() == trimmed.ToLower(), ct);
        if (exists)
        {
            throw new ArgumentException("Alias already exists.");
        }

        var alias = new AliasName(contactId, trimmed);
        _db.AliasNames.Add(alias);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // race condition fallback
            throw new ArgumentException("Alias already exists.");
        }
    }

    public async Task DeleteAliasAsync(Guid contactId, Guid ownerUserId, Guid aliasId, CancellationToken ct)
    {
        var contact = await _db.Contacts.FirstOrDefaultAsync(c => c.Id == contactId && c.OwnerUserId == ownerUserId, ct);
        if (contact == null) throw new ArgumentException("Contact not found.");

        var alias = await _db.AliasNames.FirstOrDefaultAsync(a => a.Id == aliasId && a.ContactId == contactId, ct);
        if (alias == null) throw new ArgumentException("Alias not found.");

        _db.AliasNames.Remove(alias);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<AliasNameDto>> ListAliases(Guid id, Guid userId, CancellationToken ct)
    {
        return await _db.AliasNames.AsNoTracking()
            .Where(c => c.ContactId == id)
            .OrderBy(c => c.Pattern)
            .Select(c => new AliasNameDto(c.Id, c.ContactId, c.Pattern))
            .ToListAsync(ct);
    }

    public async Task<ContactDto> MergeAsync(Guid ownerUserId, Guid sourceContactId, Guid targetContactId, CancellationToken ct)
    {
        if (sourceContactId == targetContactId)
        {
            throw new ArgumentException("Source and target contact must differ.");
        }

        var contacts = await _db.Contacts
            .Where(c => (c.Id == sourceContactId || c.Id == targetContactId) && c.OwnerUserId == ownerUserId)
            .ToListAsync(ct);

        var source = contacts.FirstOrDefault(c => c.Id == sourceContactId);
        var target = contacts.FirstOrDefault(c => c.Id == targetContactId);

        if (source is null || target is null)
        {
            throw new ArgumentException("One or both contacts not found.");
        }

        if (source.Type == ContactType.Self && target.Type != ContactType.Self)
        {
            throw new ArgumentException("Merging involving a 'Self' contact is not allowed.");
        }

        bool bankInvolved = source.Type == ContactType.Bank || target.Type == ContactType.Bank;
        if (bankInvolved && (source.Type != ContactType.Bank || target.Type != ContactType.Bank))
        {
            throw new ArgumentException("If one contact is of type 'Bank', both must be 'Bank' to merge.");
        }

        using var tx = await _db.Database.BeginTransactionAsync(ct);

        var targetAliasPatterns = await _db.AliasNames
            .Where(a => a.ContactId == target.Id)
            .Select(a => a.Pattern.ToLower())
            .ToListAsync(ct);

        // ensure target name is included in checks
        var targetNameLower = target.Name.ToLower();

        if (!string.Equals(source.Name, target.Name, StringComparison.OrdinalIgnoreCase)
            && !targetAliasPatterns.Contains(source.Name.ToLower()))
        {
            _db.AliasNames.Add(new AliasName(target.Id, source.Name));
            targetAliasPatterns.Add(source.Name.ToLower());
        }

        var sourceAliases = await _db.AliasNames
            .Where(a => a.ContactId == source.Id)
            .ToListAsync(ct);
        foreach (var alias in sourceAliases)
        {
            var aliasLower = alias.Pattern.ToLower();
            if (targetAliasPatterns.Contains(aliasLower) || aliasLower == targetNameLower)
            {
                _db.AliasNames.Remove(alias);
            }
            else
            {
                alias.ReassignTo(target.Id);
                targetAliasPatterns.Add(aliasLower);
            }
        }

        // Reassign draft entries (domain method exists)
        var draftEntries = await _db.StatementDraftEntries.Where(e => e.ContactId == source.Id).ToListAsync(ct);
        foreach (var e in draftEntries) { e.AssignContactWithoutAccounting(target.Id); }

        // Statement entries: use provider capabilities
        if (_db.Database.IsRelational())
        {
            await _db.StatementEntries
                .Where(e => e.ContactId == source.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.ContactId, target.Id), ct);
        }
        else
        {
            var entries = await _db.StatementEntries.Where(e => e.ContactId == source.Id).ToListAsync(ct);
            foreach (var e in entries)
            {
                // private setter -> set via EF entry
                _db.Entry(e).Property<Guid?>("ContactId").CurrentValue = target.Id;
            }
        }

        // Postings: reassign contact
        if (_db.Database.IsRelational())
        {
            await _db.Postings
                .Where(p => p.ContactId == source.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.ContactId, target.Id), ct);
        }
        else
        {
            var postings = await _db.Postings.Where(p => p.ContactId == source.Id).ToListAsync(ct);
            foreach (var p in postings)
            {
                _db.Entry(p).Property<Guid?>("ContactId").CurrentValue = target.Id;
            }
        }

        // Posting aggregates: merge to avoid duplicates (unique index)
        var srcAggs = await _db.PostingAggregates.Where(a => a.ContactId == source.Id).ToListAsync(ct);
        foreach (var sa in srcAggs)
        {
            var existing = await _db.PostingAggregates.FirstOrDefaultAsync(pa =>
                pa.Kind == sa.Kind && pa.AccountId == sa.AccountId && pa.ContactId == target.Id && pa.SavingsPlanId == sa.SavingsPlanId && pa.SecurityId == sa.SecurityId && pa.Period == sa.Period && pa.PeriodStart == sa.PeriodStart, ct);
            if (existing != null)
            {
                existing.Add(sa.Amount);
                _db.PostingAggregates.Remove(sa);
            }
            else
            {
                // private setter -> set via EF entry
                _db.Entry(sa).Property<Guid?>("ContactId").CurrentValue = target.Id;
            }
        }

        if (bankInvolved)
        {
            if (_db.Database.IsRelational())
            {
                await _db.Accounts
                    .Where(a => a.BankContactId == source.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.BankContactId, target.Id), ct);
            }
            else
            {
                var accounts = await _db.Accounts.Where(a => a.BankContactId == source.Id).ToListAsync(ct);
                foreach (var a in accounts) { a.SetBankContact(target.Id); }
            }
        }

        // Reassign attachments from source contact to target contact
        if (_db.Database.IsRelational())
        {
            await _db.Attachments
                .Where(a => a.OwnerUserId == ownerUserId && a.EntityKind == AttachmentEntityKind.Contact && a.EntityId == source.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.EntityId, target.Id), ct);
        }
        else
        {
            var atts = await _db.Attachments
                .Where(a => a.OwnerUserId == ownerUserId && a.EntityKind == AttachmentEntityKind.Contact && a.EntityId == source.Id)
                .ToListAsync(ct);
            foreach (var a in atts) { a.Reassign(AttachmentEntityKind.Contact, target.Id); }
        }

        _db.Contacts.Remove(source);

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new ContactDto(target.Id, target.Name, target.Type, target.CategoryId, target.Description, target.IsPaymentIntermediary);
    }

    public Task<int> CountAsync(Guid ownerUserId, CancellationToken ct)
    {
        return _db.Contacts.AsNoTracking().Where(c => c.OwnerUserId == ownerUserId).CountAsync(ct);
    }
}
