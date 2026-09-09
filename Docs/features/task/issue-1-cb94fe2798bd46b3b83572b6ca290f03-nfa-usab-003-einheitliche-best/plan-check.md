# Plan-Review: NFA-USAB-003

## Status

**Plan vollständig.**

## Prüfung gegen Anforderung

| Anforderung | Im Plan abgedeckt |
|-------------|-------------------|
| Einheitliche Bestätigungsdialoge für kritische Aktionen | Ja – `IConfirmationService` + `ConfirmDialog.razor` |
| Globale Option zur Deaktivierung | Ja – `ShowConfirmations` Property auf `User` + Settings-Seite |
| Default `true` für neue Benutzer | Ja – EF-Mapping `HasDefaultValue(true)` + Code-Default |
| Zentraler Service, kein Copy-Paste | Ja – alle ViewModels/Razor-Codebehind rufen `ConfirmationService.ConfirmAsync` auf |
| Lokalisierung (de/en) | Ja – Ressourcen-Keys in `Pages.resx`, `Pages.de.resx`, `Pages.en.resx` |
| Barrierefreiheit / Fokus | Ja – `FocusAsync()` auf Cancel-Button, ARIA-Attribute |
| Testbarkeit | Ja – Interface abstrahiert, Unit-Tests für `ConfirmationService`, `ConfirmDialog` (bUnit), Integrationstests |
| Logging nur bei Ausführung | Ja – `ConfirmationService.SetResult(true)` loggt auf `Information` |
| Keine Inline-Hardcodierung | Ja – Resource-Keys |

## Prüfung technischer Lücken

- **Architektur:** `IConfirmationService` ist im Web-Layer (`FinanceManager.Web/Services`) angesiedelt, UI-agnostisch durch Interface und mockbar.
- **Dialog-Lebenszyklus:** `TaskCompletionSource<bool>`-Muster in `ConfirmationService` plus globaler `ConfirmationDialogHost` in `MainLayout` beschrieben.
- **Settings-Cache:** `ConfirmationService` cached `ShowConfirmations` per scoped Service; `SetupProfileViewModel` invalidiert nach Speichern.
- **Zwei BaseViewModel-Klassen:** Accessor in `BaseViewModel` (Common) und `ViewModelBase` vorgesehen.
- **Massenaktionen:** Explizit als universell von `ShowConfirmations` abhängig definiert.
- **Severity-Enum:** Minimal `Default/Warning/Critical` vorgesehen.

## Hinweise / Risiken (keine Lücken, aber Beobachtungspunkte)

1. `ConfirmationService` darf nicht blockieren, wenn `DialogHost` noch nicht initialisiert ist. `DialogHost` prüft bei Initialisierung `CurrentRequest`.
2. `BaseViewModel` und `ViewModelBase` haben duplizierte Basisfunktionalität; `ConfirmationService`-Accessor muss in beiden gepflegt werden.
3. Bei Änderung von `UserProfileSettingsUpdateRequest` (record) muss die Parameter-Reihenfolge beachtet werden; `ShowConfirmations` als letzter optionaler Parameter mit Default `true` einfügen.
4. Migration wird automatisch beim Start angewendet (`ApplyMigrationsAndSeed`).

## Testbedarf geprüft

- Unit-Tests für `ConfirmationService` (Ja)
- bUnit-Tests für `ConfirmDialog` (Ja, bUnit 2.9.0 vorhanden)
- Erweiterung `SetupProfileViewModelTests` (Ja)
- Integrationstests für `UserSettingsController` (Ja)
- E2E/Smoke manuell dokumentiert (Ja)

## Entscheidung

Der Plan kann wie dokumentiert umgesetzt werden.
