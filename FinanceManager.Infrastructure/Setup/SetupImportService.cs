using FinanceManager.Application.Statements;
using FinanceManager.Domain.Accounts;
using FinanceManager.Domain.Contacts;
using FinanceManager.Domain.Savings;
using FinanceManager.Domain.Securities;
using FinanceManager.Infrastructure;
using FinanceManager.Shared.Dtos;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

public sealed class SetupImportService : ISetupImportService
{
    private readonly AppDbContext _db;
    private readonly IStatementDraftService _statementDraftService;

    public SetupImportService(AppDbContext db, IStatementDraftService statementDraftService)
    {
        _db = db;
        _statementDraftService = statementDraftService;
    }

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
            await _db.SaveChangesAsync();
        }

        var contactCategories = ImportContactCategories(backupData.ContactCategories, userId).ToList();
        var contacts = ImportContacts(backupData.Contacts, contactCategories, userId).ToList();
        var accounts = ImportAccounts(backupData.BankAccounts, userId).ToList();
        var savingsPlanCategories = ImportSavingsPlanCategories(backupData.FixedAssetCategories, userId).ToList();
        var savingsPlans = ImportSavingsPlans(backupData.FixedAssets, savingsPlanCategories, userId).ToList();
        var stocks = ImportStocks(backupData.Stocks, userId).ToList();
        await _db.SaveChangesAsync();
        await ImportLedgerEntriesAsync(meta, backupData.BankAccounts, backupData.BankAccountLedgerEntries, backupData.StockLedgerEntries, stocks, backupData.FixedAssetLedgerEntries, savingsPlans, userId, contacts, ct);
        await _db.SaveChangesAsync();
    }

    private async Task ImportLedgerEntriesAsync(BackupMeta meta, JsonElement bankAccounts, BackupBankAccountLedgerEntry[]? bankAccountLedgerEntries, JsonElement stockLedgerEntries, List<KeyValuePair<int, Guid>> stocks, JsonElement fixedAssetLedgerEntries, List<KeyValuePair<int, Guid>> savingsPlans, Guid userId, List<KeyValuePair<int, Guid>> contacts, CancellationToken ct)
    {
        if (bankAccountLedgerEntries is null)
            return;
        // 1) Erste Zeile: Meta als JSON
        var metaJson = JsonSerializer.Serialize(meta);

        // 2) Zweite Zeile: Objekt mit minimalen Feldern, die der BackupStatementFileReader erwartet
        //    - BankAccounts: enthält mindestens eine IBAN (für Header-Detektion)
        //    - BankAccountLedgerEntries: übernommene Bewegungen
        //    - BankAccountJournalLines: leeres Array (Reader erwartet Feld)
        var firstIban = await _db.Accounts
            .AsNoTracking()
            .Where(a => a.OwnerUserId == userId && a.Iban != null && a.Iban != "")
            .Select(a => a.Iban!)
            .FirstOrDefaultAsync(ct) ?? string.Empty;
        var emptyArray = new string[0];
        var dataObj = new
        {
            BankAccounts = bankAccounts,
            BankAccountLedgerEntries = bankAccountLedgerEntries.Select(entry =>
            {
                if (entry.SourceContact is not null)
                    entry.SourceContact.UID = contacts.FirstOrDefault(c => c.Key == entry.SourceContact?.Id).Value;
                var stockEntries = stockLedgerEntries.EnumerateArray().Where(e =>
                {
                    return e.GetProperty("SourceLedgerEntry").GetProperty("Id").GetInt32() == entry.Id;
                }).ToArray();
                if (stockEntries.Length > 1)
                    throw new ApplicationException("Darf es nicht geben!?!");
                if (stockEntries.FirstOrDefault() is JsonElement stockEntry){
                    if (stockEntry.ValueKind == JsonValueKind.Object)
                    {
                        var stockId = stockEntry.GetProperty("Stock").GetProperty("Id").GetInt32();
                        var stockUId = stocks.FirstOrDefault(s => s.Key == stockId).Value;
                        var stock = _db.Securities.AsNoTracking().FirstOrDefault(s => s.Id == stockUId && s.OwnerUserId == userId);
                        if (stock is not null && !entry.Description.Contains(stock.Identifier, StringComparison.OrdinalIgnoreCase))
                        {
                            entry.Description = $"{entry.Description} [Wertpapier: {stock.Name} ({stock.Identifier})]";
                        }
                    }
                }

                var fixedAssetEntries = fixedAssetLedgerEntries.EnumerateArray().Where(e =>
                {
                    var sourceLedger = e.GetProperty("SourceLedgerEntry");
                    if (sourceLedger.ValueKind == JsonValueKind.Null)
                        return false;
                    return sourceLedger.GetProperty("Id").GetInt32() == entry.Id;
                }).ToArray();
                if (fixedAssetEntries.Length > 1)
                    throw new ApplicationException("Darf es nicht geben!?!");
                if (fixedAssetEntries.FirstOrDefault() is JsonElement fixedAssetEntry)
                {
                    if (fixedAssetEntry.ValueKind == JsonValueKind.Object)
                    {
                        var savingsPlanId = fixedAssetEntry.GetProperty("FixedAsset").GetProperty("Id").GetInt32();
                        var savingsPlanUId = savingsPlans.FirstOrDefault(s => s.Key == savingsPlanId).Value;
                        var savingsPlan = _db.SavingsPlans.AsNoTracking().FirstOrDefault(s => s.Id == savingsPlanUId && s.OwnerUserId == userId);
                        if (savingsPlan is not null && !entry.Description.Contains(savingsPlan.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            entry.Description = $"{entry.Description} [Sparplan: {savingsPlan.Name}]";
                        }
                    }
                }
                return entry;
            }),
            BankAccountJournalLines = emptyArray
        };
        var dataJson = JsonSerializer.Serialize(dataObj);

        var fileContent = $"{metaJson}\n{dataJson}";
        var fileBytes = Encoding.UTF8.GetBytes(fileContent);

        // 3) Kontoauszug-Entwürfe erzeugen (Enumeration zwingend, sonst passiert nichts)
        await foreach (var draft in _statementDraftService.CreateDraftAsync(userId, "backup.ndjson", fileBytes, ct))
        {
            var result = await _statementDraftService.BookAsync(draft.DraftId, null, userId, true, ct);
        }
    }

    private IEnumerable<KeyValuePair<int, Guid>> ImportStocks(JsonElement stocks, Guid userId)
    {
        if (stocks.ValueKind != JsonValueKind.Undefined)
            foreach (var stock in stocks.EnumerateArray())
            {
                var dataId = stock.GetProperty("Id").GetInt32();
                var name = stock.GetProperty("Name").GetString();
                var description = stock.GetProperty("Description").GetString();
                var symbol = stock.GetProperty("Symbol").GetString();
                var avCode = stock.GetProperty("AlphaVantageSymbol").GetString();
                var currencyCode = stock.GetProperty("CurrencyCode").GetString();
                if (string.IsNullOrWhiteSpace(name)) continue;
                var newStock = new Security(userId, name, symbol, description, avCode, currencyCode ?? "EUR", null);
                var existing = _db.Securities.FirstOrDefault(s => s.OwnerUserId == userId && ((s.Identifier == newStock.Identifier && newStock.Identifier != "") || (s.AlphaVantageCode == newStock.AlphaVantageCode && newStock.AlphaVantageCode != "" && newStock.AlphaVantageCode != "-")));
                if (existing is null)
                    _db.Securities.Add(newStock);
                else
                {
                    existing.Update(name, symbol, description, avCode, currencyCode ?? "EUR", null);
                    newStock = existing;
                    _db.Securities.Update(existing);
                }
                _db.SaveChanges();
                yield return new KeyValuePair<int, Guid>(dataId, newStock.Id);
            }
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

                var contacts = _db.Contacts.Where(c => c.OwnerUserId == userId && c.Name == instituteName).ToArray();
                var contact = contacts.FirstOrDefault();
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

                var contactNames = account.GetProperty("Institute").GetProperty("Names");
                if (contactNames.ValueKind != JsonValueKind.Null)
                    foreach (var contactName in contactNames.EnumerateArray().Select(n => n.GetString()).Where(n => !string.IsNullOrWhiteSpace(n)))
                    {
                        var pattern = contactName ?? instituteName;
                        var existing = _db.AliasNames.Where(a => a.ContactId == contact.Id && a.Pattern == pattern).Any();
                        if (!existing)
                            _db.AliasNames.Add(new AliasName(contact.Id, pattern));
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
                _db.SaveChanges();
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
                var contractNo = "";
                if (fixedAsset.TryGetProperty("ContractNo", out var contract))
                    contractNo = contract.ValueKind == JsonValueKind.String ? contract.GetString() : null;
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
                newSavingsPlan.SetContractNumber(contractNo);
                //if (status == 3)
                  //  newSavingsPlan.Archive();

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
                var existingCategory = _db.SavingsPlanCategories.FirstOrDefault(c => c.Name == newCategory.Name && c.OwnerUserId == userId);
                if (existingCategory is null)
                    _db.SavingsPlanCategories.Add(newCategory);
                else
                    newCategory = existingCategory;
                yield return new KeyValuePair<int, Guid>(dataId, newCategory.Id);
            }
    }

    private IEnumerable<KeyValuePair<int, Guid>> ImportContacts(JsonElement contacts, IEnumerable<KeyValuePair<int, Guid>> contactCategories, Guid userId)
    {
        if (contacts.ValueKind != JsonValueKind.Undefined)
        {
            var existingContacts = _db.Contacts.ToArray();
            var counter = 0;
            Debug.WriteLine($"Import der Kontakte: {existingContacts.Length} vorher!");
            foreach (var contact in contacts.EnumerateArray())
            {
                var dataId = contact.GetProperty("Id").GetInt32();
                var name = contact.GetProperty("Name").GetString();
                var isPaymentProvider = contact.GetProperty("IsPaymentProvider").GetBoolean();
                if (string.IsNullOrWhiteSpace(name)) continue;
                var newContact = new Contact(userId, name, ContactType.Organization, null, null, isPaymentProvider);
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

                var existing = _db.Contacts.FirstOrDefault(c => c.Name == newContact.Name && c.OwnerUserId == userId);
                if (existing is not null)
                    newContact = existing;
                else
                {
                    counter += 1;
                    _db.Contacts.Add(newContact);
                }

                var contactNames = contact.GetProperty("Names");
                switch(contactNames.ValueKind)
                {
                    case JsonValueKind.Array:
                        foreach (var alias in contactNames.EnumerateArray().Select(n =>
                        {
                            switch(n.ValueKind)
                            {
                                case JsonValueKind.String:
                                    return n.GetString();
                                case JsonValueKind.Object:
                                    return n.GetProperty("Name").GetString();
                                default: return "";
                            }
                        })
                        .Where(n => !string.IsNullOrWhiteSpace(n))
                        .Where(n => !_db.AliasNames.Any(a => a.ContactId == newContact.Id && a.Pattern == n)))
                            _db.AliasNames.Add(new AliasName(newContact.Id, alias));
                        break;
                }
                                    
                yield return new KeyValuePair<int, Guid>(dataId, newContact.Id);
            }
            existingContacts = _db.Contacts.ToArray();
            Debug.WriteLine($"Import der Kontakte: {existingContacts.Length} nachher!");
            Debug.WriteLine($"Import der Kontakte: {counter} eingefügt!");
        }
    }

    private IEnumerable<KeyValuePair<int, Guid>> ImportContactCategories(JsonElement contactCategories, Guid userId)
    {
        if (contactCategories.ValueKind != JsonValueKind.Undefined)
            foreach (var category in contactCategories.EnumerateArray())
            {
                var dataId = category.GetProperty("Id").GetInt32();
                var name = category.GetProperty("Name").GetString();
                if (string.IsNullOrWhiteSpace(name)) continue;
                var newCategory = new ContactCategory(userId, name);
                var existingCategory = _db.ContactCategories.FirstOrDefault(c => c.Name == newCategory.Name && c.OwnerUserId == userId);
                if (existingCategory is null)
                    _db.ContactCategories.Add(newCategory);
                else
                    newCategory = existingCategory;
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
        public JsonElement Stocks { get; set; }
        public BackupBankAccountLedgerEntry[]? BankAccountLedgerEntries { get; set; }
        public JsonElement StockLedgerEntries { get; set; }
        public JsonElement FixedAssetLedgerEntries { get; set; }
        // Weitere Properties nach Bedarf
    }
    private sealed class BackupBankAccountLedgerEntry
    {
        public int Id { get; set; }
        public JsonElement Account { get; set; }
        public DateTime PostingDate { get; set; }
        public DateTime ValutaDate { get; set; }
        public decimal Amount { get; set; }
        public string? CurrencyCode { get; set; }
        public string? PostingDescription { get; set; }
        public string? Description { get; set; }
        public string? SourceName { get; set; }
        public BackupContact? SourceContact { get; set; }
        public JsonElement IsCostNeutral { get; set; }

    }
    private sealed class BackupContact
    {
        public int Id { get; set; }
        public Guid? UID { get; set; }
    }
}