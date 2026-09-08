# Umsetzungsplan: NFA-USAB-003 – Einheitliche Bestätigungsabfragen + globale Option

## Zusammenfassung

Einführung eines zentralen `IConfirmationService` im `FinanceManager.Web`-Layer, der vor kritischen/destruktiven/irreversiblen Aktionen einen modalen Bestätigungsdialog (`ConfirmDialog.razor`) öffnet. Eine neue benutzerspezifische Einstellung `ShowConfirmations` (Default `true`) auf der `User`-Entity erlaubt das globale Unterdrücken aller Bestätigungsdialoge. Der Dialog-Mechanismus wird über einen globalen `DialogHost.razor` (im `MainLayout`) realisiert.

## Beantwortung der offenen Fragen aus `requirement.md`

1. **Existiert bereits ein User-Settings-Konzept oder ist eine neue Tabelle nötig?**
   - **Entscheidung:** Das bestehende Einstellungskonzept auf der `User`-Entity wird erweitert. `ShowConfirmations` wird als neues `bool`-Property in `FinanceManager.Domain.Users.User` ergänzt, in `AppDbContext` gemappt (Default `true`) und über `UserProfileSettingsDto` / `UserProfileSettingsUpdateRequest` sowie den `UserSettingsController` ausgelesen/gespeichert. Eine separate `UserPreferences`-Tabelle ist nicht erforderlich.

2. **Sollen Massenaktionen immer bestätigt werden, auch bei deaktivierten globalen Dialogen?**
   - **Entscheidung:** Nein. Die globale Option `ShowConfirmations` gilt universell für alle bestätigungspflichtigen Aktionen, einschließlich Massenaktionen. Wenn der Benutzer die Dialoge deaktiviert, werden alle übersprungen.

3. **Benötigen wir Severity-Stufen für unterschiedliche Icons?**
   - **Entscheidung:** Ja, ein minimales Enum `ConfirmationSeverity { Default, Warning, Critical }` wird im `ConfirmationRequest`-Record verwendet. `ConfirmDialog.razor` kann darauf basierend CSS-Klassen/Icons setzen (Default: kein spezielles Icon, Warning: gelbes Ausrufezeichen, Critical: rotes Icon). Für den ersten Iterationsschritt reicht es, das Enum als API bereitzustellen; visuelle Unterscheidung wird optional mit CSS-Klassen umgesetzt.

## Architekturentscheidungen

### 1. `ShowConfirmations` als `User`-Property

- **Domain:** `FinanceManager.Domain/Users/User.cs`
  - `public bool ShowConfirmations { get; private set; } = true;`
  - Setter-Methode `SetShowConfirmations(bool value)` mit `Touch()`.
- **EF Mapping:** `AppDbContext.cs` im `modelBuilder.Entity<User>(...)`-Block
  - `b.Property(x => x.ShowConfirmations).HasDefaultValue(true).IsRequired();`
- **DTOs:** `UserProfileSettingsDto` und `UserProfileSettingsUpdateRequest` erhalten `bool ShowConfirmations`.
- **Controller:** `UserSettingsController.GetProfileAsync` mapped `ShowConfirmations`; `UpdateProfileAsync` ruft `user.SetShowConfirmations(req.ShowConfirmations)` auf.
- **Migration:** `AddShowConfirmationsToUser` (Code-First, SQLite-kompatibel, Default `true`).

### 2. `IConfirmationService` im Web-Layer

- **Interface:** `FinanceManager.Web/Services/IConfirmationService.cs`
  - `Task<bool> ConfirmAsync(ConfirmationRequest request, CancellationToken ct = default);`
- **Record:** `FinanceManager.Web/Services/ConfirmationRequest.cs`
  - `string TitleResourceKey`, `string MessageResourceKey`, `string? ContextId = null`, `ConfirmationSeverity Severity = ConfirmationSeverity.Default`.
- **Enum:** `FinanceManager.Web/Services/ConfirmationSeverity.cs`
  - `Default`, `Warning`, `Critical`.
