# Bestandsaufnahme: NFA-USAB-003

## Projektkontext

- **Solution**: `FinanceManager.sln`
- **Projekte**:
  - `FinanceManager.Web` (Blazor Server, .NET 10, InteractiveServer)
  - `FinanceManager.Application`
  - `FinanceManager.Domain`
  - `FinanceManager.Infrastructure` (EF Core, SQLite, Identity)
  - `FinanceManager.Shared` (Dtos, ApiClient)
  - `FinanceManager.Tests`, `FinanceManager.Tests.Integration`, `FinanceManager.Tests.E2E`

## Architekturübersicht

- **Frontend**: Blazor Server, `InteractiveServer`-Rendermode.
- **UI-Layer**: `FinanceManager.Web/Components` (Razor-Komponenten) und `FinanceManager.Web/ViewModels` (MVVM-ähnliche ViewModels).
- **State/Events**: ViewModels kommunizieren mit der UI über `StateChanged`, `AuthenticationRequired`, `UiActionRequested` / `UiActionRequestedEx`.
- **Backend**: ASP.NET Core Web API + EF Core (`AppDbContext` erbt von `IdentityDbContext<User, IdentityRole<Guid>, Guid>`).
- **Lokalisierung**: `IStringLocalizer<Pages>` und komponentenspezifische `.resx`-Dateien unter `FinanceManager.Web/Resources`.
- **API-Client**: `FinanceManager.Shared.IApiClient` / `ApiClient` in `FinanceManager.Shared`.

## Bestehende Benutzereinstellungen (User-Preferences)

### Domain-Entity `User` (`FinanceManager.Domain/Users/User.cs`)

Die `User`-Entity erweitert `IdentityUser<Guid>` und enthält bereits mehrere UI-/Präferenzfelder:

- `PreferredLanguage`
- `LastLoginUtc`
- `Active`
- `ImportSplitMode`
- `ImportMaxEntriesPerDraft`
- `ImportMonthlySplitThreshold`
- `ImportMinEntriesPerDraft`
- `MassImportDialogPolicy`
- `KnownContactAutoCreateEnabled`
- `CacheKpisInLocalStorage`
- `IsAdmin`
- `SymbolAttachmentId`

→ **Erkenntnis**: Es existiert bereits ein user-spezifisches Einstellungskonzept direkt an der `User`-Entity. Eine separate `UserPreferences`-Tabelle ist nicht nötig; `ShowConfirmations` kann als weiteres Property auf `User` ergänzt werden.

### Einstellungs-Endpunkte (`FinanceManager.Web/Controllers/UserSettingsController.cs`)

Bereits vorhandene Settings-Controller:

- `GET /api/user/settings/profile`
- `PUT /api/user/settings/profile`
- `GET /api/user/settings/notifications`
- `PUT /api/user/settings/notifications`
- `GET /api/user/settings/import-split`
- `PUT /api/user/settings/import-split`

Das DTO `UserProfileSettingsDto` und der Request `UserProfileSettingsUpdateRequest` werden für das Profil-Tab verwendet.

### Profile-Settings-UI (`SetupProfileTab.razor` + `SetupProfileViewModel`)

- `SetupProfileTab.razor` bindet an `SetupProfileViewModel.Model`.
- Felder: `PreferredLanguage`, `TimeZoneId`, `AlphaVantageApiKey`, `ShareKey`, `CacheKpisInLocalStorage`.
- Speicherung erfolgt via `ApiClient.UserSettings_UpdateProfileAsync(...)`.
- `SetupProfileViewModel.RecomputeDirty()` vergleicht aktuelle Werte mit `_original`.

→ **Erkenntnis**: Die globale Option "Bestätigungsdialoge anzeigen" kann direkt in `UserProfileSettingsDto`/`UserProfileSettingsUpdateRequest`, `User`-Entity und das `SetupProfileTab` integriert werden.

## Dialog-/Overlay-Mechanismus

### `OverlayHost.razor`

- Generischer Overlay-Host, der auf `UiActionRequested` reagiert.
- Rendert ein `DynamicComponent` basierend auf `UiOverlaySpec`.
- Verwendet Cascading Values `OverlayClose` und `OverlayOnFinished`.
- Wird in `CardPage.razor` eingebunden.

### `UiOverlaySpec` (`BaseViewModel.cs` / `ViewModelBase.cs`)

```csharp
public sealed record UiOverlaySpec(Type ComponentType, IReadOnlyDictionary<string, object?>? Parameters = null, bool Modal = true);
```

### `BaseViewModel` / `ViewModelBase`

- `RaiseUiActionRequested(string? action, object? payloadObject)` erlaubt das Senden eines `UiOverlaySpec`.
- `UiActionRequested` / `UiActionRequestedEx` events.

→ **Erkenntnis**: Ein zentrales Bestätigungsdialog-Component (`ConfirmDialog.razor`) kann über `UiOverlaySpec` vom `ConfirmationService` ausgelöst werden, sofern ein Mechanismus zum Warten auf das Ergebnis (TaskCompletionSource) eingebaut wird.

