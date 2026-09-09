using FinanceManager.Shared.Dtos.Budget;

namespace FinanceManager.Domain.Budget.ReportCalculation;

/// <summary>
/// Aggregate root that calculates a budget report for a given period. Drives the calculation through
/// five phases: Initialization (constructor), Planning (<see cref="SetPlanung"/>), Posting Assignment
/// (<see cref="AddPosting"/>), Finish (<see cref="Finish"/>) and Output (<see cref="GetCurrentResult"/>,
/// <see cref="GetCumulativeResult"/>).
/// </summary>
/// <remarks>
/// <see cref="SetPlanung"/> accepts the lightweight <c>FinanceManager.Shared.Dtos.Budget</c> DTOs
/// (<see cref="BudgetCategoryDto"/>, <see cref="BudgetPurposeDto"/>, <see cref="BudgetRuleDto"/>) rather
/// than the <c>FinanceManager.Domain.Budget</c> entities, because those DTOs already carry the stable,
/// persisted identifiers required to correlate the calculation result back to the user's real
/// categories/purposes, and because no repository access is introduced for this calculation.
/// A <see cref="BudgetRule"/> whose period spans multiple months is attributed, for display and summation
/// purposes, to a single "home month" only, so that summing months never double-counts a budgeted amount.
/// For a rule anchored on day 1 (Yearly always is, but Monthly/Quarterly/CustomMonths can be too) this is
/// the month containing the period's start - day-1 anchors align with calendar boundaries, so a
/// day-1-anchored quarterly rule for Jan-Mar is unambiguously shown in January. A rule anchored on any
/// other day produces occurrences that straddle two calendar months without a natural "which one" answer
/// (e.g. a monthly rule anchored on the 11th produces a period like 11 Jul - 10 Aug); such a rule is instead
/// homed to the month containing the period's END, since the posting that fulfills the occurrence is
/// expected to land in the month the period closes in, not the one it opens in (see
/// <c>ExpandRuleOccurrences</c>'s <c>NaturalHomeMonth</c>). Matching of actual postings against a rule is
/// independent of this "home month" and instead uses the rule's own period window.
/// <para>
/// The class name <c>Budgetbericht</c> and the constructor parameter names (<c>betrachtungsDatum</c>,
/// <c>anzahlMonate</c>, <c>intervall</c>) are intentionally German: the class is explicitly named
/// <c>Budgetbericht</c> in the customer's original requirement (<c>issue.md</c>, Akzeptanzkriterien),
/// and <see cref="SetPlanung"/> plus these constructor parameter names are an explicit design decision
/// documented in the implementation plan (<c>plan.md</c>, "Designentscheidungen" / "Konstruktor-Parameter
/// für Budgetbericht"). This is a deliberate, reviewed deviation from the codebase's otherwise English
/// naming convention, not an oversight.
/// </para>
/// </remarks>
public sealed class Budgetbericht
{
    private const string UncategorizedCategoryName = "Uncategorized";
    private const decimal ImplicitTotalBudgetAssignmentCapacity = 1_000_000_000m;

    private readonly DateOnly _periodStart;
    private readonly DateOnly _periodEnd;
    private readonly BudgetReportInterval _interval;
    private readonly BudgetReportDateBasis _dateBasis;

    private readonly List<MonthlyBudgetResult> _monthlyResults = new();
    private readonly Dictionary<DateOnly, MonthlyBudgetResult> _monthlyResultsByMonth = new();

    private readonly Dictionary<Guid, BudgetSource> _purposeSources = new();
    private readonly Dictionary<Guid, List<MonthlyBudgetExpectationPosting>> _purposeCandidatePostings = new();
    private readonly Dictionary<Guid, List<BudgetSource>> _categorySources = new();
    private readonly Dictionary<Guid, List<MonthlyBudgetExpectationPosting>> _categoryCandidatePostings = new();

    private bool _planningDone;
    private bool _finished;

    /// <summary>
    /// Initializes a new <see cref="Budgetbericht"/> for the given period, creating one empty
    /// <see cref="MonthlyBudgetResult"/> per month.
    /// </summary>
    /// <param name="betrachtungsDatum">Any date within the first month of the report period.</param>
    /// <param name="anzahlMonate">Number of months to include in the report period. Must be greater than zero.</param>
    /// <param name="intervall">Aggregation interval used by <see cref="GetCumulativeResult"/>.</param>
    /// <param name="dateBasis">Date basis used to determine which month an actual posting belongs to.</param>
    /// <exception cref="BudgetReportCalculationException">Thrown when <paramref name="anzahlMonate"/> is not greater than zero or <paramref name="betrachtungsDatum"/> is not a valid date.</exception>
    public Budgetbericht(DateOnly betrachtungsDatum, int anzahlMonate, BudgetReportInterval intervall, BudgetReportDateBasis dateBasis)
    {
        if (anzahlMonate <= 0)
        {
            throw new BudgetReportCalculationException("AnzahlMonate must be greater than zero.");
        }

        if (betrachtungsDatum == default)
        {
            throw new BudgetReportCalculationException("BetrachtungsDatum must be a valid date.");
        }

        _periodStart = new DateOnly(betrachtungsDatum.Year, betrachtungsDatum.Month, 1);
        _periodEnd = _periodStart.AddMonths(anzahlMonate).AddDays(-1);
        _interval = intervall;
        _dateBasis = dateBasis;

        for (var i = 0; i < anzahlMonate; i++)
        {
            var monthStart = _periodStart.AddMonths(i);
            var result = new MonthlyBudgetResult(monthStart.ToDateTime(TimeOnly.MinValue));
            _monthlyResults.Add(result);
            _monthlyResultsByMonth[monthStart] = result;
        }
    }