- **Implementation:** `FinanceManager.Web/Services/ConfirmationService.cs`
  - Scoped Service.
  - Abhängigkeiten: `IApiClient`, `ILogger<ConfirmationService>`.
  - Liest `ShowConfirmations` per `IApiClient.UserSettings_GetProfileAsync` (lazy, gecached im scoped Service, Default `true` bei Fehlern/keinem Eintrag).
  - Wenn `ShowConfirmations == false` → `Task.FromResult(true)` (sofortige Ausführung).
  - Wenn `ShowConfirmations == true` → öffnet modalen Dialog mittels `TaskCompletionSource<bool>`.
  - Event `EventHandler<ConfirmationRequest>? OnShow` zur Benachrichtigung des `DialogHost`.
  - Property `ConfirmationRequest? CurrentRequest { get; }` damit `DialogHost` beim Initialisieren prüfen kann, ob bereits eine Anfrage ansteht.
  - `void SetResult(bool confirmed)`: löst `TaskCompletionSource` mit `true`/`false` auf und setzt `CurrentRequest` auf `null`.
  - `void Cancel()`: synonym zu `SetResult(false)` (z. B. bei Escape/Overlay-Klick).
  - `Task InvalidateCacheAsync()`: setzt den gecachten `ShowConfirmations?`-Wert zurück; wird von `SetupProfileViewModel` nach erfolgreichem Speichern aufgerufen.
  - Thread-sicherer Zugriff auf `TaskCompletionSource`, `CurrentRequest` und Cache mittels `lock`.
  - Cancellation-Token-Support: bei `ct` wird `tcs.TrySetCanceled()` aufgerufen.

### 3. Dialog-Rendering

- **Component:** `FinanceManager.Web/Components/Shared/ConfirmDialog.razor`
  - Parameter `[Parameter] public ConfirmationRequest? Request { get; set; }`.
  - Injiziert `IConfirmationService` und `IStringLocalizer<Pages>`.
  - Löst Resource-Keys via `Localizer[Request.TitleResourceKey]` und `Localizer[Request.MessageResourceKey]` auf.
  - Rendert modalen Container mit Titel (`<h2>`), Nachricht (`<p>`), Primär-Button "Bestätigen" und Sekundär-Button "Abbrechen".
  - Ruft `IConfirmationService.SetResult(true)` bei Bestätigen und `IConfirmationService.Cancel()` bei Abbrechen, Overlay-Klick und optional Escape.
  - Fokus nach Öffnen auf den "Abbrechen"-Button: `ElementReference _cancelButtonRef` + `await _cancelButtonRef.FocusAsync()` in `OnAfterRenderAsync` (nur wenn `firstRender`).
  - CSS-Klassen je `ConfirmationSeverity`: `confirm-severity-default`, `confirm-severity-warning`, `confirm-severity-critical` (für Icon-Farben; optional im ersten Schritt ohne zusätzliche Icons).
  - ARIA: `role="dialog"`, `aria-modal="true"`, `aria-labelledby="confirm-title"`, `aria-describedby="confirm-message"`.
- **Host:** `FinanceManager.Web/Components/Shared/ConfirmationDialogHost.razor`
  - Injiziert `IConfirmationService`.
  - In `OnInitialized` abonniert es `IConfirmationService.OnShow` und prüft `IConfirmationService.CurrentRequest`.
  - Event-Handler ruft `InvokeAsync(StateHasChanged)` auf.
  - Rendert `<ConfirmDialog Request="_confirmationService.CurrentRequest" />` nur wenn `CurrentRequest != null`.
  - `IDisposable` implementieren: `OnShow`-Event abmelden.
- **Globaler Einbau:** `MainLayout.razor` bindet `<ConfirmationDialogHost />` am Ende ein (außerhalb des Layout-Grid, absolut positioniert).

### 4. Integration in ViewModels

