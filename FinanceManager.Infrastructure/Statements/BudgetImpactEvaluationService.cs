using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FinanceManager.Application.Budget;
using FinanceManager.Application.Statements;
using FinanceManager.Domain.Statements;
using FinanceManager.Shared.Dtos.Budget;
using FinanceManager.Shared.Dtos.Statements;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanceManager.Infrastructure.Statements;

/// <summary>
/// Calculates real-time and final budget impact projections for statement draft booking flows.
/// </summary>
public sealed class BudgetImpactEvaluationService : IBudgetImpactEvaluationService
{
    private sealed record ContactCategoryInfo(Guid Id, Guid? CategoryId);
    private sealed record BudgetPurposeInfo(Guid Id, string Name, BudgetSourceType SourceType, Guid SourceId, Guid? BudgetCategoryId);
    private sealed record BudgetRulePatternInfo(Guid? BudgetPurposeId, Guid? BudgetCategoryId, string? PurposePattern, bool PurposePatternIsRegex);

    private const decimal AlmostExhaustedThreshold = 0.90m;
    private const decimal StrongChangeThreshold = 0.20m;
    private static readonly TimeSpan RegexMatchTimeout = TimeSpan.FromMilliseconds(200);

    private readonly AppDbContext _db;
    private readonly IBudgetPlanningService _planning;
    private readonly ILogger<BudgetImpactEvaluationService> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="BudgetImpactEvaluationService"/>.
    /// </summary>
    /// <param name="db">The db.</param>
    /// <param name="planning">The planning.</param>
    /// <param name="logger">Logger instance.</param>
    public BudgetImpactEvaluationService(
        AppDbContext db,
        IBudgetPlanningService planning,
        ILogger<BudgetImpactEvaluationService>? logger = null)
    {
        _db = db;
        _planning = planning;
        _logger = logger ?? NullLogger<BudgetImpactEvaluationService>.Instance;
    }

    /// <inheritdoc />
    public async Task<BudgetImpactEvaluationDto?> EvaluateEntryImpactAsync(Guid draftId, Guid entryId, Guid ownerUserId, CancellationToken ct)
    {
        var draft = await _db.StatementDrafts
            .AsNoTracking()
            .Include(x => x.Entries)
            .FirstOrDefaultAsync(x => x.Id == draftId && x.OwnerUserId == ownerUserId, ct);
        if (draft == null) { return null; }

        var entry = draft.Entries.FirstOrDefault(x => x.Id == entryId);
        if (entry == null) { return null; }

        var hints = await EvaluateEntriesAsync(ownerUserId, new[] { entry }, ct);
        var ordered = hints.OrderByDescending(x => x.HintType).ThenBy(x => x.BudgetPurposeName).ThenBy(x => x.BudgetPeriod).ToArray();

        return new BudgetImpactEvaluationDto(
            entry.Id,
            DateTime.UtcNow,
            CreateFingerprint(ordered.Select(MapForFingerprint)),
            ordered);
    }

    /// <inheritdoc />
    public async Task<BookingImpactSummaryDto?> EvaluateDraftImpactAsync(Guid draftId, Guid? entryId, Guid ownerUserId, CancellationToken ct)
    {
        var draft = await _db.StatementDrafts
            .AsNoTracking()
            .Include(x => x.Entries)
            .FirstOrDefaultAsync(x => x.Id == draftId && x.OwnerUserId == ownerUserId, ct);
        if (draft == null) { return null; }

        var toEvaluate = draft.Entries
            .Where(x => (entryId == null || x.Id == entryId.Value)
                        && x.Status != StatementDraftEntryStatus.AlreadyBooked
                        && x.Status != StatementDraftEntryStatus.Announced)
            .ToArray();

        if (toEvaluate.Length == 0)
        {
            return new BookingImpactSummaryDto(
                draftId,
                entryId,
                DateTime.UtcNow,
                CreateFingerprint(Array.Empty<string>()),
                BudgetImpactHintType.Neutral,
                Array.Empty<BookingImpactSummaryItemDto>());
        }

        var hints = await EvaluateEntriesAsync(ownerUserId, toEvaluate, ct);
        var items = hints
            .Select(x => new BookingImpactSummaryItemDto(
                x.BudgetPurposeId,
                x.BudgetPurposeName,
                x.BudgetPeriod,
                x.HintType,
                x.TargetValue,
                x.ActualBefore,
                x.ActualAfter,
                x.FulfillmentRateBefore,
                x.FulfillmentRateAfter,
                x.Delta,
                x.Reason))
            .OrderByDescending(x => x.HintType)
            .ThenBy(x => x.BudgetPurposeName)
            .ThenBy(x => x.BudgetPeriod)
            .ToArray();

        return new BookingImpactSummaryDto(
            draftId,
            entryId,
            DateTime.UtcNow,
            CreateFingerprint(items.Select(MapForFingerprint)),
            items.Length == 0 ? BudgetImpactHintType.Neutral : items.Max(x => x.HintType),
            items);
    }

