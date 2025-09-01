using FinanceManager.Domain;
using FinanceManager.Domain.Accounts;
using FinanceManager.Domain.Contacts;
using FinanceManager.Domain.Statements;
using FinanceManager.Infrastructure;

public sealed class DemoDataService
{
    private readonly AppDbContext _db;

    public DemoDataService(AppDbContext db)
    {
        _db = db;
    }

    public async Task CreateDemoDataForUserAsync(Guid userId, CancellationToken ct)
    {
        await _db.ContactCategories.AddRangeAsync(new[]
        {
            new ContactCategory(userId, "Freunde & Bekannte"),
            new ContactCategory(userId, "Versicherungen"),
            new ContactCategory(userId, "Arbeit"),
            new ContactCategory(userId, "Banken"),
            new ContactCategory(userId, "Superm�rkte & Einzelhandel"),
            new ContactCategory(userId, "Transport & Tanken"),
            new ContactCategory(userId, "Lieferdienste"),
            new ContactCategory(userId, "Onlineshops"),
            new ContactCategory(userId, "Gl�cksspiele"),
            new ContactCategory(userId, "Freizeiteinrichtungen"),
            new ContactCategory(userId, "Streaminganbieter"),
            new ContactCategory(userId, "Beh�rden"),
            new ContactCategory(userId, "Autoh�user"),
            new ContactCategory(userId, "Wohlfahrtsunternehmen"),
            new ContactCategory(userId, "Baum�rkte & Gartencenter"),
            new ContactCategory(userId, "Gastronomie"),
            new ContactCategory(userId, "B�ckereien & Caf�s"),
            new ContactCategory(userId, "Dienstleister"),
            new ContactCategory(userId, "Hotels & Ferienanlagen"),
            new ContactCategory(userId, "Sanit�ranlagen"),
            new ContactCategory(userId, "Privatverk�ufer"),
            new ContactCategory(userId, "Sonstiges"),
        });
        await _db.SaveChangesAsync(ct);
    }
}
