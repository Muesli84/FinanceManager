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
        bool expandable)
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

    public void Rename(string name)
    {
        Name = Guards.NotNullOrWhiteSpace(name, nameof(name));
        Touch();
    }

    public void Update(int postingKind, bool includeCategory, ReportInterval interval, bool comparePrevious, bool compareYear, bool showChart, bool expandable)
    {
        PostingKind = postingKind;
        IncludeCategory = includeCategory;
        Interval = interval;
        ComparePrevious = comparePrevious;
        CompareYear = compareYear;
        ShowChart = showChart;
        Expandable = expandable;
        Touch();
    }
}
