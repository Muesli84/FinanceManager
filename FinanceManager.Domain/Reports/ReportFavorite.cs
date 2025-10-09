using FinanceManager.Domain; 
using FinanceManager.Domain.Reports;

namespace FinanceManager.Domain.Reports;

/// <summary>
/// A user defined favorite configuration for an aggregate report dashboard (FA-REP-008).
/// Stored per user and referenced via GUID.
/// </summary>
public sealed class ReportFavorite : Entity, IAggregateRoot
{
    private ReportFavorite() { }

    public ReportFavorite(Guid ownerUserId,
        string name,
        int postingKind,
        bool includeCategory,
        ReportInterval interval,
        bool comparePrevious,
        bool compareYear,
        bool showChart,
        bool expandable,
        int take = 24)
    {
        OwnerUserId = Guards.NotEmpty(ownerUserId, nameof(ownerUserId));
        Rename(name);
        PostingKind = postingKind;
        IncludeCategory = includeCategory;
        Interval = interval;
        ComparePrevious = comparePrevious;
        CompareYear = compareYear;
        ShowChart = showChart;
        Expandable = expandable;
        SetTake(take);
    }

    public Guid OwnerUserId { get; private set; }
    public string Name { get; private set; } = null!;
    /// <summary>
    /// Stored as int to decouple from current enum (FinanceManager.Domain.Postings.PostingKind)
    /// to allow future extension without migration churn.
    /// </summary>
    public int PostingKind { get; private set; }
    public bool IncludeCategory { get; private set; }
    public ReportInterval Interval { get; private set; }
    public bool ComparePrevious { get; private set; }
    public bool CompareYear { get; private set; }
    public bool ShowChart { get; private set; }
    public bool Expandable { get; private set; }
    public string? PostingKindsCsv { get; private set; }
    public int Take { get; private set; } = 24;

    // Persisted filter lists (CSV)
    public string? AccountIdsCsv { get; private set; }
    public string? ContactIdsCsv { get; private set; }
    public string? SavingsPlanIdsCsv { get; private set; }
    public string? SecurityIdsCsv { get; private set; }
    public string? ContactCategoryIdsCsv { get; private set; }
    public string? SavingsPlanCategoryIdsCsv { get; private set; }
    public string? SecurityCategoryIdsCsv { get; private set; }
    public string? SecuritySubTypesCsv { get; private set; }

    // New: store dividend-related toggle
    public bool? IncludeDividendRelated { get; private set; }

    public void Rename(string name)
    {
        Name = Guards.NotNullOrWhiteSpace(name, nameof(name));
        Touch();
    }

    public void Update(int postingKind, bool includeCategory, ReportInterval interval, bool comparePrevious, bool compareYear, bool showChart, bool expandable, int take)
    {
        PostingKind = postingKind;
        IncludeCategory = includeCategory;
        Interval = interval;
        ComparePrevious = comparePrevious;
        CompareYear = compareYear;
        ShowChart = showChart;
        Expandable = expandable;
        SetTake(take);
        Touch();
    }

    private void SetTake(int take)
    {
        // constrain reasonable bounds 1..120 months
        Take = Math.Clamp(take, 1, 120);
    }

    public IReadOnlyCollection<int> GetPostingKinds() =>
        (PostingKindsCsv?.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => int.Parse(s)).ToArray()) ?? new [] { PostingKind };

    public void SetPostingKinds(IEnumerable<int> kinds)
    {
        var list = kinds.Distinct().ToArray();
        PostingKindsCsv = string.Join(",", list);
        Touch();
    }

    public void SetFilters(
        IEnumerable<Guid>? accountIds,
        IEnumerable<Guid>? contactIds,
        IEnumerable<Guid>? savingsPlanIds,
        IEnumerable<Guid>? securityIds,
        IEnumerable<Guid>? contactCategoryIds,
        IEnumerable<Guid>? savingsPlanCategoryIds,
        IEnumerable<Guid>? securityCategoryIds,
        IEnumerable<int>? securitySubTypes,
        bool? includeDividendRelated = null)
    {
        AccountIdsCsv = ToCsv(accountIds);
        ContactIdsCsv = ToCsv(contactIds);
        SavingsPlanIdsCsv = ToCsv(savingsPlanIds);
        SecurityIdsCsv = ToCsv(securityIds);
        ContactCategoryIdsCsv = ToCsv(contactCategoryIds);
        SavingsPlanCategoryIdsCsv = ToCsv(savingsPlanCategoryIds);
        SecurityCategoryIdsCsv = ToCsv(securityCategoryIds);
        SecuritySubTypesCsv = ToCsvInt(securitySubTypes);
        IncludeDividendRelated = includeDividendRelated;
        Touch();
    }

    public (IReadOnlyCollection<Guid>? Accounts,
            IReadOnlyCollection<Guid>? Contacts,
            IReadOnlyCollection<Guid>? SavingsPlans,
            IReadOnlyCollection<Guid>? Securities,
            IReadOnlyCollection<Guid>? ContactCategories,
            IReadOnlyCollection<Guid>? SavingsPlanCategories,
            IReadOnlyCollection<Guid>? SecurityCategories,
            IReadOnlyCollection<int>? SecuritySubTypes,
            bool? IncludeDividendRelated) GetFilters()
    {
        return (
            FromCsv(AccountIdsCsv),
            FromCsv(ContactIdsCsv),
            FromCsv(SavingsPlanIdsCsv),
            FromCsv(SecurityIdsCsv),
            FromCsv(ContactCategoryIdsCsv),
            FromCsv(SavingsPlanCategoryIdsCsv),
            FromCsv(SecurityCategoryIdsCsv),
            FromCsvInt(SecuritySubTypesCsv),
            IncludeDividendRelated
        );
    }

    private static string? ToCsv(IEnumerable<Guid>? ids) => ids == null ? null : string.Join(",", ids.Distinct());
    private static string? ToCsvInt(IEnumerable<int>? values) => values == null ? null : string.Join(",", values.Distinct());
    private static IReadOnlyCollection<Guid>? FromCsv(string? csv)
        => string.IsNullOrWhiteSpace(csv) ? null : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Guid.Parse).ToArray();
    private static IReadOnlyCollection<int>? FromCsvInt(string? csv)
        => string.IsNullOrWhiteSpace(csv) ? null : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(int.Parse).ToArray();
}