## Aktionsbuttons / Ribbon

### Ribbon-Modell (`FinanceManager.Web/ViewModels/Common/RibbonModels.cs`)

- `UiRibbonAction(Id, Label, IconSvg, Size, Disabled, Tooltip, Callback)`
- `UiRibbonTab(Title, Items, Sort)`
- `UiRibbonRegister(Kind, Tabs)`
- ViewModels implementieren `IRibbonProvider`/`GetRibbonRegisterDefinition`.

### Ribbon-Komponente (`Ribbon.razor`)

- Rendert Actions-Tabs und führt Callbacks aus.
- Wichtig: `RaiseUiActionRequested`-Aktionen müssen in der zugehörigen Blazor-Seite/-Komponente einen Handler besitzen (Lifecycle-Regel).

### Bestehende Delete-/Aktions-Button-Muster

- **CardPage.razor**: `HandleDeleteAsync()` verwendet `JS.InvokeAsync<bool>("confirm", Localizer[confirmKey].Value)`. Dies ist genau der Ort, der durch den zentralen `IConfirmationService` ersetzt werden sollte.
- **AttachmentsPanel.razor**: `DeleteAsync` verwendet `JS.InvokeAsync<bool>("confirm", Localizer["Confirm_Delete"].Value)`.
- **ContactMergePanel.razor**: Eigener Merge-Dialog (kein Bestätigungsdialog, aber separate Aktion).
- **SetupBackupTab.razor / SetupAttachmentCategoriesTab.razor / Home.razor / ContactDetail.razor**: Direkte Delete-Handler ohne Confirm.

### Bestehende Bestätigungs-Keys in `Pages.resx` / `Pages.de.resx`

- `Confirm_Delete_Account`
- `Confirm_Delete_Item`
- `Confirm_Delete_SecurityCategory`
- `Confirm_Delete_ContactCategory`
- `Confirm_Delete_StatementDraft`
- `AttachmentsPanel.resx` / `.de.resx`: `Confirm_Delete`

→ **Erkenntnis**: Es gibt bereits einige lokalisierte Confirm-Texte, aber keine zentrale Verwaltung und keinen einheitlichen Dialog.

## Kritische Aktionen in der Codebase (Aktions-Inventory)

### Delete-Operationen (ViewModels)

| ViewModel | Methode | API-Aufruf |
|-----------|---------|------------|
| `StatementDraftCardViewModel` | `DeleteAsync` | `ApiClient.StatementDrafts_DeleteAsync(DraftId)` |
| `StatementDraftEntryCardViewModel` | `DeleteAsync` | `DeleteEntryAsync()` |
| `SecurityCardViewModel` | `DeleteAsync` | `ApiClient.Securities_DeleteAsync(Id)` |
| `SecurityCategoryCardViewModel` | `DeleteAsync` | `ApiClient.SecurityCategories_DeleteAsync(Id)` |
| `SavingsPlanCardViewModel` | `DeleteAsync` | `ApiClient.SavingsPlans_DeleteAsync(Id)` |
| `SavingsPlanCategoryCardViewModel` | `DeleteAsync` | `ApiClient.SavingsPlanCategories_DeleteAsync(Id)` |
| `ContactCardViewModel` | `DeleteAsync` | `ApiClient.Contacts_DeleteAsync(Contact.Id)` |
| `ContactGroupCardViewModel` | `DeleteAsync` | `ApiClient.ContactCategories_DeleteAsync(Id)` |
| `BankAccountCardViewModel` | `DeleteAsync` | `ApiClient.DeleteAccountAsync(Account.Id)` |
| `BudgetCategoryCardViewModel` | `DeleteAsync` | `ApiClient.Budgets_DeleteCategoryAsync(Id)` |
| `BudgetPurposeCardViewModel` | `DeleteAsync` | `ApiClient.Budgets_DeletePurposeAsync(Id)` |
| `BudgetRuleCardViewModel` | `DeleteAsync` | `ApiClient.Budgets_DeleteRuleAsync(Id)` |
| `SetupBackupsViewModel` | `DeleteAsync` | `ApiClient.Backups_DeleteAsync(id)` |
| `UserCardViewModel` | `DeleteAsync(CancellationToken)` | `ApiClient.Admin_DeleteUserAsync(User.Id)` |

### Weitere kritische Aktionen

- `StatementDraftCardViewModel`: `BookAsync` (`StatementDrafts_BookAsync`) / `BookAllAsync` (`StatementDrafts_StartBookAllAsync`) / `DeleteAllAsync` (`StatementDrafts_DeleteAllAsync`)
- `ContactCardViewModel`: `MergeAsync` (`ApiClient.Contacts_MergeAsync`)
- `SavingsPlanCardViewModel`: `ArchiveAsync` (`ApiClient.SavingsPlans_ArchiveAsync`)
- `SecurityCardViewModel`: `ArchiveAsync` (`ApiClient.Securities_ArchiveAsync`)
- `MassBookingOptionsPanel`: Startet Massenverbuchung
- `Home.razor`: RemovePendingEntry (Upload entfernen)
- `ReportDashboard.razor`: DeleteFavorite
- `SetupSecurityTab.razor`: ResetCountersAsync / DeleteAsync
- `SetupAttachmentCategoriesTab.razor`: DeleteAsync
- `ContactDetail.razor`: DeleteAliasAsync
- `HomeKpiGrid.razor`: DeleteKpiAsync

