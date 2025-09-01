using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;
using FinanceManager.Application.Statements;
using FinanceManager.Domain.Accounts;
using FinanceManager.Infrastructure;
using FinanceManager.Infrastructure.Statements;
using FinanceManager.Web.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace FinanceManager.Tests.Controllers;

public sealed class StatementDraftsControllerTests
{
    private static (StatementDraftsController controller, AppDbContext db, Guid userId) Create()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddLogging();
        var sp = services.BuildServiceProvider();
        var db = sp.GetRequiredService<AppDbContext>();
        var draftService = new StatementDraftService(db);
        var current = new TestCurrentUserService();
        var logger = sp.GetRequiredService<ILogger<StatementDraftsController>>();
        var controller = new StatementDraftsController(draftService, current, logger);
        return (controller, db, current.UserId);
    }

    private sealed class TestCurrentUserService : FinanceManager.Application.ICurrentUserService
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public bool IsAuthenticated => true;
        public bool IsAdmin => false;
        public string? PreferredLanguage => null;
    }

    [Fact]
    public async Task UploadAsync_ShouldCreateDraft()
    {
        var (controller, db, user) = Create();
        db.Accounts.Add(new Account(user, FinanceManager.Domain.AccountType.Giro, "A", null, Guid.NewGuid()));
        await db.SaveChangesAsync();

        var bytes = Encoding.UTF8.GetBytes("dummy");
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
