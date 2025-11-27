using System;
using System.Net.Mime;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FinanceManager.Application;
using FinanceManager.Domain.Users;
using FinanceManager.Shared.Dtos;
using FinanceManager.Web.Controllers;
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
        Assert.NotNull(result);
        var dto = result!.Value as ImportSplitSettingsDto;
        Assert.NotNull(dto);
        Assert.Equal(ImportSplitMode.MonthlyOrFixed, dto!.Mode);
        Assert.Equal(250, dto.MaxEntriesPerDraft);
        Assert.Equal(250, dto.MonthlySplitThreshold);
        Assert.Equal(8, dto.MinEntriesPerDraft); // new default
    }

    [Fact]
    public async Task UpdateAsync_ShouldPersistValues_IncludingMinEntries()
    {
        var (controller, db, _) = Create();
        var req = new ImportSplitSettingsUpdateRequest(
            Mode: ImportSplitMode.MonthlyOrFixed,
            MaxEntriesPerDraft: 300,
            MonthlySplitThreshold: 350,
            MinEntriesPerDraft: 5);
        var resp = await controller.UpdateAsync(req, CancellationToken.None);
        Assert.IsType<NoContentResult>(resp);

        var user = await db.Users.SingleAsync();
        Assert.Equal(ImportSplitMode.MonthlyOrFixed, user.ImportSplitMode);
        Assert.Equal(300, user.ImportMaxEntriesPerDraft);
        Assert.Equal(5, user.ImportMinEntriesPerDraft);
    }

    [Fact]
    public async Task UpdateAsync_ShouldValidateThreshold()
    {
        var (controller, db, _) = Create();
        var req = new ImportSplitSettingsUpdateRequest(
            Mode: ImportSplitMode.MonthlyOrFixed,
            MaxEntriesPerDraft: 300,
            MonthlySplitThreshold: 100,
            MinEntriesPerDraft: 8);
        var resp = await controller.UpdateAsync(req, CancellationToken.None);
        var obj = Assert.IsType<ObjectResult>(resp);
        var details = Assert.IsType<ValidationProblemDetails>(obj.Value);
        Assert.True(details.Errors.ContainsKey(nameof(req.MonthlySplitThreshold)));

        var user = await db.Users.SingleAsync();
        Assert.Equal(250, user.ImportMaxEntriesPerDraft); // unchanged
        Assert.Equal(8, user.ImportMinEntriesPerDraft);   // unchanged default
    }

    [Fact]
    public async Task UpdateAsync_ShouldAllowFixedSizeWithoutThreshold_AndPersistMinEntries()
    {
        var (controller, db, _) = Create();
        var req = new ImportSplitSettingsUpdateRequest(
            Mode: ImportSplitMode.FixedSize,
            MaxEntriesPerDraft: 400,
            MonthlySplitThreshold: null,
            MinEntriesPerDraft: 3);
        var resp = await controller.UpdateAsync(req, CancellationToken.None);
        Assert.IsType<NoContentResult>(resp);

        var user = await db.Users.SingleAsync();
        Assert.Equal(ImportSplitMode.FixedSize, user.ImportSplitMode);
        Assert.Equal(400, user.ImportMaxEntriesPerDraft);
        Assert.Equal(3, user.ImportMinEntriesPerDraft);
    }

    [Fact]
    public async Task UpdateAsync_ShouldFail_WhenMinEntriesGreaterThanMax()
    {
        var (controller, db, _) = Create();
        var req = new ImportSplitSettingsUpdateRequest(
            Mode: ImportSplitMode.Monthly,
            MaxEntriesPerDraft: 50,
            MonthlySplitThreshold: null,
            MinEntriesPerDraft: 60);
        var resp = await controller.UpdateAsync(req, CancellationToken.None);
        var obj = Assert.IsType<ObjectResult>(resp);
        var details = Assert.IsType<ValidationProblemDetails>(obj.Value);
        Assert.True(details.Errors.ContainsKey(nameof(req.MinEntriesPerDraft)));

        var user = await db.Users.SingleAsync();
        Assert.Equal(250, user.ImportMaxEntriesPerDraft); // unchanged
        Assert.Equal(8, user.ImportMinEntriesPerDraft);   // unchanged
    }
}