- **Base-Klassen erweitern:**
  - `FinanceManager.Web/ViewModels/Common/BaseViewModel.cs` und `FinanceManager.Web/ViewModels/ViewModelBase.cs` erhalten jeweils:
    ```csharp
    private IConfirmationService? _confirmationService;
    protected IConfirmationService ConfirmationService => _confirmationService ??= ServiceProvider.GetRequiredService<IConfirmationService>();
    ```
  - Das ermöglicht allen abgeleiteten ViewModels (egal ob `BaseCardViewModel`, `SetupProfileViewModel`, `HomeViewModel`, `ReportDashboardViewModel`) den Zugriff ohne Constructor-Änderungen.
- **Aktions-Methoden anpassen:**
  - Vor jeder kritischen Operation wird `if (!await ConfirmationService.ConfirmAsync(new ConfirmationRequest(titleKey, messageKey, severity: ...), ct)) return;` aufgerufen.
  - ViewModels verwenden geeignete Resource-Keys aus `Pages.resx` (z. B. `Confirmation_Delete_Title`/`Confirmation_Delete_Message`).
  - Beispielhafte Stellen (Delete in `*CardViewModel.DeleteAsync`):
    - `StatementDraftCardViewModel.DeleteAsync`
    - `StatementDraftEntryCardViewModel.DeleteAsync` / `DeleteEntryAsync`
    - `SavingsPlanCardViewModel.DeleteAsync` und `.ArchiveAsync`
    - `SecurityCardViewModel.DeleteAsync` und `.ArchiveAsync`
    - `ContactCardViewModel.DeleteAsync` und `.MergeAsync`
    - `BankAccountCardViewModel.DeleteAsync`
    - `Budget*CardViewModel.DeleteAsync`
    - `SavingsPlanCategoryCardViewModel.DeleteAsync`
    - `SecurityCategoryCardViewModel.DeleteAsync`
    - `ContactGroupCardViewModel.DeleteAsync`
    - `SetupBackupsViewModel.DeleteAsync`
    - `UserCardViewModel.DeleteAsync`
  - Weitere kritische Aktionen:
    - `StatementDraftCardViewModel.BookAsync`, `.BookAllAsync`, `.DeleteAllAsync`
    - `MassBookingOptionsPanel` (Massen-Verbuchung)
  - Razor-Codebehind-Methoden (inject `IConfirmationService`):
    - `CardPage.razor.HandleDeleteAsync` ersetzt `JS.InvokeAsync<bool>("confirm", ...)` durch `IConfirmationService`.
    - `AttachmentsPanel.razor.DeleteAsync` ersetzt JS-Confirm.
    - `ContactMergePanel.razor.ConfirmMergeAsync` führt Confirm vor `Api.Contacts_MergeAsync` aus.
    - `Home.razor.RemovePendingEntry`
    - `ReportDashboard.razor.DeleteFavoriteAsync`
    - `SetupSecurityTab.razor` Delete/Reset
    - `SetupAttachmentCategoriesTab.razor` Delete
    - `ContactDetail.razor.DeleteAliasAsync`
    - `HomeKpiGrid.razor.DeleteKpiAsync`

### 5. Settings-Seite erweitern

- `SetupProfileTab.razor`:
  - Neue Checkbox "Bestätigungsdialoge anzeigen" bindet an `_vm.Model.ShowConfirmations`.
  - Label/Hilfetext über Ressourcen-Keys.
- `SetupProfileViewModel`:
  - `Clone` und `RecomputeDirty` beachten `ShowConfirmations`.
  - Nach erfolgreichem Speichern `await ServiceProvider.GetRequiredService<IConfirmationService>().InvalidateCacheAsync()` aufrufen.

### 6. Ressourcen / Lokalisierung

