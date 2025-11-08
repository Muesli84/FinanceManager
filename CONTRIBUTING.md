# Contributing

Kurz und knapp: Bitte halte dich an die Projekt-Richtlinien, damit Änderungen konsistent und wartbar bleiben.

## Ressourcen / Lokalisation (resx)
- Platzierung: Alle `.resx`-Dateien gehören unter das `Resources`-Verzeichnis des betroffenen Projekts und zwar in Unterordnern, die dem Namespace der konsumierenden Klasse/Komponente entsprechen.
  - Beispiel: Die Komponente `Components.Pages.StatementDraftDetail` im Projekt `FinanceManager.Web` bekommt ihre Ressourcen unter
    `FinanceManager.Web/Resources/Components/Pages/StatementDraftDetail.resx` und die Kulturvariante `FinanceManager.Web/Resources/Components/Pages/StatementDraftDetail.de.resx`.
- Dateinamen:
  - Standardkultur: `{TypeName}.resx` (z. B. `StatementDraftDetail.resx`)
  - Kulturvarianten: `{TypeName}.{culture}.resx` (z. B. `StatementDraftDetail.de.resx`)
- Benennung der Schlüssel: sprechend und einheitlich, z. B. `Ribbon_AccountDetails`.
- Konsumieren in Code (Blazor/Services): Verwende `IStringLocalizer<T>` mit demselben Typ `T`, für den die Ressource gedacht ist. Beispiel:
  ```csharp
  public class StatementDraftDetail // oder razor component class
  {
      private readonly IStringLocalizer<Components.Pages.StatementDraftDetail> _L;
      public StatementDraftDetail(IStringLocalizer<Components.Pages.StatementDraftDetail> localizer) => _L = localizer;
  }
  ```
- Projektkonfiguration: Stelle sicher, dass `Program.cs`/Startup `services.AddLocalization(options => options.ResourcesPath = "Resources");` setzt.

## Pull Requests
- Prüfe vor dem Erstellen eines PRs, dass keine neuen `*.resx`-Dateien an unerwarteten Orten liegen. Nutze die bestehende Namespace-/Ordner-Struktur.
- Beschreibe im PR-Text, welche Ressourcen hinzugefügt oder geändert wurden und für welche Komponenten/Typen sie gedacht sind.

## CI / Checks (Empfehlung)
- Füge wenn möglich einen CI-Check hinzu, der sicherstellt, dass neue `resx`-Dateien unter `Resources/` liegen und dass der Pfad dem Namespace-Pattern entspricht (z. B. `Resources/**/<Namespace-as-folders>/**.resx`). Wir akzeptieren gern Hilfestellung für eine passende GitHub Action.

