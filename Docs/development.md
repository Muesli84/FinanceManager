# Entwicklung

Diese Dokumentation ergänzt die [README](../README.md) um Details für Entwickler und Betreiber.

## Inhalt

- [Lokale Ausführung](#lokale-ausf%C3%BChrung)
- [Demodaten lokal testen](#demodaten-lokal-testen)
- [Konfiguration](#konfiguration)
- [Authentifizierung und Sitzungserhaltung](#authentifizierung-und-sitzungserhaltung)
- [Relevante Endpunkte](#relevante-endpunkte)
- [Tests](#tests)
- [Git-Hooks und Codequalität](#git-hooks-und-codequalit%C3%A4t)

## Lokale Ausführung

### Voraussetzungen

- [.NET SDK 10.0](https://dotnet.microsoft.com)

Für release-nahe Aufgaben zusätzlich:

- [Node.js 22.x](https://nodejs.org/)

### Starten

```bash
dotnet restore
dotnet build FinanceManager.sln
dotnet run --project FinanceManager.Web
```

Entwicklungsprofile aus `FinanceManager.Web/Properties/launchSettings.json`:

- `http://localhost:5208`
- `https://localhost:7013`

Beim Start der Webanwendung werden in `Program.cs` und `ProgramExtensions.cs` unter anderem:

- Services und Logging registriert,
- EF-Core-Migrationen ausgeführt (`ApplyMigrationsAndSeed()`),
- gespeicherte Update-Einstellungen angewendet,
- Middleware, Authentifizierung und Routing konfiguriert.

## Demodaten lokal testen

Der Demodaten-Bestand lässt sich lokal ohne externe Dienste erzeugen:

1. `dotnet publish FinanceManager.Web -c Release` aus dem Repository-Root ausführen.
2. Die App aus dem Publish-Verzeichnis (`FinanceManager.Web/bin/Release/net10.0/publish/`) starten. Die benötigten Umgebungsvariablen stehen vollständig in [`scripts/screenshots/generate-screenshots.js`](../scripts/screenshots/generate-screenshots.js): freier Port für `ASPNETCORE_URLS` und `Api__BaseAddress`, frische SQLite-Datei über `ConnectionStrings__Default`, `BackgroundTasks__Enabled=true` sowie deaktivierte externe Worker (`Workers__SecurityPriceWorker__Enabled=false`, `Updates__HostedServicesEnabled=false`).
3. Auf `/register` den ersten Benutzer mit aktivierter Checkbox `Demodaten anlegen` registrieren; die Registrierung meldet den Browser nicht an, anschließend über `/login` einloggen.
4. Der Hintergrundtask `CreateDemoData` legt danach Stammdaten, Konten, Wertpapiere, Budgets und gebuchte Kontoauszüge für 24 Monate an (~1 Minute). Danach zeigen Startseite, Listen und Berichte den Demo-Datenbestand.

Für die automatisierte Screenshot-Erzeugung siehe [Docs/screenshot-generation.md](screenshot-generation.md).

## Konfiguration

Die wichtigsten Standardwerte stammen aus `FinanceManager.Web/appsettings.json`, `appsettings.Development.json` und `appsettings.Production.json`.

| Schlüssel | Standardwert | Bedeutung |
|---|---|---|
| `ConnectionStrings:Default` | Fallback auf `Data Source=financemanager.db` | Standarddatenbank für die Infrastruktur |
| `Jwt:Issuer` | `financemanager` | JWT-Issuer |
| `Jwt:Audience` | `financemanager` | JWT-Audience |
| `Jwt:LifetimeMinutes` | `30` | Gültigkeitsdauer der JWT-/Auth-Sitzung |
| `DataProtection:KeysPath` | leer | Optionaler persistenter Speicherort für Data-Protection-Keys |
| `Api:BaseAddress` | leer | Basisadresse für API-/Security.txt-bezogene Fallbacks |
| `BackgroundTasks:Enabled` | `true` | Aktiviert den Background-Task-Runner |
| `Workers:SecurityPriceWorker:Enabled` | `true` | Aktiviert den Kurs-Worker |
| `Updates:Enabled` | `false` | Aktiviert die Update-Funktionen |
| `Updates:SourceType` | `Github` | Update-Quelle (`Github` oder `LocalFolder`) |
| `Updates:RepositoryOwner` | `martin-stromberg` | Eigentümer des Release-Repositories |
| `Updates:RepositoryName` | `FinanceManager` | Name des Release-Repositories |
| `Updates:ManifestAssetName` | `update.json` | Manifest-Datei für Updates |
| `Updates:WorkingDirectory` | `updates` | Arbeitsverzeichnis des Update-Systems |
| `Updates:HealthTimeoutSeconds` | `120` | Timeout für Health-basierte Update-Prüfungen |
| `Backups:Security:MaxUploadBytes` | `104857600` | Maximale Backup-Uploadgröße |
| `FileLogging:Enabled` | `false` in `appsettings.json`, `true` in `appsettings.Production.json` | Schaltet Dateilogging ein/aus |
| `Identity:Lockout:MaxFailedAccessAttempts` | `3` | Maximale Fehlversuche bis zum Lockout |
| `Identity:Password:RequiredLength` | `8` | Minimale Passwortlänge |

### Wichtige Hinweise zur Produktionskonfiguration

- In produktionsnahen Umgebungen validiert `JwtOptionsValidator` die JWT-Konfiguration bereits beim Start.
- `Jwt:Key` darf dort nicht leer sein, kein Platzhalterwert sein und muss mindestens **32 UTF-8-Bytes** enthalten.
- Wenn `security.txt` keinen expliziten `Canonical`-Wert hat, erwartet der Fallback `Api:BaseAddress` eine gültige absolute URI.
- Für geschützte persistierte Secrets, z. B. AlphaVantage-Zugangsdaten, sollte `DataProtection:KeysPath` auf einen persistenten Speicher zeigen.

Typische Environment-Variablen sind beispielsweise:

- `ConnectionStrings__Default`
- `Jwt__Key`
- `Jwt__Issuer`
- `Jwt__Audience`
- `Jwt__LifetimeMinutes`
- `DataProtection__KeysPath`
- `Api__BaseAddress`

## Authentifizierung und Sitzungserhaltung

Die Anwendung verwendet JWT-basierte Authentifizierung mit Cookie-Transport:

- Login: `POST /api/auth/login`
- Registrierung: `POST /api/auth/register`
- Logout: `POST /api/auth/logout`
- Keepalive: `GET /api/auth/keepalive`

Wichtige Punkte aus dem aktuellen Code:

- Das Auth-Cookie heißt **`FinanceManager.Auth`**.
- Die konfigurierte Standardlaufzeit beträgt **30 Minuten** (`Jwt:LifetimeMinutes`).
- `JwtRefreshMiddleware` erneuert Tokens automatisch, sobald sie in ihr Renewal-Fenster kommen.
- `JwtRefreshService` validiert vor einem Refresh den Benutzerzustand, den `security_stamp` und die aktuelle Admin-Rolle erneut gegen die Datenbank.
- `MainLayout.razor` und `wwwroot/js/financeManager.js` triggern Keepalive-Aufrufe bei Navigation sowie bei Benutzerinteraktionen wie `pointerdown`, `keydown`, `focusin`, `input` und Quick-Edit-`blur`.
- Ein fehlgeschlagener Keepalive-Aufruf führt nicht selbst direkt zu einer Umleitung; die Umleitung auf geschützten Routen erfolgt über die reguläre Authentifizierungsprüfung.

Damit ist die in den Feature-Unterlagen beschriebene Sitzungserhaltung für aktive Benutzer explizit im aktuellen Codepfad abgebildet.

## Relevante Endpunkte

Eine Auswahl konkreter, im Repository vorhandener Einstiegspunkte:

- `GET /health`
- `GET /api/health`
- `POST /api/auth/login`
- `POST /api/auth/register`
- `POST /api/auth/logout`
- `GET /api/auth/keepalive`
- `POST /api/statement-drafts/upload`
- `POST /api/statement-drafts/mass-import`
- `POST /api/statement-drafts/preliminary`
- `GET /api/portfolio/analysis-report`
- `GET /api/portfolio/kpi-configuration`
- `POST /api/portfolio/kpi-configuration`
- `GET /api/setup/update/status`
- `GET /api/setup/update/settings`
- `PUT /api/setup/update/settings`
- `GET /security.txt`
- `GET /.well-known/security.txt`
- `GET /.well-known/security.md`
- `GET /.well-known/security.html`

Die Controller liegen unter `FinanceManager.Web/Controllers/`.

## Tests

Die Testprojekte in der Solution sind:

- `FinanceManager.Tests`
- `FinanceManager.Tests.Integration`
- `FinanceManager.Tests.E2E`

Frameworks laut Projektdateien:

- **xUnit v3**
- **FluentAssertions**
- **bUnit**
- **Microsoft.AspNetCore.Mvc.Testing**
- **Microsoft.Playwright**

Alle Tests der Solution starten:

```bash
dotnet test FinanceManager.sln
```

Die aktuellen Testdateien enthalten unter anderem Abdeckung für:

- Login, Registrierung, Logout
- JWT-Validierung und Refresh-Verhalten
- Keepalive bei aktiver Navigation und Interaktion
- Quick-Edit-Verhalten in Kontoauszugsentwürfen
- Summen, Filterung und Fehlerzustände der Bankübersicht

## Git-Hooks und Codequalität

Das Repository verwendet Git-Hooks aus dem Verzeichnis **`.githooks/`**. Aktivierung nach dem Klonen:

```cmd
.githooks\install-hooks.cmd
```

bzw. unter Linux/macOS:

```bash
./.githooks/install-hooks.sh
```

Die Skripte setzen `core.hooksPath` auf `.githooks`. Der **pre-commit**-Hook blockiert Commits auf `main`/`staging` und prüft Übersetzungskonsistenz, XML-Dokumentation, hartkodierte UI-Texte in Razor-Komponenten, unreferenzierte Komponenten, Stub-Implementierungen (`NotImplementedException`) und die Enum-Testabdeckung. Der **pre-push**-Hook führt die Stub-, Komponenten- und Enum-Prüfungen strikt für das gesamte Repository aus.
