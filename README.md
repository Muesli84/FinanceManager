# Finance Manager

> Persönliche Finanzverwaltung mit Kontoauszug-Import, Sparplänen, Wertpapiertracking, Auswertungen & Mehrbenutzer?/Sharing-Funktionen.
>
> Dieses Projekt wurde zum größten Teil anhand von Anweisungen an GitHub Copilot erstellt.

## Inhalt
1. Überblick
2. Kernfunktionen
3. Architektur & Schichten
4. Technologie-Stack
   4.1 NuGet-Pakete (Abhängigkeiten)
5. Roadmap (Wellen)
6. Authentifizierung & Sicherheit (Kurz)
7. Internationalisierung (i18n)
8. Installation
9. Entwicklung & Build
10. Geplante Erweiterungen / Offene Punkte
11. Accessibility (Barrierefreiheit)
12. Lizenz / Status

## 1. Überblick
FinanceManager ist eine Blazor Server Anwendung (.NET 9) zur Verwaltung persönlicher Finanzen. Importierte Kontoauszüge (CSV/PDF) werden verarbeitet, Buchungen kategorisiert und in Bank-/Kontakt-/Sparplan- und Wertpapierposten überführt. Ergänzend existieren Auswertungen (Monat, Quartal, Jahr, YTD) und ein KPI-Dashboard. Mehrere Benutzer werden unterstützt. 

## 2. Kernfunktionen
- Benutzeranmeldung
  - Registrierungsmlglichkeit nur bei leerer Datenbank, Der erste Benutzer ist Administrator
  - Benutzerverwaltung im Administrationsbereich
  - IP-Sperrliste bei wiederholten fehlerhaften Anmeldeversuchen
- Konten: Verwaltung von Giro- & Sparkonten
- Kontakte & Kategorien: Verwaltung inkl. Aliasnamen (Wildcards ? *). Automatische Zuordnung beim Import. Merge-Funktion.
- Kontoauszüge: 
  - Import von CSV & PDF
  - unterstützte Banken: ING, Wüstenrot
  - Duplikatserkennung für bereits gebuchte Posten
  - automatische Kontierung für Kontakte, Sparpläne und Wertpapiere
  - Plausibilitätsprüfungen vor dem Buchen
  - Dateianhänge pro Buchung
  - Erfassung der Kontobewegungen als Posten pro Kontierung
- Sparpläne: 
  - Einmalig (Zielbetrag/Zieldatum), wiederkehrend (Intervalle), offen. 
  - Status/Analyse (erreicht/erforderlich/Prognose). 
  - Archivierung möglich.
- Wertpapiere: 
  - Aktien/Fonds, 
  - Transaktionen (Kauf, Verkauf, Dividende/Zins, Gebühren, Steuern, Menge). 
  - Kursdienst via AlphaVantage inklusive Limit-Erkennung.
- Auswertungen: 
  - Aggregationen Monat / Quartal / Jahr, YTD, 
  - Vorjahresvergleich, 
  - P&L nach Kategorien. 
  - Export CSV/XLSX.
  - Favoritenberichte: 
    - Konfigurierbare Report-Favoriten, 
    - Filter auf einzelne Konten (Bankkonto/Kontakt/Sparpläne/Wertpapiere/Kategorien), 
    - YTD/Charts/Vergleiche.
- KPI Dashboard: 
  - Kacheln für Favoritenberichte; 
  - feste Berechnungsweisen für Dividenden/Monatsumsätze etc.
- Backups: 
  - Erstellen von Wiederherstellungspunkten pro Benutzer
  - Herunterladen (Zip mit NDJSON; v3 Schema)
  - Wiederherstellen alter Bestände, 
  - Löschen 
- Anhänge: 
  - Upload an Kontoauszug/Kontobewegung/Contact; 
  - Kategorien; 
  - Größen-/MIME-Validierung; 
  - Reassign/Referenzen bei Buchung; 
  - Download.
- Benachrichtigungen: 
  - Monatsabschluss-Reminder (letzter Werktag; Land/Subdivision/Uhrzeit konfigurierbar) inkl. Anzeige auf der Startseite.
