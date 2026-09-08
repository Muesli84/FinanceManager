namespace FinanceManager.Web.Services.Help;

/// <summary>
/// Editorial catalog for help topics that are published in the user-facing help UI.
/// Technical markdown files remain in Docs/help, but are intentionally not listed here.
/// </summary>
public static class HelpContentCatalog
{
    private static readonly IReadOnlyList<HelpTopicDocument> AttachmentsDocuments =
    [
        new("beschreibung", "beschreibung.md", "Uebersicht")
    ];

    private static readonly IReadOnlyList<HelpTopicDocument> UserInterfaceDocuments =
    [
        new("beschreibung", "beschreibung.md", "Uebersicht"),
        new("startseite", "startseite.md", "Startseite und KPI-Kacheln"),
        new("ablauf-anwender", "ablauf-anwender.md", "Bedienablauf"),
        new("installation", "installation.md", "Einrichtung")
    ];

    private static readonly IReadOnlyList<HelpTopicDocument> DescriptionOnlyDocuments =
    [
        new("beschreibung", "beschreibung.md", "Uebersicht")
    ];

    private static readonly IReadOnlyList<HelpTopicDocument> AccountsDocuments =
    [
        new("beschreibung", "beschreibung.md", "Uebersicht"),
        new("vorlaeufige-buchungen", "vorlaeufige-buchungen.md", "Vorlaeufige Buchungen")
    ];

    private static readonly IReadOnlyList<HelpTopicDocument> StatementsDocuments =
    [
        new("beschreibung", "beschreibung.md", "Uebersicht"),
        new("ablauf-anwender", "ablauf-anwender.md", "Importablauf")
    ];

    private static readonly IReadOnlyList<HelpTopicDocument> ProgramDocuments =
    [
        new("beschreibung", "beschreibung.md", "Uebersicht"),
        new("ablauf-anwender", "ablauf-anwender.md", "Nutzung")
    ];

    private static readonly IReadOnlyList<HelpTopicDocument> SetupDocuments =
    [
        new("beschreibung", "beschreibung.md", "Uebersicht"),
        new("ablauf-anwender", "ablauf-anwender.md", "Bedienablauf"),
        new("installation", "einrichtung-anwender.md", "Einrichtung"),
        new("troubleshooting", "troubleshooting.md", "Fehlerbehebung"),
        new("sicherheit-help", "sicherheit-help.md", "Sicherheit")
    ];

    private static readonly IReadOnlyList<HelpTopicDocument> UpdatesDocuments =
    [
        new("beschreibung", "beschreibung.md", "Uebersicht"),
        new("installation", "einrichtung-anwender.md", "Einrichtung"),
        new("troubleshooting", "fehlerbehebung-anwender.md", "Fehlerbehebung")
    ];

    private static readonly IReadOnlyList<HelpTopicDocument> SecuritiesDocuments =
    [
        new("beschreibung", "beschreibung.md", "Uebersicht"),
        new("ablauf-anwender", "ablauf-anwender.md", "Bedienablauf")
    ];

    /// <summary>
    /// User-facing help topics. The first document in each topic is the primary document for /help/view/{topic}.
    /// </summary>
    public static IReadOnlyList<HelpTopic> Topics { get; } =
    [
        new("anhaenge", "Anhaenge", "Belege und Dateien zu Buchungen ablegen und wiederfinden.", AttachmentsDocuments),
        new("benutzeroberflaeche", "Benutzeroberflaeche", "Navigation, Listen, Formulare und grundlegende Bedienung verstehen.", UserInterfaceDocuments),
        new("berichtswesen", "Berichtswesen", "Auswertungen nutzen, um Einnahmen, Ausgaben und Entwicklungen zu pruefen.", DescriptionOnlyDocuments),
        new("budgetplanung", "Budgetplanung", "Budgets planen, kontrollieren und mit den Buchungen abgleichen.", DescriptionOnlyDocuments),
        new("kontakte", "Kontakte", "Personen und Organisationen verwalten, die in Buchungen verwendet werden.", DescriptionOnlyDocuments),
        new("konten-und-buchungen", "Konten und Buchungen", "Konten, Buchungen und vorlaeufige Buchungen im Alltag bearbeiten.", AccountsDocuments),
        new("kontoauszuege-und-import", "Kontoauszuege und Import", "Kontoauszuege importieren und Buchungen aus Importdaten erstellen.", StatementsDocuments),
        new("programminformationen", "Programminformationen", "Versions- und Programminformationen in der Anwendung einsehen.", ProgramDocuments),
        new("sparplaene", "Sparplaene", "Regelmaessige Sparvorgaenge planen und verwalten.", DescriptionOnlyDocuments),
        new("systemverwaltung-und-setup", "Systemverwaltung und Setup", "Grundlegende Einstellungen, Installation, Sicherheit und Fehlerbehebung.", SetupDocuments),
        new("updates", "Automatische Updates", "Updates einrichten, pruefen und typische Probleme beheben.", UpdatesDocuments),
        new("wertpapiermanagement", "Wertpapiermanagement", "Wertpapiere, Kurse und Auswertungen fuer Anlagen nutzen.", SecuritiesDocuments)
    ];

