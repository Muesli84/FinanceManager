using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FinanceManager.Application.Accounts;
using FinanceManager.Domain;
using FinanceManager.Domain.Accounts;
using FinanceManager.Domain.Contacts;
using FinanceManager.Infrastructure;
using FinanceManager.Infrastructure.Accounts;
using FinanceManager.Shared.Dtos;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FinanceManager.Tests.Accounts;

public sealed class AccountServiceTests
{
    private static (AccountService sut, AppDbContext db) Create()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        var sp = services.BuildServiceProvider();
        var db = sp.GetRequiredService<AppDbContext>();
        var sut = new AccountService(db);
        return (sut, db);
    }

    [Fact]
    public async Task CreateAsync_ShouldCreate_WhenValidAndUniqueIbanPerUser()
    {
        var (sut, db) = Create();
        var owner = Guid.NewGuid();
        var bankContact = new Contact(owner, "Bank A", ContactType.Bank, null);
        db.Contacts.Add(bankContact);
        await db.SaveChangesAsync();

        var dto = await sut.CreateAsync(owner, "Konto 1", AccountType.Giro, "DE123", bankContact.Id, CancellationToken.None);

        dto.Name.Should().Be("Konto 1");
        dto.Iban.Should().Be("DE123");
        db.Accounts.Count().Should().Be(1);
    }

    [Fact]
    public async Task CreateAsync_ShouldFail_WhenDuplicateIbanForSameUser()
    {
        var (sut, db) = Create();
        var owner = Guid.NewGuid();
        var bankContact = new Contact(owner, "Bank A", ContactType.Bank, null);
        db.Contacts.Add(bankContact);
        await db.SaveChangesAsync();

        await sut.CreateAsync(owner, "A", AccountType.Giro, "DE999", bankContact.Id, CancellationToken.None);
        Func<Task> act = () => sut.CreateAsync(owner, "B", AccountType.Giro, "DE999", bankContact.Id, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*IBAN*");
    }

    [Fact]
    public async Task DeleteAsync_ShouldDeleteBankContact_WhenLastAccountOfContact()
    {
        var (sut, db) = Create();
        var owner = Guid.NewGuid();
        var bankContact = new Contact(owner, "Bank B", ContactType.Bank, null);
        db.Contacts.Add(bankContact);
        await db.SaveChangesAsync();
        var acc = await sut.CreateAsync(owner, "Main", AccountType.Giro, null, bankContact.Id, CancellationToken.None);

        var ok = await sut.DeleteAsync(acc.Id, owner, CancellationToken.None);

        ok.Should().BeTrue();
        db.Accounts.Any().Should().BeFalse();
        db.Contacts.Any(c => c.Id == bankContact.Id).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_ShouldNotDeleteBankContact_WhenOtherAccountsExist()
    {
        var (sut, db) = Create();
        var owner = Guid.NewGuid();
        var bankContact = new Contact(owner, "Bank C", ContactType.Bank, null);
        db.Contacts.Add(bankContact);
        await db.SaveChangesAsync();
        var a1 = await sut.CreateAsync(owner, "A1", AccountType.Giro, null, bankContact.Id, CancellationToken.None);
        var a2 = await sut.CreateAsync(owner, "A2", AccountType.Giro, null, bankContact.Id, CancellationToken.None);

        var ok = await sut.DeleteAsync(a1.Id, owner, CancellationToken.None);

        ok.Should().BeTrue();
        db.Contacts.Any(c => c.Id == bankContact.Id).Should().BeTrue();
    }
}
