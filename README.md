# FinanceManager (Arbeitsname)

> Persönliche Finanzverwaltung mit Kontoauszug-Import, Sparplänen, Wertpapiertracking, Auswertungen & Mehrbenutzer?/Sharing-Funktionen.
>
> Hinweis: Projektname vs. "Finanzverwaltung" noch zu vereinheitlichen (siehe Offene Punkte OP-002).

## Inhalt
1. Überblick
2. Kernfunktionen
3. Architektur & Schichten
4. Technologie-Stack
5. Roadmap (Wellen)
6. Authentifizierung & Sicherheit (Kurz)
7. Internationalisierung (i18n)
8. Installation
9. Entwicklung & Build
10. Geplante Erweiterungen / Offene Punkte
11. Lizenz / Status

## 1. Überblick
FinanceManager ist eine Blazor Server Anwendung (.NET 9) zur Verwaltung persönlicher Finanzen. Importierte Kontoauszüge (CSV/PDF) werden verarbeitet, Buchungen kategorisiert und in Bank-/Kontakt-/Sparplan- und Wertpapierposten überführt. Ergänzend existieren Auswertungen (Monat, Quartal, Jahr, YTD) und ein KPI-Dashboard. Mehrere Benutzer werden unterstützt; Bankkonten können gezielt geteilt werden. Eine .NET MAUI App (iOS) ist geplant.

## 2. Kernfunktionen
- Konten: Giro- & Sparkonten, Bankkontakt-Automatik, Sharing (Lese / optional Schreibrechte).
- Kontoauszug-Import: CSV & PDF (Strategy Pattern), Duplikatserkennung, Buch.-Blatt vor endgültiger Verbuchung, Alias-Matching für Kontakte.
- Kontakte & Kategorien: Verwaltung inkl. Aliasnamen (Wildcards ? *). Automatische Zuordnung beim Import.
- Sparpläne: Einmalig (Zielbetrag/Zieldatum), wiederkehrend (Intervalle), offen. Automatische Erkennung bei Eigen-Sparkonto-Zahlungen, Zielstatus (erreicht / nahe / verfehlt) & Archivierung.
- Wertpapiere: Aktien/Fonds, Transaktionen (Kauf, Verkauf, Dividende/Zins, Gebühren, Steuern, Menge). Menge wird bei Buchung gespeichert (Buy positiv, Sell negativ, Dividend ohne Menge). Kursdienst/Renditen in Planung.
- Auswertungen: Aggregationen Monat / Quartal / Jahr, YTD, Vorjahresvergleich, P&L nach Kategorien.
- KPI Dashboard: Monatliche/Jährliche Dividenden, Einnahmen/Ausgaben Chart, Jahres-Depotrendite.
- Benutzer & Rollen: Registrierung, erster Benutzer = Admin, Konto-Sharing, Sperren/Entsperren, Löschung (mit Datenbereinigung / -übertrag Workflow geplant).
- Internationalisierung: Deutsch & Englisch (Fallback-Kette Benutzer ? Browser ? Deutsch).
- Adminbereich: Benutzerverwaltung (Anlegen, Bearbeiten, Sperren/Entsperren, Löschen) & Audit Logs.

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

- Voraussetzungen allgemein:
  - .NET 9 Runtime (Hosting Bundle für Windows, ASP.NET Core Runtime für Linux)
  - Datenbankzugriff konfigurieren (ConnectionStrings in `appsettings.Production.json`)

- Windows
  - Veröffentlichung: `dotnet publish FinanceManager.Web -c Release -o .\publish`
  - Start (interaktiv):
    ```bash
    cd .\publish
    dotnet FinanceManager.Web.dll
    ```
  - Port-Konfiguration (optional): per Umgebungsvariable `ASPNETCORE_URLS` (z.B. `http://localhost:5000`) oder via `appsettings.Production.json` (Kestrel ? Endpoints).

- Linux (systemd)
  - Veröffentlichung auf dem Build-Rechner und Kopie nach `/opt/financemanager`:
    ```bash
    dotnet publish FinanceManager.Web -c Release -o ./publish
    sudo mkdir -p /opt/financemanager
    sudo cp -r ./publish/* /opt/financemanager/
    sudo chown -R www-data:www-data /opt/financemanager # Benutzer/Gruppe bei Bedarf anpassen
    ```
  - Port in der Konfiguration setzen (eine der beiden Varianten):
    - Umgebungsvariable (empfohlen): `ASPNETCORE_URLS=http://0.0.0.0:5005`
    - Oder `appsettings.Production.json` in `/opt/financemanager`:
      ```json
      {
        "Kestrel": {
          "Endpoints": {
            "Http": { "Url": "http://0.0.0.0:5005" }
          }
        }
      }
      ```
  - systemd-Unit `/etc/systemd/system/financemanager.service`:
    ```ini
    [Unit]
    Description=FinanceManager (Blazor Server)
    After=network.target

    [Service]
    WorkingDirectory=/opt/financemanager
    ExecStart=/usr/bin/dotnet /opt/financemanager/FinanceManager.Web.dll
    Restart=always
    RestartSec=10
    SyslogIdentifier=financemanager
    User=www-data
    Environment=ASPNETCORE_ENVIRONMENT=Production
    Environment=ASPNETCORE_URLS=http://0.0.0.0:5005

    [Install]
    WantedBy=multi-user.target
    ```
  - Dienst aktivieren/starten:
    ```bash
    sudo systemctl daemon-reload
    sudo systemctl enable financemanager
    sudo systemctl start financemanager
    sudo systemctl status financemanager -n 100
    ```
  - Optional: Reverse Proxy (nginx/Apache) für TLS/Domain vor den Dienst schalten.

## 9. Entwicklung & Build
### Voraussetzungen
- .NET 9 SDK
- Node/NPM (optional für Build Pipelines künftiger Frontend Assets)
- API Key für AlphaVantage (User Secrets / ENV VAR `ALPHAVANTAGE__APIKEY`)

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

## 11. Lizenz / Status
- Aktueller Status: Entwurf / frühe Implementierung.
- Lizenz: (noch nicht festgelegt) – bitte hinzufügen (z.B. MIT).

---
Für Details siehe: `Anforderungskatalog.md` & `.github/copilot-instructions.md`.