    private async Task<IReadOnlyList<BudgetImpactHintDto>> EvaluateEntriesAsync(Guid ownerUserId, IReadOnlyCollection<StatementDraftEntry> entries, CancellationToken ct)
    {
        if (entries.Count == 0) { return Array.Empty<BudgetImpactHintDto>(); }

        var contacts = await _db.Contacts
            .AsNoTracking()
            .Where(x => x.OwnerUserId == ownerUserId)
            .Select(x => new ContactCategoryInfo(x.Id, x.CategoryId))
            .ToListAsync(ct);

        var purposes = await _db.BudgetPurposes
            .AsNoTracking()
            .Where(x => x.OwnerUserId == ownerUserId)
            .Select(x => new BudgetPurposeInfo(x.Id, x.Name, x.SourceType, x.SourceId, x.BudgetCategoryId))
            .ToListAsync(ct);

        var entryContactCategories = entries
            .Where(x => x.ContactId.HasValue)
            .ToDictionary(x => x.Id, x => contacts.FirstOrDefault(c => c.Id == x.ContactId!.Value)?.CategoryId);

        var affectedPurposes = purposes
            .Where(p => entries.Any(e =>
                (p.SourceType == BudgetSourceType.Contact && e.ContactId == p.SourceId)
                || (p.SourceType == BudgetSourceType.SavingsPlan && e.SavingsPlanId == p.SourceId)
                || (p.SourceType == BudgetSourceType.ContactGroup
                    && e.ContactId.HasValue
                    && entryContactCategories.TryGetValue(e.Id, out var catId)
                    && catId == p.SourceId)))
            .ToArray();

        var affectedPurposeIds = affectedPurposes.Select(x => x.Id).Distinct().ToArray();
        var affectedCategoryIds = affectedPurposes
            .Where(x => x.BudgetCategoryId.HasValue && x.BudgetCategoryId.Value != Guid.Empty)
            .Select(x => x.BudgetCategoryId!.Value)
            .Distinct()
            .ToArray();

        var relevantRules = await _db.BudgetRules
            .AsNoTracking()
            .Where(x => x.OwnerUserId == ownerUserId)
            .Where(x =>
                (x.BudgetPurposeId.HasValue && affectedPurposeIds.Contains(x.BudgetPurposeId.Value))
                || (x.BudgetCategoryId.HasValue && affectedCategoryIds.Contains(x.BudgetCategoryId.Value)))
            .Select(x => new BudgetRulePatternInfo(x.BudgetPurposeId, x.BudgetCategoryId, x.PurposePattern, x.PurposePatternIsRegex))
            .ToListAsync(ct);

        if (affectedPurposes.Length == 0)
        {
            return Array.Empty<BudgetImpactHintDto>();
        }

        var groupedByPeriod = entries
            .GroupBy(x => BudgetPeriodKey.FromDate(DateOnly.FromDateTime(x.BookingDate)))
            .ToArray();

        var hints = new List<BudgetImpactHintDto>();
        foreach (var periodGroup in groupedByPeriod)
        {
            var period = periodGroup.Key;
            var periodEntries = periodGroup.ToArray();
            var periodPurposeIds = affectedPurposes.Select(x => x.Id).Distinct().ToArray();
            var planned = await _planning.CalculatePlannedValuesAsync(ownerUserId, periodPurposeIds, period, period, ct);

            foreach (var purpose in affectedPurposes)
            {
                var applicableRules = GetApplicableRulesForPurpose(relevantRules, purpose);
                var simulatedDelta = SumEntryDeltaForPurpose(periodEntries, purpose, entryContactCategories, applicableRules);
                if (simulatedDelta == 0m && !periodEntries.Any(x => MatchesPurpose(x, purpose, entryContactCategories, applicableRules)))
                {
                    continue;
                }

                var actualBefore = await GetActualBeforeAsync(ownerUserId, purpose.SourceType, purpose.SourceId, period, contacts, applicableRules, ct);
                var target = planned.GetPlanned(purpose.Id, period);
                var actualAfter = actualBefore + simulatedDelta;

                var rateBefore = CalculateRate(actualBefore, target);
                var rateAfter = CalculateRate(actualAfter, target);
                var delta = rateAfter - rateBefore;
                var hintType = ClassifyHint(target, actualBefore, actualAfter, delta);
                var reason = BuildReason(hintType, target, actualBefore, actualAfter, delta);

                hints.Add(new BudgetImpactHintDto(
                    purpose.Id,
                    purpose.Name,
                    period.ToString(),
                    hintType,
                    target,
                    actualBefore,
                    actualAfter,
                    rateBefore,
                    rateAfter,
                    delta,
                    reason));
            }
        }

        return hints;
    }

