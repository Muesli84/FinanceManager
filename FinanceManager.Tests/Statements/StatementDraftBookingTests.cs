using FinanceManager.Domain.Accounts;
using FinanceManager.Domain.Contacts;
using FinanceManager.Domain.Savings;
using FinanceManager.Domain.Securities;
using FinanceManager.Domain.Statements;
using FinanceManager.Infrastructure;
using FinanceManager.Infrastructure.Aggregates;
using FinanceManager.Infrastructure.Statements;
using FinanceManager.Infrastructure.Statements.Files;
using FinanceManager.Application.Statements;
using FinanceManager.Application.Attachments;
using FinanceManager.Domain.Attachments;
using FinanceManager.Shared.Dtos.Statements;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using FinanceManager.Application.Accounts;
using FinanceManager.Application.Portfolio;
using Microsoft.Extensions.Logging.Abstractions;
using FinanceManager.Tests.TestHelpers;
using Moq;

namespace FinanceManager.Tests.Statements;

/// <summary>
/// Covers <see cref="StatementDraftService.BookAsync"/>, the core commit logic that turns statement draft entries
/// into postings: bank/contact postings, self-contact savings-plan handling with target-date advancement, security
/// trade/fee/tax splitting for buy/sell/dividend, payment-intermediary splitting across parent and child drafts,
/// validation failures for each precondition, cache invalidation, and concurrency guards against double-booking.
/// </summary>
public sealed class StatementDraftBookingTests
{

    private sealed class TestBudgetImpactEvaluationService : IBudgetImpactEvaluationService
    {
        private readonly BookingImpactSummaryDto? _summary;

        public TestBudgetImpactEvaluationService(BookingImpactSummaryDto? summary)
        {
            _summary = summary;
        }

        public List<(Guid DraftId, Guid? EntryId, Guid OwnerUserId)> DraftCalls { get; } = [];

        public Task<BudgetImpactEvaluationDto?> EvaluateEntryImpactAsync(Guid draftId, Guid entryId, Guid ownerUserId, CancellationToken ct)
            => Task.FromResult<BudgetImpactEvaluationDto?>(null);

        public Task<BookingImpactSummaryDto?> EvaluateDraftImpactAsync(Guid draftId, Guid? entryId, Guid ownerUserId, CancellationToken ct)
        {
            DraftCalls.Add((draftId, entryId, ownerUserId));
            return Task.FromResult(_summary);
        }
    }

    private sealed class ControlledAttachmentService : IAttachmentService
    {
        private readonly Func<CancellationToken, Task> _reassignHandler;

        public ControlledAttachmentService(Func<CancellationToken, Task>? reassignHandler = null)
        {
            _reassignHandler = reassignHandler ?? (_ => Task.CompletedTask);
        }

        public int ReassignCallCount { get; private set; }

        public Task<AttachmentDto> UploadAsync(Guid ownerUserId, AttachmentEntityKind kind, Guid entityId, Stream content, string fileName, string contentType, Guid? categoryId, CancellationToken ct)
            => Task.FromException<AttachmentDto>(new NotSupportedException("ControlledAttachmentService: UploadAsync is not configured for this test scenario."));

        public Task<AttachmentDto> UploadAsync(Guid ownerUserId, AttachmentEntityKind kind, Guid entityId, Stream content, string fileName, string contentType, Guid? categoryId, AttachmentRole role, CancellationToken ct)
            => Task.FromException<AttachmentDto>(new NotSupportedException("ControlledAttachmentService: UploadAsync is not configured for this test scenario."));

        public Task<AttachmentDto> CreateUrlAsync(Guid ownerUserId, AttachmentEntityKind kind, Guid entityId, string url, string? fileName, Guid? categoryId, CancellationToken ct)
            => Task.FromException<AttachmentDto>(new NotSupportedException("ControlledAttachmentService: CreateUrlAsync is not configured for this test scenario."));

        public Task<IReadOnlyList<AttachmentDto>> ListAsync(Guid ownerUserId, AttachmentEntityKind kind, Guid entityId, int skip, int take, Guid? categoryId, bool? isUrl, string? q, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<AttachmentDto>>([]);

        public Task<int> CountAsync(Guid ownerUserId, AttachmentEntityKind kind, Guid entityId, Guid? categoryId, bool? isUrl, string? q, CancellationToken ct)
            => Task.FromResult(0);

        public Task<(Stream Content, string FileName, string ContentType)?> DownloadAsync(Guid ownerUserId, Guid attachmentId, CancellationToken ct)
            => Task.FromException<(Stream Content, string FileName, string ContentType)?>(new NotSupportedException("ControlledAttachmentService: DownloadAsync is not configured for this test scenario."));

        public Task<bool> DeleteAsync(Guid ownerUserId, Guid attachmentId, CancellationToken ct)
            => Task.FromException<bool>(new NotSupportedException("ControlledAttachmentService: DeleteAsync is not configured for this test scenario."));

        public Task<bool> UpdateCategoryAsync(Guid ownerUserId, Guid attachmentId, Guid? categoryId, CancellationToken ct)
            => Task.FromException<bool>(new NotSupportedException("ControlledAttachmentService: UpdateCategoryAsync is not configured for this test scenario."));

        public Task<bool> UpdateCoreAsync(Guid ownerUserId, Guid attachmentId, string? fileName, Guid? categoryId, CancellationToken ct)
            => Task.FromException<bool>(new NotSupportedException("ControlledAttachmentService: UpdateCoreAsync is not configured for this test scenario."));

        public Task ReassignAsync(AttachmentEntityKind fromKind, Guid fromId, AttachmentEntityKind toKind, Guid toId, Guid ownerUserId, CancellationToken ct)
        {
            ReassignCallCount++;
            return _reassignHandler(ct);
        }

        public Task<AttachmentDto> CreateReferenceAsync(Guid ownerUserId, AttachmentEntityKind kind, Guid entityId, Guid masterAttachmentId, CancellationToken ct)
            => Task.FromException<AttachmentDto>(new NotSupportedException("ControlledAttachmentService: CreateReferenceAsync is not configured for this test scenario."));
    }

    // Booking never needs to load statement files (that happens during upload/create-draft), but the
    // constructor parameter is non-nullable, so a trivial stub stands in for the real factory.
    private sealed class StubStatementFileFactory : IStatementFileFactory
    {
        public IStatementFile? Load(string fileName, byte[] fileBytes) => null;
    }

    private static (StatementDraftService sut, AppDbContext db, SqliteConnection conn, Guid owner) Create()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(conn)
            .Options;
        var db = new AppDbContext(options);
        db.Database.EnsureCreated();
        var ownerUser = new FinanceManager.Domain.Users.User("owner", "hash", true);
        db.Users.Add(ownerUser);
        db.SaveChanges();
        // ensure self contact exists
        var self = new Contact(ownerUser.Id, "Ich", ContactType.Self, null, null);
        db.Contacts.Add(self);
        db.SaveChanges();
        var accountService = new StubAccountService();
        var sut = new StatementDraftService(db, new PostingAggregateService(db), accountService, new StubStatementFileFactory(), null, NullLogger<StatementDraftService>.Instance, null);
        return (sut, db, conn, ownerUser.Id);
    }

    private static async Task<(Account account, Contact bank)> AddAccountAsync(AppDbContext db, Guid owner, AccountType type = AccountType.Giro)
    {
        var bank = new Contact(owner, "Bank", ContactType.Bank, null, null);
        db.Contacts.Add(bank);
        await db.SaveChangesAsync();
        var acc = new Account(owner, type, "Testkonto", "DE00", bank.Id);
        db.Accounts.Add(acc);
        await db.SaveChangesAsync();
        return (acc, bank);
    }

    private static async Task<StatementDraft> CreateDraftAsync(AppDbContext db, Guid owner, Guid? accountId = null)
    {
        var draft = new StatementDraft(owner, "file.csv", "", "");
        if (accountId != null)
        {
            draft.SetDetectedAccount(accountId.Value);
        }
        db.StatementDrafts.Add(draft);
        await db.SaveChangesAsync();
        return draft;
    }

