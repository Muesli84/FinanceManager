using System;
using System.Threading;
using System.Threading.Tasks;
using FinanceManager.Application.Statements;
using FinanceManager.Domain.Accounts;
using FinanceManager.Infrastructure;
using FinanceManager.Infrastructure.Statements;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FinanceManager.Tests.Statements;

public sealed class StatementDraftServiceTests
{
    private static (StatementDraftService sut, AppDbContext db) Create()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        var sp = services.BuildServiceProvider();
        var db = sp.GetRequiredService<AppDbContext>();
        var sut = new StatementDraftService(db);
        return (sut, db);
    }

    [Fact]
    public async Task CreateDraftAsync_ShouldReturnEntries_AndAutoDetectAccount_WhenSingleAccount()
    {
        var (sut, db) = Create();
        var owner = Guid.NewGuid();
        db.Accounts.Add(new Account(owner, FinanceManager.Domain.AccountType.Giro, "Test", null, Guid.NewGuid()));
        db.SaveChanges();

        var counter = 0;
        await foreach (var draft in sut.CreateDraftAsync(owner, "file.csv", new byte[] { 1, 2, 3 }, CancellationToken.None))
        {
            draft.Entries.Should().HaveCount(2);
            draft.DetectedAccountId.Should().NotBeNull();
            draft.OriginalFileName.Should().Be("file.csv");
            counter++;
        }
        counter.Should().Be(1);
    }

    [Fact]
    public async Task CreateDraftAsync_ShouldHaveNullDetectedAccount_WhenNoAccounts()
    {
        var (sut, _) = Create();
        var owner = Guid.NewGuid();

        var counter = 0;
        await foreach (var draft in sut.CreateDraftAsync(owner, "f.csv", Array.Empty<byte>(), CancellationToken.None))
        {
            draft.DetectedAccountId.Should().BeNull();
            counter++;
        }
        counter.Should().Be(1);        
    }

    [Fact]
    public async Task CommitAsync_ShouldReturnResult()
    {
        var (sut, db) = Create();
        var ownerId = Guid.NewGuid();
        var accountId = Guid.NewGuid();

        // Arrange: Account und Draft anlegen
        db.Accounts.Add(new Account(ownerId, FinanceManager.Domain.AccountType.Giro, "Testkonto", null, Guid.NewGuid()));
        db.SaveChanges();

        var draft = new FinanceManager.Domain.Statements.StatementDraft(ownerId, "file.csv", "");
        draft.AddEntry(DateTime.UtcNow.Date.AddDays(-2), 123.45m, "Sample Payment A");
        draft.AddEntry(DateTime.UtcNow.Date.AddDays(-1), -49.99m, "Sample Debit B");
        db.StatementDrafts.Add(draft);
        db.SaveChanges();

        // Act
        var result = await sut.CommitAsync(draft.Id, ownerId, db.Accounts.Single().Id, FinanceManager.Domain.ImportFormat.Csv, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.TotalEntries.Should().Be(2);
    }
}