    /// <summary>
    /// Gets the calculation results for each month of the report period, in chronological order.
    /// </summary>
    public IReadOnlyList<MonthlyBudgetResult> MonthlyResults => _monthlyResults;

    /// <summary>
    /// Builds the budget expectations for the report period from the given categories, purposes and rules.
    /// Must be called exactly once, before any call to <see cref="AddPosting"/>.
    /// </summary>
    /// <param name="categories">The budget categories owned by the user.</param>
    /// <param name="purposes">The budget purposes owned by the user.</param>
    /// <param name="rules">All budget rules (purpose-level and category-level) owned by the user.</param>
    /// <exception cref="BudgetReportCalculationException">Thrown when planning has already been executed, or a rule has an invalid interval configuration.</exception>
    public void SetPlanung(IReadOnlyList<BudgetCategoryDto> categories, IReadOnlyList<BudgetPurposeDto> purposes, IReadOnlyList<BudgetRuleDto> rules)
    {
        ArgumentNullException.ThrowIfNull(categories);
        ArgumentNullException.ThrowIfNull(purposes);
        ArgumentNullException.ThrowIfNull(rules);

        if (_planningDone)
        {
            throw new BudgetReportCalculationException("SetPlanung has already been executed for this Budgetbericht.");
        }

        foreach (var rule in rules)
        {
            ValidateRule(rule);
        }

        BuildSourceIndexes(categories, purposes);

        var (purposeExpectationPostingsByHomeMonth, categoryExpectationPostingsByHomeMonth) =
            ExpandRulesToExpectationPostings(rules, purposes);

        BuildMonthlyExpectationGroups(categories, purposes, purposeExpectationPostingsByHomeMonth, categoryExpectationPostingsByHomeMonth);

        _planningDone = true;
    }

    /// <summary>
    /// Assigns a single actual posting to the matching budget expectation, or records it as unbudgeted
    /// (or cost-neutral, when it carries a mirror <c>GroupId</c>) when no expectation matches.
    /// </summary>
    /// <param name="posting">The posting to assign.</param>
    /// <param name="dateBasis">Date basis used to determine which month the posting belongs to.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="posting"/> is <c>null</c>.</exception>
    /// <exception cref="BudgetReportCalculationException">Thrown when <see cref="Finish"/> has already been called.</exception>
    public void AddPosting(MonthlyBudgetRealization posting, BudgetReportDateBasis dateBasis)
    {
        ArgumentNullException.ThrowIfNull(posting);

        if (_finished)
        {
            throw new BudgetReportCalculationException("Cannot add postings after Finish() has been called.");
        }

        var postingDate = GetPostingDate(posting, dateBasis);
        var postingMonth = new DateOnly(postingDate.Year, postingDate.Month, 1);

        if (!_monthlyResultsByMonth.TryGetValue(postingMonth, out var monthResult))
        {
            return;
        }

        var candidates = FindCandidateExpectationPostings(posting, postingDate);
        if (candidates.Count == 0)
        {
            // A posting that matches some purpose's source/period/pattern but not its ExactPostings sign
            // is recorded against that purpose (visible there, not valued) and must NOT also be routed to
            // the month's top-level Unbudgeted/CostNeutral buckets - those are reserved for postings that
            // matched no budget purpose whatsoever.
            if (!RecordUnvaluedMatches(posting, postingDate))
            {
                RouteUnmatchedPosting(monthResult, posting);
            }

            return;
        }

        AssignSequentially(candidates, posting);
    }

    /// <summary>
    /// Finalizes the calculation. For budget purposes with several competing occurrences (several
    /// <see cref="BudgetRule"/> entries, e.g. several total budgets for one purpose), re-assigns the
    /// already collected postings in posting-date order, so that priority is resolved chronologically
    /// rather than by the order <see cref="AddPosting"/> happened to be called in.
    /// </summary>
    /// <exception cref="BudgetReportCalculationException">Thrown when <see cref="Finish"/> has already been called.</exception>
    public void Finish()
    {
        if (_finished)
        {
            throw new BudgetReportCalculationException("Finish() has already been called for this Budgetbericht.");
        }

        foreach (var monthResult in _monthlyResults)
        {
            foreach (var group in monthResult.ExpectationGroups)
            {
                foreach (var expectation in group.DirectExpectations.Concat(group.Purposes))
                {
                    ReconcileMultiOccurrenceExpectation(expectation);
                }
            }
        }

        _finished = true;
    }

    /// <summary>
    /// Returns the detail rows for the report. When <paramref name="month"/> is given, only that month
    /// is included; otherwise all months of the report period are aggregated into a single set of rows.
    /// </summary>
    /// <param name="month">Optional month to restrict the result to.</param>
    /// <returns>The report rows, including category, purpose, subtotal, unbudgeted, cost-neutral and total rows.</returns>
    public BudgetReportEntry[] GetCurrentResult(DateOnly? month = null)
    {
        var monthsToInclude = month.HasValue
            ? _monthlyResults.Where(m => DateOnly.FromDateTime(m.Month) == new DateOnly(month.Value.Year, month.Value.Month, 1)).ToList()
            : _monthlyResults;

        var entries = new List<BudgetReportEntry>();
        var (totalBudgeted, totalActual) = BuildCategoryEntries(monthsToInclude, entries);
        BuildSummaryEntries(monthsToInclude, entries, totalBudgeted, totalActual);

        return entries.ToArray();
    }