- Adminbereich: 
  - Benutzerverwaltung (Anlegen, Bearbeiten, Sperren/Entsperren, Löschen) 
  - IP-Sperrliste.


## 3. Architektur & Schichten (geplant)
```
Presentation (Blazor Server / künftig MAUI)
  → Application Layer (Use Cases, DTOs, Orchestrierung)
       → Domain Layer (Entities, Value Objects, Invarianten, Domain Services)
            → Infrastructure (EF Core, AlphaVantage, Import Parser, Logging, Security)
Shared Library (Domain + Contracts) für Wiederverwendung in Blazor + MAUI.
```
Querschnittsthemen: Logging, Auth (JWT), Internationalisierung, Validation, Rate Limiting.

## 4. Technologie-Stack
- .NET 9, C# 13
- Blazor Server (Razor Components)
- (Geplant) .NET MAUI iOS
- EF Core (Sqlite/SQL Server)
- AlphaVantage API (Wertpapierkurse; API-Key nutzer-/adminbasiert in DB)
- Auth: JWT (kurzlebig), Passwort-Hash Ziel Argon2id/bcrypt
- Logging: ILogger Abstraktion (Serilog)
- Validation: DataAnnotations / später FluentValidation
- Internationalisierung: resx Ressourcen, CultureInfo
- Build/Format: dotnet CLI, EditorConfig, Analyzers/StyleCop
- Tests: xUnit, FluentAssertions (Projekt `FinanceManager.Tests`)

### 4.1 NuGet-Pakete (Abhängigkeiten)
Gruppiert; bei gemeinsamen Namespaces mit `*`.

- Microsoft.EntityFrameworkCore*
  - Microsoft.EntityFrameworkCore — https://www.nuget.org/packages/Microsoft.EntityFrameworkCore/
  - Microsoft.EntityFrameworkCore.Sqlite — https://www.nuget.org/packages/Microsoft.EntityFrameworkCore.Sqlite/
  - Microsoft.EntityFrameworkCore.Design — https://www.nuget.org/packages/Microsoft.EntityFrameworkCore.Design/
  - Microsoft.EntityFrameworkCore.InMemory (Tests) — https://www.nuget.org/packages/Microsoft.EntityFrameworkCore.InMemory/
- Microsoft.Extensions*
  - Microsoft.Extensions.Hosting.Abstractions — https://www.nuget.org/packages/Microsoft.Extensions.Hosting.Abstractions/
  - Microsoft.Extensions.Http — https://www.nuget.org/packages/Microsoft.Extensions.Http/
  - Microsoft.Extensions.Caching.Memory — https://www.nuget.org/packages/Microsoft.Extensions.Caching.Memory/
- Microsoft.AspNetCore*
  - Microsoft.AspNetCore.Authentication.JwtBearer — https://www.nuget.org/packages/Microsoft.AspNetCore.Authentication.JwtBearer/
  - Microsoft.AspNetCore.Cryptography.KeyDerivation — https://www.nuget.org/packages/Microsoft.AspNetCore.Cryptography.KeyDerivation/
  - Microsoft.AspNetCore.Mvc.Core (Tests) — https://www.nuget.org/packages/Microsoft.AspNetCore.Mvc.Core/
- IdentityModel / JWT
  - Microsoft.IdentityModel.Tokens — https://www.nuget.org/packages/Microsoft.IdentityModel.Tokens/
  - System.IdentityModel.Tokens.Jwt — https://www.nuget.org/packages/System.IdentityModel.Tokens.Jwt/
- Serilog*
  - Serilog.AspNetCore — https://www.nuget.org/packages/Serilog.AspNetCore/
  - Serilog.Enrichers.Environment — https://www.nuget.org/packages/Serilog.Enrichers.Environment/
  - Serilog.Enrichers.Process — https://www.nuget.org/packages/Serilog.Enrichers.Process/
  - Serilog.Enrichers.Thread — https://www.nuget.org/packages/Serilog.Enrichers.Thread/
  - Serilog.Settings.Configuration — https://www.nuget.org/packages/Serilog.Settings.Configuration/
  - Serilog.Sinks.Console — https://www.nuget.org/packages/Serilog.Sinks.Console/
