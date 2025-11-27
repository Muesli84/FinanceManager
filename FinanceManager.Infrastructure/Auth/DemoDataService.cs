using FinanceManager.Domain.Contacts;
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
            new ContactCategory(userId, "Supermärkte & Einzelhandel"),
            new ContactCategory(userId, "Transport & Tanken"),
            new ContactCategory(userId, "Lieferdienste"),
            new ContactCategory(userId, "Onlineshops"),
            new ContactCategory(userId, "Glücksspiele"),
            new ContactCategory(userId, "Freizeiteinrichtungen"),
            new ContactCategory(userId, "Streaminganbieter"),
            new ContactCategory(userId, "Behörden"),
            new ContactCategory(userId, "Autohäuser"),
            new ContactCategory(userId, "Wohlfahrtsunternehmen"),
            new ContactCategory(userId, "Baumärkte & Gartencenter"),
            new ContactCategory(userId, "Gastronomie"),
            new ContactCategory(userId, "Bäckereien & Cafés"),
            new ContactCategory(userId, "Dienstleister"),
            new ContactCategory(userId, "Hotels & Ferienanlagen"),
            new ContactCategory(userId, "Sanitäranlagen"),
            new ContactCategory(userId, "Privatverkäufer"),
            new ContactCategory(userId, "Sonstiges"),
        });
        await _db.SaveChangesAsync(ct);
    }
}
