using FinanceManager.Application.Statements;
using FinanceManager.Domain.Accounts;
using FinanceManager.Domain.Contacts;
using FinanceManager.Infrastructure;
using FinanceManager.Infrastructure.Aggregates;
using FinanceManager.Infrastructure.Statements;
using FinanceManager.Shared.Dtos;
using FinanceManager.Web.Controllers;
using FluentAssertions;
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
        services.AddSingleton(db);
        services.AddLogging();
        var sp = services.BuildServiceProvider();
        db = sp.GetRequiredService<AppDbContext>();
        var draftService = new StatementDraftService(db, new PostingAggregateService(db));
        var logger = sp.GetRequiredService<ILogger<StatementDraftsController>>();
        var taskManager = new DummyBackgroundTaskManager();
        var controller = new StatementDraftsController(draftService, current, logger, taskManager);
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
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task AddEntry_ShouldReturnNotFound_ForUnknownDraft()
    {
        var (controller, _, _) = Create();
        var response = await controller.AddEntryAsync(Guid.NewGuid(), new StatementDraftsController.AddEntryRequest(DateTime.UtcNow.Date, 10m, "X"), default);
        response.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task Commit_ShouldReturnNotFound_WhenDraftMissing()
    {
        var (controller, _, _) = Create();
        var response = await controller.CommitAsync(Guid.NewGuid(), new StatementDraftsController.CommitRequest(Guid.NewGuid(), FinanceManager.Domain.ImportFormat.Csv), default);
        response.Should().BeOfType<NotFoundResult>();
    }
}
