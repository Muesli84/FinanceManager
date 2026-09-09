# FinanceManager

[![Tests](https://img.shields.io/github/actions/workflow/status/martin-stromberg/FinanceManager/pr-staging-ci.yml?label=Tests)](https://github.com/martin-stromberg/FinanceManager/actions/workflows/pr-staging-ci.yml)
[![Release](https://img.shields.io/github/actions/workflow/status/martin-stromberg/FinanceManager/release.yml?label=Release)](https://github.com/martin-stromberg/FinanceManager/actions/workflows/release.yml)
[![License](https://img.shields.io/github/license/martin-stromberg/FinanceManager)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Node.js](https://img.shields.io/badge/node-22.x-339933?logo=nodedotjs)](https://nodejs.org/)

`FinanceManager` ist eine Blazor-Server-Anwendung zur Verwaltung persönlicher Finanzen.  
Die Anwendung bündelt Stammdatenverwaltung, Kontoauszugsimport, Budget- und Reporting-Funktionen, Portfolio-Auswertungen sowie Setup- und Admin-Funktionen in einer gemeinsamen Weboberfläche.

## Überblick

Die Anwendung bietet unter anderem:

- **Authentifizierung und Benutzerverwaltung** über JWT-geschützte API-Endpunkte und ASP.NET Core Identity
- **Konten, Kontakte, Sparpläne und Wertpapiere** mit Listen-, Detail- und Bearbeitungsbereichen sowie Verteilungen in der Bankübersicht
- **Kontoauszugsverarbeitung** mit Upload, Massenimport, Klassifizierung, Schnellbearbeitung und Buchung
- **Budget- und Reporting-Funktionen** inklusive Budget-Kategorien, -Zwecken, -Regeln und Berichten
- **Portfolio-Analyse** mit Bericht und benutzerspezifischer KPI-Konfiguration
- **Betriebsfunktionen** wie Backups, Update-Steuerung, Help-System und `security.txt`

Bei der Erstregistrierung, wenn noch kein Benutzer vorhanden ist, wird der Start auf die Registrierungsseite umgeleitet. Nur der erste Benutzer sieht dort die Checkbox `Demodaten anlegen`; ist sie aktiviert, erstellt ein Hintergrundtask nach der Registrierung den vollständigen Demo-Datenbestand. Der Fortschritt ist auf der Startseite in der Background-Task-Anzeige sichtbar.

## Screenshots

Die Screenshots zeigen die Anwendung nach einer Erstregistrierung mit aktivierter Demodaten-Option:

![Demo-GIF](Docs/screenshots/demo.gif)

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

Details zu Demodaten, Konfiguration, Authentifizierung, API-Endpunkten, Tests und Git-Hooks finden sich in [Docs/development.md](Docs/development.md).

## Weitere Informationen

- [Entwickler- und Betriebsdokumentation](Docs/development.md) — Lokale Entwicklung, Demodaten, Konfiguration, Authentifizierung, API-Endpunkte, Tests und Git-Hooks
- [Help-Dokumentation](Docs/help/index.md) — Anwenderdokumentation für alle Funktionsbereiche
- [Screenshot-Erzeugung](Docs/screenshot-generation.md) — Anleitung zur Erneuerung der README-Screenshots
- [CI/CD und Branch-Strategie](CI-CD.md) — Workflows, Quality Gates und Release-Prozess
- [Contributing](CONTRIBUTING.md) — Richtlinien für Mitwirkende
- [Bekannte Einschränkungen](Docs/known-issues.md) — Aktuell bekannte Probleme
- [Changelog](CHANGELOG.md)
- [Lizenz](LICENSE)

## Repository

- GitHub: `martin-stromberg/FinanceManager`
- Issues: bitte über die GitHub-Issue-Verwaltung des Repositories melden
