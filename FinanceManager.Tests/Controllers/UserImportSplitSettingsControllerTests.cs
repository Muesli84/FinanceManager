using System;
using System.Net.Mime;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FinanceManager.Application;
using FinanceManager.Domain.Users;
using FinanceManager.Shared.Dtos;
using FinanceManager.Web.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using FinanceManager.Infrastructure; // AppDbContext
using FinanceManager.Domain.Users; // User entity
using FinanceManager.Domain; // Entity base
using System.Reflection;
using FinanceManager.Tests.TestHelpers;

namespace FinanceManager.Tests.Controllers;

public sealed class UserImportSplitSettingsControllerTests
{
    private sealed class TestCurrentUser : ICurrentUserService
    {
        public Guid UserId { get; set; }
        public string? PreferredLanguage => null;
        public bool IsAuthenticated => true;
        public bool IsAdmin => false;
    }

    private static (UserImportSplitSettingsController controller, AppDbContext db, TestCurrentUser currentUser) Create()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseSqlite("DataSource=:memory:"));
        services.AddScoped<ICurrentUserService, TestCurrentUser>();
        var sp = services.BuildServiceProvider();
        var db = sp.GetRequiredService<AppDbContext>();
        db.Database.OpenConnection();
        db.Database.EnsureCreated();

        var current = (TestCurrentUser)sp.GetRequiredService<ICurrentUserService>();
        current.UserId = Guid.NewGuid();
        var user = new User("test", "hash", false);
        // set protected Id via runtime-type reflection to avoid TargetException
        TestEntityHelper.SetEntityId(user, current.UserId);
        db.Users.Add(user);
        db.SaveChanges();

        var controller = new UserImportSplitSettingsController(db, current);
        var http = new DefaultHttpContext();
        http.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, current.UserId.ToString()) }, "test"));
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        return (controller, db, current);
    }

    [Fact]
    public async Task GetAsync_ShouldReturnDefaults()
    {
        var (controller, _, _) = Create();
        var result = await controller.GetAsync(CancellationToken.None) as OkObjectResult;
        result.Should().NotBeNull();
        var dto = result!.Value as ImportSplitSettingsDto;
        dto!.Mode.Should().Be(ImportSplitMode.MonthlyOrFixed);
        dto.MaxEntriesPerDraft.Should().Be(250);
        dto.MonthlySplitThreshold.Should().Be(250);
        dto.MinEntriesPerDraft.Should().Be(8); // new default
    }

    [Fact]
    public async Task UpdateAsync_ShouldPersistValues_IncludingMinEntries()
    {
        var (controller, db, _) = Create();
        var req = new UserImportSplitSettingsController.UpdateRequest
        {
            Mode = ImportSplitMode.MonthlyOrFixed,
            MaxEntriesPerDraft = 300,
            MonthlySplitThreshold = 350,
            MinEntriesPerDraft = 5
        };
        var resp = await controller.UpdateAsync(req, CancellationToken.None);
        resp.Should().BeOfType<NoContentResult>();

        var user = await db.Users.SingleAsync();
        user.ImportSplitMode.Should().Be(ImportSplitMode.MonthlyOrFixed);
        user.ImportMaxEntriesPerDraft.Should().Be(300);
        user.ImportMonthlySplitThreshold.Should().Be(350);
        user.ImportMinEntriesPerDraft.Should().Be(5);
    }

    [Fact]
    public async Task UpdateAsync_ShouldValidateThreshold()
    {
        var (controller, db, _) = Create();
        var req = new UserImportSplitSettingsController.UpdateRequest
        {
            Mode = ImportSplitMode.MonthlyOrFixed,
            MaxEntriesPerDraft = 300,
            MonthlySplitThreshold = 100,
            MinEntriesPerDraft = 8
        };
        var resp = await controller.UpdateAsync(req, CancellationToken.None);
        var obj = resp.Should().BeOfType<ObjectResult>().Subject;
        var details = obj.Value.Should().BeOfType<ValidationProblemDetails>().Subject;
        details.Errors.Should().ContainKey(nameof(req.MonthlySplitThreshold));

        var user = await db.Users.SingleAsync();
        user.ImportMaxEntriesPerDraft.Should().Be(250); // unchanged
        user.ImportMinEntriesPerDraft.Should().Be(8); // unchanged default
    }

    [Fact]
    public async Task UpdateAsync_ShouldAllowFixedSizeWithoutThreshold_AndPersistMinEntries()
    {
        var (controller, db, _) = Create();
        var req = new UserImportSplitSettingsController.UpdateRequest
        {
            Mode = ImportSplitMode.FixedSize,
            MaxEntriesPerDraft = 400,
            MonthlySplitThreshold = null,
            MinEntriesPerDraft = 3 // ignored in fixed size but persisted for later
        };
        var resp = await controller.UpdateAsync(req, CancellationToken.None);
        resp.Should().BeOfType<NoContentResult>();

        var user = await db.Users.SingleAsync();
        user.ImportSplitMode.Should().Be(ImportSplitMode.FixedSize);
        user.ImportMaxEntriesPerDraft.Should().Be(400);
        user.ImportMinEntriesPerDraft.Should().Be(3);
    }

    [Fact]
    public async Task UpdateAsync_ShouldFail_WhenMinEntriesGreaterThanMax()
    {
        var (controller, db, _) = Create();
        var req = new UserImportSplitSettingsController.UpdateRequest
        {
            Mode = ImportSplitMode.Monthly,
            MaxEntriesPerDraft = 50,
            MonthlySplitThreshold = null,
            MinEntriesPerDraft = 60 // invalid
        };
        var resp = await controller.UpdateAsync(req, CancellationToken.None);
        var obj = resp.Should().BeOfType<ObjectResult>().Subject;
        var details = obj.Value.Should().BeOfType<ValidationProblemDetails>().Subject;
        details.Errors.Should().ContainKey(nameof(req.MinEntriesPerDraft));

        var user = await db.Users.SingleAsync();
        user.ImportMaxEntriesPerDraft.Should().Be(250); // unchanged
        user.ImportMinEntriesPerDraft.Should().Be(8);   // unchanged
    }
}
