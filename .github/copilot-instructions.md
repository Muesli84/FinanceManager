# Programmierrichtlinien

Diese Richtlinien definieren konsistente, wartbare und sichere Standards für die Solution.

## 1. Allgemeine Grundsätze
- Sprache für Code, Kommentare und Commits: Englisch (UI-Texte lokalisiert via Ressourcen).
- Single Responsibility: Jede Klasse, Methode, Datei hat klaren Zweck.
- KISS / DRY / YAGNI konsequent anwenden.
- Defensive & intention‑revealing code (aussagekräftige Namen > Kommentare).Kommentare nur für Warum / Kontext / Randbedingungen.
- TODO und Ziel Kommentare verwenden um Absichten zu verdeutlichen.

## 2. C# Konventionen
### 2.1 Naming
- PascalCase: Classes, Records, Interfaces (I prefix), Methods, Properties, Events.
- camelCase: Local variables, parameters.
- _camelCase: Private fields.
- CONSTANT_CASE: Nur für wirklich globale Konstanten (sonst readonly static PascalCase).
- Async-Methoden enden auf `Async`.

### 2.2 Stil & Struktur
- Immer geschweifte Klammern bei Kontrollblöcken (if/else/for/while/foreach/using) auch bei Einzelzeilen.
- Eine Anweisung pro Zeile; keine verketteten Zuweisungen.
- Guard Clauses statt tiefer Verschachtelung.
- Keine magischen Zahlen/Strings: Konstanten, Enums oder konfigurierte Optionen.
- Max. Methodenlänge: thematisch sinnvolle Einheit (Richtwert < 40 Zeilen). Größere Blöcke extrahieren.
- Eine Typdefinition pro Datei.
- Vermeide regions außer für umfangreiche partielle Klassen oder logisch gruppierte öffentliche API; kein Feingranular-Spam.
- Nullable Reference Types aktivieren (<Nullable>enable</Nullable> in allen csproj). Null-Handhabung explizit (ArgumentNullException / Optionals).
- immer `async`/`await` nutzen, wenn Datenbank‑ oder API‑Zugriffe stattfinden
- Jeder Für jeden Befehlsblock (if, for, while, etc.) sind geschweifte Klammern zu verwenden, auch wenn der Block nur eine Anweisung enthält.

### 2.3 Dokumentation
- Öffentliche / geschützte APIs: XML-Dokumentation (Summary + param + returns + exceptions bei Bedarf).
- Interne / private nur bei nicht offensichtlicher Intention.
- Jede Klasse, in der ein IDisposable-Objekt erzeugt, ist für dessen Freigabe zuständig. Falls diese Verantwortung einmal an andere Objekte weitergegeben werden muss, muss dies in den Dokumentationskommentaren beschrieben sein.

### 2.4 Exceptions
- Domänenspezifische Fehler: Eigene DomainException (nicht für Control Flow missbrauchen).
- Throw früh (Fail Fast). Kein Swallowing stillschweigend. Catch nur zur: (a) Kontextanreicherung (wrap), (b) Mapping auf fachliche Reaktion, (c) Logging an Systemgrenzen.
- Middleware wandelt technische Exceptions in ProblemDetails.

### 2.5 Async / Parallel
- Async durchgängig bis Aufrufer (kein .Result / .Wait()).
- CancellationToken Parameter für IO-/DB-/externen Zugriff; konsequent weiterreichen.
- Keine Fire-and-Forget Tasks ohne Fehlerkanal – wenn nötig, über BackgroundService / Channel / Queue abstrahieren.

### 2.6 Dependency Injection
- Services klar trennen: Application (UseCases), Domain (reine Logik), Infrastructure (DB, APIs).
- Lifetime:
  - Singleton: Stateless / pure / Konfiguration.
  - Scoped: EF DbContext, Services mit Kontextabhängigkeit.
  - Transient: Kurzlebige Hilfsobjekte.