    private async Task<decimal> GetActualBeforeAsync(
        Guid ownerUserId,
        BudgetSourceType sourceType,
        Guid sourceId,
        BudgetPeriodKey period,
        IReadOnlyCollection<ContactCategoryInfo> contacts,
        IReadOnlyList<BudgetRulePatternInfo> applicableRules,
        CancellationToken ct)
    {
        var periodStart = period.StartDate.ToDateTime(TimeOnly.MinValue);
        var periodEnd = period.EndDate.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var query = _db.Postings.AsNoTracking().Where(x => x.BookingDate >= periodStart && x.BookingDate < periodEnd);

        if (sourceType == BudgetSourceType.Contact)
        {
            query = query.Where(x => x.ContactId == sourceId);
        }
        else if (sourceType == BudgetSourceType.SavingsPlan)
        {
            query = query.Where(x => x.SavingsPlanId == sourceId);
        }
        else if (sourceType == BudgetSourceType.ContactGroup)
        {
            var contactIds = contacts
                .Where(c => c.CategoryId == sourceId)
                .Select(c => (Guid)c.Id)
                .ToArray();
            if (contactIds.Length == 0) { return 0m; }
            query = query.Where(x => x.ContactId != null && contactIds.Contains(x.ContactId.Value));
        }

        var postings = await query
            .Select(x => new { x.Amount, x.Subject, x.Description })
            .ToListAsync(ct);

        var actual = postings
            .Where(x => applicableRules.Any(rule => BudgetRulePatternMatcher.MatchesPosting(x.Subject, x.Description, rule.PurposePattern, rule.PurposePatternIsRegex, RegexMatchTimeout)))
            .Sum(x => x.Amount);
        _logger.LogDebug("Budget impact actual before. Owner={OwnerUserId} SourceType={SourceType} SourceId={SourceId} Period={Period} Amount={Amount}",
            ownerUserId, sourceType, sourceId, period.ToString(), actual);
        return actual;
    }

    private static bool MatchesPurpose(
        StatementDraftEntry entry,
        BudgetPurposeInfo purpose,
        IReadOnlyDictionary<Guid, Guid?> entryContactCategories,
        IReadOnlyList<BudgetRulePatternInfo> applicableRules)
    {
        var matchesSource = purpose.SourceType switch
        {
            BudgetSourceType.Contact => entry.ContactId == purpose.SourceId,
            BudgetSourceType.SavingsPlan => entry.SavingsPlanId == purpose.SourceId,
            BudgetSourceType.ContactGroup => entry.ContactId.HasValue
                && entryContactCategories.TryGetValue(entry.Id, out var categoryId)
                && categoryId == purpose.SourceId,
            _ => false
        };

        if (!matchesSource)
        {
            return false;
        }

        return applicableRules.Any(rule => BudgetRulePatternMatcher.MatchesPosting(entry.Subject, entry.BookingDescription, rule.PurposePattern, rule.PurposePatternIsRegex, RegexMatchTimeout));
    }

