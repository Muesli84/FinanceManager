using FinanceManager.Application.Contacts;
using FinanceManager.Domain;
using FinanceManager.Domain.Contacts;
using FinanceManager.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

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

        // Prevent changing type to or from Self.
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
        // Only apply type change if not self (guard above ensures validity)
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
        _db.Contacts.Remove(contact);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<ContactDto>> ListAsync(Guid ownerUserId, int skip, int take, ContactType? type, CancellationToken ct)
    {
        var query = _db.Contacts.AsNoTracking()
            .Where(c => c.OwnerUserId == ownerUserId);

        if (type.HasValue)
        {
            query = query.Where(c => c.Type == type.Value);
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

        var alias = new AliasName(contactId, pattern.Trim());
        _db.AliasNames.Add(alias);
        await _db.SaveChangesAsync(ct);
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

        // Aliase des Zielkontakts laden
        var targetAliasPatterns = await _db.AliasNames
            .Where(a => a.ContactId == target.Id)
            .Select(a => a.Pattern.ToLower())
            .ToListAsync(ct);

        // Name des Quellkontakts als Alias hinzufügen (falls nicht schon vorhanden / identisch)
        if (!string.Equals(source.Name, target.Name, StringComparison.OrdinalIgnoreCase)
            && !targetAliasPatterns.Contains(source.Name.ToLower()))
        {
            _db.AliasNames.Add(new AliasName(target.Id, source.Name));
        }

        // Aliase des Quellkontakts übernehmen (umhängen) – Duplikate überspringen
        var sourceAliases = await _db.AliasNames
            .Where(a => a.ContactId == source.Id)
            .ToListAsync(ct);
        foreach (var alias in sourceAliases)
        {
            if (targetAliasPatterns.Contains(alias.Pattern.ToLower()) ||
                string.Equals(alias.Pattern, target.Name, StringComparison.OrdinalIgnoreCase))
            {
                // Duplikat -> löschen
                _db.AliasNames.Remove(alias);
            }
            else
            {
                alias.ReassignTo(target.Id); // Falls es keine Methode gibt: alias.ContactId = target.Id;
            }
        }

        // Statement Draft Entries (TODO: Tabellen-/Entity-Namen prüfen)
        await _db.StatementDraftEntries
            .Where(e => e.ContactId == source.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.ContactId, target.Id), ct);

        // Committed Statement Entries
        await _db.StatementEntries
            .Where(e => e.ContactId == source.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.ContactId, target.Id), ct);

        // Falls Bankkontakte: zugehörige Accounts umbuchen
        if (bankInvolved)
        {
            await _db.Accounts
                .Where(a => a.BankContactId == source.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.BankContactId, target.Id), ct);
        }

        // Quellkontakt entfernen
        _db.Contacts.Remove(source);

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new ContactDto(target.Id, target.Name, target.Type, target.CategoryId, target.Description, target.IsPaymentIntermediary);
    }
}