    // Builds the Category/Purpose/Subtotal rows (one group of rows per budget category present in
    // monthsToInclude), appending them to entries, and returns the total budgeted/actual amount summed
    // across all categories - used by BuildSummaryEntries for the Total row.
    private static (decimal TotalBudgeted, decimal TotalActual) BuildCategoryEntries(
        IReadOnlyList<MonthlyBudgetResult> monthsToInclude,
        List<BudgetReportEntry> entries)
    {
        var allGroups = monthsToInclude.SelectMany(m => m.ExpectationGroups).ToList();
        var categoryIds = allGroups.Select(g => g.BudgetCategoryId).Distinct().ToList();
        var showCategoryRows = categoryIds.Count > 1 || categoryIds.Any(id => id != Guid.Empty);

        decimal totalBudgeted = 0m;
        decimal totalActual = 0m;

        foreach (var categoryId in categoryIds)
        {
            var groupsForCategory = allGroups.Where(g => g.BudgetCategoryId == categoryId).ToList();
            var categoryName = groupsForCategory[0].CategoryName;

            // Each MonthlyBudgetExpectationGroup (and therefore each MonthlyBudgetExpectation) is scoped to
            // a single month, so a multi-month report period produces one expectation object per month for
            // the same purpose/direct-category-rule. These are merged here by budget purpose id (Guid.Empty
            // for the category's direct expectation) into a single row per purpose/direct-rule spanning the
            // whole included range, so a multi-month GetCurrentResult() doesn't emit one duplicate "Purpose"
            // row per month for the same purpose.
            var directExpectations = MergeAcrossMonths(groupsForCategory.SelectMany(g => g.DirectExpectations));
            var purposeExpectations = MergeAcrossMonths(groupsForCategory.SelectMany(g => g.Purposes));

            var categoryBudgeted = directExpectations.Sum(e => e.SumExpectedAmount) + purposeExpectations.Sum(e => e.SumExpectedAmount);
            var categoryActual = directExpectations.Sum(e => e.SumActualAmount) + purposeExpectations.Sum(e => e.SumActualAmount);

            if (showCategoryRows)
            {
                entries.Add(CreateEntry(BudgetReportEntryRowKind.Category, categoryName, categoryBudgeted, categoryActual, Array.Empty<MonthlyBudgetRealization>(), categoryId));
            }

            foreach (var direct in directExpectations)
            {
                entries.Add(CreateEntry(BudgetReportEntryRowKind.Purpose, direct.Name, direct.SumExpectedAmount, direct.SumActualAmount, direct.Postings, categoryId, direct.BudgetPurposeId));
            }

            foreach (var purpose in purposeExpectations)
            {
                entries.Add(CreateEntry(BudgetReportEntryRowKind.Purpose, purpose.Name, purpose.SumExpectedAmount, purpose.SumActualAmount, purpose.Postings, categoryId, purpose.BudgetPurposeId));
            }

            entries.Add(CreateEntry(BudgetReportEntryRowKind.Subtotal, categoryName, categoryBudgeted, categoryActual, Array.Empty<MonthlyBudgetRealization>(), categoryId));

            totalBudgeted += categoryBudgeted;
            totalActual += categoryActual;
        }

        return (totalBudgeted, totalActual);
    }

    // Appends the Unbudgeted, CostNeutral and Total rows to entries, given the category totals already
    // accumulated by BuildCategoryEntries.
    private static void BuildSummaryEntries(
        IReadOnlyList<MonthlyBudgetResult> monthsToInclude,
        List<BudgetReportEntry> entries,
        decimal totalBudgeted,
        decimal totalActual)
    {
        var unbudgeted = monthsToInclude.SelectMany(m => m.UnbudgetedPostings).ToArray();
        var costNeutral = monthsToInclude.SelectMany(m => m.CostNeutralPostings).ToArray();
        var unbudgetedSum = unbudgeted.Sum(p => p.Amount);
        var costNeutralSum = costNeutral.Sum(p => p.Amount);

        entries.Add(CreateEntry(BudgetReportEntryRowKind.Unbudgeted, "Unbudgeted", 0m, unbudgetedSum, unbudgeted));
        entries.Add(CreateEntry(BudgetReportEntryRowKind.CostNeutral, "CostNeutral", 0m, costNeutralSum, costNeutral));

        totalActual += unbudgetedSum + costNeutralSum;
        entries.Add(CreateEntry(BudgetReportEntryRowKind.Total, "Total", totalBudgeted, totalActual, Array.Empty<MonthlyBudgetRealization>()));
    }

