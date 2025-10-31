using FinanceManager.Application.Statements;
using FinanceManager.Domain.Accounts;
using FinanceManager.Domain.Contacts;
using FinanceManager.Infrastructure;
using FinanceManager.Infrastructure.Aggregates;
using FinanceManager.Infrastructure.Statements;
using FinanceManager.Shared.Dtos;
using FinanceManager.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using FinanceManager.Application;
using System.Collections.Generic;
using FinanceManager.Application.Attachments;
using FinanceManager.Infrastructure.Attachments;

namespace FinanceManager.Tests.Controllers;

public sealed class StatementDraftsControllerTests
{
    private static (StatementDraftsController controller, AppDbContext db, Guid userId) Create()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(conn)
            .Options;
        var db = new AppDbContext(options);
        db.Database.EnsureCreated();
        var owner = new FinanceManager.Domain.Users.User("owner", "hash", true);
        db.Users.Add(owner);
        db.SaveChanges();
        var ownerContact = new Contact(owner.Id, "Ich", ContactType.Self, null, null);
        db.Contacts.Add(ownerContact);
        db.SaveChanges();

        var current = new TestCurrentUserService { UserId = owner.Id };
        var services = new ServiceCollection();
        services.AddScoped<IAttachmentService, AttachmentService>();
        services.AddSingleton(db);
        services.AddLogging();
        var sp = services.BuildServiceProvider();
        db = sp.GetRequiredService<AppDbContext>();
        var draftService = new StatementDraftService(db, new PostingAggregateService(db));
        var logger = sp.GetRequiredService<ILogger<StatementDraftsController>>();
        var taskManager = new DummyBackgroundTaskManager();
        var attachment = sp.GetRequiredService<IAttachmentService>();
        var controller = new StatementDraftsController(draftService, current, logger, taskManager, attachment);
        return (controller, db, current.UserId);
    }

    private sealed class DummyBackgroundTaskManager : IBackgroundTaskManager
    {
        private readonly List<BackgroundTaskInfo> _tasks = new();
        public BackgroundTaskInfo Enqueue(BackgroundTaskType type, Guid userId, object? payload = null, bool allowDuplicate = false)
        {
            var info = new BackgroundTaskInfo(Guid.NewGuid(), type, userId, DateTime.UtcNow, BackgroundTaskStatus.Queued, 0, 0, "Queued", 0, 0, null, null, null, null, null, null, null);
            _tasks.Add(info);
            return info;
        }
        public IReadOnlyList<BackgroundTaskInfo> GetAll() => _tasks;
        public BackgroundTaskInfo? Get(Guid id) => _tasks.FirstOrDefault(t => t.Id == id);
        public bool TryCancel(Guid id) => false;
        public bool TryRemoveQueued(Guid id) => false;
        public bool TryDequeueNext(out Guid id) { id = Guid.Empty; return false; }
        public void UpdateTaskInfo(BackgroundTaskInfo info) { }
        public SemaphoreSlim Semaphore => new(1, 1);
    }

    private sealed class TestCurrentUserService : FinanceManager.Application.ICurrentUserService
    {
        public Guid UserId { get; internal set; } = Guid.NewGuid();
        public bool IsAuthenticated => true;
        public bool IsAdmin => false;
        public string? PreferredLanguage => null;
    }

    [Fact]
    public async Task UploadAsync_ShouldCreateDraft()
    {
        var (controller, db, user) = Create();
        var account = new Account(user, FinanceManager.Domain.AccountType.Giro, "A", null, Guid.NewGuid());
        db.Accounts.Add(account);
        await db.SaveChangesAsync();

        var bytes = Encoding.UTF8.GetBytes($"{{\"Type\":\"Backup\",\"Version\":2}}\n{{ \"BankAccounts\": [{{ \"IBAN\": \"{account.Iban}\"}}], \"BankAccountLedgerEntries\": [], \"BankAccountJournalLines\": [{{\"Id\": 1,\"PostingDate\": \"2017-07-15T00:00:00\",\"ValutaDate\": \"2017-07-15T00:00:00\",\"PostingDescription\": \"Lastschrift\",\"SourceName\": \"GEZ\",\"Description\": \"GEZ Gebuehr\",\"CurrencyCode\": \"EUR\",\"Amount\": -97.95,\"CreatedAt\": \"2017-07-16T12:33:42.000041\"}}] }}");
        var formFile = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "file.csv")
        {
            Headers = new HeaderDictionary(),
            ContentType = "text/csv"
        };

        var result = await controller.UploadAsync(formFile, default);
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task AddEntry_ShouldReturnNotFound_ForUnknownDraft()
    {
        var (controller, _, _) = Create();
        var response = await controller.AddEntryAsync(Guid.NewGuid(), new StatementDraftsController.AddEntryRequest(DateTime.UtcNow.Date, 10m, "X"), default);
        Assert.IsType<NotFoundResult>(response);
    }

    [Fact]
    public async Task Commit_ShouldReturnNotFound_WhenDraftMissing()
    {
        var (controller, _, _) = Create();
        var response = await controller.CommitAsync(Guid.NewGuid(), new StatementDraftsController.CommitRequest(Guid.NewGuid(), FinanceManager.Domain.ImportFormat.Csv), default);
        Assert.IsType<NotFoundResult>(response);
    }

    [Fact]
    public async Task GetEntryAsync_ShouldReturnSplitSumAcrossUploadGroup()
    {
        var (controller, db, user) = Create();
        // Parent draft (no upload group, no account required for this test)
        var parent = new FinanceManager.Domain.Statements.StatementDraft(user, "parent.pdf", null, null);
        db.StatementDrafts.Add(parent);
        await db.SaveChangesAsync();

        // Intermediary contact for parent entry
        var intermediary = new Contact(user, "PayService", ContactType.Organization, null, null, isPaymentIntermediary: true);
        db.Contacts.Add(intermediary);
        await db.SaveChangesAsync();

        var parentEntry = parent.AddEntry(DateTime.Today, 300m, "Root", intermediary.Name, DateTime.Today, "EUR", null, false);
        parentEntry.MarkAccounted(intermediary.Id);
        db.Entry(parentEntry).State = EntityState.Added;
        await db.SaveChangesAsync();

        // Child drafts in SAME upload group (group of split parts)
        var groupId = Guid.NewGuid();
        var child1 = new FinanceManager.Domain.Statements.StatementDraft(user, "c1.pdf", null, null); child1.SetUploadGroup(groupId); db.StatementDrafts.Add(child1);
        var child2 = new FinanceManager.Domain.Statements.StatementDraft(user, "c2.pdf", null, null); child2.SetUploadGroup(groupId); db.StatementDrafts.Add(child2);
        var child3 = new FinanceManager.Domain.Statements.StatementDraft(user, "c3.pdf", null, null); child3.SetUploadGroup(groupId); db.StatementDrafts.Add(child3);
        await db.SaveChangesAsync();

        // Recipient contacts
        var cA = new Contact(user, "Alice", ContactType.Person, null, null);
        var cB = new Contact(user, "Bob", ContactType.Person, null, null);
        var cC = new Contact(user, "Carol", ContactType.Person, null, null);
        db.Contacts.AddRange(cA, cB, cC);
        await db.SaveChangesAsync();

        // Entries in child drafts: 120 + 80 + 100 = 300
        var e1 = child1.AddEntry(DateTime.Today, 120m, "A", cA.Name, DateTime.Today, "EUR", null, false); e1.MarkAccounted(cA.Id); db.Entry(e1).State = EntityState.Added;
        var e2 = child2.AddEntry(DateTime.Today, 80m, "B", cB.Name, DateTime.Today, "EUR", null, false); e2.MarkAccounted(cB.Id); db.Entry(e2).State = EntityState.Added;
        var e3 = child3.AddEntry(DateTime.Today, 100m, "C", cC.Name, DateTime.Today, "EUR", null, false); e3.MarkAccounted(cC.Id); db.Entry(e3).State = EntityState.Added;
        await db.SaveChangesAsync();

        // Link only ONE child draft (child2) via controller endpoint
        var linkResult = await controller.SetEntrySplitDraftAsync(parent.Id, parentEntry.Id, new StatementDraftsController.SetSplitDraftRequest(child2.Id), CancellationToken.None);
        Assert.IsType<OkObjectResult>(linkResult);
        var okLink = (OkObjectResult)linkResult;
        var splitSumProp = okLink.Value!.GetType().GetProperty("SplitSum")!.GetValue(okLink.Value);
        var diffProp = okLink.Value!.GetType().GetProperty("Difference")!.GetValue(okLink.Value);
        Assert.Equal(300m, Convert.ToDecimal(splitSumProp));
        Assert.Equal(0m, Convert.ToDecimal(diffProp));

        // Now query entry again and verify SplitSum uses full upload group
        var getResult = await controller.GetEntryAsync(parent.Id, parentEntry.Id, CancellationToken.None);
        Assert.IsType<OkObjectResult>(getResult);
        var okGet = (OkObjectResult)getResult;
        var splitSumGet = okGet.Value!.GetType().GetProperty("SplitSum")!.GetValue(okGet.Value);
        var diffGet = okGet.Value!.GetType().GetProperty("Difference")!.GetValue(okGet.Value);
        Assert.Equal(300m, Convert.ToDecimal(splitSumGet));
        Assert.Equal(0m, Convert.ToDecimal(diffGet));
    }
}
