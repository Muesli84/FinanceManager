# Umsetzungsplan: GitHooks

## Ziel

Hooks aus `Pattern-Collection` übernehmen (erledigt: `.githooks/`, `core.hooksPath`) und alle daraus resultierenden Prüffehler im Anwendungscode beheben, ohne die Prüfungen zu entschärfen.

## Arbeitspakete (thematisch getrennt)

### P1 – translation-check (1 Befund)
- resx-Key `Merge_Preference_LABEL` in den passenden Ressourcendateien (alle Kulturvarianten des Pakets) ergänzen.
- Prüfen: `python .githooks/translation-check.py --all` → OK.

### P2 – csproj-xmldoc-check (~2586 Zeilen Befunde)
- Alle fehlenden `<param>`/`<typeparam>`/`<returns>`/`<response>`-Tags ergänzen (ausschließlich Dokumentation, kein Codeverhalten).
- Ggf. `#pragma warning disable` für XML-Doc-Codes entfernen, fehlende `GenerateDocumentationFile`-Konfiguration in .csproj ergänzen.
- Vorgehen: dateiweise, danach Check erneut laufen lassen, bis sauber.
- Prüfen: `python .githooks/csproj-xmldoc-check.py --all` → OK.

### P3 – razor-l10n-check (~10 Dateien)
- Hartcodierte UI-Strings durch `@L["Key"]`/Localizer-Aufrufe ersetzen; Keys in resx-Paketen (neutral + Sprachvarianten) ergänzen.
- Begründete Ausnahmen (z. B. `Error.razor` ohne Localizer-Kontext) über vorhandene Lokalisierungsinfrastruktur lösen, nicht durch Ignorieren.
- Prüfen: `python .githooks/razor-l10n-check.py --all` → OK.

### P4 – razor-usage-check (31 Befunde)
- Pro gemeldeter Komponente prüfen:
  a) Tatsächlich verwendet, aber nur qualifiziert/über C# referenziert → unqualifizierte Referenz in `.razor` herstellen (z. B. `typeof(MainLayout)` mit Namespace-Import) oder Verwendung als Tag ergänzen.
  b) Tatsächlich verwaist → Komponente inkl. Code-Behind löschen.
- Prüfen: `python .githooks/razor-usage-check.py --all --strict` → OK.

### P5 – no-notimplemented-check (Test-Stubs)
- Test-Helper/Stubs in `FinanceManager.Tests` und `FinanceManager.Tests.Integration` vollständig implementieren (sinnvolle Default-Implementierung statt `throw`) oder entfernen.
- Prüfen: `python .githooks/no-notimplemented-check.py --all --strict` → OK.

### P6 – enum-coverage-check (~31 Enums)
- Testdatei(en) ergänzen, die jeden Enum-Wert benennen (z. B. `EnumCoverageTests` mit `Assert.True(Enum.IsDefined(...))` je Wert, sinnvoll gruppiert pro Enum).
- Prüfen: `python .githooks/enum-coverage-check.py --all --strict` → OK.

## Tests / Abnahme

- Jeder Check einzeln: Exit-Code 0 (strict, `--all`).
- `dotnet build FinanceManager.sln` erfolgreich.
- `dotnet test` für betroffene Testprojekte erfolgreich (mind. FinanceManager.Tests).
- Vollständiger Commit-Lauf des `pre-commit`-Hooks schlägt nicht fehl.
- Kein UI-Flow betroffen → keine E2E-Tests erforderlich; negative Kriterien: keine Änderungen an `.githooks/*` zur Abschwächung; keine gelöschten produktiven Komponenten ohne Ersatz.

## Offene Punkte

- Keine.
