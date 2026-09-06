namespace FinanceManager.Domain.Reports;

/// <summary>
/// A user-defined favorite configuration for an aggregate report dashboard.
/// Stored per user and referenced via GUID.
/// </summary>
public sealed class ReportFavorite : Entity, IAggregateRoot
{
    /// <summary>
    /// Parameterless constructor for persistence/ORM and deserialization.
    /// </summary>
    private ReportFavorite() { }

    /// <summary>
    /// Creates a new report favorite configuration for a user.
    /// </summary>
    /// <param name="ownerUserId">The identifier of the user owning this favorite. Must not be empty.</param>
    /// <param name="name">Name of the favorite. Must not be null or whitespace.</param>
    /// <param name="postingKind">Primary posting kind for the report.</param>
    /// <param name="includeCategory">Whether to include category grouping in the report.</param>
    /// <param name="interval">Time interval used for the report aggregation.</param>
    /// <param name="comparePrevious">Whether to compare to the previous interval.</param>
    /// <param name="compareYear">Whether to compare to the same period in the previous year.</param>
    /// <param name="compareProjection">Whether to include dividend projection amounts.</param>
    /// <param name="showChart">Whether to show a chart representation.</param>
    /// <param name="expandable">Whether the result should be expandable in the UI.</param>
    /// <param name="take">How many intervals to include (default 24). Will be clamped to a reasonable range.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="ownerUserId"/> is empty or <paramref name="name"/> is null/whitespace.</exception>
    public ReportFavorite(Guid ownerUserId,
        string name,
        PostingKind postingKind,
        bool includeCategory,
        ReportInterval interval,
        bool comparePrevious,
        bool compareYear,
        bool compareProjection,
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
        CompareProjection = compareProjection;
        ShowChart = showChart;
        Expandable = expandable;
        SetTake(take);
    }

    /// <summary>
    /// Compatibility constructor for callers that do not provide projection settings.
    /// </summary>
    /// <param name="ownerUserId">The identifier of the user owning this favorite.</param>
    /// <param name="name">Name of the favorite.</param>
    /// <param name="postingKind">Primary posting kind for the report.</param>
    /// <param name="includeCategory">Whether to include category grouping in the report.</param>
    /// <param name="interval">Time interval used for the report aggregation.</param>
    /// <param name="comparePrevious">Whether to compare to the previous interval.</param>
    /// <param name="compareYear">Whether to compare to the same period in the previous year.</param>
    /// <param name="showChart">Whether to show a chart representation.</param>
    /// <param name="expandable">Whether the result should be expandable in the UI.</param>
    /// <param name="take">How many intervals to include.</param>
    public ReportFavorite(Guid ownerUserId,
        string name,
        PostingKind postingKind,
        bool includeCategory,
        ReportInterval interval,
        bool comparePrevious,
        bool compareYear,
        bool showChart,
        bool expandable,
        int take = 24)
        : this(ownerUserId, name, postingKind, includeCategory, interval, comparePrevious, compareYear, false, showChart, expandable, take)
    {
    }

    /// <summary>
    /// Owner user identifier for this favorite.
    /// </summary>
    /// <value>The owner user's GUID.</value>
    public Guid OwnerUserId { get; private set; }

    /// <summary>
    /// Display name of the favorite configuration.
    /// </summary>
    /// <value>The name string.</value>
    public string Name { get; private set; } = null!;

    /// <summary>
    /// Primary posting kind used by the favorite.
    /// </summary>
    /// <value>The posting kind.</value>
    public PostingKind PostingKind { get; private set; }

    /// <summary>
    /// Whether category grouping is included in the report.
    /// </summary>
    /// <value>True when categories are included.</value>
    public bool IncludeCategory { get; private set; }

    /// <summary>
    /// Time interval for aggregation in the report.
    /// </summary>
    /// <value>The report interval.</value>
    public ReportInterval Interval { get; private set; }

    /// <summary>
    /// Whether the report should compare to the previous interval.
    /// </summary>
    /// <value>True to compare to previous interval.</value>
    public bool ComparePrevious { get; private set; }

    /// <summary>
    /// Whether the report should compare to the same period in the previous year.
    /// </summary>
    /// <value>True to compare year-on-year.</value>
    public bool CompareYear { get; private set; }