    /// <summary>
    /// Returns the report aggregated per interval bucket (month, quarter or year, depending on the
    /// interval given to the constructor).
    /// </summary>
    /// <remarks>
    /// This method is part of the customer-mandated public API of <see cref="Budgetbericht"/> (see
    /// <c>issue.md</c>, Akzeptanzkriterien: "GetCummulativeResult liefert korrekte Intervall-Zusammenfassungen
    /// für Monat, Quartal und Jahr."). <c>BudgetReportService.GetReportAsync</c> uses it to build the period
    /// table returned by <c>BudgetReportsController.GetAsync</c> (the <see cref="Budgetbericht"/> instance
    /// used there is always constructed with a monthly interval, so each bucket corresponds to exactly one
    /// calendar month, matching the report period table's granularity). It is also covered directly by unit
    /// tests for the Quarter/Year bucketing that no current production call site exercises yet.
    /// </remarks>
    /// <returns>One <see cref="BudgetReportCumulativeEntry"/> per interval bucket, in chronological order.</returns>
    public BudgetReportCumulativeEntry[] GetCumulativeResult()
    {
        var buckets = new List<(DateOnly Start, string Label, List<MonthlyBudgetResult> Months)>();

        foreach (var monthResult in _monthlyResults)
        {
            var monthStart = DateOnly.FromDateTime(monthResult.Month);
            var bucketStart = GetBucketStart(monthStart);

            var bucketIndex = buckets.FindIndex(b => b.Start == bucketStart);
            if (bucketIndex < 0)
            {
                buckets.Add((bucketStart, GetBucketLabel(bucketStart), new List<MonthlyBudgetResult>()));
                bucketIndex = buckets.Count - 1;
            }

            buckets[bucketIndex].Months.Add(monthResult);
        }

        var result = new List<BudgetReportCumulativeEntry>();
        foreach (var bucket in buckets.OrderBy(b => b.Start))
        {
            decimal budgeted = 0m;
            decimal actual = 0m;

            foreach (var monthResult in bucket.Months)
            {
                foreach (var expectation in monthResult.ExpectationGroups.SelectMany(g => g.DirectExpectations.Concat(g.Purposes)))
                {
                    budgeted += expectation.SumExpectedAmount;
                    actual += expectation.SumActualAmount;
                }

                actual += monthResult.UnbudgetedPostings.Sum(p => p.Amount);
                actual += monthResult.CostNeutralPostings.Sum(p => p.Amount);
            }

            var (deviation, deviationPct) = CalculateDeviation(budgeted, actual);

            result.Add(new BudgetReportCumulativeEntry
            {
                IntervalStartDate = bucket.Start,
                IntervalLabel = bucket.Label,
                BudgetedAmount = budgeted,
                ActualAmount = actual,
                Deviation = deviation,
                DeviationPercentage = deviationPct
            });
        }

        return result.ToArray();
    }

    private DateOnly GetBucketStart(DateOnly monthStart) => _interval switch
    {
        BudgetReportInterval.Quarter => new DateOnly(monthStart.Year, ((monthStart.Month - 1) / 3 * 3) + 1, 1),
        BudgetReportInterval.Year => new DateOnly(monthStart.Year, 1, 1),
        _ => monthStart
    };

    private string GetBucketLabel(DateOnly bucketStart) => _interval switch
    {
        BudgetReportInterval.Quarter => $"Q{((bucketStart.Month - 1) / 3) + 1}/{bucketStart.Year}",
        BudgetReportInterval.Year => bucketStart.Year.ToString(),
        _ => $"{bucketStart.Month:D2}/{bucketStart.Year}"
    };

    // Populates _purposeSources/_categorySources (which Contact/ContactGroup/SavingsPlan a purpose or
    // category matches against) and initializes the corresponding candidate-posting lists.
    private void BuildSourceIndexes(IReadOnlyList<BudgetCategoryDto> categories, IReadOnlyList<BudgetPurposeDto> purposes)
    {
        foreach (var purpose in purposes)
        {
            _purposeSources[purpose.Id] = new BudgetSource(purpose.SourceType, purpose.SourceId);
            _purposeCandidatePostings[purpose.Id] = new List<MonthlyBudgetExpectationPosting>();
        }

        foreach (var category in categories)
        {
            var sources = purposes
                .Where(p => p.BudgetCategoryId == category.Id)
                .Select(p => new BudgetSource(p.SourceType, p.SourceId))
                .Distinct()
                .ToList();
            _categorySources[category.Id] = sources;
            _categoryCandidatePostings[category.Id] = new List<MonthlyBudgetExpectationPosting>();
        }
    }

    // Expands every rule's occurrences within the report period into MonthlyBudgetExpectationPosting
    // instances, registers them as candidates for posting assignment, and groups them by "home month" (see
    // ExpandRuleOccurrences' NaturalHomeMonth, clamped to the report's own month range) for later use when
    // building the monthly expectation groups.
    private (
        Dictionary<(Guid PurposeId, DateOnly Month), List<MonthlyBudgetExpectationPosting>> PurposeExpectationPostingsByHomeMonth,
        Dictionary<(Guid CategoryId, DateOnly Month), List<MonthlyBudgetExpectationPosting>> CategoryExpectationPostingsByHomeMonth
        ) ExpandRulesToExpectationPostings(IReadOnlyList<BudgetRuleDto> rules, IReadOnlyList<BudgetPurposeDto> purposes)
    {
        var purposeExpectationPostingsByHomeMonth = new Dictionary<(Guid PurposeId, DateOnly Month), List<MonthlyBudgetExpectationPosting>>();
        var categoryExpectationPostingsByHomeMonth = new Dictionary<(Guid CategoryId, DateOnly Month), List<MonthlyBudgetExpectationPosting>>();
        var lastMonth = new DateOnly(_periodEnd.Year, _periodEnd.Month, 1);

        var creationOrder = 0;
        foreach (var rule in rules.OrderBy(r => r.StartDate))
        {
            var valuationType = ResolveValuationType(rule, purposes);
            var occurrences = ExpandRuleOccurrences(rule, _periodStart, _periodEnd).ToList();

            // An occurrence whose natural home month (see ExpandRuleOccurrences) falls outside the report's
            // own month range can still be the right match for a posting dated within the report period -
            // e.g. a monthly rule anchored on the 11th produces an occurrence spanning two calendar months,
            // and the report may only cover one of them, or a quarterly rule's cycle straddles the report's
            // own start because the report range does not happen to align with the rule's own cycle
            // boundary. Such an occurrence is homed to the nearest report boundary month instead of being
            // dropped. Whether it is also excluded from that month's budgeted-amount sum (see
            // MonthlyBudgetExpectationPosting.BudgetedDisplayAmount) depends on whether this rule has ANY
            // other, properly (non-clamped) homed occurrence elsewhere in the report: if it does, counting
            // this boundary occurrence too would double the budgeted amount for a cycle that is otherwise
            // already fully represented. If it does not - e.g. the rule only started shortly before the
            // report's end, so this is its only occurrence anywhere near the report - there is nothing to
            // double-count against, and dropping it would make the rule vanish from the report entirely, so
            // it keeps its budgeted amount.
            var homeMonths = ComputeHomeMonths(occurrences, lastMonth);
            var hasNaturalOccurrence = HasNaturalOccurrence(occurrences, homeMonths);

            for (var i = 0; i < occurrences.Count; i++)
            {
                var homeMonth = homeMonths[i];
                if (!_monthlyResultsByMonth.ContainsKey(homeMonth))
                {
                    continue;
                }

                var expectationPosting = CreateExpectationPosting(rule, valuationType, creationOrder++, occurrences[i], homeMonth, hasNaturalOccurrence);
                RegisterExpectationPosting(rule, homeMonth, expectationPosting, purposeExpectationPostingsByHomeMonth, categoryExpectationPostingsByHomeMonth);
            }
        }

        return (purposeExpectationPostingsByHomeMonth, categoryExpectationPostingsByHomeMonth);
    }

