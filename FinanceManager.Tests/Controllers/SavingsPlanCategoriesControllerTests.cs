using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FinanceManager.Application;
using FinanceManager.Shared.Dtos;
using FinanceManager.Web.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using FinanceManager.Infrastructure;
using FinanceManager.Domain.Users;
using FinanceManager.Domain;
using FinanceManager.Tests.TestHelpers;

namespace FinanceManager.Tests.Controllers;

public sealed class SavingsPlanCategoriesControllerTests
{
    private sealed class TestCurrentUser : ICurrentUserService
    {
        public Guid UserId { get; set; }
        public string? PreferredLanguage => null;
        public bool IsAuthenticated => true;
        public bool IsAdmin => false;
    }

    private static (SavingsPlanCategoriesController controller, AppDbContext db, TestCurrentUser current) Create()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseSqlite("DataSource=:memory:"));
        services.AddScoped<ICurrentUserService, TestCurrentUser>();
        services.AddScoped<ISavingsPlanCategoryService, SavingsPlanCategoryService>();
        var sp = services.BuildServiceProvider();

        var db = sp.GetRequiredService<AppDbContext>();
        db.Database.OpenConnection();
        db.Database.EnsureCreated();

        var current = (TestCurrentUser)sp.GetRequiredService<ICurrentUserService>();
        current.UserId = Guid.NewGuid();
        // seed a user (required by FK ownership if enforced later)
        var user = new User("tester", "hash", false);
        // set protected Id via TestEntityHelper to avoid reflection issues
        TestEntityHelper.SetEntityId(user, current.UserId);
        db.Users.Add(user);
        db.SaveChanges();

        var controller = new SavingsPlanCategoriesController(sp.GetRequiredService<ISavingsPlanCategoryService>(), current);
        return (controller, db, current);
    }

    [Fact]
    public async Task GetAsync_ShouldReturnNotFound_ForUnknownId()
    {
        var (controller, _, _) = Create();
        var resp = await controller.GetAsync(Guid.NewGuid(), CancellationToken.None);
        resp.Result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task Create_And_Get_ShouldReturnCategory()
    {
        var (controller, _, _) = Create();
        var created = await controller.CreateAsync(new SavingsPlanCategoryDto { Name = "MyCat" }, CancellationToken.None);
        created.Value.Should().NotBeNull();

        var id = created.Value!.Id;
        var get = await controller.GetAsync(id, CancellationToken.None);
        get.Value.Should().NotBeNull();
        get.Value!.Name.Should().Be("MyCat");
    }

    [Fact]
    public async Task Update_ShouldModifyName()
    {
        var (controller, _, _) = Create();
        var created = await controller.CreateAsync(new SavingsPlanCategoryDto { Name = "Old" }, CancellationToken.None);
        var id = created.Value!.Id;

        var updated = await controller.UpdateAsync(id, new SavingsPlanCategoryDto { Name = "NewName" }, CancellationToken.None);
        updated.Value!.Name.Should().Be("NewName");

        var get = await controller.GetAsync(id, CancellationToken.None);
        get.Value!.Name.Should().Be("NewName");
    }
}
