using FinanceManager.Application.Common;
using FinanceManager.Shared.Dtos.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinanceManager.Infrastructure.Common;

/// <summary>
/// Central registry-based implementation for server-side "create and assign" flows.
/// </summary>
public sealed class ParentAssignmentService : IParentAssignmentService
{
    private readonly AppDbContext _db;
    private readonly ILogger<ParentAssignmentService> _logger;
    private readonly IReadOnlyDictionary<string, Func<Guid, ParentLinkRequest, string, Guid, CancellationToken, Task<bool>>> _handlers;

    /// <summary>
    /// Creates a new instance.
    /// </summary>
    /// <param name="db">The db.</param>
    /// <param name="logger">Logger instance.</param>
    public ParentAssignmentService(AppDbContext db, ILogger<ParentAssignmentService> logger)
    {
        _db = db;
        _logger = logger;

        _handlers = new Dictionary<string, Func<Guid, ParentLinkRequest, string, Guid, CancellationToken, Task<bool>>>(StringComparer.OrdinalIgnoreCase)
        {
            // Budget purpose <- budget category
            ["budget/purposes:budget/categories"] = AssignBudgetCategoryToBudgetPurposeAsync,

            // Statement draft entry <- linked entities
            ["statement-drafts/entries:contacts"] = AssignContactToStatementDraftEntryAsync,
            ["statement-drafts/entries:savings-plans"] = AssignSavingsPlanToStatementDraftEntryAsync,
            ["statement-drafts/entries:securities"] = AssignSecurityToStatementDraftEntryAsync
        };
    }

    /// <inheritdoc />
    public async Task<bool> TryAssignAsync(Guid ownerUserId, ParentLinkRequest? parent, string createdKind, Guid createdId, CancellationToken ct)
    {
        if (parent == null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(parent.ParentKind) || parent.ParentId == Guid.Empty)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(createdKind) || createdId == Guid.Empty)
        {
            return false;
        }

        var key = $"{Normalize(parent.ParentKind)}:{Normalize(createdKind)}";
        if (!_handlers.TryGetValue(key, out var handler))
        {
            _logger.LogInformation("No parent assignment handler registered for {Key}", key);
            return false;
        }

        return await handler(ownerUserId, parent, createdKind, createdId, ct);
    }

    private static string Normalize(string s)
    {
        var v = (s ?? string.Empty).Trim().ToLowerInvariant();
        if (v.StartsWith('/')) v = v.TrimStart('/');
        if (v.EndsWith('/')) v = v.TrimEnd('/');
        return v;
    }

    private async Task<bool> AssignBudgetCategoryToBudgetPurposeAsync(Guid ownerUserId, ParentLinkRequest parent, string createdKind, Guid createdId, CancellationToken ct)
    {
        var purpose = await _db.BudgetPurposes
            .FirstOrDefaultAsync(p => p.OwnerUserId == ownerUserId && p.Id == parent.ParentId, ct);

        if (purpose == null)
        {
            return false;
        }

        var categoryExists = await _db.BudgetCategories
            .AnyAsync(c => c.OwnerUserId == ownerUserId && c.Id == createdId, ct);

        if (!categoryExists)
        {
            return false;
        }

        purpose.SetCategory(createdId);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    private async Task<bool> AssignContactToStatementDraftEntryAsync(Guid ownerUserId, ParentLinkRequest parent, string createdKind, Guid createdId, CancellationToken ct)
    {
        // Validate created contact belongs to owner
        var exists = await _db.Contacts.AsNoTracking()
            .AnyAsync(c => c.OwnerUserId == ownerUserId && c.Id == createdId, ct);

        if (!exists)
        {
            return false;
        }

        // Entry entity does not carry OwnerUserId; validate ownership via parent draft.
        var entry = await _db.StatementDraftEntries
            .FirstOrDefaultAsync(e => e.Id == parent.ParentId, ct);

        if (entry == null)
        {
            return false;
        }

        var draftOwned = await _db.StatementDrafts.AsNoTracking()
            .AnyAsync(d => d.OwnerUserId == ownerUserId && d.Id == entry.DraftId, ct);

        if (!draftOwned)
        {
            return false;
        }

        if (entry.ContactId == createdId)
        {
            _logger.LogInformation(
                "Contact assignment is idempotent no-op: OwnerUserId={OwnerUserId}, DraftId={DraftId}, EntryId={EntryId}, ContactId={ContactId}, AssignmentResult={AssignmentResult}",
                ownerUserId,
                entry.DraftId,
                entry.Id,
                createdId,
                "already-assigned");
            return true;
        }

        entry.AssignContactWithoutAccounting(createdId);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "Contact assigned to statement draft entry: OwnerUserId={OwnerUserId}, DraftId={DraftId}, EntryId={EntryId}, ContactId={ContactId}, AssignmentResult={AssignmentResult}",
            ownerUserId,
            entry.DraftId,
            entry.Id,
            createdId,
            "assigned");
        return true;
    }

    private async Task<bool> AssignSavingsPlanToStatementDraftEntryAsync(Guid ownerUserId, ParentLinkRequest parent, string createdKind, Guid createdId, CancellationToken ct)
    {
        var exists = await _db.SavingsPlans.AsNoTracking()
            .AnyAsync(s => s.OwnerUserId == ownerUserId && s.Id == createdId, ct);

        if (!exists)
        {
            return false;
        }

        var entry = await _db.StatementDraftEntries
            .FirstOrDefaultAsync(e => e.Id == parent.ParentId, ct);

        if (entry == null)
        {
            return false;
        }

        var draftOwned = await _db.StatementDrafts.AsNoTracking()
            .AnyAsync(d => d.OwnerUserId == ownerUserId && d.Id == entry.DraftId, ct);

        if (!draftOwned)
        {
            return false;
        }

        entry.AssignSavingsPlan(createdId);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    private async Task<bool> AssignSecurityToStatementDraftEntryAsync(Guid ownerUserId, ParentLinkRequest parent, string createdKind, Guid createdId, CancellationToken ct)
    {
        var exists = await _db.Securities.AsNoTracking()
            .AnyAsync(s => s.OwnerUserId == ownerUserId && s.Id == createdId, ct);

        if (!exists)
        {
            return false;
        }

        var entry = await _db.StatementDraftEntries
            .FirstOrDefaultAsync(e => e.Id == parent.ParentId, ct);

        if (entry == null)
        {
            return false;
        }

        var draftOwned = await _db.StatementDrafts.AsNoTracking()
            .AnyAsync(d => d.OwnerUserId == ownerUserId && d.Id == entry.DraftId, ct);

        if (!draftOwned)
        {
            return false;
        }

        entry.SetSecurity(createdId, txType: null, quantity: null, fee: null, tax: null);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
