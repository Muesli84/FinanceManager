namespace FinanceManager.Domain.Reports;

/// <summary>
/// A single KPI configuration entry on the home dashboard for a specific user.
/// Can point to a predefined KPI or a report favorite.
/// </summary>
public sealed class HomeKpi : Entity, IAggregateRoot
{
    private HomeKpi() { }

    public HomeKpi(Guid ownerUserId, HomeKpiKind kind, HomeKpiDisplayMode displayMode, int sortOrder, Guid? reportFavoriteId = null)
    {
        OwnerUserId = Guards.NotEmpty(ownerUserId, nameof(ownerUserId));
        Kind = kind;
        DisplayMode = displayMode;
        SortOrder = sortOrder;
        ReportFavoriteId = reportFavoriteId;
        Validate();
    }

    public Guid OwnerUserId { get; private set; }
    public HomeKpiKind Kind { get; private set; }
    public Guid? ReportFavoriteId { get; private set; }
    public HomeKpiDisplayMode DisplayMode { get; private set; }
    public int SortOrder { get; private set; }
    public string? Title { get; private set; }
    public HomeKpiPredefined? PredefinedType { get; private set; }

    public void SetDisplayMode(HomeKpiDisplayMode mode)
    {
        DisplayMode = mode;
        Touch();
    }

    public void SetSortOrder(int order)
    {
        SortOrder = order;
        Touch();
    }

    public void SetFavorite(Guid? favoriteId)
    {
        ReportFavoriteId = favoriteId;
        Validate();
        Touch();
    }

    public void SetPredefined(HomeKpiPredefined? predefined)
    {
        PredefinedType = predefined;
        Validate();
        Touch();
    }

    public void SetTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            Title = null;
        }
        else
        {
            var t = title.Trim();
            if (t.Length > 120) { t = t.Substring(0, 120); }
            Title = t;
        }
        Touch();
    }

    private void Validate()
    {
        if (Kind == HomeKpiKind.ReportFavorite && ReportFavoriteId == null)
        {
            throw new ArgumentException("ReportFavoriteId required for ReportFavorite KPIs", nameof(ReportFavoriteId));
        }
    }
}
