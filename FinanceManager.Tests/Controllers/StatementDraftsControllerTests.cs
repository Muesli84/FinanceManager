using FinanceManager.Application.Statements;
using FinanceManager.Domain.Accounts;
using FinanceManager.Domain.Contacts;
using FinanceManager.Infrastructure;
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
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;
using Xunit;

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

        var current = new TestCurrentUserService()
        {
            UserId = owner.Id
        };
        var services = new ServiceCollection();
        services.AddSingleton<AppDbContext>(db);
        services.AddLogging();
        var sp = services.BuildServiceProvider();
        db = sp.GetRequiredService<AppDbContext>();
        var draftService = new StatementDraftService(db);        
        var logger = sp.GetRequiredService<ILogger<StatementDraftsController>>();
        var controller = new StatementDraftsController(draftService, current, logger);        
        return (controller, db, current.UserId);
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
