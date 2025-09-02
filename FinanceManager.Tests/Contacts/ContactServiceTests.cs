using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FinanceManager.Application.Contacts;
using FinanceManager.Domain;
using FinanceManager.Domain.Contacts;
using FinanceManager.Infrastructure;
using FinanceManager.Infrastructure.Contacts;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FinanceManager.Tests.Contacts;

public sealed class ContactServiceTests
{
    private static (ContactService sut, AppDbContext db) Create()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        var sp = services.BuildServiceProvider();
        var db = sp.GetRequiredService<AppDbContext>();
        var sut = new ContactService(db);
        return (sut, db);
    }

    [Fact]
    public async Task CreateAsync_ShouldCreateContact_WhenValid()
    {
        var (sut, db) = Create();
        var owner = Guid.NewGuid();

        var dto = await sut.CreateAsync(owner, "Alice", ContactType.Person, null, null, null, CancellationToken.None);

        dto.Name.Should().Be("Alice");
        dto.Type.Should().Be(ContactType.Person);
        db.Contacts.Count().Should().Be(1);
    }

    [Fact]
    public async Task CreateAsync_ShouldThrow_WhenTypeSelf()
    {
        var (sut, _) = Create();
        var owner = Guid.NewGuid();
        Func<Task> act = () => sut.CreateAsync(owner, "Me", ContactType.Self, null, null, null, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Self*");
    }

    [Fact]
    public async Task UpdateAsync_ShouldUpdateNameAndType_WhenNotSelf()
    {
        var (sut, db) = Create();
        var owner = Guid.NewGuid();
        var created = await sut.CreateAsync(owner, "BankX", ContactType.Organization, null, null, null, CancellationToken.None);

        var updated = await sut.UpdateAsync(created.Id, owner, "BankY", ContactType.Bank, null, null, null, CancellationToken.None);

        updated.Should().NotBeNull();
        updated!.Name.Should().Be("BankY");
        updated.Type.Should().Be(ContactType.Bank);
    }

    [Fact]
    public async Task UpdateAsync_ShouldThrow_WhenChangingFromSelf()
    {
        var (sut, db) = Create();
        var owner = Guid.NewGuid();
        // Seed self contact directly (service prevents creation)
        var self = new Contact(owner, "Me", ContactType.Self, null);
        db.Contacts.Add(self);
        await db.SaveChangesAsync();

        Func<Task> act = () => sut.UpdateAsync(self.Id, owner, "Me2", ContactType.Person, null, null, null, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Self*");
    }

    [Fact]
    public async Task UpdateAsync_ShouldThrow_WhenChangingToSelf()
    {
        var (sut, db) = Create();
        var owner = Guid.NewGuid();
        var created = await sut.CreateAsync(owner, "Org", ContactType.Organization, null, null, null, CancellationToken.None);

        Func<Task> act = () => sut.UpdateAsync(created.Id, owner, "Org", ContactType.Self, null, null, null, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Self*");
    }

    [Fact]
    public async Task DeleteAsync_ShouldDelete_WhenNotSelf()
    {
        var (sut, db) = Create();
        var owner = Guid.NewGuid();
        var c = await sut.CreateAsync(owner, "Temp", ContactType.Other, null, null, null, CancellationToken.None);

        var ok = await sut.DeleteAsync(c.Id, owner, CancellationToken.None);

        ok.Should().BeTrue();
        db.Contacts.Count().Should().Be(0);
    }

    [Fact]
    public async Task DeleteAsync_ShouldThrow_WhenSelf()
    {
        var (sut, db) = Create();
        var owner = Guid.NewGuid();
        var self = new Contact(owner, "Me", ContactType.Self, null);
        db.Contacts.Add(self);
        await db.SaveChangesAsync();

        Func<Task> act = () => sut.DeleteAsync(self.Id, owner, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Self*");
    }

    [Fact]
    public async Task ListAsync_ShouldReturnOnlyOwnersContacts()
    {
        var (sut, db) = Create();
        var owner1 = Guid.NewGuid();
        var owner2 = Guid.NewGuid();
        db.Contacts.AddRange(
            new Contact(owner1, "A", ContactType.Person, null),
            new Contact(owner2, "B", ContactType.Person, null),
            new Contact(owner1, "C", ContactType.Person, null));
        await db.SaveChangesAsync();

        var list = await sut.ListAsync(owner1, 0, 50, CancellationToken.None);

        list.Should().HaveCount(2);
        list.Select(c => c.Name).Should().BeEquivalentTo(new[] { "A", "C" });
    }

    [Fact]
    public async Task GetAsync_ShouldReturnNull_WhenOtherOwner()
    {
        var (sut, db) = Create();
        var owner1 = Guid.NewGuid();
        var owner2 = Guid.NewGuid();
        var c = new Contact(owner1, "A", ContactType.Person, null);
        db.Contacts.Add(c);
        await db.SaveChangesAsync();

        var dto = await sut.GetAsync(c.Id, owner2, CancellationToken.None);
        dto.Should().BeNull();
    }
}