    private List<DateOnly> ComputeHomeMonths(
        IReadOnlyList<(DateOnly PeriodStart, DateOnly PeriodEnd, DateOnly NaturalHomeMonth)> occurrences,
        DateOnly lastMonth) =>
        occurrences
            .Select(o => ClampHomeMonth(o.NaturalHomeMonth, lastMonth))
            .ToList();

    private DateOnly ClampHomeMonth(DateOnly naturalHomeMonth, DateOnly lastMonth)
    {
        if (naturalHomeMonth < _periodStart)
        {
            return _periodStart;
        }

        if (naturalHomeMonth > lastMonth)
        {
            return lastMonth;
        }

        return naturalHomeMonth;
    }

    private static bool HasNaturalOccurrence(
        IReadOnlyList<(DateOnly PeriodStart, DateOnly PeriodEnd, DateOnly NaturalHomeMonth)> occurrences,
        IReadOnlyList<DateOnly> homeMonths) =>
        Enumerable.Range(0, occurrences.Count).Any(idx => homeMonths[idx] == occurrences[idx].NaturalHomeMonth);

    private static MonthlyBudgetExpectationPosting CreateExpectationPosting(
        BudgetRuleDto rule,
        BudgetValuationType valuationType,
        int creationOrder,
        (DateOnly PeriodStart, DateOnly PeriodEnd, DateOnly NaturalHomeMonth) occurrence,
        DateOnly homeMonth,
        bool hasNaturalOccurrence)
    {
        var (periodStart, periodEnd, naturalHomeMonth) = occurrence;
        var isCarriedOver = homeMonth != naturalHomeMonth && hasNaturalOccurrence;
        return new MonthlyBudgetExpectationPosting(
            rule.Amount,
            valuationType,
            rule.StartDate,
            creationOrder,
            new RuleOccurrencePeriod(periodStart, periodEnd),
            new PurposeMatchPattern(rule.PurposePattern, rule.UseRegex),
            isCarriedOverAcrossReportBoundary: isCarriedOver);
    }

    private void RegisterExpectationPosting(
        BudgetRuleDto rule,
        DateOnly homeMonth,
        MonthlyBudgetExpectationPosting expectationPosting,
        Dictionary<(Guid PurposeId, DateOnly Month), List<MonthlyBudgetExpectationPosting>> purposeExpectationPostingsByHomeMonth,
        Dictionary<(Guid CategoryId, DateOnly Month), List<MonthlyBudgetExpectationPosting>> categoryExpectationPostingsByHomeMonth)
    {
        if (rule.BudgetPurposeId.HasValue)
        {
            RegisterPurposePosting(rule.BudgetPurposeId.Value, homeMonth, expectationPosting, purposeExpectationPostingsByHomeMonth);
            return;
        }

        if (rule.BudgetCategoryId.HasValue)
        {
            RegisterCategoryPosting(rule.BudgetCategoryId.Value, homeMonth, expectationPosting, categoryExpectationPostingsByHomeMonth);
        }
    }

    private void RegisterPurposePosting(
        Guid purposeId,
        DateOnly homeMonth,
        MonthlyBudgetExpectationPosting expectationPosting,
        Dictionary<(Guid PurposeId, DateOnly Month), List<MonthlyBudgetExpectationPosting>> purposeExpectationPostingsByHomeMonth)
    {
        AddToHomeMonth(purposeExpectationPostingsByHomeMonth, (purposeId, homeMonth), expectationPosting);
        if (_purposeCandidatePostings.TryGetValue(purposeId, out var purposeCandidates))
        {
            purposeCandidates.Add(expectationPosting);
        }
    }

    private void RegisterCategoryPosting(
        Guid categoryId,
        DateOnly homeMonth,
        MonthlyBudgetExpectationPosting expectationPosting,
        Dictionary<(Guid CategoryId, DateOnly Month), List<MonthlyBudgetExpectationPosting>> categoryExpectationPostingsByHomeMonth)
    {
        AddToHomeMonth(categoryExpectationPostingsByHomeMonth, (categoryId, homeMonth), expectationPosting);
        if (_categoryCandidatePostings.TryGetValue(categoryId, out var categoryCandidates))
        {
            categoryCandidates.Add(expectationPosting);
        }
    }

    private static BudgetValuationType ResolveValuationType(BudgetRuleDto rule, IReadOnlyList<BudgetPurposeDto> purposes)
    {
        if (!rule.BudgetPurposeId.HasValue)
        {
            return BudgetValuationType.ExactPostings;
        }

        var purpose = purposes.FirstOrDefault(p => p.Id == rule.BudgetPurposeId.Value);
        return purpose?.ValuationType ?? BudgetValuationType.ExactPostings;
    }

