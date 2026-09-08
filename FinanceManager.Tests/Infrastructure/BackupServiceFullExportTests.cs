using FinanceManager.Domain;
using FinanceManager.Domain.Accounts;
using FinanceManager.Domain.Attachments;
using FinanceManager.Domain.Contacts;
using FinanceManager.Domain.Notifications;
using FinanceManager.Domain.Postings;
using FinanceManager.Domain.Reports;
using FinanceManager.Domain.Savings;
using FinanceManager.Domain.Securities;
using FinanceManager.Domain.Statements;
using FinanceManager.Infrastructure;
using FinanceManager.Infrastructure.Backups;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace FinanceManager.Tests.Infrastructure
{
    /// <summary>
    /// Round-trip integrity guard for the full backup export produced by <c>BackupService.CreateAsync</c>.
    /// Seeds exactly one row of every domain entity type that a full backup is expected to contain, exports it,
    /// and compares the resulting NDJSON payload against each entity's own <c>ToBackupDto()</c> projection. This
    /// class exists to catch the case where a newly added entity type, or a change to an existing one, is not
    /// (or is incorrectly) wired into the export path - a defect that would only otherwise surface when a user
    /// tries to restore a backup and discovers data silently missing.
    /// </summary>
    public class BackupServiceFullExportTests
    {
        /// <summary>
        /// Minimal stand-in for <see cref="IHostEnvironment"/> that redirects the backup service's file storage
        /// to an isolated temporary directory instead of the real application content root, so backup ZIP files
        /// created during the test never touch (or depend on) the actual application data directory.
        /// </summary>
        private sealed class TestHostEnvironment : IHostEnvironment
        {
            /// <summary>Fixed to "Development" for tests; not exercised by the assertions in this file.</summary>
            public string EnvironmentName { get; set; } = "Development";
            /// <summary>Fixed application name identifying the test host.</summary>
            public string ApplicationName { get; set; } = "FinanceManager.Tests";
            /// <summary>The isolated temporary directory the backup service should treat as its content root.</summary>
            public string ContentRootPath { get; set; } = string.Empty;
            /// <summary>Not used by these tests; present only to satisfy the <see cref="IHostEnvironment"/> contract.</summary>
            /// <returns>The result.</returns>
            public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
        }

        /// <summary>
        /// Seeds one deterministic row (fixed ids and timestamps) for every entity collection a full backup is
        /// expected to export - accounts, contacts, contact categories, alias names, attachment categories,
        /// attachments, savings plans and their categories, securities and their prices, statement imports,
        /// statement entries, postings, statement drafts and draft entries, report favorites, home KPIs,
        /// notifications, and account shares - then runs <c>BackupService.CreateAsync</c> and unpacks the
        /// resulting ZIP/NDJSON backup. For every entity type it asserts the exported JSON array is non-empty and
        /// that its serialized element is byte-for-byte identical to the entity's own <c>ToBackupDto()</c> output
        /// (statement drafts are compared with their nested <c>Entries</c> excluded, since draft entries are
        /// exported as their own top-level collection instead). This is the safety net against a backup silently
        /// dropping an entity type, or its export mapping drifting from the live domain model, which would only
        /// be discovered when a user later tries to restore and finds data missing.
        /// </summary>
        /// <returns>A task that completes once every seeded entity type has been verified against the export.</returns>
        [Fact]
        public async Task CreateAsync_FullBackup_IncludesAllSeededData()
        {
            var dbName = Guid.NewGuid().ToString();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options;

            await using var db = new AppDbContext(options);

            // deterministic values
            var owner = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var fixedDate = DateTime.SpecifyKind(new DateTime(2025, 1, 2, 3, 4, 5), DateTimeKind.Utc);

            // helper to set common properties via reflection
            void SetGuid(object obj, string propName, Guid value)
            {
                var p = obj.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (p != null && p.CanWrite) p.SetValue(obj, value);
            }
            void SetDateProps(object obj, DateTime dt)
            {
                var names = new[] { "CreatedUtc", "ModifiedUtc", "ArchivedUtc", "ImportedAtUtc", "GrantedUtc", "ScheduledDateUtc", "PriceErrorSinceUtc" };
                foreach (var name in names)
                {
                    var p = obj.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (p != null && p.CanWrite) p.SetValue(obj, dt);
                }
            }

            // Create one instance per exported collection and set deterministic Id/CreatedUtc
            var contactCategory = new ContactCategory(owner, "BanksRT");
            SetGuid(contactCategory, "Id", Guid.Parse("20000000-0000-0000-0000-000000000001"));
            SetDateProps(contactCategory, fixedDate);
            db.ContactCategories.Add(contactCategory);

            var contact = new Contact(owner, "MyBankRT", ContactType.Bank, contactCategory.Id);
            SetGuid(contact, "Id", Guid.Parse("20000000-0000-0000-0000-000000000002"));
            SetDateProps(contact, fixedDate);
            db.Contacts.Add(contact);

            var alias = new AliasName(contact.Id, "MyBankAliasRT");
            SetGuid(alias, "Id", Guid.Parse("20000000-0000-0000-0000-000000000003"));
            SetDateProps(alias, fixedDate);
            db.AliasNames.Add(alias);

            var attachmentCategory = new AttachmentCategory(owner, "DocsRT");
            SetGuid(attachmentCategory, "Id", Guid.Parse("20000000-0000-0000-0000-000000000004"));
            SetDateProps(attachmentCategory, fixedDate);
            db.AttachmentCategories.Add(attachmentCategory);

            var attachment = new Attachment(owner, AttachmentEntityKind.Account, Guid.Parse("30000000-0000-0000-0000-000000000001"), "file_rt.txt", "text/plain", 3, null, null, new byte[] { 1, 2, 3 }, null);
            // set additional metadata via reflection
            var shaProp = attachment.GetType().GetProperty("Sha256", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            shaProp?.SetValue(attachment, "sha256abc");
            var urlProp = attachment.GetType().GetProperty("Url", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            urlProp?.SetValue(attachment, "http://example.com/file_rt.txt");
            var noteProp = attachment.GetType().GetProperty("Note", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            noteProp?.SetValue(attachment, "note_rt");
            SetGuid(attachment, "Id", Guid.Parse("20000000-0000-0000-0000-000000000005"));
            SetDateProps(attachment, fixedDate);
            db.Attachments.Add(attachment);

            // choose a non-default enum value for AccountType
            AccountType accountType;
            var acctEnum = typeof(AccountType);
            var acctVals = Enum.GetValues(acctEnum).Cast<object>().ToArray();
            accountType = (AccountType)(acctVals.FirstOrDefault(v => Convert.ToInt64(v) != 0) ?? acctVals.First());
            var account = new Account(owner, accountType, "CheckingRT", "DE1234567890", contact.Id);
            SetGuid(account, "Id", Guid.Parse("20000000-0000-0000-0000-000000000006"));
            SetDateProps(account, fixedDate);
            db.Accounts.Add(account);

            var savingsCat = new SavingsPlanCategory(owner, "SavingsCatRT");
            SetGuid(savingsCat, "Id", Guid.Parse("20000000-0000-0000-0000-000000000007"));
            SetDateProps(savingsCat, fixedDate);
            db.SavingsPlanCategories.Add(savingsCat);

            var sptype = (SavingsPlanType)Enum.GetValues(typeof(SavingsPlanType)).Cast<object>().FirstOrDefault(v => Convert.ToInt64(v) != 0)!;
            var spinterval = (SavingsPlanInterval)Enum.GetValues(typeof(SavingsPlanInterval)).Cast<object>().FirstOrDefault(v => Convert.ToInt64(v) != 0)!;
            var savings = new SavingsPlan(owner, "MyPlanRT", sptype, 50m, fixedDate.Date.AddDays(1), spinterval, savingsCat.Id);
            SetGuid(savings, "Id", Guid.Parse("20000000-0000-0000-0000-000000000008"));
            SetDateProps(savings, fixedDate);
            db.SavingsPlans.Add(savings);

            var securityCat = new SecurityCategory(owner, "StocksRT");
            SetGuid(securityCat, "Id", Guid.Parse("20000000-0000-0000-0000-000000000009"));
            SetDateProps(securityCat, fixedDate);
            db.SecurityCategories.Add(securityCat);

            var security = new Security(owner, "ACMERT", "ACMEIDRT", "Desc", "ACME.AV", "EUR", securityCat.Id);
            SetGuid(security, "Id", Guid.Parse("20000000-0000-0000-0000-000000000010"));
            SetDateProps(security, fixedDate);
            db.Securities.Add(security);

            var price = new SecurityPrice(security.Id, fixedDate.Date, 12.34m);
            SetGuid(price, "Id", Guid.Parse("20000000-0000-0000-0000-000000000011"));
            SetDateProps(price, fixedDate);
            db.SecurityPrices.Add(price);

            var stmtImport = new StatementImport(account.Id, ImportFormat.Csv, "stmt_rt.csv");
            SetGuid(stmtImport, "Id", Guid.Parse("20000000-0000-0000-0000-000000000012"));
            SetDateProps(stmtImport, fixedDate);
            db.StatementImports.Add(stmtImport);

            var stmtEntry = new StatementEntry(stmtImport.Id, fixedDate.Date, 7.89m, "SubjectRT", "hashRT", "RecipientRT", fixedDate.Date, "EUR", "statement description", false, false);
            SetGuid(stmtEntry, "Id", Guid.Parse("20000000-0000-0000-0000-000000000013"));
            SetDateProps(stmtEntry, fixedDate);
            db.StatementEntries.Add(stmtEntry);

            var pk = (PostingKind)Enum.GetValues(typeof(PostingKind)).Cast<object>().FirstOrDefault(v => Convert.ToInt64(v) != 0)!;
            var posting = new Posting(Guid.NewGuid(), pk, account.Id, contact.Id, savings.Id, security.Id, fixedDate.Date, 7.89m);
            SetGuid(posting, "Id", Guid.Parse("20000000-0000-0000-0000-000000000014"));
            SetDateProps(posting, fixedDate);
            db.Postings.Add(posting);

            var draft = new StatementDraft(owner, "orig_rt.csv", "AcctRT", "descRT");
            SetGuid(draft, "Id", Guid.Parse("20000000-0000-0000-0000-000000000015"));
            SetDateProps(draft, fixedDate);
            db.StatementDrafts.Add(draft);

            var sdstatus = (StatementDraftEntryStatus)Enum.GetValues(typeof(StatementDraftEntryStatus)).Cast<object>().FirstOrDefault(v => Convert.ToInt64(v) != 0)!;
            var draftEntry = new StatementDraftEntry(0, draft.Id, fixedDate.Date, 2.34m, "subjRT", "RecipientDraft", fixedDate.Date, "EUR", "draft description", false, false, sdstatus);
            SetGuid(draftEntry, "Id", Guid.Parse("20000000-0000-0000-0000-000000000016"));
            SetDateProps(draftEntry, fixedDate);
            db.StatementDraftEntries.Add(draftEntry);

            var rfPk = (PostingKind)Enum.GetValues(typeof(PostingKind)).Cast<object>().FirstOrDefault(v => Convert.ToInt64(v) != 0)!;
            var reportFavorite = new ReportFavorite(owner, "RFRT", rfPk, false, ReportInterval.Month, false, false, true, false);
            SetGuid(reportFavorite, "Id", Guid.Parse("20000000-0000-0000-0000-000000000017"));
            SetDateProps(reportFavorite, fixedDate);
            db.ReportFavorites.Add(reportFavorite);

            var hkKind = (HomeKpiKind)Enum.GetValues(typeof(HomeKpiKind)).Cast<object>().FirstOrDefault(v => Convert.ToInt64(v) != 0)!;
            var hkDisplay = (HomeKpiDisplayMode)Enum.GetValues(typeof(HomeKpiDisplayMode)).Cast<object>().FirstOrDefault(v => Convert.ToInt64(v) != 0)!;
            var homeKpi = new HomeKpi(owner, hkKind, hkDisplay, 1, reportFavorite.Id);
            SetGuid(homeKpi, "Id", Guid.Parse("20000000-0000-0000-0000-000000000018"));
            SetDateProps(homeKpi, fixedDate);
            db.HomeKpis.Add(homeKpi);

            var nType = (NotificationType)Enum.GetValues(typeof(NotificationType)).Cast<object>().FirstOrDefault(v => Convert.ToInt64(v) != 0)!;
            var nTarget = (NotificationTarget)Enum.GetValues(typeof(NotificationTarget)).Cast<object>().FirstOrDefault(v => Convert.ToInt64(v) != 0)!;
            var notification = new Notification { OwnerUserId = owner, Title = "TRT", Message = "MRT", Type = nType, Target = nTarget, ScheduledDateUtc = fixedDate, CreatedUtc = fixedDate };
            SetGuid(notification, "Id", Guid.Parse("20000000-0000-0000-0000-000000000019"));
            SetDateProps(notification, fixedDate);
            db.Notifications.Add(notification);

            // Account share: create directly using public type and enum
            var accountShare = new AccountShare(account.Id, Guid.Parse("22222222-2222-2222-2222-222222222222"), AccountShareRole.Read);
            SetGuid(accountShare, "Id", Guid.Parse("20000000-0000-0000-0000-000000000020"));
            SetDateProps(accountShare, fixedDate);
            db.AccountShares.Add(accountShare);

            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var temp = Path.Combine(Path.GetTempPath(), "fmtests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(temp);
            var backupsDir = Path.Combine(temp, "backups");
            Directory.CreateDirectory(backupsDir);

            var env = new TestHostEnvironment { ContentRootPath = temp };
            var services = new ServiceCollection().BuildServiceProvider();
            var logger = NullLogger<BackupService>.Instance;

            var svc = new BackupService(db, env, logger, services);
            var backupDto = await svc.CreateAsync(owner, CancellationToken.None);
            Assert.NotNull(backupDto);

            var rec = db.Backups.FirstOrDefault(b => b.Id == backupDto.Id);
            Assert.NotNull(rec);
            var full = Path.Combine(backupsDir, rec.StoragePath);
            Assert.True(File.Exists(full));

            using var fs = File.OpenRead(full);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read, leaveOpen: false);
            var entry = zip.Entries.FirstOrDefault();
            Assert.NotNull(entry);
            using var es = entry.Open();
            using var ms = new MemoryStream();
            await es.CopyToAsync(ms, TestContext.Current.CancellationToken);
            ms.Position = 0;
            var text = Encoding.UTF8.GetString(ms.ToArray());

            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.True(lines.Length >= 2);

            var data = JsonDocument.Parse(lines[1]).RootElement;

            // mapping from json property name -> domain type that declares the BackupDto nested record
            var map = new Dictionary<string, Type>
            {
                ["Accounts"] = typeof(Account),
                ["Contacts"] = typeof(Contact),
                ["ContactCategories"] = typeof(ContactCategory),
                ["AliasNames"] = typeof(AliasName),
                ["AttachmentCategories"] = typeof(AttachmentCategory),
                ["Attachments"] = typeof(Attachment),
                ["SavingsPlanCategories"] = typeof(SavingsPlanCategory),
                ["SavingsPlans"] = typeof(SavingsPlan),
                ["SecurityCategories"] = typeof(SecurityCategory),
                ["Securities"] = typeof(Security),
                ["SecurityPrices"] = typeof(SecurityPrice),
                ["StatementImports"] = typeof(StatementImport),
                ["StatementEntries"] = typeof(StatementEntry),
                ["Postings"] = typeof(Posting),
                ["StatementDrafts"] = typeof(StatementDraft),
                ["StatementDraftEntries"] = typeof(StatementDraftEntry),
                ["ReportFavorites"] = typeof(ReportFavorite),
                ["HomeKpis"] = typeof(HomeKpi),
                ["Notifications"] = typeof(Notification),
                ["AccountShares"] = typeof(object) // handled dynamically
            };

            foreach (var kv in map)
            {
                if (!data.TryGetProperty(kv.Key, out var arr)) throw new ArgumentException($"{kv.Key} not found in mapping list.");
                Assert.True(arr.GetArrayLength() > 0, $"Expected {kv.Key} to have at least one element");
                var element = arr[0];

                Type? dtoType = null;
                if (kv.Value == typeof(object))
                {
                    // account share: try to find backup dto type by name convention
                    var acctShareType = typeof(Account).Assembly.GetType("FinanceManager.Domain.Accounts.AccountShare");
                    dtoType = acctShareType?.GetNestedType("AccountShareBackupDto", BindingFlags.Public | BindingFlags.NonPublic);
                }
                else
                {
                    dtoType = kv.Value.GetNestedType(kv.Value.Name + "BackupDto", BindingFlags.Public | BindingFlags.NonPublic);
                }

                Assert.NotNull(dtoType);

                // deserialize JSON element into DTO instance
                var raw = element.GetRawText();
                var deserializedDto = JsonSerializer.Deserialize(raw, dtoType);
                Assert.NotNull(deserializedDto);

                // get original domain object and its ToBackupDto
                object? originalObj = null;
                switch (kv.Key)
                {
                    case "Accounts": originalObj = account; break;
                    case "Contacts": originalObj = contact; break;
                    case "ContactCategories": originalObj = contactCategory; break;
                    case "AliasNames": originalObj = alias; break;
                    case "AttachmentCategories": originalObj = attachmentCategory; break;
                    case "Attachments": originalObj = attachment; break;
                    case "SavingsPlanCategories": originalObj = savingsCat; break;
                    case "SavingsPlans": originalObj = savings; break;
                    case "SecurityCategories": originalObj = securityCat; break;
                    case "Securities": originalObj = security; break;
                    case "SecurityPrices": originalObj = price; break;
                    case "StatementImports": originalObj = stmtImport; break;
                    case "StatementEntries": originalObj = stmtEntry; break;
                    case "Postings": originalObj = posting; break;
                    case "StatementDrafts": originalObj = draft; break;
                    case "StatementDraftEntries": originalObj = draftEntry; break;
                    case "ReportFavorites": originalObj = reportFavorite; break;
                    case "HomeKpis": originalObj = homeKpi; break;
                    case "Notifications": originalObj = notification; break;
                    case "AccountShares": originalObj = null; break;
                }

                // obtain original's DTO
                var origToDto = originalObj != null ? originalObj.GetType().GetMethod("ToBackupDto", BindingFlags.Public | BindingFlags.Instance) : null;
                Assert.True(origToDto != null || kv.Key == "AccountShares", $"ToBackupDto not found for {kv.Key}");

                if (origToDto != null)
                {
                    var origDto = origToDto.Invoke(originalObj, null);
                    var origJson = JsonSerializer.Serialize(origDto, dtoType);
                    // For StatementDraft, the original's DTO may contain nested Entries while the backup's draft entry is exported separately.
                    if (kv.Key == "StatementDrafts")
                    {
                        var origNode = JsonNode.Parse(origJson)?.AsObject();
                        var rawNode = JsonNode.Parse(raw)?.AsObject();
                        origNode?.Remove("Entries");
                        rawNode?.Remove("Entries");
                        var origNormalized = origNode?.ToJsonString();
                        var rawNormalized = rawNode?.ToJsonString();
                        Assert.Equal(origNormalized, rawNormalized);
                        continue;
                    }
                    // compare exact equality
                    Assert.Equal(origJson, raw);
                }
            }

            try { Directory.Delete(temp, true); } catch { }
        }
    }
}