- `FinanceManager.Web/Resources/Pages.resx` (Default = de) erweitern um:
  - `Settings_ShowConfirmations_Label` → "Bestätigungsdialoge anzeigen"
  - `Settings_ShowConfirmations_Description` → "Vor kritischen Aktionen wie Löschen oder Verbuchen nachfragen."
  - `Confirmation_Button_Confirm` → "Bestätigen"
  - `Confirmation_Button_Cancel` → "Abbrechen"
  - `Confirmation_Delete_Title` → "Löschen bestätigen"
  - `Confirmation_Delete_Message` → "Der Eintrag wird dauerhaft entfernt."
  - `Confirmation_Finalize_Title` → "Abschließen bestätigen"
  - `Confirmation_Finalize_Message` → "Die Aktion kann nicht rückgängig gemacht werden."
  - `Confirmation_Merge_Title` → "Zusammenführen bestätigen"
  - `Confirmation_Merge_Message` → "Die ausgewählten Kontakte werden zusammengeführt."
  - `Confirmation_Archive_Title` → "Archivieren bestätigen"
  - `Confirmation_Archive_Message` → "Das Objekt wird archiviert."
  - `Confirmation_Book_Title` → "Verbuchen bestätigen"
  - `Confirmation_Book_Message` → "Die Buchungen werden finalisiert."
- Entsprechende Einträge in `Pages.de.resx` und `Pages.en.resx`.
- Bestehende `Confirm_Delete_*` Keys bleiben erhalten, werden aber schrittweise durch zentrale `ConfirmationService`-Aufrufe abgelöst.

### 7. Logging

- `ConfirmationService` loggt nur tatsächliche Ausführung (`SetResult(true)`) auf `Information` (keine sensitiven Daten, nur Action-Key).
- Kein Logging beim reinen Öffnen des Dialogs.

## Detaillierte Umsetzungsschritte

### Schritt 1: Domain + Persistence

1. `User.cs`: `ShowConfirmations`-Property + `SetShowConfirmations(bool)` hinzufügen.
2. `AppDbContext.cs`: EF-Mapping für `ShowConfirmations` ergänzen.
3. Migration `YYYYMMDDHHMMSS_AddShowConfirmationsToUser` generieren und prüfen.
4. `UserProfileSettingsDto` + `UserProfileSettingsUpdateRequest` um `bool ShowConfirmations` erweitern.
5. `UserSettingsController` anpassen (GET/PUT).
6. `IApiClient` / `ApiClient.User.cs` anpassen (Dtos ändern sich automatisch, API-Client compiled neu).

### Schritt 2: ConfirmationService + Dialog

1. `ConfirmationSeverity.cs` anlegen.
2. `ConfirmationRequest.cs` anlegen.
3. `IConfirmationService.cs` anlegen.
4. `ConfirmationService.cs` implementieren (Scoped, lazy Settings-Lookup, TCS, OnShow-Event, InvalidateCache).
5. `ConfirmDialog.razor` anlegen.
6. `ConfirmationDialogHost.razor` anlegen.
7. `MainLayout.razor` um `<ConfirmationDialogHost />` erweitern.
8. `ProgramExtensions.RegisterAppServices` registrieren: `builder.Services.AddScoped<IConfirmationService, ConfirmationService>()`.

### Schritt 3: BaseViewModels erweitern

1. `FinanceManager.Web/ViewModels/Common/BaseViewModel.cs`: `ConfirmationService`-Accessor hinzufügen.
2. `FinanceManager.Web/ViewModels/ViewModelBase.cs`: `ConfirmationService`-Accessor hinzufügen.

### Schritt 4: Settings-Seite

1. `SetupProfileTab.razor`: Checkbox für `ShowConfirmations`.
2. `SetupProfileViewModel`: `ShowConfirmations` in `Clone`/`RecomputeDirty`/`SaveAsync` (Cache-Invalidierung).
3. Ressourcen-Keys hinzufügen.

### Schritt 5: Aktionsbuttons anpassen

1. `CardPage.razor.HandleDeleteAsync`:
   - Bestätigung via `IConfirmationService` (per `@inject IConfirmationService`).
   - Kein `JS.InvokeAsync("confirm", ...)` mehr.
2. `AttachmentsPanel.razor`:
   - `@inject IConfirmationService`.
   - `DeleteAsync` prüft `ConfirmAsync`.
3. ViewModels (jeweils `DeleteAsync`, `ArchiveAsync`, `BookAsync`, `MergeAsync` etc.):
   - `await ConfirmationService.ConfirmAsync(...)` vor der eigentlichen Operation.
   - Bei `false` sofort return.
