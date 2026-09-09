# FinanceManager

[![Tests](https://img.shields.io/github/actions/workflow/status/martin-stromberg/FinanceManager/pr-staging-ci.yml?label=Tests)](https://github.com/martin-stromberg/FinanceManager/actions/workflows/pr-staging-ci.yml)
[![Release](https://img.shields.io/github/actions/workflow/status/martin-stromberg/FinanceManager/release.yml?label=Release)](https://github.com/martin-stromberg/FinanceManager/actions/workflows/release.yml)
[![License](https://img.shields.io/github/license/martin-stromberg/FinanceManager)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Node.js](https://img.shields.io/badge/node-22.x-339933?logo=nodedotjs)](https://nodejs.org/)

`FinanceManager` ist eine Blazor-Server-Anwendung zur Verwaltung persönlicher Finanzen.  
Die Anwendung bündelt Stammdatenverwaltung, Kontoauszugsimport, Budget- und Reporting-Funktionen, Portfolio-Auswertungen sowie Setup- und Admin-Funktionen in einer gemeinsamen Weboberfläche.

## Überblick

Im aktuellen Code sind unter anderem folgende Bereiche vorhanden:

- **Authentifizierung und Benutzerverwaltung** über JWT-geschützte API-Endpunkte und ASP.NET Core Identity
- **Konten, Kontakte, Sparpläne und Wertpapiere** mit eigenen Listen-, Detail- und Bearbeitungsbereichen sowie Summen und Verteilungen in der Bankübersicht
- **Kontoauszugsverarbeitung** mit Upload, Massenimport, Klassifizierung, Schnellbearbeitung und Buchung
- **Budget- und Reporting-Funktionen** inklusive Budget-Kategorien, -Zwecken, -Regeln und Berichten
- **Portfolio-Analyse** mit Bericht und benutzerspezifischer KPI-Konfiguration
- **Betriebsfunktionen** wie Backups, Update-Steuerung, Help-System und `security.txt`

Die Navigation in `FinanceManager.Web/Components/Layout/MainLayout.razor` verweist aktuell auf Home, Konten, Kontoauszüge, Kontakte, Sparpläne, Wertpapiere, Budgetzwecke, Reports, Setup, Benutzerverwaltung und Help.

Neu im Erstregistrierungsfluss: Wenn noch kein Benutzer vorhanden ist, wird beim Start auf die Registrierungsseite weitergeleitet. Dort erscheint nur für den ersten Benutzer die Checkbox `Demodaten anlegen`, sie ist standardmäßig deaktiviert. Wenn sie aktiviert ist, startet nach der Erstregistrierung automatisch ein Hintergrundtask, der mit den vorhandenen Business-Services den vollständigen Demo-Datenbestand erstellt. Der Fortschritt wird in der allgemeinen Background-Task-Anzeige auf der Startseite sichtbar. Es gibt dafür keine zusätzliche globale Konfiguration.

## Screenshots

Die folgenden Screenshots zeigen die Anwendung nach einer Erstregistrierung mit aktivierter Demodaten-Option:

![Demo-GIF](Docs/screenshots/demo.gif)

Eine aufgezeichnete Tour durch die Anwendung mit Demodaten gibt es als Video:

[![Demo-Tour (Video)](Docs/screenshots/home.png)](Docs/screenshots/demo-tour.mp4)

Die Screenshots und das GIF können mit den Skripten unter `scripts/screenshots/` erneuert werden, siehe [Docs/screenshot-generation.md](Docs/screenshot-generation.md).

## Demodaten lokal testen

Der Demodaten-Bestand lässt sich lokal ohne externe Dienste erzeugen:

1. `dotnet publish FinanceManager.Web -c Release` aus dem Repository-Root ausführen.
2. Die App aus dem Publish-Verzeichnis (`FinanceManager.Web/bin/Release/net10.0/publish/`) starten. Die benötigten Umgebungsvariablen stehen vollständig in [`scripts/screenshots/generate-screenshots.js`](scripts/screenshots/generate-screenshots.js): freier Port für `ASPNETCORE_URLS` und `Api__BaseAddress`, frische SQLite-Datei über `ConnectionStrings__Default`, `BackgroundTasks__Enabled=true` sowie deaktivierte externe Worker (`Workers__SecurityPriceWorker__Enabled=false`, `Updates__HostedServicesEnabled=false`).
3. Auf `/register` den ersten Benutzer mit aktivierter Checkbox `Demodaten anlegen` registrieren; die Registrierung meldet den Browser nicht an, anschließend über `/login` einloggen.
4. Der Hintergrundtask `CreateDemoData` legt danach Stammdaten, Konten, Wertpapiere, Budgets und gebuchte Kontoauszüge für 24 Monate an (~1 Minute). Danach zeigen Startseite, Listen und Berichte den Demo-Datenbestand.

## Tech-Stack

- **.NET 10 / ASP.NET Core**
- **Blazor Server** mit interaktiven Razor Components
- **Entity Framework Core 10** mit **SQLite**
- **ASP.NET Core Identity** und **JWT-Bearer-Authentifizierung**
- **xUnit v3**, **FluentAssertions**, **bUnit** für Unit-/Komponententests
- **Microsoft.Playwright** für End-to-End-Tests
- **Node.js 22.x** für Release-/Versionsskripte und GitHub-Workflows

Zusätzlich werden lokale Paketquellen aus `external/` verwendet:

- `external/msTools.Web.Blazor`
- `external/msTools.Updater`

## Projektstruktur

Die Solution `FinanceManager.sln` enthält aktuell diese Projekte:

```text
FinanceManager.Web                      Blazor Server UI, API-Controller, Hosting
FinanceManager.Application              Anwendungslogik und Services
FinanceManager.Domain                   Domänenmodelle
FinanceManager.Infrastructure           Persistenz, Integrationen, Auth- und Setup-Infrastruktur
FinanceManager.Shared                   Gemeinsame DTOs und API-Client-Typen

FinanceManager.Tests                    Unit- und Komponenten-Tests
FinanceManager.Tests.Integration        Integrationstests
FinanceManager.Tests.E2E                End-to-End-Tests mit Playwright

tools/FinanceManager.HelpSearchIndexGenerator
                                        Build-Tool für Help-Suchindizes
```

## Voraussetzungen

Für die lokale Ausführung der Webanwendung:

- .NET SDK **10.0**

Zusätzlich für Release-/CI-nahe Aufgaben:

- Node.js **22.x**

## Lokal starten

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

## Help, Betrieb und Sicherheit

- Die Help-Oberfläche ist unter **`/help`** verfügbar.
- Die Markdown-Quellen liegen unter **`Docs/help/`**.
- Während des Builds werden Help-Suchindizes über `tools/FinanceManager.HelpSearchIndexGenerator` erzeugt.
- Öffentliche Security-Kontaktinformationen werden über `SecurityTxtController` unter `/security.txt` und `/.well-known/security.*` ausgeliefert.
- `HealthController` stellt `/health` und `/api/health` bereit.

## CI/CD und Releases

Im Repository sind folgende GitHub-Workflows vorhanden:

- **`pr-staging-ci.yml`** für Pull Requests gegen `staging`
- **`staging-ci.yml`** als Pre-Release-Pipeline für Pushes nach `staging`
- **`staging-to-main-promotion.yml`** für den automatisierten Draft-PR von `staging` nach `main`
- **`release.yml`** für Releases auf `main` und für Tags im Format `v*.*.*`
- **`security-scan.yml`** für Sicherheitsprüfungen

Aus den aktuellen Workflow-Dateien ergeben sich diese Punkte:

- PRs gegen `staging` führen Formatprüfung, Security-Scan, Build und Tests aus.
- Die Coverage-Schwelle für Unit- und Integrationstests liegt bei **70 % Line Coverage**.
- E2E-Tests werden in PR- und Staging-CI ausgeführt, sind dort aber als **best effort** markiert.
- Der Release-Workflow baut `FinanceManager.Web` als **self-contained** Paket für **`win-x64`** und **`linux-x64`**.
- Zusätzlich wird ein **`update.json`**-Manifest für das Update-System erzeugt.
- Versionsableitung für automatische Releases erfolgt über **Semantic Release** und Conventional Commits.

## Bekannte Einschränkungen

- Die Registrierungsseite zeigt vor dem ersten Login englische Bezeichnungen (z. B. `Registration`, `Create demo data`); erst nach dem Login erscheint die deutsche Benutzeroberfläche.
- Einzelne Seiten geben Währungs- bzw. Trennzeichen verzerrt aus (`¤`-Symbol bzw. Ersatzzeichen), etwa bei Kontosalden, dem Budget-KPI auf der Startseite und im Berichtszeitraum.

## Weitere Dokumentation

- [CONTRIBUTING.md](CONTRIBUTING.md)
- [CHANGELOG.md](CHANGELOG.md)
- [changes.log](changes.log)
- [CI-CD.md](CI-CD.md)

## Lizenz

Dieses Repository steht unter der **MIT-Lizenz**. Details siehe [LICENSE](LICENSE).

## Repository

- GitHub: `martin-stromberg/FinanceManager`
- Issues: bitte über die GitHub-Issue-Verwaltung des Repositories melden