- Singletons injizieren keine Scoped/Transient direkt (Factory oder IServiceScopeFactory verwenden).
- Registriere Services als Singleton wenn mehrere Thread nur eine Verbindung zu einer Ressource herstellen sollen.
- Vermeide Benutzerkontexte in Singletons
- Singletons dürfen keine Transient oder Scoped Services direkt injizieren. Nutze stattdessen ein Factory-Pattern oder Lazy-loading.

### 2.7 Ressourcen & IDisposable
- Der Ersteller eines IDisposable ist für Dispose verantwortlich (Ownership klar dokumentieren).
- `IAsyncDisposable` nutzen wo async Clean-up nötig.

### 2.8 Logging
- ILogger<T>. Provider zentral (z.B. Serilog) konfiguriert.
- Log-Level Guidance:
  - Trace/Debug: Entwicklungsdiagnostik.
  - Information: Geschäftsrelevante Events (Import abgeschlossen, Konto geteilt, Kursabruf gestartet/beendet). Nicht jede Service-Methode automatisch loggen → Vermeide Log Noise.
  - Warning: Erwartbare aber ungewöhnliche Zustände (Retry, Rate Limit nahe).
  - Error: Fehler, Operation abgebrochen.
  - Critical: System nicht funktionsfähig.
- Keine sensiblen Werte (Passwörter, vollständige Tokens, personenbezogene Volltexte) im Log.
- Nutze das ILogger<>-Interface für die Protokollierung
- Jede öffentliche Methode eines Service sollte mit einem Informations-Log starten, der den Aufruf mit seinen Parametern protokolliert.
- Benutzerbezogene Daten sind in den Protokollen auf ein Minimum zu reduzieren und nur auszugeben, wenn sie dem Verständnis über den Programmablauf dienen.
- Try-Catch-Blöcke, die dem Abfangen unerwarteter Fehlerzustände dienen, sollen den Ausnahmefehler als Error protokollieren.
- Ausnahmefehler, die dem Ablauf der Geschäftslogik dienen brauchen nicht protokolliert sein und wenn, dann als Information oder Debug-Log

### 2.9 Security & Secrets
- Keine Secrets im Code/Repo. Nutzung von User Secrets / Environment Variablen / Secret Store.
- Passwort-Hash: Argon2id (Fallback bcrypt mit cost >= 12). Kein Klartext, keine reversible Verschlüsselung.
- Eingaben validieren (FluentValidation oder DataAnnotations). Output-Encoding bei Rendering.
- Prinzip minimaler Rechte bei Konto-Sharing.

### 2.10 Daten / EF Core
- Separate DbContext pro Bounded Context falls nötig (vorerst einer).
- Keine Lazy Loading Proxies; explizite Includes oder projektion auf DTO.
- Standard: AsNoTracking für reine Leseabfragen.
- Migrationsnamen: `yyyyMMddHHmm_<ShortDescription>`.
- Transaktionen auf Service-/UseCase-Ebene bündeln.
- Concurrency-Tokens (RowVersion) wo parallele Aktualisierung kritisch.

### 2.11 DTO / Domain / ViewModel
- Domain Entities: Geschäftslogik + Invarianten (keine UI-Attribute).
- DTOs: API Contract (Request/Response), keine Navigation-Properties direkt exponieren.
- ViewModels: UI-spezifische Aggregation/State (Blazor / MAUI). Mapping über Mapper (manuell oder AutoMapper sparsam).

### 2.12 Internationalisierung
- Keine hartkodierten UI Strings in Komponenten / Services; Nutzung von resx Ressourcen pro Kultur (de, en). Fallback-Kette: User -> Browser -> de.
- Datums-/Zahlenformat via CultureInfo.CurrentUICulture.

### 2.13 Code Quality
- Roslyn Analyzer + StyleCop / EditorConfig aktivieren.
- `dotnet format` in CI.
- Cyclomatic Complexity beobachten; Refactoring bei Hotspots.

### 2.14 Performance
- Linq nur soweit nötig, keine überflüssigen ToList().
- Caching für häufige, unveränderte Abfragen (MemoryCache / Distributed). Invalidation klar definieren.

