# Plan-Check: GitHooks

**Status: Plan vollständig**

## Geprüfte Punkte

- Alle Akzeptanzkriterien aus `requirement.md` sind Arbeitspaketen zugeordnet (Hooks übernommen, alte ersetzt, Fehlerkorrektur ohne Abschwächung).
- Jeder Hook-Check hat ein eigenes Paket mit klarer Abnahme (`Exit 0` im strict/`--all`-Lauf).
- Negative Kriterien benannt: keine Änderung an `.githooks/*` zur Abschwächung, kein Löschen produktiver Komponenten ohne Prüfung.

## Risiken und Hinweise (keine Blocker)

1. **razor-usage-check (P4)**: Erkennt nur unqualifizierte Referenzen in `.razor`-Dateien. `typeof(Layout.MainLayout)` in `Routes.razor` wird als Verweis nicht erkannt → Lösung: Namespace-Import + unqualifizierte Referenz. Echte Waisen erst nach manueller Verwendungsprüfung (inkl. `.razor.cs`, ViewModels, DI-Registrierungen) löschen.
2. **enum-coverage-check (P6)**: Textueller Abgleich — Testwerte müssen als Literale/`nameof` in Testdateien stehen; `Enum.IsDefined`-basierte Tests sind echte Tests und erfüllen den Check.
3. **no-notimplemented-check (P5)**: Befunde liegen ausschließlich in Testprojekten → minimale echte Implementierungen (Defaults/Mocks), kein Risiko für Produktivcode.
4. **csproj-xmldoc-check (P2)**: Größtes Paket, rein mechanisch; zusätzlich zu `<param>`-Tags auch `.csproj`-Konfiguration (`GenerateDocumentationFile`, keine CS1591-Unterdrückung via `NoWarn`/`#pragma`) prüfen.
5. **razor-l10n-check (P3)**: `Error.razor` o. ä. könnten ohne Localizer-Infrastruktur sein → pro Komponente prüfen, wie Lokalisierung angebunden ist; keine Strings ungeprüft als "nicht lokalisierbar" durchwinken.
6. **Reihenfolge**: P1/P3 (resx) und P4 (ggf. neue/verschobene Referenzen) können neue `.razor`-/`.cs`-Änderungen erzeugen → nach Abschluss aller Pakete Gesamtlauf aller Checks wiederholen.