    // Builds the per-month MonthlyBudgetExpectationGroup tree (one group per real category, plus the
    // virtual "Uncategorized" group) from the expectation postings produced by ExpandRulesToExpectationPostings.
    private void BuildMonthlyExpectationGroups(
        IReadOnlyList<BudgetCategoryDto> categories,
        IReadOnlyList<BudgetPurposeDto> purposes,
        Dictionary<(Guid PurposeId, DateOnly Month), List<MonthlyBudgetExpectationPosting>> purposeExpectationPostingsByHomeMonth,
        Dictionary<(Guid CategoryId, DateOnly Month), List<MonthlyBudgetExpectationPosting>> categoryExpectationPostingsByHomeMonth)
    {
        var uncategorizedPurposes = purposes.Where(p => !p.BudgetCategoryId.HasValue).OrderBy(p => p.Name).ToList();
        var purposeIdsWithOwnRules = purposeExpectationPostingsByHomeMonth.Keys
            .Select(x => x.PurposeId)
            .ToHashSet();

        foreach (var monthStart in _monthlyResultsByMonth.Keys.OrderBy(m => m))
        {
            var monthResult = _monthlyResultsByMonth[monthStart];
            var groupsByCategory = new List<MonthlyBudgetExpectationGroup>();

            foreach (var category in categories.OrderBy(c => c.Name))
            {
                var group = new MonthlyBudgetExpectationGroup(category.Id, category.Name);

                if (categoryExpectationPostingsByHomeMonth.TryGetValue((category.Id, monthStart), out var directPostings))
                {
                    var directExpectation = new MonthlyBudgetExpectation(null, category.Name);
                    foreach (var directPosting in directPostings)
                    {
                        directExpectation.AddPosting(directPosting);
                    }

                    group.AddDirectExpectation(directExpectation);
                }

                foreach (var purpose in purposes.Where(p => p.BudgetCategoryId == category.Id).OrderBy(p => p.Name))
                {
                    group.AddPurposeExpectation(BuildPurposeExpectation(
                        purpose,
                        monthStart,
                        purposeExpectationPostingsByHomeMonth,
                        hasOwnRules: purposeIdsWithOwnRules.Contains(purpose.Id)));
                }

                groupsByCategory.Add(group);
            }

            if (uncategorizedPurposes.Count > 0)
            {
                var uncategorizedGroup = new MonthlyBudgetExpectationGroup(Guid.Empty, UncategorizedCategoryName);
                foreach (var purpose in uncategorizedPurposes)
                {
                    uncategorizedGroup.AddPurposeExpectation(BuildPurposeExpectation(
                        purpose,
                        monthStart,
                        purposeExpectationPostingsByHomeMonth,
                        hasOwnRules: purposeIdsWithOwnRules.Contains(purpose.Id)));
                }

                groupsByCategory.Add(uncategorizedGroup);
            }

            foreach (var group in groupsByCategory)
            {
                monthResult.AddExpectationGroup(group);
            }
        }
    }

    private MonthlyBudgetExpectation BuildPurposeExpectation(
        BudgetPurposeDto purpose,
        DateOnly monthStart,
        Dictionary<(Guid PurposeId, DateOnly Month), List<MonthlyBudgetExpectationPosting>> purposeExpectationPostingsByHomeMonth,
        bool hasOwnRules)
    {
        var expectation = new MonthlyBudgetExpectation(purpose.Id, purpose.Name);
        if (purposeExpectationPostingsByHomeMonth.TryGetValue((purpose.Id, monthStart), out var postings))
        {
            foreach (var posting in postings)
            {
                expectation.AddPosting(posting);
            }

            return expectation;
        }

        // A purpose without any own rules still needs a monthly fallback expectation so
        // category-level budgets can be broken down into purpose rows in the report details table.
        if (!hasOwnRules && _purposeCandidatePostings.TryGetValue(purpose.Id, out var purposeCandidates))
        {
            var fallbackPosting = CreateImplicitPurposePosting(monthStart);
            purposeCandidates.Add(fallbackPosting);
            expectation.AddPosting(fallbackPosting);
        }

        return expectation;
    }

    private static MonthlyBudgetExpectationPosting CreateImplicitPurposePosting(DateOnly monthStart)
    {
        var periodEnd = monthStart.AddMonths(1).AddDays(-1);
        return new MonthlyBudgetExpectationPosting(
            ImplicitTotalBudgetAssignmentCapacity,
            BudgetValuationType.TotalBudget,
            DateOnly.MinValue,
            int.MinValue,
            new RuleOccurrencePeriod(monthStart, periodEnd),
            new PurposeMatchPattern(null, false),
            isCarriedOverAcrossReportBoundary: true);
    }

    private static void AddToHomeMonth<TKey>(Dictionary<TKey, List<MonthlyBudgetExpectationPosting>> map, TKey key, MonthlyBudgetExpectationPosting posting) where TKey : notnull
    {
        if (!map.TryGetValue(key, out var list))
        {
            list = new List<MonthlyBudgetExpectationPosting>();
            map[key] = list;
        }

        list.Add(posting);
    }

    // Assigns 'posting' across 'candidates' (all belonging to the same purpose/category, ordered by
    // priority) in order, filling each occurrence's remaining capacity before moving to the next. Any
    // amount left over once every candidate is exhausted still originated from a posting that matched
    // this purpose/category — it is therefore recorded against the last (lowest-priority) candidate via
    // AddUnvaluedMatch rather than routed to the month's top-level Unbudgeted/CostNeutral buckets, which
    // are reserved for postings that matched no budget at all (see RouteUnmatchedPosting).
    private static void AssignSequentially(List<MonthlyBudgetExpectationPosting> candidates, MonthlyBudgetRealization posting)
    {
        var remaining = posting;
        foreach (var candidate in candidates)
        {
            if (remaining.Amount == 0m)
            {
                break;
            }

            var leftover = candidate.Assign(remaining);
            remaining = remaining with { Amount = leftover };
        }

        if (remaining.Amount != 0m)
        {
            candidates[^1].AddUnvaluedMatch(remaining);
        }
    }

