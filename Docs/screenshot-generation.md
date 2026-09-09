# Screenshot-Erzeugung

Diese Anleitung dokumentiert, wie die Screenshots und das animierte GIF in der README erzeugt wurden, damit der Vorgang bei neuen Features wiederholt werden kann.

## Voraussetzungen

- [.NET SDK 10](https://dotnet.microsoft.com)
- [Node.js](https://nodejs.org/) (22.x)
- Chromium-Browser für Playwright

## Verwendete Tools

| Tool | Zweck |
|---|---|
| `dotnet publish` | Erzeugt die Release-Ausgabe von `FinanceManager.Web` mit korrekt ausgelieferten statischen Assets. |
| `playwright` (Node.js) | Startet Chromium headless, führt Registrierung mit Demodaten und Login durch und erstellt die Screenshots. |
| `pngjs` | Dekodiert die PNG-Dateien für die GIF-Erzeugung. |
| `gifenc` | Erzeugt das animierte GIF aus den einzelnen Screenshots. |

Die Skripte liegen unter [`scripts/screenshots/`](../scripts/screenshots/):

- [`generate-screenshots.js`](../scripts/screenshots/generate-screenshots.js) – erstellt die PNGs.
- [`make-gif.js`](../scripts/screenshots/make-gif.js) – erzeugt `Docs/screenshots/demo.gif`.

## Ablauf

1. **Abhängigkeiten installieren**

   ```bash
   cd scripts/screenshots
   npm install
   # Einmalig Chromium für Playwright bereitstellen:
   npx playwright install chromium
   ```

2. **Anwendung veröffentlichen**

   ```bash
   dotnet publish FinanceManager.Web -c Release
   ```

3. **Screenshots erstellen**

   ```bash
   cd scripts/screenshots
   node generate-screenshots.js
   ```

   Das Skript startet die veröffentlichte Anwendung auf einem zufälligen Port mit einer temporären SQLite-Datenbank, registriert den ersten Benutzer über die echte UI mit aktivierter Demodaten-Checkbox, meldet sich anschließend über die Login-Seite an und wartet, bis der Background-Task für die Demodaten abgeschlossen ist. Danach werden nacheinander Screenshots der gewünschten Seiten aufgenommen. Alle Bilder werden in `Docs/screenshots/` gespeichert.

4. **GIF erzeugen**

   ```bash
   node make-gif.js
   ```

   Das Skript liest die PNGs aus `Docs/screenshots/`, schneidet sie auf 1280×900 Pixel zu und schreibt `Docs/screenshots/demo.gif` mit einer Bild-Anzeigezeit von 2 Sekunden pro Frame.

## Aufgenommene Seiten

| Datei | Seite |
|---|---|
| `register.png` | `/register` – Registrierung mit Demodaten-Checkbox |
| `home.png` | `/` – Startseite mit KPI-Kacheln |
| `accounts.png` | `/list/accounts` – Kontenübersicht |
| `statement-drafts.png` | `/list/statement-drafts` – offene Kontoauszugs-Entwürfe |
| `contacts.png` | `/list/contacts` – Kontakte |
| `savings-plans.png` | `/list/savings-plans` – Sparpläne |
| `securities.png` | `/list/securities` – Wertpapiere |
| `budget-purposes.png` | `/list/budget/purposes` – Budgetzwecke |
| `reports.png` | `/reports` – Berichtsfavoriten |
| `budget-report.png` | `/reports/budget` – Budgetbericht |

## Wichtige Details

- **Viewport:** Die Aufnahmen verwenden einen Viewport von 1280×900 Pixel (`fullPage: false`), so dass alle Bilder den sichtbaren Bereich in derselben Größe zeigen. Für das GIF werden alle Bilder in `make-gif.js` auf 1280×900 zugeschnitten.
- **Sprache und Zeitzone:** Der Browser-Kontext läuft mit Locale `de-DE` und Zeitzone `Europe/Berlin`, damit UI und Demodaten deutsch erscheinen.
- **Wartebedingungen:** Es wird nicht auf `networkidle` gewartet, weil Blazor Server eine offene WebSocket-Verbindung hält. Stattdessen werden seiten-spezifische Selektoren genutzt (z. B. `.kpi-grid .kpi-tile` auf der Startseite oder eine Mindestzahl an `.fm-table`-Zeilen auf Listenseiten).
- **Demodaten:** Der erste registrierte Benutzer erhält auf der Registrierungsseite die Checkbox „Demodaten anlegen" (`#create-demo-data`). Die Registrierung löst den Background-Task `CreateDemoData` aus, der Stammdaten, Konten, Wertpapiere, Budgets und Kontoauszugs-Buchungen für 24 Monate anlegt. Das Skript fragt `GET /api/background-tasks/active` ab, bis keine aktiven Tasks mehr vorhanden sind.
- **Login:** Die Registrierung authentifiziert den Browser nicht selbst, deshalb meldet das Skript sich anschließend über `/login` an. Der `FinanceManager.Auth`-Cookie enthält das JWT und wird auch als Bearer-Token für die Statusabfrage der Background-Tasks verwendet.
- **Umgebung:** Der App-Prozess läuft mit `ASPNETCORE_ENVIRONMENT=Development`, `E2E__DisableHttpsRedirection=true` und deaktivierten Hintergrund-Workern (`Workers__SecurityPriceWorker__Enabled=false`, `Updates__HostedServicesEnabled=false`), damit keine externen Dienste nötig sind.
- **Temporäre Datenbank:** Das Skript legt für jeden Lauf eine neue SQLite-Datenbank im System-Temp-Verzeichnis an und entfernt sie danach. Bei einem Fehler bleibt das Verzeichnis samt `app.log` zum Debuggen erhalten und der Pfad wird ausgegeben.

## Erweiterung für neue Features

1. In `generate-screenshots.js` eine neue `page.goto`/`page.screenshot`-Sequenz ergänzen.
2. Die neue PNG-Datei in das `frames`-Array in `make-gif.js` eintragen, damit sie auch im GIF erscheint.
3. Die README ggf. aktualisieren.