    /// <summary>
    /// Whether the report should include dividend projection amounts.
    /// </summary>
    /// <value>True to include projections where supported.</value>
    public bool CompareProjection { get; private set; }

    /// <summary>
    /// Whether a chart should be shown for this favorite.
    /// </summary>
    /// <value>True to show a chart.</value>
    public bool ShowChart { get; private set; }

    /// <summary>
    /// Whether the UI can expand details for this favorite.
    /// </summary>
    /// <value>True if expandable.</value>
    public bool Expandable { get; private set; }

    /// <summary>
    /// Optional CSV of posting kinds when multiple are selected.
    /// </summary>
    /// <value>CSV string or null.</value>
    public string? PostingKindsCsv { get; private set; }

    /// <summary>
    /// Number of intervals to take for the report.
    /// </summary>
    /// <value>Number of intervals (clamped between 1 and 120).</value>
    public int Take { get; private set; } = 24;

    // Persisted filter lists (CSV)

    /// <summary>
    /// CSV of filtered account ids.
    /// </summary>
    public string? AccountIdsCsv { get; private set; }
    /// <summary>
    /// CSV of filtered contact ids.
    /// </summary>
    public string? ContactIdsCsv { get; private set; }
    /// <summary>
    /// CSV of filtered savings plan ids.
    /// </summary>
    public string? SavingsPlanIdsCsv { get; private set; }
    /// <summary>
    /// CSV of filtered security ids.
    /// </summary>
    public string? SecurityIdsCsv { get; private set; }
    /// <summary>
    /// CSV of filtered contact category ids.
    /// </summary>
    public string? ContactCategoryIdsCsv { get; private set; }
    /// <summary>
    /// CSV of filtered savings plan category ids.
    /// </summary>
    public string? SavingsPlanCategoryIdsCsv { get; private set; }
    /// <summary>
    /// CSV of filtered security category ids.
    /// </summary>
    public string? SecurityCategoryIdsCsv { get; private set; }
    /// <summary>
    /// CSV of filtered security sub-types (integers).
    /// </summary>
    public string? SecuritySubTypesCsv { get; private set; }

    /// <summary>
    /// Optional toggle whether dividend-related entries are included.
    /// </summary>
    public bool? IncludeDividendRelated { get; private set; }

    /// <summary>
    /// When true the report aggregates by ValutaDate instead of BookingDate.
    /// </summary>
    public bool UseValutaDate { get; private set; }

    /// <summary>
    /// Renames the favorite.
    /// </summary>
    /// <param name="name">New name. Must not be null or whitespace.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is null or whitespace.</exception>
    public void Rename(string name)
    {
        Name = Guards.NotNullOrWhiteSpace(name, nameof(name));
        Touch();
    }

    /// <summary>
    /// Updates primary configuration values for the favorite.
    /// </summary>
    /// <param name="postingKind">Primary posting kind.</param>
    /// <param name="includeCategory">Whether to include categories.</param>
    /// <param name="interval">Aggregation interval.</param>
    /// <param name="comparePrevious">Compare to previous interval.</param>
    /// <param name="compareYear">Compare to previous year.</param>
    /// <param name="compareProjection">Include dividend projection amounts.</param>
    /// <param name="showChart">Show chart.</param>
    /// <param name="expandable">Expandable in UI.</param>
    /// <param name="take">Number of intervals to include; will be clamped.</param>
    /// <param name="useValutaDate">Whether to aggregate by ValutaDate.</param>
    public void Update(PostingKind postingKind, bool includeCategory, ReportInterval interval, bool comparePrevious, bool compareYear, bool compareProjection, bool showChart, bool expandable, int take, bool useValutaDate)
    {
        PostingKind = postingKind;
        IncludeCategory = includeCategory;
        Interval = interval;
        ComparePrevious = comparePrevious;
        CompareYear = compareYear;
        CompareProjection = compareProjection;
        ShowChart = showChart;
        Expandable = expandable;
        SetTake(take);
        UseValutaDate = useValutaDate;
        Touch();
    }

    /// <summary>
    /// Sets and clamps the Take value to acceptable bounds.
    /// </summary>
    /// <param name="take">Requested number of intervals.</param>
    private void SetTake(int take)
    {
        // constrain reasonable bounds 1..120 months
        Take = Math.Clamp(take, 1, 120);
    }