    /// <summary>
    /// Booking a single entry out of a multi-entry draft must only remove and post that entry - the draft must
    /// stay in <see cref="StatementDraftStatus.Draft"/> with the untouched entry still present, rather than being
    /// committed as a whole just because one of its entries was booked.
    /// </summary>
    [Fact]
    public async Task Booking_SingleEntry_ShouldNotCommitWholeDraft_And_RemoveOnlyThatEntry()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, _) = await AddAccountAsync(db, owner);
        var draft = await CreateDraftAsync(db, owner, acc.Id);

        // normal recipient contact (no intermediary, no self)
        var shop = new Contact(owner, "Shop GmbH", ContactType.Organization, null, null, false);
        db.Contacts.Add(shop);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // two entries
        var e1 = draft.AddEntry(DateTime.Today, 10m, "A", shop.Name, DateTime.Today, "EUR", null, false);
        var e2 = draft.AddEntry(DateTime.Today, 20m, "B", shop.Name, DateTime.Today, "EUR", null, false);
        db.Entry(e1).State = EntityState.Added;
        db.Entry(e2).State = EntityState.Added;
        e1.MarkAccounted(shop.Id); e2.MarkAccounted(shop.Id);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // IMPORTANT: simulate production by using a fresh DbContext (new scope)
        var freshOptions = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options;
        using var freshDb = new AppDbContext(freshOptions);
        var freshSut = new StatementDraftService(freshDb, new PostingAggregateService(freshDb), new StubAccountService(), new StubStatementFileFactory(), null, NullLogger<StatementDraftService>.Instance, null);

        // Act: book only first entry on fresh context
        var res = await freshSut.BookAsync(draft.Id, e1.Id, owner, false, CancellationToken.None);

        // Assert
        Assert.True(res.Success);

        // Reload draft and verify status and remaining entries using fresh context
        var reloaded = await freshDb.StatementDrafts.Include(d => d.Entries).FirstAsync(d => d.Id == draft.Id, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(StatementDraftStatus.Draft, reloaded.Status); // not committed because one entry remains
        Assert.Equal(1, reloaded.Entries.Count);
        Assert.Equal("B", reloaded.Entries.Single().Subject);

        // Exactly two postings (bank + contact) for the booked entry
        Assert.Equal(2, freshDb.Postings.Count());
        Assert.Equal(1, freshDb.Postings.Count(p => p.Kind == PostingKind.Bank));
        Assert.Equal(1, freshDb.Postings.Count(p => p.Kind == PostingKind.Contact));

        conn.Dispose();
    }

    /// <summary>
    /// Even a partial booking (only one of several entries in a draft) must still run the budget impact
    /// evaluation for that specific entry and attach the resulting summary to the booking result, so the UI can
    /// show the budget effect regardless of whether the whole draft or just one entry was booked.
    /// </summary>
    [Fact]
    public async Task Booking_SingleEntry_ShouldAttachBudgetImpactSummary_WhenBookingIsPartial()
    {
        var (_, db, conn, owner) = Create();
        var (acc, _) = await AddAccountAsync(db, owner);
        var draft = await CreateDraftAsync(db, owner, acc.Id);

        var shop = new Contact(owner, "Shop GmbH", ContactType.Organization, null, null, false);
        db.Contacts.Add(shop);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var e1 = draft.AddEntry(DateTime.Today, 10m, "A", shop.Name, DateTime.Today, "EUR", null, false);
        var e2 = draft.AddEntry(DateTime.Today, 20m, "B", shop.Name, DateTime.Today, "EUR", null, false);
        db.Entry(e1).State = EntityState.Added;
        db.Entry(e2).State = EntityState.Added;
        e1.MarkAccounted(shop.Id);
        e2.MarkAccounted(shop.Id);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var summary = new BookingImpactSummaryDto(
            draft.Id,
            e1.Id,
            DateTime.UtcNow,
            "fingerprint-partial",
            BudgetImpactHintType.StronglyChanged,
            [
                new BookingImpactSummaryItemDto(
                    Guid.NewGuid(),
                    "Groceries",
                    "2026-05",
                    BudgetImpactHintType.StronglyChanged,
                    100m,
                    80m,
                    90m,
                    0.8m,
                    0.9m,
                    0.1m,
                    "Changed")
            ]);
        var budgetImpact = new TestBudgetImpactEvaluationService(summary);
        var bookingSut = new StatementDraftService(db, new PostingAggregateService(db), new StubAccountService(), new StubStatementFileFactory(), null, NullLogger<StatementDraftService>.Instance, null, null, budgetImpact);

        var res = await bookingSut.BookAsync(draft.Id, e1.Id, owner, false, CancellationToken.None);

        Assert.True(res.Success);
        Assert.NotNull(res.BudgetImpactSummary);
        Assert.Equal(summary.EvaluationFingerprint, res.BudgetImpactSummary!.EvaluationFingerprint);
        Assert.Equal(summary.HighestSeverity, res.BudgetImpactSummary.HighestSeverity);
        Assert.NotEmpty(budgetImpact.DraftCalls);
        Assert.Contains(budgetImpact.DraftCalls, x =>
            x.DraftId == draft.Id
            && x.EntryId == e1.Id
            && x.OwnerUserId == owner);

        conn.Dispose();
    }

    /// <summary>
    /// Booking must fail with the "NO_ACCOUNT" validation code when the draft has no account assigned, since a
    /// bank posting cannot be created without knowing which account it belongs to.
    /// </summary>
    [Fact]
    public async Task Booking_ShouldFail_WhenNoAccountAssigned()
    {
        var (sut, db, conn, owner) = Create();
        var draft = await CreateDraftAsync(db, owner, null);
        var entry = draft.AddEntry(DateTime.Today, 10m, "Payment A", null, DateTime.Today, "EUR", null, false);
        db.Entry(entry).State = EntityState.Added;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var res = await sut.BookAsync(draft.Id, null, owner, false, CancellationToken.None);

        Assert.False(res.Success);
        Assert.True(res.Validation.Messages.Any(m => m.Code == "NO_ACCOUNT"));
        conn.Dispose();
    }

    /// <summary>
    /// Booking must fail with the "ENTRY_NO_CONTACT" validation code when an entry has not yet been assigned a
    /// counterparty contact, since the contact posting needs a target contact to attribute the money to.
    /// </summary>
    [Fact]
    public async Task Booking_ShouldFail_WhenEntryHasNoContact()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, _) = await AddAccountAsync(db, owner);
        var draft = await CreateDraftAsync(db, owner, acc.Id);
        var entry = draft.AddEntry(DateTime.Today, 10m, "Payment A", null, DateTime.Today, "EUR", null, false);
        db.Entry(entry).State = EntityState.Added;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var res = await sut.BookAsync(draft.Id, null, owner, false, CancellationToken.None);

        Assert.False(res.Success);
        Assert.True(res.Validation.Messages.Any(m => m.Code == "ENTRY_NO_CONTACT"));
        conn.Dispose();
    }

    /// <summary>
    /// A booking accounted to the "self" contact without an assigned savings plan is treated as suspicious (it
    /// usually means the user forgot to link a savings plan) and must be rejected with a "SAVINGSPLAN_MISSING_FOR_SELF"
    /// warning that requires explicit confirmation. Once confirmed, the booking must proceed and attach the
    /// budget impact summary evaluated at the draft level (entry id null) as well as create exactly one bank and
    /// one contact posting.
    /// </summary>
    [Fact]
    public async Task Booking_SelfContact_ShouldRequireConfirmation_AndCreateBankAndContactPostings()
    {
        var (_, db, conn, owner) = Create();
        var (acc, _) = await AddAccountAsync(db, owner);
        var draft = await CreateDraftAsync(db, owner, acc.Id);
        var self = await db.Contacts.FirstAsync(c => c.OwnerUserId == owner && c.Type == ContactType.Self, cancellationToken: TestContext.Current.CancellationToken);
        var entry = draft.AddEntry(DateTime.Today, 25.5m, "Self transfer", self.Name, DateTime.Today, "EUR", null, false);
        db.Entry(entry).State = EntityState.Added;
        entry.MarkAccounted(self.Id);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var summary = new BookingImpactSummaryDto(
            draft.Id,
            null,
            DateTime.UtcNow,
            "fingerprint-full",
            BudgetImpactHintType.AlmostExhausted,
            [
                new BookingImpactSummaryItemDto(
                    Guid.NewGuid(),
                    "Savings",
                    "2026-05",
                    BudgetImpactHintType.AlmostExhausted,
                    50m,
                    40m,
                    65.5m,
                    0.8m,
                    1.31m,
                    0.51m,
                    "Almost exhausted")
            ]);
        var budgetImpact = new TestBudgetImpactEvaluationService(summary);
        var bookingSut = new StatementDraftService(db, new PostingAggregateService(db), new StubAccountService(), new StubStatementFileFactory(), null, NullLogger<StatementDraftService>.Instance, null, null, budgetImpact);

        var res1 = await bookingSut.BookAsync(draft.Id, null, owner, false, CancellationToken.None);
        Assert.False(res1.Success);
        Assert.True(res1.HasWarnings);
        Assert.True(res1.Validation.Messages.Any(m => m.Code == "SAVINGSPLAN_MISSING_FOR_SELF"));

        var res2 = await bookingSut.BookAsync(draft.Id, null, owner, true, CancellationToken.None);
        Assert.True(res2.Success);
        Assert.NotNull(res2.BudgetImpactSummary);
        Assert.Equal(summary.EvaluationFingerprint, res2.BudgetImpactSummary!.EvaluationFingerprint);
        Assert.Equal(summary.HighestSeverity, res2.BudgetImpactSummary.HighestSeverity);
        Assert.NotEmpty(budgetImpact.DraftCalls);
        Assert.Contains(budgetImpact.DraftCalls, x =>
            x.DraftId == draft.Id
            && x.EntryId == null
            && x.OwnerUserId == owner);
        Assert.Equal(2, db.Postings.Count());
        Assert.Equal(1, db.Postings.Count(p => p.Kind == PostingKind.Bank));
        Assert.Equal(1, db.Postings.Count(p => p.Kind == PostingKind.Contact));
        // Aggregates created for month/quarter/halfyear/year for account and contact
        // New behavior: aggregates created per DateKind (Booking + Valuta) so counts are doubled
        Assert.Equal(8, db.PostingAggregates.Count(a => a.Kind == PostingKind.Bank));
        Assert.Equal(8, db.PostingAggregates.Count(a => a.Kind == PostingKind.Contact));
        conn.Dispose();
    }

    /// <summary>
    /// When a self-contact entry is explicitly linked to a savings plan, booking must succeed without requiring
    /// confirmation and must create bank, contact, and savings-plan postings, with the savings-plan posting
    /// carrying the negated entry amount (money leaving the regular flow into the plan).
    /// </summary>
    [Fact]
    public async Task Booking_SelfContactWithSavingsPlan_ShouldCreateBankContactAndSavingsPostings()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, _) = await AddAccountAsync(db, owner);
        var draft = await CreateDraftAsync(db, owner, acc.Id);
        var self = await db.Contacts.FirstAsync(c => c.OwnerUserId == owner && c.Type == ContactType.Self, cancellationToken: TestContext.Current.CancellationToken);
        var plan = new SavingsPlan(owner, "Plan A", SavingsPlanType.OneTime, null, null, null);
        db.SavingsPlans.Add(plan);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var e = draft.AddEntry(DateTime.Today, 100m, "Save", self.Name, DateTime.Today, "EUR", null, false);
        e.AssignSavingsPlan(plan.Id);
        e.MarkAccounted(self.Id);
        db.Entry(e).State = EntityState.Added;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var res = await sut.BookAsync(draft.Id, null, owner, false, CancellationToken.None);
        Assert.True(res.Success);
        Assert.Equal(3, db.Postings.Count());
        Assert.Equal(1, db.Postings.Count(p => p.Kind == PostingKind.Bank));
        Assert.Equal(1, db.Postings.Count(p => p.Kind == PostingKind.Contact));
        Assert.Equal(1, db.Postings.Count(p => p.Kind == PostingKind.SavingsPlan));
        Assert.Equal(-100m, db.Postings.Single(p => p.Kind == PostingKind.SavingsPlan).Amount);
        // Aggregates exist for bank/contact/savingsplan (doubled due to DateKind)
        Assert.Equal(8, db.PostingAggregates.Count(a => a.Kind == PostingKind.Bank));
        Assert.Equal(8, db.PostingAggregates.Count(a => a.Kind == PostingKind.Contact));
        Assert.Equal(8, db.PostingAggregates.Count(a => a.Kind == PostingKind.SavingsPlan));
        conn.Dispose();
    }

    /// <summary>
    /// An entry accounted to a payment intermediary contact (e.g. PayPal) that stands in for the real recipient
    /// must not be booked as-is - without a linked split draft breaking the amount down to the actual
    /// counterparties, booking must fail with "INTERMEDIARY_NO_SPLIT".
    /// </summary>
    [Fact]
    public async Task Booking_PaymentIntermediaryWithoutSplit_ShouldFail()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, _) = await AddAccountAsync(db, owner);
        var draft = await CreateDraftAsync(db, owner, acc.Id);
        var intermediary = new Contact(owner, "PayPal", ContactType.Organization, null, null, true);
        db.Contacts.Add(intermediary);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var e = draft.AddEntry(DateTime.Today, 50m, "PayPal", intermediary.Name, DateTime.Today, "EUR", null, false);
        db.Entry(e).State = EntityState.Added;
        e.MarkAccounted(intermediary.Id);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var res = await sut.BookAsync(draft.Id, null, owner, false, CancellationToken.None);
        Assert.False(res.Success);
        Assert.True(res.Validation.Messages.Any(m => m.Code == "INTERMEDIARY_NO_SPLIT"));
        conn.Dispose();
    }

    /// <summary>
    /// A child draft linked as an intermediary's split cannot be booked on its own; only booking the parent entry
    /// is valid, and doing so must create zero-amount bank/contact postings on the parent (the intermediary is a
    /// pass-through, not a real economic movement) plus the real, correctly-dated bank/contact postings on each
    /// child entry, each referencing its corresponding parent posting via <c>ParentId</c>. Child postings keep the
    /// parent's booking date but their own valuta date, and both the parent and child drafts end up committed.
    /// </summary>
    [Fact]
    public async Task Booking_SplitDrafts_ParentCreatesZeroAndChildPostings_AndBothCommitted()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, bank) = await AddAccountAsync(db, owner);

        // Parent draft with intermediary contact
        var parent = await CreateDraftAsync(db, owner, acc.Id);
        var intermediary = new Contact(owner, "PayService", ContactType.Organization, null, null, true);
        db.Contacts.Add(intermediary);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        // parent booking and valuta differ
        var pBooking = new DateTime(2024, 6, 1);
        var pValuta = new DateTime(2024, 6, 5);
        var pEntry = parent.AddEntry(pBooking, 80m, "Split Root", intermediary.Name, pValuta, "EUR", null, false);
        pEntry.MarkAccounted(intermediary.Id);
        db.Entry(pEntry).State = EntityState.Added;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Child draft without account, two entries with assigned contacts totaling 80
        var child = await CreateDraftAsync(db, owner, null);
        var rec1 = new Contact(owner, "Alice", ContactType.Person, null, null);
        var rec2 = new Contact(owner, "Bob", ContactType.Person, null, null);
        db.Contacts.AddRange(rec1, rec2);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        // children have different booking/valuta (and different from parent)
        var c1Booking = new DateTime(2024, 6, 2);
        var c1Valuta = new DateTime(2024, 6, 3);
        var c2Booking = new DateTime(2024, 6, 2);
        var c2Valuta = new DateTime(2024, 6, 4);
        var c1 = child.AddEntry(c1Booking, 30m, "Child A", rec1.Name, c1Valuta, "EUR", null, false);
        var c2 = child.AddEntry(c2Booking, 50m, "Child B", rec2.Name, c2Valuta, "EUR", null, false);
        c1.MarkAccounted(rec1.Id);
        c2.MarkAccounted(rec2.Id);
        db.Entry(c1).State = EntityState.Added;
        db.Entry(c2).State = EntityState.Added;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Link child as split draft to parent entry
        pEntry.AssignSplitDraft(child.Id);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Booking the child (split) draft should fail
        var childRes = await sut.BookAsync(child.Id, null, owner, false, CancellationToken.None);
        Assert.False(childRes.Success);

        // Booking the parent should succeed and create 0-amount parent postings + child postings
        var parentRes = await sut.BookAsync(parent.Id, null, owner, false, CancellationToken.None);
        Assert.True(parentRes.Success);

        var bankPostings = db.Postings.Where(p => p.Kind == PostingKind.Bank).ToList();
        var contactPostings = db.Postings.Where(p => p.Kind == PostingKind.Contact).ToList();

        Assert.Equal(3, bankPostings.Count); // 1 parent (0) + 2 child
        Assert.Equal(1, bankPostings.Count(p => p.Amount == 0m));
        Assert.Equal(80m, bankPostings.Where(p => p.Amount != 0m).Sum(p => p.Amount));

        Assert.Equal(3, contactPostings.Count); // 1 parent (0) + 2 child
        Assert.Equal(1, contactPostings.Count(p => p.Amount == 0m));
        Assert.Equal(80m, contactPostings.Where(p => p.Amount != 0m).Sum(p => p.Amount));

        // All created postings (parent + children) must have parent's valuta date
        var allPostings = db.Postings.ToList();
        Assert.All(allPostings, p => Assert.Equal(pEntry.BookingDate, p.BookingDate));

        // Additionally, booking dates of postings must match their source entry booking dates
        var parentBank = db.Postings.Single(p => p.SourceId == pEntry.Id && p.Kind == PostingKind.Bank);
        var parentContact = db.Postings.Single(p => p.SourceId == pEntry.Id && p.Kind == PostingKind.Contact);
        Assert.Equal(pEntry.BookingDate, parentBank.BookingDate);
        Assert.Equal(pEntry.ValutaDate, parentBank.ValutaDate);
        Assert.Equal(0m, parentBank.Amount);
        Assert.Equal(pEntry.BookingDate, parentContact.BookingDate);
        Assert.Equal(pEntry.ValutaDate, parentContact.ValutaDate);
        Assert.Equal(0m, parentContact.Amount);

        var child1Bank = db.Postings.Single(p => p.SourceId == c1.Id && p.Kind == PostingKind.Bank);
        var child1Contact = db.Postings.Single(p => p.SourceId == c1.Id && p.Kind == PostingKind.Contact);
        Assert.Equal(pEntry.BookingDate, child1Bank.BookingDate);
        Assert.Equal(c1Booking, child1Bank.ValutaDate);
        Assert.Equal(c1.Amount, child1Bank.Amount);
        Assert.Equal(pEntry.BookingDate, child1Contact.BookingDate);
        Assert.Equal(c1Booking, child1Contact.ValutaDate);
        Assert.Equal(c1.Amount, child1Contact.Amount);

        var child2Bank = db.Postings.Single(p => p.SourceId == c2.Id && p.Kind == PostingKind.Bank);
        var child2Contact = db.Postings.Single(p => p.SourceId == c2.Id && p.Kind == PostingKind.Contact);
        Assert.Equal(pEntry.BookingDate, child2Bank.BookingDate);
        Assert.Equal(c2Booking, child2Bank.ValutaDate);
        Assert.Equal(c2.Amount, child2Bank.Amount);
        Assert.Equal(pEntry.BookingDate, child2Contact.BookingDate);
        Assert.Equal(c2Booking, child2Contact.ValutaDate);
        Assert.Equal(c2.Amount, child2Contact.Amount);

        // Parent postings should not have parent set
        Assert.Null(parentBank.ParentId);
        Assert.Null(parentContact.ParentId);

        // Child postings must reference parent's corresponding posting ids
        Assert.Equal(parentBank.Id, child1Bank.ParentId);
        Assert.Equal(parentContact.Id, child1Contact.ParentId);
        Assert.Equal(parentBank.Id, child2Bank.ParentId);
        Assert.Equal(parentContact.Id, child2Contact.ParentId);

        // Both drafts expected committed
        Assert.Equal(StatementDraftStatus.Committed, (await db.StatementDrafts.FindAsync(new object?[] { parent.Id }, TestContext.Current.CancellationToken))!.Status);
        Assert.Equal(StatementDraftStatus.Committed, (await db.StatementDrafts.FindAsync(new object?[] { child.Id }, TestContext.Current.CancellationToken))!.Status);

        conn.Dispose();
    }

    /// <summary>
    /// If the child entries of a linked split draft have not been assigned contacts, booking the parent must fail
    /// with an "ENTRY_NO_CONTACT" message prefixed by "[Split]" (so the user can tell the failure originates in
    /// the split, not the parent entry itself) - and must not be misreported as a missing split
    /// ("INTERMEDIARY_NO_SPLIT" must not fire, since a split is in fact present, just incomplete).
    /// </summary>
    [Fact]
    public async Task Booking_ParentFails_WhenSplitDraftHasMissingContacts()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, bank) = await AddAccountAsync(db, owner);
        var parent = await CreateDraftAsync(db, owner, acc.Id);
        var intermediary = new Contact(owner, "PayService", ContactType.Organization, null, null, true);
        db.Contacts.Add(intermediary);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var pEntry = parent.AddEntry(DateTime.Today, 80m, "Split Root", intermediary.Name, DateTime.Today, "EUR", null, false);
        pEntry.MarkAccounted(intermediary.Id);
        db.Entry(pEntry).State = EntityState.Added;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var child = await CreateDraftAsync(db, owner, null);
        var c1 = child.AddEntry(DateTime.Today, 30m, "Child A", null, DateTime.Today, "EUR", null, false);
        var c2 = child.AddEntry(DateTime.Today, 50m, "Child B", null, DateTime.Today, "EUR", null, false);
        db.Entry(c1).State = EntityState.Added;
        db.Entry(c2).State = EntityState.Added;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        pEntry.AssignSplitDraft(child.Id);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var res = await sut.BookAsync(parent.Id, null, owner, false, CancellationToken.None);
        Assert.False(res.Success);
        Assert.False(res.Validation.Messages.Any(m => m.Code == "INTERMEDIARY_NO_SPLIT"));
        Assert.True(res.Validation.Messages.Any(m => m.Message.Contains("[Split]") && m.Code == "ENTRY_NO_CONTACT"));
        conn.Dispose();
    }

    /// <summary>
    /// The sum of a split's child entry amounts must equal the parent intermediary entry's amount; otherwise
    /// booking must fail with "SPLIT_AMOUNT_MISMATCH" - guarding against a split that would silently lose or
    /// double-count money relative to what the intermediary actually moved.
    /// </summary>
    [Fact]
    public async Task Booking_ParentFails_WhenSplitTotalsDoNotMatch()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, bank) = await AddAccountAsync(db, owner);
        var parent = await CreateDraftAsync(db, owner, acc.Id);
        var intermediary = new Contact(owner, "PayService", ContactType.Organization, null, null, true);
        db.Contacts.Add(intermediary);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var pEntry = parent.AddEntry(DateTime.Today, 100m, "Split Root", intermediary.Name, DateTime.Today, "EUR", null, false);
        pEntry.MarkAccounted(intermediary.Id);
        db.Entry(pEntry).State = EntityState.Added;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var child = await CreateDraftAsync(db, owner, null);
        var c1 = child.AddEntry(DateTime.Today, 30m, "Child A", null, DateTime.Today, "EUR", null, false);
        var c2 = child.AddEntry(DateTime.Today, 60m, "Child B", null, DateTime.Today, "EUR", null, false);
        db.Entry(c1).State = EntityState.Added;
        db.Entry(c2).State = EntityState.Added;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        pEntry.AssignSplitDraft(child.Id);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var res = await sut.BookAsync(parent.Id, null, owner, false, CancellationToken.None);
        Assert.False(res.Success);
        Assert.True(res.Validation.Messages.Any(m => m.Code == "SPLIT_AMOUNT_MISMATCH"));
        conn.Dispose();
    }

    /// <summary>
    /// A split child entry accounted to the self contact must go through the same "missing savings plan"
    /// confirmation guard as a direct self-booking, with the resulting warning message prefixed "[Split]" so it
    /// is traceable back to the specific child entry inside the split rather than the parent.
    /// </summary>
    [Fact]
    public async Task Booking_Warns_WhenSplitContainsSelfContact()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, bank) = await AddAccountAsync(db, owner);
        var parent = await CreateDraftAsync(db, owner, acc.Id);
        var intermediary = new Contact(owner, "PayService", ContactType.Organization, null, null, true);
        db.Contacts.Add(intermediary);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var pEntry = parent.AddEntry(DateTime.Today, 100m, "Split Root", intermediary.Name, DateTime.Today, "EUR", null, false);
        pEntry.MarkAccounted(intermediary.Id);
        db.Entry(pEntry).State = EntityState.Added;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var child = await CreateDraftAsync(db, owner, null);
        var self = await db.Contacts.FirstAsync(c => c.OwnerUserId == owner && c.Type == ContactType.Self, cancellationToken: TestContext.Current.CancellationToken);
        var c1 = child.AddEntry(DateTime.Today, 100m, "Child A", self.Name, DateTime.Today, "EUR", null, false);
        c1.MarkAccounted(self.Id);
        db.Entry(c1).State = EntityState.Added;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        pEntry.AssignSplitDraft(child.Id);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var res = await sut.BookAsync(parent.Id, null, owner, false, CancellationToken.None);
        Assert.False(res.Success);
        Assert.True(res.HasWarnings);
        Assert.True(res.Validation.Messages.Any(m => m.Message.Contains("[Split]") && m.Code == "SAVINGSPLAN_MISSING_FOR_SELF"));
        conn.Dispose();
    }

    /// <summary>
    /// Intermediary chaining is not supported implicitly: if a split's child entry is itself accounted to another
    /// payment intermediary without its own further split, booking the parent must fail with
    /// "INTERMEDIARY_NO_SPLIT" (prefixed "[Split]"), the same rule that applies at the top level.
    /// </summary>
    [Fact]
    public async Task Booking_Fails_WhenSplitContainsIntermediaryWithoutFurtherSplit()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, bank) = await AddAccountAsync(db, owner);
        var parent = await CreateDraftAsync(db, owner, acc.Id);
        var intermediary = new Contact(owner, "PayService", ContactType.Organization, null, null, true);
        db.Contacts.Add(intermediary);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var pEntry = parent.AddEntry(DateTime.Today, 100m, "Split Root", intermediary.Name, DateTime.Today, "EUR", null, false);
        db.Entry(pEntry).State = EntityState.Added;
        pEntry.MarkAccounted(intermediary.Id);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var child = await CreateDraftAsync(db, owner, null);
        var c1 = child.AddEntry(DateTime.Today, 100m, "Child A", intermediary.Name, DateTime.Today, "EUR", null, false);
        db.Entry(c1).State = EntityState.Added;
        c1.MarkAccounted(intermediary.Id);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        pEntry.AssignSplitDraft(child.Id);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var res = await sut.BookAsync(parent.Id, null, owner, false, CancellationToken.None);
        Assert.False(res.Success);
        Assert.True(res.Validation.Messages.Any(m => m.Message.Contains("[Split]") && m.Code == "INTERMEDIARY_NO_SPLIT"));
        conn.Dispose();
    }

    /// <summary>
    /// An entry with a security assigned but no <see cref="SecurityTransactionType"/> chosen cannot be booked -
    /// booking must fail with "SECURITY_MISSING_TXTYPE" since the transaction type drives which security
    /// sub-postings (buy/sell/dividend) get created.
    /// </summary>
    [Fact]
    public async Task Booking_Fails_ForSecurityMissingTransactionType()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, bank) = await AddAccountAsync(db, owner);
        var draft = await CreateDraftAsync(db, owner, acc.Id);
        var account = await db.Accounts.FirstOrDefaultAsync(acc => acc.Id == draft.DetectedAccountId, cancellationToken: TestContext.Current.CancellationToken);
        var entry = draft.AddEntry(DateTime.Today, 200m, "Trade", bank.Name, DateTime.Today, "EUR", null, false);
        db.Entry(entry).State = EntityState.Added;
        entry.MarkAccounted(account!.BankContactId);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var sec = new Security(owner, "ETF X", "DE000A0", null, null, "EUR", null);
        db.Securities.Add(sec);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await sut.SetEntrySecurityAsync(draft.Id, entry.Id, sec.Id, null, null, null, null, owner, CancellationToken.None);

        var res = await sut.BookAsync(draft.Id, null, owner, false, CancellationToken.None);
        Assert.False(res.Success);
        Assert.True(res.Validation.Messages.Any(m => m.Code == "SECURITY_MISSING_TXTYPE"));
        conn.Dispose();
    }

    /// <summary>
    /// A Buy transaction requires a quantity; booking must fail with "SECURITY_MISSING_QUANTITY" when the
    /// quantity was never set, since the resulting holding size would otherwise be undefined.
    /// </summary>
    [Fact]
    public async Task Booking_Fails_ForSecurityMissingQuantity()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, bank) = await AddAccountAsync(db, owner);
        var draft = await CreateDraftAsync(db, owner, acc.Id);
        var account = await db.Accounts.FirstOrDefaultAsync(acc => acc.Id == draft.DetectedAccountId, cancellationToken: TestContext.Current.CancellationToken);
        var entry = draft.AddEntry(DateTime.Today, 200m, "Trade", bank.Name, DateTime.Today, "EUR", null, false);
        db.Entry(entry).State = EntityState.Added;
        entry.MarkAccounted(account!.BankContactId);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var sec = new Security(owner, "ETF X", "DE000A0", null, null, "EUR", null);
        db.Securities.Add(sec);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await sut.SetEntrySecurityAsync(draft.Id, entry.Id, sec.Id, SecurityTransactionType.Buy, null, null, null, owner, CancellationToken.None);

        var res = await sut.BookAsync(draft.Id, null, owner, false, CancellationToken.None);
        Assert.False(res.Success);
        Assert.True(res.Validation.Messages.Any(m => m.Code == "SECURITY_MISSING_QUANTITY"));
        conn.Dispose();
    }

    /// <summary>
    /// Even with an otherwise valid security assignment, booking must fail with "SECURITY_ACCOUNT_NOT_ALLOWED"
    /// when the target account has security processing disabled - preventing security trades from landing on
    /// accounts that were never configured to hold them.
    /// </summary>
    [Fact]
    public async Task Booking_Fails_WhenSecurityProcessingDisabledForAccount()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, bank) = await AddAccountAsync(db, owner);
        acc.SetSecurityProcessingEnabled(false);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var draft = await CreateDraftAsync(db, owner, acc.Id);
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == draft.DetectedAccountId, cancellationToken: TestContext.Current.CancellationToken);
        var entry = draft.AddEntry(DateTime.Today, 200m, "Trade", bank.Name, DateTime.Today, "EUR", null, false);
        db.Entry(entry).State = EntityState.Added;
        entry.MarkAccounted(account!.BankContactId);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var sec = new Security(owner, "ETF X", "DE000A0", null, null, "EUR", null);
        db.Securities.Add(sec);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await sut.SetEntrySecurityAsync(draft.Id, entry.Id, sec.Id, SecurityTransactionType.Buy, 1.0m, 1.00m, 1.00m, owner, CancellationToken.None);

        var res = await sut.BookAsync(draft.Id, null, owner, false, CancellationToken.None);
        Assert.False(res.Success);
        Assert.True(res.Validation.Messages.Any(m => m.Code == "SECURITY_ACCOUNT_NOT_ALLOWED"));
        conn.Dispose();
    }

    /// <summary>
    /// Fee plus tax must not exceed the entry's total amount; otherwise booking must fail with
    /// "SECURITY_FEE_TAX_EXCEEDS_AMOUNT" rather than silently creating a main trade posting with a nonsensical
    /// negative or inverted amount from bad data entry.
    /// </summary>
    [Fact]
    public async Task Booking_Fails_WhenSecurityFeePlusTaxExceedsAmount()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, bank) = await AddAccountAsync(db, owner);
        var draft = await CreateDraftAsync(db, owner, acc.Id);
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == draft.DetectedAccountId, cancellationToken: TestContext.Current.CancellationToken);

        // entry with amount smaller than fee+tax
        var entry = draft.AddEntry(DateTime.Today, 100m, "Trade", bank.Name, DateTime.Today, "EUR", null, false);
        db.Entry(entry).State = EntityState.Added;
        entry.MarkAccounted(account!.BankContactId);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var sec = new Security(owner, "ETF X", "DE000A0", null, null, "EUR", null);
        db.Securities.Add(sec);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Set security with Buy, quantity present, but fee+tax exceed entry amount
        await sut.SetEntrySecurityAsync(draft.Id, entry.Id, sec.Id, SecurityTransactionType.Buy, 1.0m, 70.00m, 40.00m, owner, CancellationToken.None);

        var res = await sut.BookAsync(draft.Id, null, owner, false, CancellationToken.None);
        Assert.False(res.Success);
        Assert.True(res.Validation.Messages.Any(m => m.Code == "SECURITY_FEE_TAX_EXCEEDS_AMOUNT"));
        conn.Dispose();
    }

    /// <summary>
    /// A Buy trade must split into separate main/fee/tax security postings whose amounts sum exactly back to the
    /// entry amount, and must preserve a high-precision (six decimal place) quantity on the main posting only -
    /// fee and tax postings carry no quantity of their own.
    /// </summary>
    [Fact]
    public async Task Booking_CreatesSecurityTradeFeeTaxPostings_WithSixDecimalQuantity_AndSumsToEntryAmount_ForBuy()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, bank) = await AddAccountAsync(db, owner);

        var draft = await CreateDraftAsync(db, owner, acc.Id);
        var account = await db.Accounts.FirstOrDefaultAsync(acc => acc.Id == draft.DetectedAccountId, cancellationToken: TestContext.Current.CancellationToken);
        var entry = draft.AddEntry(DateTime.Today, 1000m, "Trade", bank.Name, DateTime.Today, "EUR", null, false);
        db.Entry(entry).State = EntityState.Added;
        entry.MarkAccounted(account!.BankContactId);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var sec = new Security(owner, "ETF X", "DE000A0", null, null, "EUR", null);
        db.Securities.Add(sec);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await sut.SetEntrySecurityAsync(draft.Id, entry.Id, sec.Id, SecurityTransactionType.Buy, 1.123456m, 2.50m, 5.00m, owner, CancellationToken.None);

        var res = await sut.BookAsync(draft.Id, null, owner, false, CancellationToken.None);
        Assert.True(res.Success);
        Assert.Equal(3, db.Postings.Count(p => p.Kind == PostingKind.Security));

        // Main security posting must be of subtype Buy
        var main = db.Postings.Single(p => p.Kind == PostingKind.Security && p.SecuritySubType == SecurityPostingSubType.Buy);
        var fee = db.Postings.Single(p => p.Kind == PostingKind.Security && p.SecuritySubType == SecurityPostingSubType.Fee);
        var tax = db.Postings.Single(p => p.Kind == PostingKind.Security && p.SecuritySubType == SecurityPostingSubType.Tax);
        Assert.Equal(1000m - 2.50m - 5.00m, main.Amount);
        Assert.Equal(2.50m, fee.Amount);
        Assert.Equal(5.00m, tax.Amount);

        // Sum of security postings equals original entry amount
        Assert.Equal(entry.Amount, main.Amount + fee.Amount + tax.Amount);

        // Quantity assertions
        Assert.Equal(1.123456m, main.Quantity);
        Assert.Null(fee.Quantity);
        Assert.Null(tax.Quantity);

        conn.Dispose();
    }

    /// <summary>
    /// For a Sell, the sign convention is opposite to Buy: the main posting equals amount + fee + tax while fee
    /// and tax postings themselves are negative, and the quantity on the main posting is negative (reducing the
    /// holding). The three postings must still sum back to the original entry amount.
    /// </summary>
    [Fact]
    public async Task Booking_CreatesSecurityPostings_ForSell_WithExpectedSigns_AndSumsToEntryAmount()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, bank) = await AddAccountAsync(db, owner);

        var draft = await CreateDraftAsync(db, owner, acc.Id);
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == draft.DetectedAccountId, cancellationToken: TestContext.Current.CancellationToken);
        var entry = draft.AddEntry(DateTime.Today, 800m, "Sell", bank.Name, DateTime.Today, "EUR", null, false);
        db.Entry(entry).State = EntityState.Added;
        entry.MarkAccounted(account!.BankContactId);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var sec = new Security(owner, "ETF Y", "DE000B1", null, null, "EUR", null);
        db.Securities.Add(sec);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var feeAmt = 3.40m; var taxAmt = 7.60m;
        await sut.SetEntrySecurityAsync(draft.Id, entry.Id, sec.Id, SecurityTransactionType.Sell, 5.0m, feeAmt, taxAmt, owner, CancellationToken.None);

        var res = await sut.BookAsync(draft.Id, null, owner, false, CancellationToken.None);
        Assert.True(res.Success);
        Assert.Equal(3, db.Postings.Count(p => p.Kind == PostingKind.Security));

        var main = db.Postings.Single(p => p.Kind == PostingKind.Security && p.SecuritySubType == SecurityPostingSubType.Sell);
        var fee = db.Postings.Single(p => p.Kind == PostingKind.Security && p.SecuritySubType == SecurityPostingSubType.Fee);
        var tax = db.Postings.Single(p => p.Kind == PostingKind.Security && p.SecuritySubType == SecurityPostingSubType.Tax);

        // Sell: main = amount + fee + tax; fee/tax negative
        Assert.Equal(800m + feeAmt + taxAmt, main.Amount);
        Assert.Equal(-feeAmt, fee.Amount);
        Assert.Equal(-taxAmt, tax.Amount);
        Assert.Equal(entry.Amount, main.Amount + fee.Amount + tax.Amount);

        // Quantity assertions: sell should be negative
        Assert.Equal(-5.0m, main.Quantity);
        Assert.Null(fee.Quantity);
        Assert.Null(tax.Quantity);

        conn.Dispose();
    }

    /// <summary>
    /// Dividends do not require a quantity (it may remain null, unlike Buy) but must still split into main/fee/tax
    /// postings that follow the Sell-style sign convention (fee/tax negative) and sum back to the entry amount.
    /// </summary>
    [Fact]
    public async Task Booking_CreatesSecurityPostings_ForDividend_QuantityOptional_AndSumsToEntryAmount()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, bank) = await AddAccountAsync(db, owner);

        var draft = await CreateDraftAsync(db, owner, acc.Id);
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == draft.DetectedAccountId, cancellationToken: TestContext.Current.CancellationToken);
        var entry = draft.AddEntry(DateTime.Today, 123.45m, "Dividend", bank.Name, DateTime.Today, "EUR", null, false);
        db.Entry(entry).State = EntityState.Added;
        entry.MarkAccounted(account!.BankContactId);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var sec = new Security(owner, "ETF Z", "DE000C1", null, null, "EUR", null);
        db.Securities.Add(sec);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var feeAmt = 1.00m; var taxAmt = 0.50m;
        // Dividend: quantity can be null
        await sut.SetEntrySecurityAsync(draft.Id, entry.Id, sec.Id, SecurityTransactionType.Dividend, null, feeAmt, taxAmt, owner, CancellationToken.None);

        var res = await sut.BookAsync(draft.Id, null, owner, false, CancellationToken.None);
        Assert.True(res.Success);

        var main = db.Postings.Single(p => p.Kind == PostingKind.Security && p.SecuritySubType == SecurityPostingSubType.Dividend);
        var fee = db.Postings.Single(p => p.Kind == PostingKind.Security && p.SecuritySubType == SecurityPostingSubType.Fee);
        var tax = db.Postings.Single(p => p.Kind == PostingKind.Security && p.SecuritySubType == SecurityPostingSubType.Tax);

        // Dividend: main = amount + fee + tax; fee/tax negative
        Assert.Equal(entry.Amount + feeAmt + taxAmt, main.Amount);
        Assert.Equal(-feeAmt, fee.Amount);
        Assert.Equal(-taxAmt, tax.Amount);
        Assert.Equal(entry.Amount, main.Amount + fee.Amount + tax.Amount);

        // Quantity assertions: dividend should have no quantity
        Assert.Null(main.Quantity);
        Assert.Null(fee.Quantity);
        Assert.Null(tax.Quantity);

        conn.Dispose();
    }

    /// <summary>
    /// Regression case for a foreign-currency (USD security, EUR entry) dividend booked net-of-tax with no fee:
    /// given an 11.88 EUR net entry and 1.68 EUR tax, the main dividend posting must gross back up to 13.56 EUR
    /// so that main + tax reproduces the original net entry amount exactly.
    /// </summary>
    [Fact]
    public async Task Booking_Dividend_WithTaxOnly_Net1188_ShouldCreateMain1356_AndTax168()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, bank) = await AddAccountAsync(db, owner);
        var draft = await CreateDraftAsync(db, owner, acc.Id);
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == draft.DetectedAccountId, cancellationToken: TestContext.Current.CancellationToken);

        // Entry: 11.88 EUR net dividend
        var entry = draft.AddEntry(new DateTime(2024, 5, 10), 11.88m, "Dividend", bank.Name, new DateTime(2024, 5, 10), "EUR", null, false);
        db.Entry(entry).State = EntityState.Added;
        entry.MarkAccounted(account!.BankContactId);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Security in USD
        var sec = new Security(owner, "US ETF", "US000123", null, null, "USD", null);
        db.Securities.Add(sec);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assign as Dividend with only tax 1.68 EUR
        await sut.SetEntrySecurityAsync(draft.Id, entry.Id, sec.Id, SecurityTransactionType.Dividend, null, null, 1.68m, owner, CancellationToken.None);

        var res = await sut.BookAsync(draft.Id, null, owner, false, CancellationToken.None);
        Assert.True(res.Success);

        var securityPosts = db.Postings.Where(p => p.Kind == PostingKind.Security).ToList();
        Assert.Equal(2, securityPosts.Count);

        var main = securityPosts.Single(p => p.SecuritySubType == SecurityPostingSubType.Dividend);
        var tax = securityPosts.Single(p => p.SecuritySubType == SecurityPostingSubType.Tax);

        Assert.Equal(13.56m, main.Amount);
        Assert.Equal(-1.68m, tax.Amount);
        Assert.Equal(entry.Amount, main.Amount + tax.Amount);

        conn.Dispose();
    }

    /// <summary>
    /// A dividend correction/storno (negative entry amount, negative tax) must produce a negative main posting and
    /// a positive tax reversal that still sum back to the entry amount - verifying the sign handling introduced
    /// for the positive case is not accidentally inverted for reversals.
    /// </summary>
    [Fact]
    public async Task Booking_Dividend_Storno_NegativeValues_ShouldCreateMainMinus184_TaxPlus022()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, bank) = await AddAccountAsync(db, owner);
        var draft = await CreateDraftAsync(db, owner, acc.Id);
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == draft.DetectedAccountId, cancellationToken: TestContext.Current.CancellationToken);

        // Entry: -1.62 EUR storno dividend
        var entry = draft.AddEntry(new DateTime(2024, 5, 10), -1.62m, "Dividend Storno", bank.Name, new DateTime(2024, 5, 10), "EUR", null, false);
        db.Entry(entry).State = EntityState.Added;
        entry.MarkAccounted(account!.BankContactId);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // USD security
        var sec = new Security(owner, "US ETF", "US000XYZ", null, null, "USD", null);
        db.Securities.Add(sec);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assign Dividend with tax -0.22 EUR
        await sut.SetEntrySecurityAsync(draft.Id, entry.Id, sec.Id, SecurityTransactionType.Dividend, null, null, -0.22m, owner, CancellationToken.None);

        var res = await sut.BookAsync(draft.Id, null, owner, false, CancellationToken.None);
        Assert.True(res.Success);

        var securityPosts = db.Postings.Where(p => p.Kind == PostingKind.Security).ToList();
        Assert.Equal(2, securityPosts.Count);

        var main = securityPosts.Single(p => p.SecuritySubType == SecurityPostingSubType.Dividend);
        var tax = securityPosts.Single(p => p.SecuritySubType == SecurityPostingSubType.Tax);

        // Expect main -1.84; tax +0.22; total equals entry amount
        Assert.Equal(-1.84m, main.Amount);
        Assert.Equal(0.22m, tax.Amount);
        Assert.Equal(entry.Amount, main.Amount + tax.Amount);

        conn.Dispose();
    }

    /// <summary>
    /// Mirror of the storno case: a normal positive-amount dividend with positive tax must produce a positive
    /// main posting and a negative tax posting summing back to the entry amount, confirming the sign convention
    /// holds in the non-reversal direction too.
    /// </summary>
    [Fact]
    public async Task Booking_Dividend_PositiveValues_ShouldCreateMainPlus184_TaxMinus022()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, bank) = await AddAccountAsync(db, owner);
        var draft = await CreateDraftAsync(db, owner, acc.Id);
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == draft.DetectedAccountId, cancellationToken: TestContext.Current.CancellationToken);

        // Entry: +1.62 EUR dividend
        var entry = draft.AddEntry(new DateTime(2024, 5, 10), 1.62m, "Dividend", bank.Name, new DateTime(2024, 5, 10), "EUR", null, false);
        db.Entry(entry).State = EntityState.Added;
        entry.MarkAccounted(account!.BankContactId);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // USD security
        var sec = new Security(owner, "US ETF", "US000XYZ", null, null, "USD", null);
        db.Securities.Add(sec);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assign Dividend with tax +0.22 EUR
        await sut.SetEntrySecurityAsync(draft.Id, entry.Id, sec.Id, SecurityTransactionType.Dividend, null, null, 0.22m, owner, CancellationToken.None);

        var res = await sut.BookAsync(draft.Id, null, owner, false, CancellationToken.None);
        Assert.True(res.Success);

        var securityPosts = db.Postings.Where(p => p.Kind == PostingKind.Security).ToList();
        Assert.Equal(2, securityPosts.Count);

        var main = securityPosts.Single(p => p.SecuritySubType == SecurityPostingSubType.Dividend);
        var tax = securityPosts.Single(p => p.SecuritySubType == SecurityPostingSubType.Tax);

        // Expect main +1.84; tax -0.22; total equals entry amount
        Assert.Equal(1.84m, main.Amount);
        Assert.Equal(-0.22m, tax.Amount);
        Assert.Equal(entry.Amount, main.Amount + tax.Amount);

        conn.Dispose();
    }

    /// <summary>
    /// Booking a security-related posting must invalidate the cached portfolio analysis report for the owner, so
    /// that a subsequent report request recomputes performance figures instead of serving stale cached data.
    /// </summary>
    [Fact]
    public async Task BookAsync_SecurityPosting_InvalidatesPortfolioAnalysisReportCache()
    {
        var (_, db, conn, owner) = Create();
        var (acc, bank) = await AddAccountAsync(db, owner);
        var draft = await CreateDraftAsync(db, owner, acc.Id);
        var entry = draft.AddEntry(new DateTime(2024, 5, 10), -100m, "Buy", bank.Name, new DateTime(2024, 5, 10), "EUR", null, false);
        entry.MarkAccounted(bank.Id);
        db.Entry(entry).State = EntityState.Added;

        var security = new Security(owner, "ETF", "ETF0001", null, null, "EUR", null);
        db.Securities.Add(security);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var portfolioCache = new Mock<IPortfolioAnalysisReportCacheService>();
        var sut = new StatementDraftService(
            db,
            new PostingAggregateService(db),
            new StubAccountService(),
            new StubStatementFileFactory(),
            null,
            NullLogger<StatementDraftService>.Instance,
            null,
            null,
            null,
            null,
            portfolioCache.Object);

        await sut.SetEntrySecurityAsync(draft.Id, entry.Id, security.Id, SecurityTransactionType.Buy, 1m, null, null, owner, CancellationToken.None);

        var result = await sut.BookAsync(draft.Id, null, owner, false, CancellationToken.None);

        Assert.True(result.Success);
        portfolioCache.Verify(c => c.InvalidateCacheAsync(owner, It.IsAny<CancellationToken>()), Times.Once);
        conn.Dispose();
    }

    /// <summary>
    /// Booking an ordinary posting on an account with security processing disabled must not trigger portfolio
    /// analysis report cache invalidation, since it has no bearing on security holdings - avoids unnecessary
    /// cache churn for postings unrelated to securities.
    /// </summary>
    [Fact]
    public async Task BookAsync_NormalPostingOnNonSecurityAccount_DoesNotInvalidatePortfolioAnalysisReportCache()
    {
        var (_, db, conn, owner) = Create();
        var (acc, _) = await AddAccountAsync(db, owner);
        acc.SetSecurityProcessingEnabled(false);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var draft = await CreateDraftAsync(db, owner, acc.Id);
        var shop = new Contact(owner, "Shop", ContactType.Organization, null, null, false);
        db.Contacts.Add(shop);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var entry = draft.AddEntry(new DateTime(2024, 5, 10), -25m, "Groceries", shop.Name, new DateTime(2024, 5, 10), "EUR", null, false);
        entry.MarkAccounted(shop.Id);
        db.Entry(entry).State = EntityState.Added;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var portfolioCache = new Mock<IPortfolioAnalysisReportCacheService>();
        var sut = new StatementDraftService(
            db,
            new PostingAggregateService(db),
            new StubAccountService(),
            new StubStatementFileFactory(),
            null,
            NullLogger<StatementDraftService>.Instance,
            null,
            null,
            null,
            null,
            portfolioCache.Object);

        var result = await sut.BookAsync(draft.Id, null, owner, false, CancellationToken.None);

        Assert.True(result.Success);
        portfolioCache.Verify(c => c.InvalidateCacheAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        conn.Dispose();
    }

    /// <summary>
    /// Booking a recurring savings-plan contribution whose current target date sits on a month-end (Jan 31) must
    /// advance the plan's next target date by one calendar month using end-of-month clamping (landing on Feb 29
    /// in a leap year) rather than naive date arithmetic that would overflow into an invalid day.
    /// </summary>
    [Fact]
    public async Task Booking_SelfContactWithRecurringSavingsPlan_MonthEnd_31Jan_To_29Feb()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, _) = await AddAccountAsync(db, owner);
        var draft = await CreateDraftAsync(db, owner, acc.Id);
        var self = await db.Contacts.FirstAsync(c => c.OwnerUserId == owner && c.Type == ContactType.Self, cancellationToken: TestContext.Current.CancellationToken);

        var plan = new SavingsPlan(owner, "Recurring", SavingsPlanType.Recurring, 1000m, new DateTime(2024, 1, 31), SavingsPlanInterval.Monthly);
        db.SavingsPlans.Add(plan);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var e = draft.AddEntry(new DateTime(2024, 1, 31), 100m, "Save", self.Name, new DateTime(2024, 1, 31), "EUR", null, false);
        e.AssignSavingsPlan(plan.Id);
        e.MarkAccounted(self.Id);
        db.Entry(e).State = EntityState.Added;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var res = await sut.BookAsync(draft.Id, null, owner, false, CancellationToken.None);
        Assert.True(res.Success);

        var updatedPlan = await db.SavingsPlans.FirstAsync(p => p.Id == plan.Id, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(new DateTime(2024, 2, 29), updatedPlan.TargetDate!.Value.Date);

        conn.Dispose();
    }

    /// <summary>
    /// Even when the plan's current target date is not itself a month-end (Jan 30), advancing it one month must
    /// still clamp to the shorter month's last valid day (Feb 29) rather than overflowing - the clamping behavior
    /// applies regardless of whether the starting date happened to be a month-end.
    /// </summary>
    [Fact]
    public async Task Booking_SelfContactWithRecurringSavingsPlan_NonMonthEnd_30Jan_To_29Feb_Capped()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, _) = await AddAccountAsync(db, owner);
        var draft = await CreateDraftAsync(db, owner, acc.Id);
        var self = await db.Contacts.FirstAsync(c => c.OwnerUserId == owner && c.Type == ContactType.Self, cancellationToken: TestContext.Current.CancellationToken);

        var plan = new SavingsPlan(owner, "Recurring", SavingsPlanType.Recurring, 1000m, new DateTime(2024, 1, 30), SavingsPlanInterval.Monthly);
        db.SavingsPlans.Add(plan);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var e = draft.AddEntry(new DateTime(2024, 1, 30), 100m, "Save", self.Name, new DateTime(2024, 1, 30), "EUR", null, false);
        e.AssignSavingsPlan(plan.Id);
        e.MarkAccounted(self.Id);
        db.Entry(e).State = EntityState.Added;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var res = await sut.BookAsync(draft.Id, null, owner, false, CancellationToken.None);
        Assert.True(res.Success);

        var updatedPlan = await db.SavingsPlans.FirstAsync(p => p.Id == plan.Id, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(new DateTime(2024, 2, 29), updatedPlan.TargetDate!.Value.Date);

        conn.Dispose();
    }

    /// <summary>
    /// When the booking date lies well beyond the plan's current target date, the target must be advanced
    /// repeatedly, one month at a time, until it reaches or passes the booking date (Jan 31 -> Feb 29 -> Mar 31),
    /// rather than jumping straight to the booking date or only advancing a single step.
    /// </summary>
    [Fact]
    public async Task Booking_SelfContactWithRecurringSavingsPlan_MultipleAdvance_To_MarchEnd()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, _) = await AddAccountAsync(db, owner);
        var draft = await CreateDraftAsync(db, owner, acc.Id);
        var self = await db.Contacts.FirstAsync(c => c.OwnerUserId == owner && c.Type == ContactType.Self, cancellationToken: TestContext.Current.CancellationToken);

        var plan = new SavingsPlan(owner, "Recurring", SavingsPlanType.Recurring, 1000m, new DateTime(2024, 1, 31), SavingsPlanInterval.Monthly);
        db.SavingsPlans.Add(plan);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Booking on March 15th should advance Jan 31 -> Feb 29 -> Mar 31 (stop because > March 15)
        var e = draft.AddEntry(new DateTime(2024, 3, 15), 100m, "Save", self.Name, new DateTime(2024, 3, 15), "EUR", null, false);
        e.AssignSavingsPlan(plan.Id);
        e.MarkAccounted(self.Id);
        db.Entry(e).State = EntityState.Added;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var res = await sut.BookAsync(draft.Id, null, owner, false, CancellationToken.None);
        Assert.True(res.Success);

        var updatedPlan = await db.SavingsPlans.FirstAsync(p => p.Id == plan.Id, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(new DateTime(2024, 3, 31), updatedPlan.TargetDate!.Value.Date);

        conn.Dispose();
    }

    /// <summary>
    /// Booking a savings-plan contribution that would push the plan's total past its configured target amount
    /// must be blocked with a "SAVINGSPLAN_GOAL_EXCEEDS" warning referencing the specific plan, requiring the
    /// caller to explicitly confirm before the booking (and its bank/contact/savings-plan postings) is created.
    /// </summary>
    [Fact]
    public async Task Booking_Prevented_When_SavingsPlan_Target_WouldBeExceeded()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, bank) = await AddAccountAsync(db, owner);

        // create savings plan target 100
        var plan = new SavingsPlan(owner, "PlanTarget", SavingsPlanType.OneTime, 100m, null, null);
        db.SavingsPlans.Add(plan);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // existing booked savings plan postings totaling 90 (create Posting with positive amount to produce remaining = 10)
        var existing = new Domain.Postings.Posting(Guid.NewGuid(), PostingKind.SavingsPlan, null, null, plan.Id, null, DateTime.Today, DateTime.Today, 90m, "existing", null, null, null);
        db.Postings.Add(existing);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // create draft with one entry of -20 assigned to savings plan (negative amount so planned becomes +20 in validation logic)
        var draft = await CreateDraftAsync(db, owner, acc.Id);
        var self = await db.Contacts.FirstAsync(c => c.OwnerUserId == owner && c.Type == ContactType.Self, cancellationToken: TestContext.Current.CancellationToken);
        var entry = draft.AddEntry(DateTime.Today, -20m, "Save Over", self.Name, DateTime.Today, "EUR", null, false);
        entry.AssignSavingsPlan(plan.Id);
        entry.MarkAccounted(self.Id);
        db.Entry(entry).State = EntityState.Added;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        var res = await sut.BookAsync(draft.Id, null, owner, false, CancellationToken.None);

        // Assert: booking should be prevented and validation should contain SAVINGSPLAN_GOAL_EXCEEDS
        Assert.False(res.Success);
        Assert.True(res.HasWarnings);
        Assert.True(res.Validation.Messages.Any(m => m.Code == "SAVINGSPLAN_GOAL_EXCEEDS"));
        var savingsPlanGoalExceeds = Assert.Single(res.Validation.Messages.Where(m => m.Code == "SAVINGSPLAN_GOAL_EXCEEDS"));
        Assert.Equal("savings-plans", savingsPlanGoalExceeds.RelatedRecordKind);
        Assert.Equal(plan.Id, savingsPlanGoalExceeds.RelatedRecordId);

        // Now retry booking while ignoring warnings -> should succeed
        var beforeCount = db.Postings.Count();
        var res2 = await sut.BookAsync(draft.Id, null, owner, true, CancellationToken.None);
        Assert.True(res2.Success);
        // postings created: bank + contact + savings plan -> delta of 3
        Assert.Equal(beforeCount + 3, db.Postings.Count());
        // Ensure the newly created postings reference the source entry and contain expected kinds
        var newPosts = db.Postings.Where(p => p.SourceId == entry.Id).ToList();
        Assert.Equal(3, newPosts.Count);
        Assert.Equal(1, newPosts.Count(p => p.Kind == PostingKind.Bank));
        Assert.Equal(1, newPosts.Count(p => p.Kind == PostingKind.Contact));
        Assert.Equal(1, newPosts.Count(p => p.Kind == PostingKind.SavingsPlan));

        conn.Dispose();
    }

    /// <summary>
    /// Re-booking a draft that has already been committed must be rejected with "BOOKING_ALREADY_PROCESSED" and
    /// marked as non-retryable, preventing a duplicate/repeated booking request from creating duplicate postings.
    /// </summary>
    [Fact]
    public async Task Booking_ShouldRejectAsAlreadyProcessed_WhenDraftWasAlreadyBooked()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, _) = await AddAccountAsync(db, owner);
        var draft = await CreateDraftAsync(db, owner, acc.Id);
        var shop = new Contact(owner, "Shop GmbH", ContactType.Organization, null, null, false);
        db.Contacts.Add(shop);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var entry = draft.AddEntry(DateTime.Today, 15m, "Booked once", shop.Name, DateTime.Today, "EUR", null, false);
        entry.MarkAccounted(shop.Id);
        db.Entry(entry).State = EntityState.Added;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var first = await sut.BookAsync(draft.Id, null, owner, false, CancellationToken.None);
        Assert.True(first.Success);

        var second = await sut.BookAsync(draft.Id, null, owner, false, CancellationToken.None);
        Assert.False(second.Success);
        Assert.Equal("BOOKING_ALREADY_PROCESSED", second.ErrorCode);
        Assert.False(second.Retryable);

        conn.Dispose();
    }

    /// <summary>
    /// If a <see cref="StatementDraftBookingGuard"/> row already marks the draft as currently being booked, a new
    /// booking attempt must be rejected with "BOOKING_IN_PROGRESS" and marked retryable - protecting against
    /// overlapping concurrent booking attempts for the same draft racing each other into duplicate postings.
    /// </summary>
    [Fact]
    public async Task Booking_ShouldRejectWithInProgress_WhenDraftGuardIsAlreadyActive()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, _) = await AddAccountAsync(db, owner);
        var draft = await CreateDraftAsync(db, owner, acc.Id);

        db.StatementDraftBookingGuards.Add(new StatementDraftBookingGuard(
            owner,
            draft.Id,
            Guid.NewGuid(),
            DateTime.UtcNow,
            DateTime.UtcNow.AddMinutes(2)));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await sut.BookAsync(draft.Id, null, owner, false, CancellationToken.None);
        Assert.False(result.Success);
        Assert.Equal("BOOKING_IN_PROGRESS", result.ErrorCode);
        Assert.True(result.Retryable);

        conn.Dispose();
    }

    /// <summary>
    /// Ensures booking changes are rolled back when a late-stage exception occurs.
    /// </summary>
    [Fact]
    public async Task BookAsync_ShouldRollbackAllChanges_WhenReassignThrowsDuringCommit()
    {
        var (_, db, conn, owner) = Create();
        var (account, _) = await AddAccountAsync(db, owner);
        var draft = await CreateDraftAsync(db, owner, account.Id);
        var recipient = new Contact(owner, "Rollback Recipient", ContactType.Organization, null, null, false);
        db.Contacts.Add(recipient);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var entry = draft.AddEntry(DateTime.Today, 45m, "Rollback Booking", recipient.Name, DateTime.Today, "EUR", null, false);
        entry.MarkAccounted(recipient.Id);
        db.Entry(entry).State = EntityState.Added;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var attachments = new ControlledAttachmentService(_ => Task.FromException(new InvalidOperationException("reassign failed")));
        var sut = new StatementDraftService(db, new PostingAggregateService(db), new StubAccountService(), new StubStatementFileFactory(), null, NullLogger<StatementDraftService>.Instance, attachments);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.BookAsync(draft.Id, null, owner, false, CancellationToken.None));

        var verifyOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(conn)
            .Options;
        using var verifyDb = new AppDbContext(verifyOptions);

        var persistedDraft = await verifyDb.StatementDrafts.FirstAsync(d => d.Id == draft.Id, cancellationToken: TestContext.Current.CancellationToken);
        var persistedAccount = await verifyDb.Accounts.FirstAsync(a => a.Id == account.Id, cancellationToken: TestContext.Current.CancellationToken);
        var postingCount = await verifyDb.Postings.CountAsync(p => p.SourceId == entry.Id, cancellationToken: TestContext.Current.CancellationToken);
        var aggregateCount = await verifyDb.PostingAggregates.CountAsync(cancellationToken: TestContext.Current.CancellationToken);
        var guardCount = await verifyDb.StatementDraftBookingGuards.CountAsync(g => g.OwnerUserId == owner && g.DraftId == draft.Id, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(StatementDraftStatus.Draft, persistedDraft.Status);
        Assert.Equal(0m, persistedAccount.CurrentBalance);
        Assert.Equal(0, postingCount);
        Assert.Equal(0, aggregateCount);
        Assert.Equal(0, guardCount);

        conn.Dispose();
    }

    /// <summary>
    /// Ensures a concurrent second booking attempt is rejected while the first booking still holds the guard.
    /// </summary>
    [Fact]
    public async Task BookAsync_ShouldReturnInProgress_ForSecondConcurrentAttemptOnSameDraft()
    {
        var dbName = $"statement-booking-{Guid.NewGuid()}";
        var connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";
        await using var keepAlive = new SqliteConnection(connectionString);
        await keepAlive.OpenAsync(TestContext.Current.CancellationToken);

        var initOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connectionString)
            .Options;

        Guid ownerId;
        Guid draftId;
        Guid entryId;
        await using (var initDb = new AppDbContext(initOptions))
        {
            await initDb.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            var ownerUser = new FinanceManager.Domain.Users.User("parallel-owner", "hash", true);
            initDb.Users.Add(ownerUser);
            await initDb.SaveChangesAsync(TestContext.Current.CancellationToken);

            initDb.Contacts.Add(new Contact(ownerUser.Id, "Ich", ContactType.Self, null, null));
            var bank = new Contact(ownerUser.Id, "Bank", ContactType.Bank, null, null);
            initDb.Contacts.Add(bank);
            await initDb.SaveChangesAsync(TestContext.Current.CancellationToken);

            var account = new Account(ownerUser.Id, AccountType.Giro, "Parallel Account", "DE11", bank.Id);
            initDb.Accounts.Add(account);
            await initDb.SaveChangesAsync(TestContext.Current.CancellationToken);

            var draft = new StatementDraft(ownerUser.Id, "parallel.csv", "", "");
            draft.SetDetectedAccount(account.Id);
            initDb.StatementDrafts.Add(draft);
            await initDb.SaveChangesAsync(TestContext.Current.CancellationToken);

            var contact = new Contact(ownerUser.Id, "Parallel Recipient", ContactType.Organization, null, null, false);
            initDb.Contacts.Add(contact);
            await initDb.SaveChangesAsync(TestContext.Current.CancellationToken);

            var entry = draft.AddEntry(DateTime.Today, 30m, "Parallel Booking", contact.Name, DateTime.Today, "EUR", null, false);
            entry.MarkAccounted(contact.Id);
            initDb.Entry(entry).State = EntityState.Added;
            await initDb.SaveChangesAsync(TestContext.Current.CancellationToken);

            ownerId = ownerUser.Id;
            draftId = draft.Id;
            entryId = entry.Id;
        }

        var gateEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var gateRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var db1 = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connectionString).Options);
        await using var db2 = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connectionString).Options);

        var blockingAttachments = new ControlledAttachmentService(async ct =>
        {
            gateEntered.TrySetResult(true);
            await gateRelease.Task.WaitAsync(ct);
        });
        var sut1 = new StatementDraftService(db1, new PostingAggregateService(db1), new StubAccountService(), new StubStatementFileFactory(), null, NullLogger<StatementDraftService>.Instance, blockingAttachments);
        var sut2 = new StatementDraftService(db2, new PostingAggregateService(db2), new StubAccountService(), new StubStatementFileFactory(), null, NullLogger<StatementDraftService>.Instance, null);

        var firstTask = sut1.BookAsync(draftId, null, ownerId, false, CancellationToken.None);
        await Task.WhenAny(gateEntered.Task, Task.Delay(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Assert.True(gateEntered.Task.IsCompleted);

        BookingResult? secondResult = null;
        var secondAttemptHitSqliteLock = false;
        try
        {
            secondResult = await sut2.BookAsync(draftId, null, ownerId, false, CancellationToken.None);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException sqliteException && sqliteException.SqliteErrorCode == 6)
        {
            secondAttemptHitSqliteLock = true;
        }

        gateRelease.TrySetResult(true);
        var firstResult = await firstTask;
        Assert.True(firstResult.Success);
        Assert.True(
            secondAttemptHitSqliteLock ||
            (secondResult is not null &&
             !secondResult.Success &&
             string.Equals(secondResult.ErrorCode, "BOOKING_IN_PROGRESS", StringComparison.Ordinal) &&
             secondResult.Retryable == true));

        await using var verifyDb = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connectionString).Options);
        var persistedPostings = await verifyDb.Postings.CountAsync(p => p.SourceId == entryId, cancellationToken: TestContext.Current.CancellationToken);
        var persistedGuardCount = await verifyDb.StatementDraftBookingGuards.CountAsync(g => g.OwnerUserId == ownerId && g.DraftId == draftId, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, persistedPostings);
        Assert.Equal(0, persistedGuardCount);
    }

    /// <summary>
    /// Ensures re-booking a previously booked single entry returns already-processed and creates no duplicates.
    /// </summary>
    [Fact]
    public async Task BookAsync_ShouldReturnAlreadyProcessed_WhenSpecificEntryWasPreviouslyBooked()
    {
        var (sut, db, conn, owner) = Create();
        var (account, _) = await AddAccountAsync(db, owner);
        var draft = await CreateDraftAsync(db, owner, account.Id);
        var recipient = new Contact(owner, "Entry Recipient", ContactType.Organization, null, null, false);
        db.Contacts.Add(recipient);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var firstEntry = draft.AddEntry(DateTime.Today, 12m, "First Entry", recipient.Name, DateTime.Today, "EUR", null, false);
        var secondEntry = draft.AddEntry(DateTime.Today, 18m, "Second Entry", recipient.Name, DateTime.Today, "EUR", null, false);
        firstEntry.MarkAccounted(recipient.Id);
        secondEntry.MarkAccounted(recipient.Id);
        db.Entry(firstEntry).State = EntityState.Added;
        db.Entry(secondEntry).State = EntityState.Added;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var firstResult = await sut.BookAsync(draft.Id, firstEntry.Id, owner, false, CancellationToken.None);
        Assert.True(firstResult.Success);
        var postingCountAfterFirstBooking = await db.Postings.CountAsync(p => p.SourceId == firstEntry.Id, cancellationToken: TestContext.Current.CancellationToken);

        var secondResult = await sut.BookAsync(draft.Id, firstEntry.Id, owner, false, CancellationToken.None);

        Assert.False(secondResult.Success);
        Assert.Equal("BOOKING_ALREADY_PROCESSED", secondResult.ErrorCode);
        Assert.False(secondResult.Retryable);
        Assert.True(secondResult.Validation.Messages.Any(m => m.Code == "BOOKING_ALREADY_PROCESSED"));
        Assert.Equal(postingCountAfterFirstBooking, await db.Postings.CountAsync(p => p.SourceId == firstEntry.Id, cancellationToken: TestContext.Current.CancellationToken));

        conn.Dispose();
    }

    /// <summary>
    /// Verifies that a newly created preliminary draft is linked to the specified account and marked as preliminary.
    /// </summary>
    [Fact]
    public async Task CreatePreliminaryDraftAsync_Should_CreatePreliminaryDraft_ForAccount()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, _) = await AddAccountAsync(db, owner);

        var dto = await sut.CreatePreliminaryDraftAsync(owner, acc.Id, CancellationToken.None);

        Assert.NotNull(dto);
        Assert.Equal(acc.Id, dto.DetectedAccountId);
        Assert.True(dto.IsPreliminary);

        conn.Dispose();
    }

    /// <summary>
    /// Verifies that booking a preliminary draft propagates the preliminary flag to all created postings.
    /// </summary>
    [Fact]
    public async Task BookAsync_PreliminaryDraft_ShouldCreatePreliminaryPostings()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, _) = await AddAccountAsync(db, owner);
        var draft = await CreateDraftAsync(db, owner, acc.Id);
        draft.MarkAsPreliminary();
        db.Entry(draft).State = EntityState.Modified;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var shop = new Contact(owner, "Shop GmbH", ContactType.Organization, null, null, false);
        db.Contacts.Add(shop);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var entry = draft.AddEntry(DateTime.Today, 10m, "A", shop.Name, DateTime.Today, "EUR", null, false);
        db.Entry(entry).State = EntityState.Added;
        entry.MarkAccounted(shop.Id);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await sut.BookAsync(draft.Id, entry.Id, owner, false, CancellationToken.None);

        Assert.True(result.Success);
        var postings = await db.Postings.Where(p => p.SourceId == entry.Id).ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.All(postings, p => Assert.True(p.IsPreliminary));

        conn.Dispose();
    }

    /// <summary>
    /// Verifies that booking a real statement reverses all preliminary postings for the same account.
    /// </summary>
    [Fact]
    public async Task BookAsync_RealDraft_ShouldReversePreliminaryPostings()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, _) = await AddAccountAsync(db, owner);

        // 1. Book a preliminary posting
        var prelimDraft = await CreateDraftAsync(db, owner, acc.Id);
        prelimDraft.MarkAsPreliminary();
        db.Entry(prelimDraft).State = EntityState.Modified;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var shop = new Contact(owner, "Shop GmbH", ContactType.Organization, null, null, false);
        db.Contacts.Add(shop);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var prelimEntry = prelimDraft.AddEntry(DateTime.Today, 10m, "A", shop.Name, DateTime.Today, "EUR", null, false);
        db.Entry(prelimEntry).State = EntityState.Added;
        prelimEntry.MarkAccounted(shop.Id);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var prelimResult = await sut.BookAsync(prelimDraft.Id, prelimEntry.Id, owner, false, CancellationToken.None);
        Assert.True(prelimResult.Success);

        // 2. Book a real statement for the same account
        var realDraft = await CreateDraftAsync(db, owner, acc.Id);
        var realEntry = realDraft.AddEntry(DateTime.Today, 20m, "B", shop.Name, DateTime.Today, "EUR", null, false);
        db.Entry(realEntry).State = EntityState.Added;
        realEntry.MarkAccounted(shop.Id);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var realResult = await sut.BookAsync(realDraft.Id, realEntry.Id, owner, true, CancellationToken.None);
        Assert.True(realResult.Success);

        // Preliminary postings are reversed and zeroed
        var prelimPostings = await db.Postings.Where(p => p.SourceId == prelimEntry.Id).ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.All(prelimPostings, p =>
        {
            Assert.True(p.IsReversed);
            Assert.Equal(0m, p.Amount);
            Assert.True(p.OriginalAmount.HasValue);
        });

        // Real postings are not preliminary
        var realPostings = await db.Postings.Where(p => p.SourceId == realEntry.Id).ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.All(realPostings, p =>
        {
            Assert.False(p.IsPreliminary);
            Assert.False(p.IsReversed);
        });

        conn.Dispose();
    }

    /// <summary>
    /// Verifies that another preliminary booking does not reverse existing preliminary postings.
    /// </summary>
    [Fact]
    public async Task BookAsync_SecondPreliminaryDraft_ShouldNotReversePreliminaryPostings()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, _) = await AddAccountAsync(db, owner);

        var shop = new Contact(owner, "Shop GmbH", ContactType.Organization, null, null, false);
        db.Contacts.Add(shop);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var firstDraft = await CreateDraftAsync(db, owner, acc.Id);
        firstDraft.MarkAsPreliminary();
        db.Entry(firstDraft).State = EntityState.Modified;
        var firstEntry = firstDraft.AddEntry(DateTime.Today, 10m, "A", shop.Name, DateTime.Today, "EUR", null, false);
        db.Entry(firstEntry).State = EntityState.Added;
        firstEntry.MarkAccounted(shop.Id);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var firstResult = await sut.BookAsync(firstDraft.Id, firstEntry.Id, owner, false, CancellationToken.None);
        Assert.True(firstResult.Success);

        var secondDraft = await CreateDraftAsync(db, owner, acc.Id);
        secondDraft.MarkAsPreliminary();
        db.Entry(secondDraft).State = EntityState.Modified;
        var secondEntry = secondDraft.AddEntry(DateTime.Today, 20m, "B", shop.Name, DateTime.Today, "EUR", null, false);
        db.Entry(secondEntry).State = EntityState.Added;
        secondEntry.MarkAccounted(shop.Id);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var secondResult = await sut.BookAsync(secondDraft.Id, secondEntry.Id, owner, false, CancellationToken.None);
        Assert.True(secondResult.Success);

        var firstPostings = await db.Postings.Where(p => p.SourceId == firstEntry.Id).ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.All(firstPostings, p =>
        {
            Assert.False(p.IsReversed);
            Assert.NotEqual(0m, p.Amount);
        });

        var secondPostings = await db.Postings.Where(p => p.SourceId == secondEntry.Id).ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.All(secondPostings, p => Assert.True(p.IsPreliminary));

        conn.Dispose();
    }
}
