using FinanceManager.Domain.Accounts;
using FinanceManager.Domain.Contacts;
using FinanceManager.Domain.Savings;
using FinanceManager.Infrastructure;
using FinanceManager.Shared.Dtos;
using System.Diagnostics.Metrics;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

public sealed class SetupImportService : ISetupImportService
{
    private readonly AppDbContext _db;
    public SetupImportService(AppDbContext db) { _db = db; }

    public async Task ImportAsync(Guid userId, Stream fileStream, bool replaceExisting, CancellationToken ct)
    {
        using var reader = new StreamReader(fileStream, Encoding.UTF8);

        // Erste Zeile: Metadaten-Objekt
        var metaLine = await reader.ReadLineAsync();
        if (string.IsNullOrWhiteSpace(metaLine))
            throw new InvalidOperationException("Backup-Metadaten fehlen.");

        var meta = JsonSerializer.Deserialize<BackupMeta>(metaLine);
        if (meta == null || meta.Type != "Backup" || meta.Version != 2)
            throw new InvalidOperationException("Ungültiges Backup-Format.");

        // Rest der Datei: eigentlicher Datenbestand
        var jsonData = await reader.ReadToEndAsync();
        var backupData = JsonSerializer.Deserialize<BackupData>(jsonData);
        if (backupData == null)
            throw new InvalidOperationException("Backup-Daten konnten nicht gelesen werden.");
        if (replaceExisting)
        {
            _db.ClearUserData(userId);
        }

        var contactCategories = ImportContactCategories(backupData.ContactCategories, userId).ToList();        
        var contacts = ImportContacts(backupData.Contacts, contactCategories, userId).ToList();
        var accounts = ImportAccounts(backupData.BankAccounts, userId).ToList();
        var savingsPlanCategories = ImportSavingsPlanCategories(backupData.FixedAssetCategories, userId).ToList();
        var savingsPlans = ImportSavingsPlans(backupData.FixedAssets, savingsPlanCategories, userId).ToList();
        await _db.SaveChangesAsync();
    }

    private IEnumerable<KeyValuePair<string, Guid>> ImportAccounts(JsonElement bankAccounts, Guid userId)
    {        
            var isEmpty = !_db.Accounts.Any(acc => acc.OwnerUserId == userId);
        if (bankAccounts.ValueKind != JsonValueKind.Undefined)
            foreach (var account in bankAccounts.EnumerateArray())
            {
                var iban = account.GetProperty("IBAN").GetString();
                var instituteName = account.GetProperty("InstituteName").ToString();
                var name = account.GetProperty("Description").GetString();
                if (string.IsNullOrWhiteSpace(iban)) continue;
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (string.IsNullOrWhiteSpace(instituteName)) continue;

                var contact = _db.Contacts.Where(c => c.OwnerUserId == userId && c.Name == instituteName).FirstOrDefault();
                if (contact is null)
                {
                    contact = new Contact(userId, instituteName, ContactType.Bank, null);
                    _db.Contacts.Add(contact);
                }
                if (contact.Type != ContactType.Bank)
                {
                    contact.ChangeType(ContactType.Bank);
                    _db.Contacts.Update(contact);
                }

                var existingAccount = _db.Accounts.FirstOrDefault(a => a.OwnerUserId == userId && a.Iban == iban);
                if (existingAccount is not null)
                {
                    existingAccount.SetBankContact(contact.Id);
                    _db.Accounts.Update(existingAccount);
                    yield return new KeyValuePair<string, Guid>(iban, existingAccount.Id);
                }
                else
                {
                    var newAccount = new Account(userId, isEmpty ? FinanceManager.Domain.AccountType.Giro : FinanceManager.Domain.AccountType.Savings, name, iban, contact.Id);
                    _db.Accounts.Add(newAccount);
                    yield return new KeyValuePair<string, Guid>(iban, newAccount.Id);
                }
            }
    }