    private static decimal SumEntryDeltaForPurpose(
        IReadOnlyCollection<StatementDraftEntry> entries,
        BudgetPurposeInfo purpose,
        IReadOnlyDictionary<Guid, Guid?> entryContactCategories,
        IReadOnlyList<BudgetRulePatternInfo> applicableRules)
    {
        return entries.Where(e => MatchesPurpose(e, purpose, entryContactCategories, applicableRules)).Sum(e => e.Amount);
    }

    private static IReadOnlyList<BudgetRulePatternInfo> GetApplicableRulesForPurpose(
        IReadOnlyList<BudgetRulePatternInfo> rules,
        BudgetPurposeInfo purpose)
    {
        return rules
            .Where(x => x.BudgetPurposeId == purpose.Id || (purpose.BudgetCategoryId.HasValue && x.BudgetCategoryId == purpose.BudgetCategoryId))
            .ToArray();
    }

    private static decimal CalculateRate(decimal actual, decimal target)
    {
        if (target == 0m) { return actual == 0m ? 0m : 1m; }
        return actual / target;
    }

    private static BudgetImpactHintType ClassifyHint(decimal target, decimal actualBefore, decimal actualAfter, decimal delta)
    {
        if (target == 0m && actualAfter != 0m) { return BudgetImpactHintType.Exceeded; }

        var targetAbs = Math.Abs(target);
        if (targetAbs == 0m) { return BudgetImpactHintType.Neutral; }

        var utilizationAfter = Math.Abs(actualAfter) / targetAbs;
        if (utilizationAfter > 1m) { return BudgetImpactHintType.Exceeded; }
        if (utilizationAfter >= AlmostExhaustedThreshold) { return BudgetImpactHintType.AlmostExhausted; }
        if (Math.Abs(delta) >= StrongChangeThreshold) { return BudgetImpactHintType.StronglyChanged; }

        return actualBefore != actualAfter ? BudgetImpactHintType.StronglyChanged : BudgetImpactHintType.Neutral;
    }

    private static string BuildReason(BudgetImpactHintType hintType, decimal target, decimal actualBefore, decimal actualAfter, decimal delta)
    {
        return hintType switch
        {
            BudgetImpactHintType.Exceeded => $"Budget_BudgetExceeded|{target.ToString("0.##", CultureInfo.InvariantCulture)}|{actualAfter.ToString("0.##", CultureInfo.InvariantCulture)}",
            BudgetImpactHintType.AlmostExhausted => $"Budget_BudgetAlmostExhausted|{target.ToString("0.##", CultureInfo.InvariantCulture)}|{actualAfter.ToString("0.##", CultureInfo.InvariantCulture)}",
            BudgetImpactHintType.StronglyChanged => $"Budget_StronglyChanged|{(delta * 100m).ToString("0.##", CultureInfo.InvariantCulture)}",
            _ => $"Budget_NoCriticalDeviation|{actualBefore.ToString("0.##", CultureInfo.InvariantCulture)}|{actualAfter.ToString("0.##", CultureInfo.InvariantCulture)}"
        };
    }

    private static string MapForFingerprint(BudgetImpactHintDto hint)
        => $"{hint.BudgetPurposeId}:{hint.BudgetPeriod}:{hint.HintType}:{hint.TargetValue:0.####}:{hint.ActualBefore:0.####}:{hint.ActualAfter:0.####}:{hint.Delta:0.####}";

    private static string MapForFingerprint(BookingImpactSummaryItemDto item)
        => $"{item.BudgetPurposeId}:{item.BudgetPeriod}:{item.HintType}:{item.TargetValue:0.####}:{item.ActualBefore:0.####}:{item.ActualAfter:0.####}:{item.Delta:0.####}";

    private static string CreateFingerprint(IEnumerable<string> rows)
    {
        var payload = string.Join("|", rows.OrderBy(x => x, StringComparer.Ordinal));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash);
    }
}
