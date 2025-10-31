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

        Assert.Equal("Konto 1", dto.Name);
        Assert.Equal("DE123", dto.Iban);
        Assert.Equal(1, db.Accounts.Count());
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

        var ex = await Assert.ThrowsAsync<ArgumentException>(act);
        Assert.Contains("IBAN", ex.Message, StringComparison.OrdinalIgnoreCase);
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

        Assert.True(ok);
        Assert.False(db.Accounts.Any());
        Assert.False(db.Contacts.Any(c => c.Id == bankContact.Id));
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

        Assert.True(ok);
        Assert.True(db.Contacts.Any(c => c.Id == bankContact.Id));
    }
}