4. Razor-Codebehind-Methoden (z. B. `Home.razor`, `ReportDashboard.razor`, `ContactDetail.razor`, `SetupSecurityTab.razor`, `SetupAttachmentCategoriesTab.razor`, `HomeKpiGrid.razor`):
   - `@inject IConfirmationService`.
   - Bestätigung vor Delete/Reset/Archive/Merge.

### Schritt 6: Ressourcen vervollständigen

1. `Pages.resx`, `Pages.de.resx`, `Pages.en.resx` mit allen neuen Keys befüllen.
2. Sicherstellen, dass Fallback `de` greift, falls `en` fehlt.

### Schritt 7: Tests

1. **Unit:** `ConfirmationServiceTests`
   - `ConfirmAsync_ReturnsTrue_WhenShowConfirmationsIsFalse`
   - `ConfirmAsync_ReturnsTrue_WhenUserConfirms`
   - `ConfirmAsync_ReturnsFalse_WhenUserCancels`
   - `ConfirmAsync_OpensDialog_WhenShowConfirmationsIsTrue`
2. **Unit:** `SetupProfileViewModelTests` erweitern um `ShowConfirmations`.
3. **Unit:** `ConfirmDialogTests` (bUnit 2.9.0 ist bereits im `FinanceManager.Tests`-Projekt referenziert; analog zu `OverlayHostTests.cs` mit `BunitContext`/PassthroughLocalizer).
4. **Integration:** `UserSettingsControllerTests` um `ShowConfirmations` erweitern (`FinanceManager.Tests/Controllers` oder `FinanceManager.Tests.Integration`).
5. **E2E:** Playwright-Test für Delete + Settings-Toggle (falls Playwright-Infrastruktur vorhanden; ansonsten als manueller Smoke-Test dokumentieren).

### Schritt 8: Build, Migration, manuelle Prüfung

1. `dotnet build`.
2. `dotnet ef migrations add` (sofern EF-Tools verfügbar).
3. `dotnet test` (Unit + Integration).
4. Manuell: Delete, Merge, Finalize mit an/aus Option prüfen.

## Betroffene Dateien (Auswahl)

### Domain / Persistence

- `FinanceManager.Domain/Users/User.cs`
- `FinanceManager.Infrastructure/AppDbContext.cs`
- `FinanceManager.Shared/Dtos/Users/UserProfileSettingsDto.cs`
- `FinanceManager.Shared/Dtos/Users/UserProfileSettingsRequests.cs`
- `FinanceManager.Web/Controllers/UserSettingsController.cs`
- Migrations-Verzeichnis (neu generiert)

### Service / Dialog

- `FinanceManager.Web/Services/ConfirmationSeverity.cs` (neu)
- `FinanceManager.Web/Services/ConfirmationRequest.cs` (neu)
- `FinanceManager.Web/Services/IConfirmationService.cs` (neu)
- `FinanceManager.Web/Services/ConfirmationService.cs` (neu)
- `FinanceManager.Web/Components/Shared/ConfirmDialog.razor` (neu)
- `FinanceManager.Web/Components/Shared/ConfirmationDialogHost.razor` (neu)
- `FinanceManager.Web/Components/Layout/MainLayout.razor`
- `FinanceManager.Web/ProgramExtensions.cs`

### ViewModels / Components

- `FinanceManager.Web/ViewModels/Common/BaseViewModel.cs`
- `FinanceManager.Web/ViewModels/ViewModelBase.cs`
- `FinanceManager.Web/Components/Pages/CardPage.razor`
- `FinanceManager.Web/Components/Shared/AttachmentsPanel.razor`
- `FinanceManager.Web/Components/Shared/ContactMergePanel.razor`
- `FinanceManager.Web/Components/Pages/Setup/SetupProfileTab.razor`
- `FinanceManager.Web/ViewModels/Setup/SetupProfileViewModel.cs`
- `FinanceManager.Web/Components/Pages/Home.razor`
- `FinanceManager.Web/Components/Pages/ReportDashboard.razor`
- `FinanceManager.Web/Components/Pages/ContactDetail.razor`
- `FinanceManager.Web/Components/Pages/Setup/SetupSecurityTab.razor`
- `FinanceManager.Web/Components/Pages/Setup/SetupAttachmentCategoriesTab.razor`
- `FinanceManager.Web/Components/Shared/HomeKpiGrid.razor`
- `FinanceManager.Web/ViewModels/StatementDrafts/StatementDraftCardViewModel.cs`
- `FinanceManager.Web/ViewModels/Securities/SecurityCardViewModel.cs`
- `FinanceManager.Web/ViewModels/SavingsPlans/SavingsPlanCardViewModel.cs`
- `FinanceManager.Web/ViewModels/Contacts/ContactCardViewModel.cs`
- `FinanceManager.Web/ViewModels/Setup/SetupBackupsViewModel.cs`
- ggf. weitere Listen-ViewModels mit Massenaktionen