## 3. Unit Tests
### 3.1 Allgemein
- Framework: xUnit, Assertions: FluentAssertions, Mocking: bevorzugt Moq (oder NSubstitute).
- Testmethoden-Pattern: Arrange / Act / Assert (AAA) mit Leerzeilen trennen.
- Tests isoliert, deterministisch, kein externer I/O. Datenbank → InMemory / SQLite InMemory pro Test frisch.
- Naming: `<MethodName>_Should<Erwartung>_When<Umstand>()`.
- Eine Testklasse pro Produktivklasse. Namespace & Ordnerstruktur spiegeln Quellprojekt.
- Minimale, sinnvolle Assertions (meist 1 fachliche Kernaussage). Mehrfach Assertions nur wenn thematisch untrennbar.
- Vermeide die Verwendung von `Thread.Sleep` in Tests.
- Vermeide die Verwendung von `async void` in Testmethoden.
- Vermeide die Verwendung von `try-catch` in Testmethoden
- Vermeide die Verwendung von `Console.WriteLine` in Testmethoden
- Bei der Verwendung eines DbContext mit einer InMemory-Datenbank in Tests, stelle sicher, dass die Datenbank für jeden Test neu initialisiert wird, um Seiteneffekte zu vermeiden.
- Bei der Verwendung von DbContext mit einer InMemory-Datenbank in Tests, registriere den DbContext als Scoped in der Test-Setup-Methode, um sicherzustellen, dass jeder Test eine neue Instanz erhält.

### 3.2 Coverage & Fokus
- Kritische Domain/Service Logik abdecken (Happy + Edge + Fehlerpfade).
- Kein künstliches 100% Ziel, Qualität > Quantität. Mindestziel initial 70% für Kern-Domain, evaluieren.

## 4. Projekttyp-spezifische Richtlinien
### 4.1 Blazor Server
- Verzeichnisstruktur:
  - /Pages: Razor Components (Routen via @page)
  - /Shared: Layouts & wiederverwendbare Components
  - /Services: Application-/UI-nahe Services (keine EF direkt)
  - /Data: EF DbContext & Migrations
  - /ViewModels: UI State / Präsentationsmodelle
  - /wwwroot: statische Assets
- Razor Components schlank: UI Binding + minimale Orchestrierung. Business in Services / Domain.
- Code-Behind (.razor.cs) nur bei umfangreicher (> ~150 Zeilen) oder wiederverwendbarer Logik.
- Validierung: DataAnnotations + EditForm + ValidationSummary / ValidationMessage.
- JS Interop getrennt in dateibenannte .js Dateien pro Funktionalitätsblock.
- CSS: Shared styles in app.css, komponentenspezifisch in <Komponente>.css (Scoped CSS bevorzugen).
- SignalR nur für tatsächliche Echtzeitbedarfe (Kursevents / KPI-Liveupdate).

### 4.2 Web API (integriert im Blazor Projekt oder separat)
- Ordner: /Controllers, /Services, /Data, /Dtos (statt ServiceModels), /Mappings, /Infrastructure.
- Controller: Dünn (Parameter → DTO Validierung → Service → Ergebnis mapping). Kein Geschäftslogik-Duplikat.
- Konsistentes Fehlerformat: ProblemDetails.
- Global Exception Handling Middleware.
- Pagination/Filter/Sort Query Parameter: Standard (page, pageSize, sort, filter). Max pageSize begrenzen.
- Versionierung vorbereiten (z.B. URL oder Header) – optional initial.

### 4.3 .NET MAUI
- Ordner: /Views, /ViewModels, /Services, /Data, /Resources.
- MVVM Pattern strikt (INotifyPropertyChanged). Keine direkte Logik in Code-Behind außer UI-spezifische Event-Wiring.
- Gemeinsame Logik via Shared/Domain Assemblies wiederverwenden.

## 5. Logging & Observability
- CorrelationId (RequestId) pro eingehendem API/Blazor Circuit Request propagieren.
- Strukturierte Logs (Serilog mit Enrichment: MachineName, Environment, UserId optional anonymisiert).
- Metriken (Prometheus/OpenTelemetry) perspektivisch; Events (Domain Events) debugbar.