### IDeletableViewModel

- `FinanceManager.Web/ViewModels/Common/IDeletableViewModel.cs` definiert `Task<bool> DeleteAsync()` und `string? LastError`.
- `CardPage.razor` prüft `_vm is IDeletableViewModel`.

## Lokalisierung

- Zentraler `IStringLocalizer<Pages>` wird in `MainLayout.razor`, `CardPage.razor`, vielen Komponenten verwendet.
- `Pages.resx` (Fallback, default de), `Pages.de.resx`, `Pages.en.resx` liegen unter `FinanceManager.Web/Resources`.
- Komponentenspezifische RESX unter `FinanceManager.Web/Resources/Components/...`.
- `PagesStringLocalizer` (`FinanceManager.Web/Services/PagesStringLocalizer.cs`) ist als Singleton registriert.

## Tests

- **Unit**: `FinanceManager.Tests` (xUnit)
- **Integration**: `FinanceManager.Tests.Integration` (WebApplicationFactory)
- **E2E**: `FinanceManager.Tests.E2E` (Playwright)
- Beispiele für ViewModel-Tests vorhanden (`SetupProfileViewModelTests.cs`, `SecurityCardViewModelTests.cs`, `ContactCardViewModelTests.cs`).

## Wichtige offene Punkte (für Planung)

1. **ShowConfirmations-Property**: Direkt in `User`-Entity ergänzen (keine separate Tabelle nötig). Default `true`.
2. **ConfirmationService-Design**: Soll das Service im Web-Layer (`FinanceManager.Web`) oder in `FinanceManager.Application` leben? Der Service muss UI-agnostisch sein, aber `ConfirmAsync` benötigt einen Dialog-Mechanismus.
3. **Dialog-Mechanismus**: `UiOverlaySpec` + `OverlayHost` unterstützt kein `Task`-Rückgabewert für Confirm-Ergebnis. Es wird eine `TaskCompletionSource`-basierte Lösung in `ConfirmationService` oder ein neuer globaler `DialogHost` benötigt.
4. **Integration in ViewModels**: Soll `IConfirmationService` per DI in ViewModels injiziert werden? Oder über `BaseViewModel` als geschützter Helper?
5. **Fokus-Management**: `ConfirmDialog.razor` muss `@ref` + `ElementReference`/`JS` verwenden, um Fokus auf "Abbrechen" zu setzen.
6. **Bestätigungspflichtige Aktionen**: Vollständiges Inventar aller Buttons, die Confirm benötigen, ist noch nicht erledigt; muss im Implementierungsschritt erfolgen.
7. **Massenaktionen**: `ListPage.razor` und `GenericListPage.razor` haben Massenaktionen; diese müssen ebenfalls abgedeckt werden.
8. **E2E-Tests**: Playwright-Tests für Delete/Merge/Finalize mit an/aus-Option müssen geplant werden.

## Dateiverweise

- <ref_file file="D:\Repositories\softwareschmiede\cb94fe27-98bd-46b3-b835-72b6ca290f03\FinanceManager.Domain\Users\User.cs" />
- <ref_file file="D:\Repositories\softwareschmiede\cb94fe27-98bd-46b3-b835-72b6ca290f03\FinanceManager.Web\Controllers\UserSettingsController.cs" />
- <ref_file file="D:\Repositories\softwareschmiede\cb94fe27-98bd-46b3-b835-72b6ca290f03\FinanceManager.Web\Components\Pages\CardPage.razor" />
- <ref_file file="D:\Repositories\softwareschmiede\cb94fe27-98bd-46b3-b835-72b6ca290f03\FinanceManager.Web\Components\Shared\OverlayHost.razor" />
- <ref_file file="D:\Repositories\softwareschmiede\cb94fe27-98bd-46b3-b835-72b6ca290f03\FinanceManager.Web\ViewModels\ViewModelBase.cs" />
- <ref_file file="D:\Repositories\softwareschmiede\cb94fe27-98bd-46b3-b835-72b6ca290f03\FinanceManager.Web\ViewModels\Common\RibbonModels.cs" />
- <ref_file file="D:\Repositories\softwareschmiede\cb94fe27-98bd-46b3-b835-72b6ca290f03\FinanceManager.Web\ViewModels\Setup\SetupProfileViewModel.cs" />
- <ref_file file="D:\Repositories\softwareschmiede\cb94fe27-98bd-46b3-b835-72b6ca290f03\FinanceManager.Infrastructure\AppDbContext.cs" />