### Ressourcen

- `FinanceManager.Web/Resources/Pages.resx`
- `FinanceManager.Web/Resources/Pages.de.resx`
- `FinanceManager.Web/Resources/Pages.en.resx`

### Tests

- `FinanceManager.Tests/Services/ConfirmationServiceTests.cs` (neu)
- `FinanceManager.Tests/Web/Components/ConfirmDialogTests.cs` (neu)
- `FinanceManager.Tests/ViewModels/SetupProfileViewModelTests.cs` (erweitern)
- `FinanceManager.Tests.Integration/Controllers/UserSettingsControllerTests.cs` (erweitern)
- ggf. `FinanceManager.Tests.E2E/...` (neu)

## Testplan

| Test | Art | Erwartetes Ergebnis |
|------|-----|---------------------|
| `ConfirmationService` ohne Dialog bestätigt sofort bei `ShowConfirmations=false` | Unit | `ConfirmAsync` returned `true`, kein `OnShow` |
| `ConfirmationService` öffnet Dialog bei `ShowConfirmations=true` | Unit | `OnShow` wird ausgelöst, TCS wartet auf `SetResult` |
| `ConfirmDialog` rendert Titel/Nachricht | Unit/Component | HTML enthält Titel und Buttons |
| Fokus liegt auf "Abbrechen" nach Öffnen | E2E/Manuell | Tab-Order startet am Abbrechen-Button |
| Profil-Einstellung `ShowConfirmations` wird gespeichert/geladen | Integration | GET/PUT `api/user/settings/profile` liefert korrekten Wert |
| Delete bei aktivierten Dialogen abgebrochen → keine Änderung | E2E/Manuell | Entity bleibt erhalten |
| Delete bei deaktivierten Dialogen → sofortige Ausführung | E2E/Manuell | Entity wird gelöscht, kein Dialog |
| Merge/Archive/Book erhalten Bestätigungsdialog | E2E/Manuell | Dialog erscheint |
| Lokalisierung Fallback `de` funktioniert | Unit/Manuell | Englische RESX fehlt → Deutsch wird verwendet |
| Kein Bestätigungsdialog bei rein lesenden Aktionen | Review | Analyse aller betroffenen Buttons |

## Risiken / Hinweise

- **Zwei BaseViewModel-Klassen:** `ConfirmationService`-Accessor muss in beiden eingefügt werden (`FinanceManager.Web/ViewModels/Common/BaseViewModel.cs` und `FinanceManager.Web/ViewModels/ViewModelBase.cs`).
- **Async im UI-Thread:** `ConfirmAsync` muss innerhalb von Blazor-Event-Handlern aufgerufen werden; `DialogHost` kümmert sich um `InvokeAsync`.
- **Massenaktionen:** Falls in `ListPage.razor` / `GenericListPage.razor` Delete-Aktionen existieren, müssen diese ebenfalls `IConfirmationService` verwenden.
- **Testdaten:** Für SQLite-Tests wird die Migration automatisch angewendet (`ApplyMigrationsAndSeed`).
- **Ressourcen-Fallback:** `Pages.resx` ist der Default (de); `Pages.en.resx` muss ergänzt werden.

## Offene Punkte

Keine. Alle offenen Fragen aus `requirement.md` wurden beantwortet.
