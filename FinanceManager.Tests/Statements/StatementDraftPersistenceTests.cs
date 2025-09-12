using FinanceManager.Application.Statements;
using FinanceManager.Domain.Accounts;
using FinanceManager.Infrastructure;
using FinanceManager.Infrastructure.Statements;
using FluentAssertions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using System;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace FinanceManager.Tests.Statements;

public sealed class StatementDraftPersistenceTests
{
    private static (StatementDraftService sut, AppDbContext db) Create()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString()), ServiceLifetime.Scoped);
        var sp = services.BuildServiceProvider();
        var db = sp.GetRequiredService<AppDbContext>();
        var sut = new StatementDraftService(db);
        return (sut, db);
    }

    [Fact]
    public async Task GetDraftAsync_ShouldReturnPersistedDraft()
    {
        var (sut, db) = Create();
        var owner = Guid.NewGuid();
        db.Accounts.Add(new Account(owner, FinanceManager.Domain.AccountType.Giro, "A", null, Guid.NewGuid()));
        await db.SaveChangesAsync();

        var counter = 0;
        await foreach (var created in sut.CreateDraftAsync(owner, "x.csv", Array.Empty<byte>(), CancellationToken.None))
        {
            var fetched = await sut.GetDraftAsync(created.DraftId, owner, CancellationToken.None);
            fetched.Should().NotBeNull();
            fetched!.Entries.Should().HaveCount(created.Entries.Count);
            counter++;
        }
        counter.Should().Be(1);
    }

    [Fact]
    public async Task AddEntryAsync_ShouldAppendEntry()
    {
        var (sut, db) = Create();
        var owner = Guid.NewGuid();
        db.Accounts.Add(new Account(owner, FinanceManager.Domain.AccountType.Giro, "A", null, Guid.NewGuid()));
        await db.SaveChangesAsync();

        var counter = 0;
        await foreach (var draft in sut.CreateDraftAsync(owner, "y.csv", Array.Empty<byte>(), CancellationToken.None))
        {
            var updated = await sut.AddEntryAsync(draft.DraftId, owner, DateTime.UtcNow.Date, 10m, "Manual", CancellationToken.None);

            updated.Should().NotBeNull();
            updated!.Entries.Should().HaveCount(draft.Entries.Count + 1);
            updated.Entries.Any(e => e.Subject == "Manual").Should().BeTrue();
            counter++;
        }
        counter.Should().Be(1);
    }

    [Fact]
    public async Task CancelAsync_ShouldRemoveDraft()
    {
        var (sut, db) = Create();
        var owner = Guid.NewGuid();
        var counter = 0;
        await foreach (var draft in sut.CreateDraftAsync(owner, "z.csv", Array.Empty<byte>(), CancellationToken.None))
        {
            var ok = await sut.CancelAsync(draft.DraftId, owner, CancellationToken.None);
            ok.Should().BeTrue();

            var fetched = await sut.GetDraftAsync(draft.DraftId, owner, CancellationToken.None);
            fetched.Should().BeNull();
            counter++;
        }
        counter.Should().Be(1);
    }

    [Fact]
    public async Task CommitAsync_ShouldPersistImportAndEntries_AndMarkDraftCommitted()
    {
        var (sut, db) = Create();
        var owner = Guid.NewGuid();
        var account = new Account(owner, FinanceManager.Domain.AccountType.Giro, "Acc", null, Guid.NewGuid());
        db.Accounts.Add(account);
        await db.SaveChangesAsync();

        var counter = 0;
        await foreach (var draft in sut.CreateDraftAsync(owner, "c.csv", Array.Empty<byte>(), CancellationToken.None))
        {
            var result = await sut.CommitAsync(draft.DraftId, owner, account.Id, FinanceManager.Domain.ImportFormat.Csv, CancellationToken.None);

            result.Should().NotBeNull();
            db.StatementImports.Count().Should().Be(1);
            db.StatementEntries.Count().Should().Be(draft.Entries.Count);
            var persistedDraft = await sut.GetDraftAsync(draft.DraftId, owner, CancellationToken.None);
            persistedDraft!.Status.Should().Be(FinanceManager.Domain.StatementDraftStatus.Committed);
            counter++;
        }
        counter.Should().Be(1);
    }
}