    /// <summary>
    /// Returns the selected posting kinds as a read-only collection.
    /// </summary>
    /// <returns>A collection of posting kinds. If none configured, returns a single-element collection with <see cref="PostingKind"/>.</returns>
    public IReadOnlyCollection<PostingKind> GetPostingKinds()
        => (PostingKindsCsv?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => TryParseKind(s))
                .Where(k => k.HasValue)
                .Select(k => k!.Value)
                .ToArray()) ?? new PostingKind[] { PostingKind };

    /// <summary>
    /// Attempts to parse a posting kind from string input. Accepts numeric or named values.
    /// </summary>
    /// <param name="s">String to parse.</param>
    /// <returns>The parsed <see cref="PostingKind"/>, or null if parsing failed.</returns>
    private static PostingKind? TryParseKind(string s)
    {
        if (int.TryParse(s, out var v))
        {
            if (Enum.IsDefined(typeof(PostingKind), v)) { return (PostingKind)v; }
            return null;
        }
        if (Enum.TryParse<PostingKind>(s, ignoreCase: true, out var kind))
        {
            return kind;
        }
        return null;
    }

    /// <summary>
    /// Stores the provided posting kinds as CSV.
    /// </summary>
    /// <param name="kinds">Collection of posting kinds to store.</param>
    public void SetPostingKinds(IEnumerable<PostingKind> kinds)
    {
        var list = kinds.Distinct().Select(k => ((int)k).ToString()).ToArray();
        PostingKindsCsv = string.Join(',', list);
        Touch();
    }

    /// <summary>
    /// Sets filter lists for the favorite; values are persisted as CSV strings.
    /// </summary>
    /// <param name="accountIds">Optional list of account ids to filter by.</param>
    /// <param name="contactIds">Optional list of contact ids to filter by.</param>
    /// <param name="savingsPlanIds">Optional list of savings plan ids to filter by.</param>
    /// <param name="securityIds">Optional list of security ids to filter by.</param>
    /// <param name="contactCategoryIds">Optional list of contact category ids to filter by.</param>
    /// <param name="savingsPlanCategoryIds">Optional list of savings plan category ids to filter by.</param>
    /// <param name="securityCategoryIds">Optional list of security category ids to filter by.</param>
    /// <param name="securitySubTypes">Optional list of security sub-types (integers).</param>
    /// <param name="includeDividendRelated">Optional toggle for including dividend-related entries.</param>
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

    /// <summary>
    /// Retrieves the persisted filters as typed collections.
    /// </summary>
    /// <param name="Accounts">The accounts.</param>
    /// <param name="Contacts">The contacts.</param>
    /// <param name="SavingsPlans">The savings plans.</param>
    /// <param name="Securities">The securities.</param>
    /// <param name="ContactCategories">The contact categories.</param>
    /// <param name="SavingsPlanCategories">The savings plan categories.</param>
    /// <param name="SecurityCategories">The security categories.</param>
    /// <param name="SecuritySubTypes">The security sub types.</param>
    /// <returns>
    /// A tuple containing optional collections for Accounts, Contacts, SavingsPlans, Securities,
    /// ContactCategories, SavingsPlanCategories, SecurityCategories, SecuritySubTypes and the IncludeDividendRelated flag.
    /// </returns>
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

    private static string? ToCsv(IEnumerable<Guid>? ids) => ids == null ? null : string.Join(',', ids.Distinct());
    private static string? ToCsvInt(IEnumerable<int>? values) => values == null ? null : string.Join(',', values.Distinct());
    private static IReadOnlyCollection<Guid>? FromCsv(string? csv)
        => string.IsNullOrWhiteSpace(csv) ? null : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Guid.Parse).ToArray();
    private static IReadOnlyCollection<int>? FromCsvInt(string? csv)
        => string.IsNullOrWhiteSpace(csv) ? null : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(int.Parse).ToArray();

    // Backup DTO
    /// <summary>
    /// Backup data transfer object for ReportFavorite.
    /// </summary>
    /// <returns>The result.</returns>
    public sealed record ReportFavoriteBackupDto(
        Guid Id,
        Guid OwnerUserId,
        string Name,
        PostingKind PostingKind,
        bool IncludeCategory,
        ReportInterval Interval,
        int Take,
        bool ComparePrevious,
        bool CompareYear,
        bool CompareProjection,
        bool ShowChart,
        bool Expandable,
        string? PostingKindsCsv,
        string? AccountIdsCsv,
        string? ContactIdsCsv,
        string? SavingsPlanIdsCsv,
        string? SecurityIdsCsv,
        string? ContactCategoryIdsCsv,
        string? SavingsPlanCategoryIdsCsv,
        string? SecurityCategoryIdsCsv,
        string? SecuritySubTypesCsv,
        bool? IncludeDividendRelated,
        bool UseValutaDate);

    /// <summary>
    /// Creates a backup DTO representing the serializable state of this ReportFavorite.
    /// </summary>
    /// <param name="OwnerUserId">The owner user id.</param>
    /// <param name="Name">The name.</param>
    /// <param name="PostingKind">The posting kind.</param>
    /// <param name="IncludeCategory">The include category.</param>
    /// <param name="Interval">The interval.</param>
    /// <param name="Take">The take.</param>
    /// <param name="ComparePrevious">The compare previous.</param>
    /// <param name="CompareYear">The compare year.</param>
    /// <param name="CompareProjection">The compare projection.</param>
    /// <param name="ShowChart">The show chart.</param>
    /// <param name="Expandable">The expandable.</param>
    /// <param name="PostingKindsCsv">The posting kinds csv.</param>
    /// <param name="AccountIdsCsv">The account ids csv.</param>
    /// <param name="ContactIdsCsv">The contact ids csv.</param>
    /// <param name="SavingsPlanIdsCsv">The savings plan ids csv.</param>
    /// <param name="SecurityIdsCsv">The security ids csv.</param>
    /// <param name="ContactCategoryIdsCsv">The contact category ids csv.</param>
    /// <param name="SavingsPlanCategoryIdsCsv">The savings plan category ids csv.</param>
    /// <param name="SecurityCategoryIdsCsv">The security category ids csv.</param>
    /// <param name="SecuritySubTypesCsv">The security sub types csv.</param>
    /// <param name="IncludeDividendRelated">The include dividend related.</param>
    /// <param name="UseValutaDate">The use valuta date.</param>
    /// <returns>A <see cref="ReportFavoriteBackupDto"/> containing the backup data.</returns>
    public ReportFavoriteBackupDto ToBackupDto() => new ReportFavoriteBackupDto(Id, OwnerUserId, Name, PostingKind, IncludeCategory, Interval, Take, ComparePrevious, CompareYear, CompareProjection, ShowChart, Expandable, PostingKindsCsv, AccountIdsCsv, ContactIdsCsv, SavingsPlanIdsCsv, SecurityIdsCsv, ContactCategoryIdsCsv, SavingsPlanCategoryIdsCsv, SecurityCategoryIdsCsv, SecuritySubTypesCsv, IncludeDividendRelated, UseValutaDate);

    /// <summary>
    /// Assigns values from a backup DTO to this entity. Uses existing setters to preserve invariants where applicable.
    /// </summary>
    /// <param name="dto">Backup DTO to apply.</param>
    public void AssignBackupDto(ReportFavoriteBackupDto dto)
    {
        if (dto == null) throw new ArgumentNullException(nameof(dto));
        Rename(dto.Name);
        PostingKind = dto.PostingKind;
        IncludeCategory = dto.IncludeCategory;
        Interval = dto.Interval;
        Take = dto.Take;
        ComparePrevious = dto.ComparePrevious;
        CompareYear = dto.CompareYear;
        CompareProjection = dto.CompareProjection;
        ShowChart = dto.ShowChart;
        Expandable = dto.Expandable;
        PostingKindsCsv = dto.PostingKindsCsv;
        AccountIdsCsv = dto.AccountIdsCsv;
        ContactIdsCsv = dto.ContactIdsCsv;
        SavingsPlanIdsCsv = dto.SavingsPlanIdsCsv;
        SecurityIdsCsv = dto.SecurityIdsCsv;
        ContactCategoryIdsCsv = dto.ContactCategoryIdsCsv;
        SavingsPlanCategoryIdsCsv = dto.SavingsPlanCategoryIdsCsv;
        SecurityCategoryIdsCsv = dto.SecurityCategoryIdsCsv;
        SecuritySubTypesCsv = dto.SecuritySubTypesCsv;
        IncludeDividendRelated = dto.IncludeDividendRelated;
        UseValutaDate = dto.UseValutaDate;
    }
}
