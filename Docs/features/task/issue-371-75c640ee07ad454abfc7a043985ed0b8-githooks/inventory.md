# Bestandsaufnahme: GitHooks

## Ausgangslage

- **Repository**: FinanceManager (.NET, Blazor Server, Solution `FinanceManager.sln`)
- **Bestehende Hooks** (veraltet): `githooks/` enthält `pre-commit` (dotnet format + translation-check), `install-hooks.cmd/.sh`, `translation-check.py`
- **Referenz**: `Pattern-Collection` (Commit 9683e0c) liefert `Git-Hooks/githooks/` mit 10 Dateien:
  `pre-commit`, `pre-push`, `translation-check.py`, `csproj-xmldoc-check.py`, `razor-l10n-check.py`,
  `razor-usage-check.py`, `no-notimplemented-check.py`, `enum-coverage-check.py`, `install-hooks.cmd`, `install-hooks.sh`
- Die neuen Skripte erwarten den Hook-Pfad `.githooks/` (`core.hooksPath = .githooks`).
- Python 3.13.14 und dotnet SDK sind verfügbar.
- Keine `SecretScan.csproj`/`MarkdownLinkCheck.csproj` im Repo → diese Schritte werden vom Hook übersprungen.

## Bereits durchgeführt (Schritt vor Planung, technische Vorbereitung)

- `.githooks/` mit allen 10 Dateien aus dem Referenz-Repo angelegt
- altes Verzeichnis `githooks/` entfernt (git rm)
- `git config --local core.hooksPath .githooks` gesetzt

## Befundlage (vollständige Läufe mit `--all` bzw. `--strict`)

| Check | Befunde |
|-------|---------|
| translation-check | 1 fehlender resx-Key: `Merge_Preference_LABEL` in `FinanceManager.Web/Components/Shared/ContactMergePanel.razor` |
| csproj-xmldoc-check | ~2586 Ausgabezeilen: fehlende `<param>`/`<typeparam>`/`<returns>`/`<response>`-Tags in Application, Domain, Infrastructure, Web u. a.; ggf. Pragma-/NoWarn-Verstöße |
| razor-l10n-check | Hartcodierte UI-Strings in ~10 `.razor`-Dateien (QuickEditTable, HelpLayout, MainLayout, Error, GenericCardPage, GenericListPage, BenchmarkTab, OverviewTab, TimeSeriesTab, SetupBackupTab, …) |
| razor-usage-check (strict) | 31 als "verwaist" gemeldete Komponenten. **Teils Fehlalarm**: Check erkennt nur unqualifizierte Referenzen in `.razor`-Dateien (`<Name`, `@layout Name`, `typeof(Name)`); `typeof(Layout.MainLayout)` u. ä. werden nicht erkannt |
| no-notimplemented-check (strict) | Stub-Implementierungen (`=> throw new NotImplementedException()` u. a. Single-throw-Member) in Testprojekten: `FinanceManager.Tests` (StubAccountService, RequestLoggingMiddlewareTests, StatementDraftBookingTests), `FinanceManager.Tests.Integration` (UpdateControllerIntegrationTests) |
| enum-coverage-check (strict) | ~31 Enums ohne (vollständige) Testabdeckung, u. a. `StatementEntryStatus`, `ReportEntityGroup`, `SavingsPlanInterval`, `ChartTimeRange`, `ImportFormat`, `NotificationTarget/Type`, `BackupApplyStatus`, `PostingExportFormat` |

## Erkenntnisse zu den Checks

- Keine Suppression-/Ignore-Mechanismen in den Skripten → alle Befunde müssen im Code behoben werden.
- `razor-usage-check`: Komponenten mit `@page`, `App.razor`, `Routes.razor`, `_*.razor` sind ausgenommen. Referenzen werden nur in `.razor`-Dateien desselben Projekts gesucht. Fehlalarme lassen sich beheben, indem Referenzen unqualifiziert geschrieben werden (z. B. `typeof(MainLayout)` statt `typeof(Layout.MainLayout)`, sofern Namespace importiert).
- `enum-coverage-check`: Enum-Wert-Namen müssen **textuell** in Testdateien (`*Test*`/`Tests`-Verzeichnisse) vorkommen.
- `no-notimplemented-check`: Auch Test-Stubs sind verboten → Test-Helper müssen echte Implementierungen erhalten oder entfernt werden.
- `csproj-xmldoc-check`: erzwingt `GenerateDocumentationFile`/CS1591-Konfiguration und vollständige XML-Doc-Tags.
