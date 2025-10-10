# FinanceManager (Arbeitsname)

> Persönliche Finanzverwaltung mit Kontoauszug-Import, Sparplänen, Wertpapiertracking, Auswertungen & Mehrbenutzer?/Sharing-Funktionen.
>
> Hinweis: Projektname vs. "Finanzverwaltung" noch zu vereinheitlichen (siehe Offene Punkte OP-002).

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
FinanceManager ist eine Blazor Server Anwendung (.NET 9) zur Verwaltung persönlicher Finanzen. Importierte Kontoauszüge (CSV/PDF) werden verarbeitet, Buchungen kategorisiert und in Bank-/Kontakt-/Sparplan- und Wertpapierposten überführt. Ergänzend existieren Auswertungen (Monat, Quartal, Jahr, YTD) und ein KPI-Dashboard. Mehrere Benutzer werden unterstützt; Bankkonten können gezielt geteilt werden. Eine .NET MAUI App (iOS) ist geplant.

## 2. Kernfunktionen
- Konten: Giro- & Sparkonten, Bankkontakt-Automatik, Sharing (Lese / optional Schreibrechte).
- Kontoauszug-Import: CSV & PDF (Strategy Pattern), Duplikatserkennung, Buch.-Blatt vor endgültiger Verbuchung, Alias-Matching für Kontakte.
- Kontoauszug Massenbuchung: Dialog mit Optionen (Warnungen ignorieren, bei erster Warnung/Fehler abbrechen, Einträge einzeln buchen). Einzelbuchungsmodus bucht jeden offenen Eintrag separat; nur Einträge mit Warnung/Fehler verbleiben offen.
- Kontakte & Kategorien: Verwaltung inkl. Aliasnamen (Wildcards ? *). Automatische Zuordnung beim Import.
- Sparpläne: Einmalig (Zielbetrag/Zieldatum), wiederkehrend (Intervalle), offen. Automatische Erkennung bei Eigen-Sparkonto-Zahlungen, Zielstatus (erreicht / nahe / verfehlt) & Archivierung.
- Wertpapiere: Aktien/Fonds, Transaktionen (Kauf, Verkauf, Dividende/Zins, Gebühren, Steuern, Menge). Menge wird bei Buchung gespeichert (Buy positiv, Sell negativ, Dividend ohne Menge). Kursdienst/Renditen in Planung.
- Auswertungen: Aggregationen Monat / Quartal / Jahr, YTD, Vorjahresvergleich, P&L nach Kategorien.
- KPI Dashboard: Monatliche/Jährliche Dividenden, Einnahmen/Ausgaben Chart, Jahres-Depotrendite.
- Benutzer & Rollen: Registrierung, erster Benutzer = Admin, Konto-Sharing, Sperren/Entsperren, Löschung (mit Datenbereinigung / -übertrag Workflow geplant).
- Internationalisierung: Deutsch & Englisch (Fallback-Kette Benutzer ? Browser ? Deutsch).
- Adminbereich: Benutzerverwaltung (Anlegen, Bearbeiten, Sperren/Entsperren, Löschen) & Audit Logs.
- Einheitliches Menüband (Ribbon-Komponente) für Aktionsschaltflächen auf Detailseiten: Gruppierung, Tastatur-Navigation (Arrow Keys), ARIA Rollen (tablist, toolbar, group) & Zustände (aria-selected, aria-disabled) für verbesserte Bedienbarkeit und Tests.

## 3. Architektur & Schichten (geplant)
```
Presentation (Blazor Server / künftig MAUI)
  ?? Application Layer (Use Cases, DTO Mapping, Orchestrierung)
       ?? Domain Layer (Entities, Value Objects, Invarianten, Domain Services)
            ?? Infrastructure (EF Core, AlphaVantage Client, Import Parser, Logging, Security)
Shared Library (Domain + Contracts) für Wiederverwendung in Blazor + MAUI.
```
Querschnittsthemen: Logging, Auth (JWT), Internationalisierung, Caching, Validation, Rate Limiting.

## 4. Technologie-Stack
- .NET 9, C# 13 (Preview je nach SDK)
- Blazor Server (Razor Components)
- (Geplant) .NET MAUI iOS
- EF Core (relationale DB, Provider TBD)
- AlphaVantage API (Wertpapierkurse; API-Key optional)
- Auth: JWT (kurzlebig), Passwort-Hash Argon2id/bcrypt
- Logging: ILogger Abstraktion (Serilog)
- Validation: DataAnnotations / FluentValidation (geplant)
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
  - FluentAssertions — https://www.nuget.org/packages/FluentAssertions/
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
- Username/Passwort ? JWT Token (Bearer) für API Calls.
- Konto-Sharing mit granularen Rechten (Lesen/Schreiben; Admin Modell offen).
- Duplikatserkennung bei Import (Hash / zusammengesetzte Schlüssel) verhindert Doppelbuchung.
- Audit Logging: sicherheitsrelevante Aktionen (Login, Sharing, Admin-Operationen).
- Rate Limiting & Retry Policy für Kursabrufe.

## 7. Internationalisierung
- Sprachen: de, en.
- Benutzerpräferenz speicherbar; dynamischer Wechsel ohne Re-Login.
- Fallback-Kette: Benutzer > Browser > de.
- Alle UI-Texte via Ressourcen – keine Hardcoded Strings.

## 8. Installation

Siehe Installationsanleitung in `docs/install.md`.

## 9. Entwicklung & Build
### Voraussetzungen
- .NET 9 SDK
- Optional: AlphaVantage API?Key (`AlphaVantage:ApiKey` bzw. ENV `ALPHAVANTAGE__APIKEY`). Ohne Key werden keine Kurse abgerufen.

### Lokaler Start
```bash
dotnet restore
dotnet build
cd FinanceManager.Web
dotnet run
```
Standard URL: https://localhost:5001 (HTTPS) / http://localhost:5000 (HTTP)

### Tests
```bash
dotnet test
```
- Unit-Tests unter `FinanceManager.Tests` (xUnit, FluentAssertions).

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
- OP-014: Erweiterte Accessibility-Audits (Focus Management, High Contrast, Screenreader-Flow) – Teil 1 umgesetzt (Ribbon ARIA Semantik, explicit `aria-disabled` Werte, Tastaturnavigation Tabs).

## 11. Accessibility (Barrierefreiheit)
Aktueller Stand (inkrementell):
- Ribbon-Komponente: ARIA Roles `tablist`, `tab`, `tabpanel`, `toolbar`, `group` implementiert.
- Zustände: `aria-selected` für aktiven Tab, `aria-disabled="true"` für deaktivierte Aktionen; gleichzeitig natives `disabled` Attribut zur konsistenten Tastatur- und Screenreader-Erkennung.
- Tastatur: Horizontaler Tabwechsel (Pfeiltasten) vorbereitet; weitere Shortcuts geplant.
- Beschriftungen: Gruppen via `aria-labelledby`, Buttons mit `aria-label` wenn Text vorhanden.
Geplante nächste Schritte:
- Fokus-Ring & sichtbare Fokusreihenfolge optimieren.
- Skip-Link / Landmark Regions für Hauptbereiche.
- Farbkontrastprüfung (WCAG AA) automatisieren.

## 12. Lizenz / Status
- Aktueller Status: Entwurf / frühe Implementierung.
- Lizenz: (noch nicht festgelegt) – bitte hinzufügen (z.B. MIT).

---
Für Details siehe: `Anforderungskatalog.md` & `.github/copilot-instructions.md`.