## 6. Security & Privacy
- JWT (kurzlebig) + optional Refresh Token (Rotation). Blacklist bei Logout / Kompromittierung.
- Rate Limiting für Kurs-API und Auth Endpoints.
- Input Validation + Output Encoding (Razor AntiXSS standardmäßig, keine Raw HTML ohne Prüfung).
- GDPR: Lösch-/Anonymisierungspfad für Benutzerdaten (Owner-Löschung → Transfer oder Delete per Businessregel).

## 7. Internationalisierung (i18n)
- Ressourcen: `Resources/<Bereich>/<Name>.<culture>.resx`.
- Fallback-Kette implementieren (siehe GR-012 im Anforderungskatalog).
- Keine Format-Strings ohne Kultur (nutze string.Format(CultureInfo.CurrentUICulture,...)).

## 8. Build & Qualitätssicherung
- EditorConfig erzwingt Formatierung; CI führt `dotnet build`, `dotnet test`, `dotnet format --verify-no-changes` aus.
- Analyzers / Warning Level = TreatWarningsAsErrors (nach Stabilisierung).
- Abhängigkeiten regelmäßig prüfen (Dependabot / Renovate). Sicherheitsupdates bevorzugt.

## 9. Git & Branching (Empfehlung)
- main: stabile Releases.
- develop: Integrationszweig (optional).
- feature/<kurz-beschreibung>, bugfix/<id>, hotfix/<id>.
- Conventional Commits: `feat:`, `fix:`, `refactor:`, `test:`, `docs:`, `chore:`.
- Pull Requests: Code Review Pflicht (min. 1 Approver), CI grün vor Merge.

## 10. Performance & Skalierung
- N+1 vermeiden (Explizite Includes / Projektion).
- Batch-Verarbeitung für große Imports (Streaming / Chunking > 1000 Zeilen).
- Caching invalidieren bei Schreiboperationen (Konto-/Posten-/Kursdaten) über schlanke Cache Keys.

## 11. Fehlerbehandlung
- Einheitliche Exception → ProblemDetails Mapping (Code, Title, Detail, TraceId).
- Domänenvalidierungen liefern aussagekräftige Fehlermeldungen (lokalisierbar).
- Retries (Polly) nur bei transienten Fehlern (HTTP 5xx, Timeout, Rate Limit) mit Exponential Backoff & Jitter.

## 12. Beispiel Service Skeleton
```csharp
public sealed class AccountImportService : IAccountImportService
{
    private readonly ILogger<AccountImportService> _logger;
    private readonly IImportStrategyResolver _strategyResolver;

    public AccountImportService(ILogger<AccountImportService> logger, IImportStrategyResolver strategyResolver)
    {
        _logger = logger;
        _strategyResolver = strategyResolver;
    }

    public async Task<ImportResult> ImportAsync(Stream file, string fileName, Guid accountId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(file);
        var strategy = _strategyResolver.Resolve(fileName);
        _logger.LogInformation("Starting import {FileName} for {AccountId} using {Strategy}", fileName, accountId, strategy.GetType().Name);
        return await strategy.ExecuteAsync(file, accountId, ct);
    }
}
```

## 13. Anti-Pattern (vermeiden)
- God Classes / Service mit gemischten Verantwortlichkeiten.
- Anemische Domain ohne Invarianten (Logik gehört nicht ausschließlich in Services, fachliche Regeln auch in Entities/ValueObjects platzieren).
- Statische Hilfsklassen mit verstecktem Zustand.
- Copy-Paste Mapping.
- Übermäßiges Logging jeder Kleinigkeit.
- Direktes Binden von EF Entities an UI.

## 14. Offene Erweiterungen (künftige Ergänzungen)
- OpenTelemetry Tracing.
- Caching Layer Konkretisierung (Redis optional).
- Domain Events → Integration Events Architektur.
- Feature Flags (z.B. für neue Importformate).

---
Diese Richtlinien werden stetig weiterentwickelt; Änderungen über Pull Request dokumentieren.
