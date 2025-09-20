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
        // set protected Id via reflection
        typeof(Entity).GetProperty("Id")!.SetValue(user, current.UserId);
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
    }

    [Fact]
    public async Task UpdateAsync_ShouldPersistValues()
    {
        var (controller, db, _) = Create();
        var req = new UserImportSplitSettingsController.UpdateRequest
        {
            Mode = ImportSplitMode.MonthlyOrFixed,
            MaxEntriesPerDraft = 300,
            MonthlySplitThreshold = 350
        };
        var resp = await controller.UpdateAsync(req, CancellationToken.None);
        resp.Should().BeOfType<NoContentResult>();

        var user = await db.Users.SingleAsync();
        user.ImportSplitMode.Should().Be(ImportSplitMode.MonthlyOrFixed);
        user.ImportMaxEntriesPerDraft.Should().Be(300);
        user.ImportMonthlySplitThreshold.Should().Be(350);
    }

    [Fact]
    public async Task UpdateAsync_ShouldValidateThreshold()
    {
        var (controller, db, _) = Create();
        var req = new UserImportSplitSettingsController.UpdateRequest
        {
            Mode = ImportSplitMode.MonthlyOrFixed,
            MaxEntriesPerDraft = 300,
            MonthlySplitThreshold = 100
        };
        var resp = await controller.UpdateAsync(req, CancellationToken.None);
        var obj = resp.Should().BeOfType<ObjectResult>().Subject;
        var details = obj.Value.Should().BeOfType<ValidationProblemDetails>().Subject;

        var user = await db.Users.SingleAsync();
        user.ImportMaxEntriesPerDraft.Should().Be(250); // unchanged
    }

    [Fact]
    public async Task UpdateAsync_ShouldAllowFixedSizeWithoutThreshold()
    {
        var (controller, db, _) = Create();
        var req = new UserImportSplitSettingsController.UpdateRequest
        {
            Mode = ImportSplitMode.FixedSize,
            MaxEntriesPerDraft = 400,
            MonthlySplitThreshold = null
        };
        var resp = await controller.UpdateAsync(req, CancellationToken.None);
        resp.Should().BeOfType<NoContentResult>();

        var user = await db.Users.SingleAsync();
        user.ImportSplitMode.Should().Be(ImportSplitMode.FixedSize);
        user.ImportMaxEntriesPerDraft.Should().Be(400);
        user.ImportMonthlySplitThreshold.Should().NotBeNull(); // previous value retained
    }
}