    private IEnumerable<KeyValuePair<int, Guid>> ImportSavingsPlans(JsonElement fixedAssets, IEnumerable<KeyValuePair<int, Guid>> savingsPlanCategories, Guid userId)
    {
        if (fixedAssets.ValueKind != JsonValueKind.Undefined)
            foreach (var fixedAsset in fixedAssets.EnumerateArray())
        {
            var dataId = fixedAsset.GetProperty("Id").GetInt32();
            var name = fixedAsset.GetProperty("Name").GetString();
            var expectedPurchaseActive = fixedAsset.GetProperty("ExpectedPurchaseActive").GetBoolean();
            var amount = fixedAsset.GetProperty("ExpectedPurchaseAmount").GetDecimal();
            var dueDate = fixedAsset.GetProperty("ExpectedPurchaseDate").GetDateTime();
            var interval = fixedAsset.GetProperty("PurchaseInterval").GetInt32();
            var status = fixedAsset.GetProperty("Status").GetInt32();
            if (string.IsNullOrWhiteSpace(name)) continue;
            var savingsPlanName = name;
            var nameExists = _db.SavingsPlans.Any(p => p.OwnerUserId == userId && p.Name == savingsPlanName);
            var counter = 1;
            while (nameExists)
            {
                savingsPlanName = $"{name} ({++counter})";
                nameExists = _db.SavingsPlans.Any(p => p.OwnerUserId == userId && p.Name == savingsPlanName);
            }
            var newSavingsPlan = new SavingsPlan(
                userId,
                savingsPlanName,
                expectedPurchaseActive ? (interval == 0 ? SavingsPlanType.OneTime : SavingsPlanType.Recurring) : SavingsPlanType.Open,
                amount,
                expectedPurchaseActive ? dueDate : null,
                interval == 0 ? null : (SavingsPlanInterval)(interval - 1));
            if (status == 3)
                newSavingsPlan.Archive();

            if (fixedAsset.TryGetProperty("Category", out var categoryProp))
                if (categoryProp.TryGetProperty("Id", out var categoryIdProp) && categoryIdProp.ValueKind == JsonValueKind.Number)
                {
                    var categoryId = categoryIdProp.GetInt32();
                    var matchingCategory = savingsPlanCategories.FirstOrDefault(c => c.Key == categoryId);
                    if (matchingCategory.Value != Guid.Empty)
                    {
                        newSavingsPlan.SetCategory(matchingCategory.Value);
                    }
                }
            _db.SavingsPlans.Add(newSavingsPlan);
            _db.SaveChanges();
            yield return new KeyValuePair<int, Guid>(dataId, newSavingsPlan.Id);
        }
    }

    private IEnumerable<KeyValuePair<int, Guid>> ImportSavingsPlanCategories(JsonElement fixedAssetCategories, Guid userId)
    {
        if (fixedAssetCategories.ValueKind != JsonValueKind.Undefined)
            foreach (var category in fixedAssetCategories.EnumerateArray())
        {
            var dataId = category.GetProperty("Id").GetInt32();
            var name = category.GetProperty("Name").GetString();
            if (string.IsNullOrWhiteSpace(name)) continue;
            var newCategory = new SavingsPlanCategory(userId, name);
            _db.SavingsPlanCategories.Add(newCategory);
            yield return new KeyValuePair<int, Guid>(dataId, newCategory.Id);
        }
    }

    private IEnumerable<KeyValuePair<int, Guid>> ImportContacts(JsonElement contacts, IEnumerable<KeyValuePair<int, Guid>> contactCategories, Guid userId)
    {
        if (contacts.ValueKind != JsonValueKind.Undefined)
        foreach (var contact in contacts.EnumerateArray())
        {
            var dataId = contact.GetProperty("Id").GetInt32();
            var name = contact.GetProperty("Name").GetString();
            var isPaymentProvider = contact.GetProperty("IsPaymentProvider").GetBoolean();
            if (string.IsNullOrWhiteSpace(name)) continue;
            var newContact = new Contact(userId, name, ContactType.Organization, null);
            if (contact.TryGetProperty("Category", out var categoryProp))
                if (categoryProp.TryGetProperty("Id", out var categoryIdProp) && categoryIdProp.ValueKind == JsonValueKind.Number)
                {
                    var categoryId = categoryIdProp.GetInt32();
                    var matchingCategory = contactCategories.FirstOrDefault(c => c.Key == categoryId);
                    if (matchingCategory.Value != Guid.Empty)
                    {
                        newContact.SetCategory(matchingCategory.Value);
                    }
                }
            _db.Contacts.Add(newContact);
            yield return new KeyValuePair<int, Guid>(dataId, newContact.Id);
        }
    }

    private IEnumerable<KeyValuePair<int,Guid>> ImportContactCategories(JsonElement contactCategories, Guid userId)
    {
        if (contactCategories.ValueKind != JsonValueKind.Undefined)
        foreach (var category in contactCategories.EnumerateArray())
        {
            var dataId = category.GetProperty("Id").GetInt32();
            var name = category.GetProperty("Name").GetString();
            if (string.IsNullOrWhiteSpace(name)) continue;
            var newCategory = new ContactCategory(userId, name);
            _db.ContactCategories.Add(newCategory);
            yield return new KeyValuePair<int, Guid>(dataId, newCategory.Id);
        }
    }

    private sealed class BackupMeta
    {
        public string Type { get; set; } = string.Empty;
        public int Version { get; set; }
    }

    private sealed class BackupData
    {
        public JsonElement BankAccounts { get; set; }
        public JsonElement Contacts { get; set; }
        public JsonElement ContactCategories { get; set; }
        public JsonElement FixedAssetCategories { get; set; }
        public JsonElement FixedAssets { get; set; }
        // Weitere Properties nach Bedarf
    }
}