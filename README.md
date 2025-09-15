# FinanceManager (Arbeitsname)

> Pers�nliche Finanzverwaltung mit Kontoauszug-Import, Sparpl�nen, Wertpapiertracking, Auswertungen & Mehrbenutzer?/Sharing-Funktionen.
>
> Hinweis: Projektname vs. "Finanzverwaltung" noch zu vereinheitlichen (siehe Offene Punkte OP-002).

## Inhalt
1. �berblick
2. Kernfunktionen
3. Architektur & Schichten
4. Technologie-Stack
5. Roadmap (Wellen)
6. Authentifizierung & Sicherheit (Kurz)
7. Internationalisierung (i18n)
8. Entwicklung & Build
9. Geplante Erweiterungen / Offene Punkte
10. Lizenz / Status

## 1. �berblick
FinanceManager ist eine Blazor Server Anwendung (.NET 9) zur Verwaltung pers�nlicher Finanzen. Importierte Kontoausz�ge (CSV/PDF) werden verarbeitet, Buchungen kategorisiert und in Bank-/Kontakt-/Sparplan- und Wertpapierposten �berf�hrt. Erg�nzend existieren Auswertungen (Monat, Quartal, Jahr, YTD) und ein KPI-Dashboard. Mehrere Benutzer werden unterst�tzt; Bankkonten k�nnen gezielt geteilt werden. Eine .NET MAUI App (iOS) ist geplant.

## 2. Kernfunktionen
- Konten: Giro- & Sparkonten, Bankkontakt-Automatik, Sharing (Lese / optional Schreibrechte).
- Kontoauszug-Import: CSV & PDF (Strategy Pattern), Duplikatserkennung, Buch.-Blatt vor endg�ltiger Verbuchung, Alias-Matching f�r Kontakte.
- Kontakte & Kategorien: Verwaltung inkl. Aliasnamen (Wildcards ? *). Automatische Zuordnung beim Import.
- Sparpl�ne: Einmalig (Zielbetrag/Zieldatum), wiederkehrend (Intervalle), offen. Automatische Erkennung bei Eigen-Sparkonto-Zahlungen, Zielstatus (erreicht / nahe / verfehlt) & Archivierung.
- Wertpapiere: Aktien/Fonds, Transaktionen (Kauf, Verkauf, Dividende/Zins, Geb�hren, Steuern, Menge). Menge wird bei Buchung gespeichert (Buy positiv, Sell negativ, Dividend ohne Menge). Kursdienst/Renditen in Planung.
- Auswertungen: Aggregationen Monat / Quartal / Jahr, YTD, Vorjahresvergleich, P&L nach Kategorien.
- KPI Dashboard: Monatliche/J�hrliche Dividenden, Einnahmen/Ausgaben Chart, Jahres-Depotrendite.
- Benutzer & Rollen: Registrierung, erster Benutzer = Admin, Konto-Sharing, Sperren/Entsperren, L�schung (mit Datenbereinigung / -�bertrag Workflow geplant).
- Internationalisierung: Deutsch & Englisch (Fallback-Kette Benutzer ? Browser ? Deutsch).
- Adminbereich: Benutzerverwaltung (Anlegen, Bearbeiten, Sperren/Entsperren, L�schen) & Audit Logs.

## 3. Architektur & Schichten (geplant)
```
Presentation (Blazor Server / k�nftig MAUI)
  ?? Application Layer (Use Cases, DTO Mapping, Orchestrierung)
       ?? Domain Layer (Entities, Value Objects, Invarianten, Domain Services)
            ?? Infrastructure (EF Core, AlphaVantage Client, Import Parser, Logging, Security)
Shared Library (Domain + Contracts) f�r Wiederverwendung in Blazor + MAUI.
```
Querschnittsthemen: Logging, Auth (JWT), Internationalisierung, Caching, Validation, Rate Limiting.

## 4. Technologie-Stack
- .NET 9, C# 13 (Preview je nach SDK)
- Blazor Server (Razor Components)
- (Geplant) .NET MAUI iOS
- EF Core (relationale DB, Provider TBD)
- AlphaVantage API (Wertpapierkurse)
- Auth: JWT (kurzlebig), Passwort-Hash Argon2id/bcrypt
- Logging: ILogger Abstraktion (Serilog geplant)
- Validation: DataAnnotations / FluentValidation (geplant)
- Internationalisierung: resx Ressourcen, CultureInfo
- Build/Format: dotnet CLI, EditorConfig, Analyzers/StyleCop
- Tests: xUnit, FluentAssertions (Projekt `FinanceManager.Tests`)

## 5. Roadmap (Wellen)
| Welle | Fokus |
|-------|-------|
| 1 | Benutzer & Auth-Basis, Konten, Kontakte, Import (CSV/PDF), Buchung, Alias, Basis-Dashboard |
| 2 | Sparpl�ne + Statuslogik, Erweiterte Auswertungen |
| 3 | Wertpapiere + Kursdienst + Renditen |
| 4 | i18n Verfeinerung, zus�tzliche Importformate, Admin-Erweiterungen, Optimierungen |

## 6. Authentifizierung & Sicherheit (Kurz)
- Username/Passwort ? JWT Token (Bearer) f�r API Calls.
- Konto-Sharing mit granularen Rechten (Lesen/Schreiben; Admin Modell offen).
- Duplikatserkennung bei Import (Hash / zusammengesetzte Schl�ssel) verhindert Doppelbuchung.
- Audit Logging: sicherheitsrelevante Aktionen (Login, Sharing, Admin-Operationen).
- Rate Limiting & Retry Policy f�r Kursabrufe.

## 7. Internationalisierung
- Sprachen: de, en.
- Benutzerpr�ferenz speicherbar; dynamischer Wechsel ohne Re-Login.
- Fallback-Kette: Benutzer > Browser > de.
- Alle UI-Texte via Ressourcen � keine Hardcoded Strings.

## 8. Entwicklung & Build
### Voraussetzungen
- .NET 9 SDK
- Node/NPM (optional f�r Build Pipelines k�nftiger Frontend Assets)
- API Key f�r AlphaVantage (User Secrets / ENV VAR `ALPHAVANTAGE__APIKEY`)

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

### Code-Qualit�t
- `dotnet format` vor Pull Request.

## 9. Geplante Erweiterungen / Offene Punkte (Auszug)
- OP-002: Konsolidierung Projekt-/Produktname.
- OP-003: Finale Wahl Auth (Identity Integration vs. eigenes Minimal-Setup).
- OP-009: Rollenmodell f�r Konto-Freigaben (erweitert?).
- OP-010: Ownership Transfer bei L�schung Prim�rbesitzer.
- OP-011: Audit Log Aufbewahrung & DSGVO.
- OP-012: Weitere Importformate (MT940, CAMT). 
- OP-013: Persistenter Sprachwechsel (Client State / Server Profil).

## 10. Lizenz / Status
- Aktueller Status: Entwurf / fr�he Implementierung.
- Lizenz: (noch nicht festgelegt) � bitte hinzuf�gen (z.B. MIT).

---
F�r Details siehe: `Anforderungskatalog.md` & `.github/copilot-instructions.md`.