    private void ReconcileMultiOccurrenceExpectation(MonthlyBudgetExpectation expectation)
    {
        if (expectation.Postings.Count <= 1)
        {
            return;
        }

        var allAssigned = expectation.Postings.SelectMany(p => p.AssignedPostings).ToList();
        if (allAssigned.Count == 0)
        {
            return;
        }

        foreach (var occurrence in expectation.Postings)
        {
            occurrence.Reset();
        }

        var orderedOccurrences = expectation.Postings
            .OrderBy(p => p.StartDate)
            .ThenBy(p => p.CreationOrder)
            .ToList();

        var sortedAssigned = allAssigned
            .OrderBy(a => GetPostingDate(a, _dateBasis))
            .ToList();

        foreach (var assigned in sortedAssigned)
        {
            AssignSequentially(orderedOccurrences, assigned);
        }
    }

    private List<MonthlyBudgetExpectationPosting> FindCandidateExpectationPostings(MonthlyBudgetRealization posting, DateOnly postingDate)
    {
        var candidates = new List<MonthlyBudgetExpectationPosting>();

        foreach (var (purposeId, source) in _purposeSources)
        {
            if (!MatchesSource(source, posting))
            {
                continue;
            }

            foreach (var candidate in _purposeCandidatePostings[purposeId])
            {
                if (IsEligible(candidate, posting, postingDate))
                {
                    candidates.Add(candidate);
                }
            }
        }

        foreach (var (categoryId, sources) in _categorySources)
        {
            if (!sources.Any(s => MatchesSource(s, posting)))
            {
                continue;
            }

            foreach (var candidate in _categoryCandidatePostings[categoryId])
            {
                if (IsEligible(candidate, posting, postingDate))
                {
                    candidates.Add(candidate);
                }
            }
        }

        return candidates
            .OrderBy(c => c.StartDate)
            .ThenBy(c => c.CreationOrder)
            .ToList();
    }

    // Records, for every purpose whose source matches the posting, any ExactPostings-valued expectation
    // occurrence whose period and purpose pattern match the (otherwise fully unmatched) posting but whose
    // sign does not — so it can still be shown against the purpose (with IsValuedForBudgetPurpose = false
    // at the DTO layer) even though it is not counted toward the purpose's actual amount. Returns true when
    // at least one such match was recorded, so the caller can skip routing the posting to the month's
    // top-level Unbudgeted/CostNeutral buckets as well (a posting shown at a purpose must not also appear
    // there — those buckets are reserved for postings that matched no budget purpose whatsoever).
    private bool RecordUnvaluedMatches(MonthlyBudgetRealization posting, DateOnly postingDate)
    {
        var recorded = false;

        foreach (var (purposeId, source) in _purposeSources)
        {
            if (!MatchesSource(source, posting))
            {
                continue;
            }

            if (!_purposeCandidatePostings.TryGetValue(purposeId, out var candidates))
            {
                continue;
            }

            foreach (var candidate in candidates)
            {
                if (candidate.BudgetType != BudgetValuationType.ExactPostings)
                {
                    continue;
                }

                if (postingDate < candidate.PeriodStart || postingDate > candidate.PeriodEnd)
                {
                    continue;
                }

                if (!BudgetRulePatternMatcher.MatchesPosting(posting.Purpose, posting.Description, candidate.PurposePattern, candidate.PurposePatternIsRegex))
                {
                    continue;
                }

                candidate.AddUnvaluedMatch(posting);
                recorded = true;
            }
        }

        return recorded;
    }

    private static bool IsEligible(MonthlyBudgetExpectationPosting candidate, MonthlyBudgetRealization posting, DateOnly postingDate)
    {
        if (postingDate < candidate.PeriodStart || postingDate > candidate.PeriodEnd)
        {
            return false;
        }

        if (!BudgetRulePatternMatcher.MatchesPosting(posting.Purpose, posting.Description, candidate.PurposePattern, candidate.PurposePatternIsRegex))
        {
            return false;
        }

        if (candidate.BudgetType == BudgetValuationType.ExactPostings && Math.Sign(posting.Amount) != Math.Sign(candidate.Amount))
        {
            return false;
        }

        return true;
    }

    private static bool MatchesSource(BudgetSource source, MonthlyBudgetRealization posting) => source.SourceType switch
    {
        BudgetSourceType.Contact => posting.ContactId.HasValue && posting.ContactId.Value == source.SourceId,
        BudgetSourceType.ContactGroup => posting.ContactGroupId.HasValue && posting.ContactGroupId.Value == source.SourceId,
        BudgetSourceType.SavingsPlan => posting.SavingsPlanId.HasValue && posting.SavingsPlanId.Value == source.SourceId,
        _ => false
    };

    private static void RouteUnmatchedPosting(MonthlyBudgetResult monthResult, MonthlyBudgetRealization posting)
    {
        // GroupId links a posting to its paired ledger leg (e.g. the bank-side and contact-side leg of the
        // same booked transaction) and is set for essentially every booked posting - on its own it does not
        // identify a cost-neutral self-contact mirror transfer (e.g. a savings-plan contribution or an
        // internal transfer booked against the Self contact). Only postings that are BOTH grouped AND
        // attributed to the Self contact are cost-neutral; every other unmatched posting is genuinely
        // unbudgeted.
        if (posting.GroupId.HasValue && posting.IsSelfContact)
        {
            monthResult.AddCostNeutralPosting(posting);
        }
        else
        {
            monthResult.AddUnbudgetedPosting(posting);
        }
    }