- Dokumente / Krypto
  - DocumentFormat.OpenXml — https://www.nuget.org/packages/DocumentFormat.OpenXml/
  - itext — https://www.nuget.org/packages/itext/
  - itext.bouncy-castle-adapter — https://www.nuget.org/packages/itext.bouncy-castle-adapter/
  - Portable.BouncyCastle — https://www.nuget.org/packages/Portable.BouncyCastle/
- Test-Tooling
  - coverlet.collector — https://www.nuget.org/packages/coverlet.collector/
  - Microsoft.NET.Test.Sdk — https://www.nuget.org/packages/Microsoft.NET.Test.Sdk/
  - xunit — https://www.nuget.org/packages/xunit/
  - xunit.runner.visualstudio — https://www.nuget.org/packages/xunit.runner.visualstudio/
  - Moq — https://www.nuget.org/packages/Moq/
  - bunit — https://www.nuget.org/packages/bunit/

## 5. Roadmap (Wellen)
| Welle | Fokus |
|-------|-------|
| 1 | Benutzer & Auth-Basis, Konten, Kontakte, Import (CSV/PDF), Buchung, Alias, Basis-Dashboard |
| 2 | Sparpläne + Statuslogik, Erweiterte Auswertungen |
| 3 | Wertpapiere + Kursdienst + Renditen |
| 4 | i18n Verfeinerung, zusätzliche Importformate, Admin-Erweiterungen, Optimierungen |

## 6. Authentifizierung & Sicherheit (Kurz)
- JWT Bearer für API Calls.
- Konto-Sharing vorbereitet; Rollen/Entzug offen.
- Duplikatserkennung verhindert Doppelbuchung.
- IP-Sperren bei Fehlversuchen; Admin-Verwaltung.

## 7. Internationalisierung
- Sprachen: de, en. Fallback-Kette: Benutzer > Browser/System > de.
- Benutzerpräferenz speicherbar; dynamischer Wechsel ohne Re-Login geplant.
- Alle UI-Texte via Ressourcen – keine Hardcoded Strings.

## 8. Installation
- AlphaVantage API-Key wird nicht in `appsettings` gespeichert. Schlüssel pro Benutzer im Profil; optional Freigabe eines Admin-Keys, der für Hintergrundjobs verwendet werden kann.
- Weitere Installationshinweise folgen in `docs/install.md`.

## 9. Entwicklung & Build
### Voraussetzungen
- .NET 9 SDK

### Lokaler Start
```
dotnet restore
dotnet build
cd FinanceManager.Web
dotnet run
```
Standard URL: https://localhost:5001 (HTTPS) / http://localhost:5000 (HTTP)

### Tests
```
dotnet test
```

### Code-Qualität
- `dotnet format` vor Pull Request.

## 10. Geplante Erweiterungen / Offene Punkte (Auszug)
- OP-002: Konsolidierung Projekt-/Produktname.
- OP-003: Finale Wahl Auth (Identity Integration vs. eigenes Minimal-Setup).
- OP-009: Rollenmodell für Konto-Freigaben (erweitert?).
- OP-010: Ownership Transfer bei Löschung Primärbesitzer.
- OP-011: Audit Log Aufbewahrung & DSGVO.
- OP-012: Weitere Importformate (MT940, CAMT). 
- OP-013: Persistenter Sprachwechsel (Client State / Server Profil).
- OP-014: Erweiterte Accessibility-Audits.

## 11. Accessibility (Barrierefreiheit)
- Ribbon: ARIA Roles (`tablist`, `tab`, `tabpanel`, `toolbar`, `group`), `aria-selected`, `aria-disabled`, Tastaturnavigation.

## 12. Lizenz / Status
- Lizenz: MIT. Siehe Datei `LICENSE` im Repository.
- Aktueller Status: Betriebsbereit, aber Nutzung auf eigene Verantwortung (Beta). Feedback willkommen!

---
Weiterführende Dokumente:
- Anforderungen: `docs/Anforderungskatalog.md`
- Umsetzungsstatus: `docs/Anforderungsstatus.md`
- Programmierrichtlinien: `.github/copilot-instructions.md`