    /// <summary>
    /// Markdown document names that are kept as technical documentation and excluded from user navigation.
    /// </summary>
    /// <returns>The result.</returns>
    public static IReadOnlySet<string> TechnicalOnlyDocumentNames { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "api.md",
        "ablauf-technisch.md",
        "bereitstellung.md",
        "business-rules.md",
        "datenmodell.md",
        "index.md"
    };

    /// <summary>
    /// Finds a published user-facing help topic by route identifier.
    /// </summary>
    /// <param name="topicId">The requested help topic route identifier.</param>
    /// <param name="topic">The matching catalog topic.</param>
    /// <returns><c>true</c> when the topic is published in the user help catalog.</returns>
    public static bool TryGetTopic(string? topicId, out HelpTopic topic)
    {
        var normalizedTopicId = (topicId ?? string.Empty).Trim().ToLowerInvariant();
        topic = Topics.FirstOrDefault(candidate => candidate.Id.Equals(normalizedTopicId, StringComparison.OrdinalIgnoreCase))!;
        return topic is not null;
    }

    /// <summary>
    /// Resolves a normalized help route to a published catalog document and markdown file.
    /// </summary>
    /// <param name="docsPath">The absolute Docs/help source path.</param>
    /// <param name="language">The normalized help language.</param>
    /// <param name="helpPath">The normalized route path below /help/view.</param>
    /// <param name="topic">The resolved catalog topic.</param>
    /// <param name="document">The resolved catalog document.</param>
    /// <param name="markdownPath">The absolute markdown file path.</param>
    /// <returns><c>true</c> when the route maps to an existing published markdown file.</returns>
    public static bool TryResolveDocument(
        string docsPath,
        string language,
        string helpPath,
        out HelpTopic topic,
        out HelpTopicDocument document,
        out string markdownPath)
    {
        topic = default!;
        document = default!;
        markdownPath = string.Empty;

        var segments = helpPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length is 0 or > 2 || !TryGetTopic(segments[0], out topic))
        {
            return false;
        }

        var routeSegment = segments.Length == 1 || segments[1].Equals("index", StringComparison.OrdinalIgnoreCase)
            ? topic.PrimaryDocument.RouteSegment
            : segments[1];

        document = topic.Documents.FirstOrDefault(candidate => candidate.RouteSegment.Equals(routeSegment, StringComparison.OrdinalIgnoreCase))!;
        if (document is null)
        {
            return false;
        }

        var topicDirectory = Path.Combine(docsPath, topic.Id);
        var localizedFileName = ToLocalizedFileName(document.FileName, language);
        var localizedPath = Path.Combine(topicDirectory, localizedFileName);
        var defaultPath = Path.Combine(topicDirectory, document.FileName);
        markdownPath = File.Exists(localizedPath) ? localizedPath : defaultPath;

        return File.Exists(markdownPath);
    }

    /// <summary>
    /// Checks whether a markdown path is listed as a published catalog document.
    /// </summary>
    /// <param name="docsPath">The absolute Docs/help source path.</param>
    /// <param name="markdownPath">The absolute markdown file path.</param>
    /// <returns><c>true</c> when the file is published in the user help catalog.</returns>
    public static bool IsCatalogMarkdownFile(string docsPath, string markdownPath)
    {
        var fullPath = Path.GetFullPath(markdownPath);
        return Topics
            .SelectMany(topic => topic.Documents.Select(document => Path.Combine(docsPath, topic.Id, document.FileName)))
            .Select(Path.GetFullPath)
            .Any(path => path.Equals(fullPath, StringComparison.OrdinalIgnoreCase));
    }

    private static string ToLocalizedFileName(string fileName, string language)
    {
        var extension = Path.GetExtension(fileName);
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        return $"{baseName}.{language}{extension}";
    }
}

/// <summary>
/// A user-facing help topic published on the help hub.
/// </summary>
/// <param name="Id">The route-safe topic identifier.</param>
/// <param name="Title">The display title.</param>
/// <param name="Description">The short user-facing description.</param>
/// <param name="Documents">The published documents for the topic.</param>
/// <returns>The result.</returns>
public sealed record HelpTopic(string Id, string Title, string Description, IReadOnlyList<HelpTopicDocument> Documents)
{
    /// <summary>
    /// Gets the primary document used for /help/view/{topic}.
    /// </summary>
    public HelpTopicDocument PrimaryDocument => Documents[0];
}

/// <summary>
/// A markdown document that is published for a help topic.
/// </summary>
/// <param name="RouteSegment">The route segment used below the topic route.</param>
/// <param name="FileName">The markdown file name below the topic directory.</param>
/// <param name="Title">The display label used in topic navigation.</param>
/// <returns>The result.</returns>
public sealed record HelpTopicDocument(string RouteSegment, string FileName, string Title);