    private static DateOnly GetPostingDate(MonthlyBudgetRealization posting, BudgetReportDateBasis dateBasis)
    {
        if (dateBasis == BudgetReportDateBasis.ValutaDate && posting.ValutaDate.HasValue)
        {
            return DateOnly.FromDateTime(posting.ValutaDate.Value);
        }

        return DateOnly.FromDateTime(posting.BookingDate);
    }

    // NaturalHomeMonth is the calendar month an occurrence is attributed to for BUDGETED-amount display.
    // Monthly rules use calendar months (1st to last day), quarterly rules use calendar quarters
    // (Jan/Apr/Jul/Oct), and yearly/custom rules use the month of the StartDate as the first anchor.
    // StartDate is only used to determine the first calendar unit, not as the period boundary.
    private static IEnumerable<(DateOnly PeriodStart, DateOnly PeriodEnd, DateOnly NaturalHomeMonth)> ExpandRuleOccurrences(BudgetRuleDto rule, DateOnly from, DateOnly to)
    {
        var stepMonths = rule.Interval switch
        {
            BudgetIntervalType.Monthly => 1,
            BudgetIntervalType.Quarterly => 3,
            BudgetIntervalType.Yearly => 12,
            BudgetIntervalType.CustomMonths => Math.Max(1, rule.CustomIntervalMonths ?? 1),
            _ => 1
        };

        var ruleEnd = rule.EndDate ?? to;

        var current = rule.Interval == BudgetIntervalType.Quarterly
            ? new DateOnly(rule.StartDate.Year, ((rule.StartDate.Month - 1) / 3) * 3 + 1, 1)
            : new DateOnly(rule.StartDate.Year, rule.StartDate.Month, 1);

        while (current.AddMonths(stepMonths).AddDays(-1) < from)
        {
            current = current.AddMonths(stepMonths);
            if (current > ruleEnd)
            {
                yield break;
            }
        }

        while (current <= to && current <= ruleEnd)
        {
            var next = current.AddMonths(stepMonths);
            var periodEnd = next.AddDays(-1);
            if (periodEnd > ruleEnd)
            {
                periodEnd = ruleEnd;
            }

            if (periodEnd > to)
            {
                periodEnd = to;
            }

            yield return (current, periodEnd, current);
            current = next;
        }
    }

    private static void ValidateRule(BudgetRuleDto rule)
    {
        if (!Enum.IsDefined(rule.Interval))
        {
            throw new BudgetReportCalculationException($"Budget rule {rule.Id} has an invalid interval.");
        }

        if (rule.Interval == BudgetIntervalType.CustomMonths && !rule.CustomIntervalMonths.HasValue)
        {
            throw new BudgetReportCalculationException($"Budget rule {rule.Id} uses a custom interval but has no CustomIntervalMonths configured.");
        }
    }

    private static BudgetReportEntry CreateEntry(
        BudgetReportEntryRowKind kind,
        string name,
        decimal budgeted,
        decimal actual,
        IReadOnlyList<MonthlyBudgetRealization> postings,
        Guid? budgetCategoryId = null,
        Guid? budgetPurposeId = null)
    {
        var (deviation, deviationPct) = CalculateDeviation(budgeted, actual);

        return new BudgetReportEntry
        {
            RowKind = kind,
            Name = name,
            BudgetedAmount = budgeted,
            ActualAmount = actual,
            Deviation = deviation,
            DeviationPercentage = deviationPct,
            Postings = postings.ToArray(),
            BudgetCategoryId = budgetCategoryId,
            BudgetPurposeId = budgetPurposeId
        };
    }

    // Shared deviation calculation used by both GetCurrentResult (via CreateEntry) and GetCumulativeResult.
    private static (decimal Deviation, decimal DeviationPercentage) CalculateDeviation(decimal budgeted, decimal actual)
    {
        var deviation = actual - budgeted;
        var deviationPct = budgeted != 0m ? deviation / Math.Abs(budgeted) * 100m : 0m;
        return (deviation, deviationPct);
    }

    private static IReadOnlyList<MonthlyBudgetRealization> CollectAssigned(MonthlyBudgetExpectation expectation) =>
        expectation.Postings.SelectMany(p => p.AssignedPostings).ToArray();

    // Merges the (per-month) MonthlyBudgetExpectation instances belonging to the same budget purpose (or,
    // for direct category-level expectations, the same category) into a single MergedExpectation per
    // purpose/direct-rule, summing amounts and concatenating assigned postings across all included months.
    // See GetCurrentResult for why this merge is necessary.
    private static List<MergedExpectation> MergeAcrossMonths(IEnumerable<MonthlyBudgetExpectation> expectations) =>
        expectations
            .GroupBy(e => e.BudgetPurposeId)
            .Select(g => new MergedExpectation(
                g.Key,
                g.First().Name,
                g.Sum(e => e.SumExpectedAmount),
                g.Sum(e => e.SumActualAmount),
                g.SelectMany(CollectAssigned).ToArray()))
            .ToList();

    private readonly record struct MergedExpectation(
        Guid? BudgetPurposeId,
        string Name,
        decimal SumExpectedAmount,
        decimal SumActualAmount,
        IReadOnlyList<MonthlyBudgetRealization> Postings);

    // Small value type replacing the previous (BudgetSourceType, Guid) tuple used to identify what a
    // budget purpose or category matches actual postings against (a Contact, ContactGroup or SavingsPlan).
    private readonly record struct BudgetSource(BudgetSourceType SourceType, Guid SourceId);
}
