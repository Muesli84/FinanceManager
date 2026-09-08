## `FinanceManager.Infrastructure.Demo.DemoDataService`
Datei: `FinanceManager.Infrastructure/Demo/DemoDataService.cs`

| Methode | Sichtbarkeit | Kurzbeschreibung |
|---------|-------------|------------------|
| `CreateDemoDataAsync(Guid userId, bool createPostings, CancellationToken ct)` | `public` | Zentrale Anlage von Demo-Kategorien, Kontakten, Wertpapier, Sparplänen, Budgets, Konten sowie optional Buchungen/Entwürfen |
| `CreateSecurityPrices(Guid userId, SecurityDto msci, CancellationToken ct)` | `private` | Erzeugt 24 monatliche Kurspunkte rückwirkend per `_securityPriceService.CreateAsync` |
| `CreateContactAsync(...)` | `private` | Legt Kontakt an und ergänzt Alias-Namen |
| `CreateDemoPostingsForInsurance(...)` | `private` | Erstellt/bucht Entwurf mit 9 Monatsbuchungen für Versicherungsrückstellung |
| `CreateDemoPostingsForSavingsPlan(...)` | `private` | Erstellt/bucht Entwürfe für Sparplan-Soll/Ist-Gegenbuchungen über `months` Monate |
| `CreateSvgSymbolAsync(...)` | `private` | Erzeugt SVG und lädt es als Attachment hoch |
| `CreateMonthlyUnbookedStatementsAsync(...)` | `private` | Erstellt ungebuchte Entwürfe für den aktuellen Monat (Giro + 2 Sparkonten) |
| `CreateDemoBudgetsAsync(...)` | `private` | Legt BudgetPurposes und BudgetRules für die drei angelegten Sparpläne/Versicherung an |

Abonnierte Events: keine.
Publizierte Events: keine.

Querverweise:
- Wird per DI als `IDemoDataService` registriert (`FinanceManager.Infrastructure/ServiceCollectionExtensions.cs`).
- Wird aufgerufen von `UsersController.CreateDemoDataAsync` (direkter API-Aufruf).
- Wird aufgerufen von `DemoDataTaskExecutor.ExecuteAsync` (Background-Flow für Erstbenutzer).
- Wird in Test `ApiClientBackupsWithDemoDataTests.Backup_With_DemoData_Restore_Removes_NewlyCreatedContact` direkt aus DI verwendet.

Ist-Befunde zur fachlichen Vollständigkeit:
- Vorhanden: Anlage von Basisobjekten über Business-Services (kein direkter DbContext-Schreibzugriff in dieser Klasse).
- Fehlend gegenüber Anforderung:
  - Keine vollständige Kontakt-/Gruppenliste (nur wenige Demo-Kontakte wie `Demo Giro Bank`, `Demo Savings Bank`, `Aldi`, `KFZ Versicherung`).
  - Keine geforderten zwei konkreten Wertpapiere (`USHSIV-MSCI WLD`, `Inländische Post AG`) inkl. fachlicher Attribute.
  - Kein werktäglicher 2-Jahres-Kursverlauf, sondern 24 monatliche Werte.
  - Kein Kursimport über bestehenden Importmechanismus (`ISecurityPriceImportService` / `ISecurityPriceImportServiceFactory`), sondern direkte Erstellung via `_securityPriceService.CreateAsync`.
  - Keine 24-Monats-Kontoauszug-/Buchungsorchestrierung gemäß Regelwerk (nur Teilmengen: 9/12/15 Monate plus ungebuchte aktuelle Monatsentwürfe).

## `FinanceManager.Infrastructure.Auth.UserAuthService`
Datei: `FinanceManager.Infrastructure/Auth/UserAuthService.cs`

| Methode | Sichtbarkeit | Kurzbeschreibung |
|---------|-------------|------------------|
| `RegisterAsync(RegisterUserCommand command, CancellationToken ct)` | `public` | Registriert Benutzer, erkennt Erstbenutzer, legt Self-Kontakt an, queued optional DemoData-Background-Task |
| `LoginAsync(LoginCommand command, CancellationToken ct)` | `public` | Login inkl. Lockout-/Token-Logik |
| `_user_manager_create_wrapper(...)` | `private` | Testfreundlicher Wrapper für `UserManager.CreateAsync` |

Abonnierte Events: keine.
Publizierte Events: keine.

Querverweise:
- Wird aufgerufen von `AuthController.RegisterAsync`.
- Nutzt `IBackgroundTaskManager.Enqueue(BackgroundTaskType.CreateDemoData, ...)` wenn `command.CreateDemoData && isFirst`.

## `FinanceManager.Web.Services.DemoDataTaskExecutor`
Datei: `FinanceManager.Web/Services/DemoDataTaskExecutor.cs`

| Methode | Sichtbarkeit | Kurzbeschreibung |
|---------|-------------|------------------|
| `ExecuteAsync(BackgroundTaskContext context, CancellationToken ct)` | `public` | Führt `IDemoDataService.CreateDemoDataAsync(context.UserId, createPostings: true, ct)` als Background-Task aus |

Abonnierte Events: keine.
Publizierte Events: keine.

Querverweise:
- Registriert als `IBackgroundTaskExecutor` in `ProgramExtensions`.
- Fortschrittsmeldungen werden per `context.ReportProgress` gesetzt (nur Start/Ende bzw. Fehler).

## `FinanceManager.Application.BackgroundTaskManager`
Datei: `FinanceManager.Application/BackgroundTaskManager.cs`

| Methode | Sichtbarkeit | Kurzbeschreibung |
|---------|-------------|------------------|
| `Enqueue(...)` | `public` | Legt Task an, verhindert Duplikate pro Benutzer/Typ (sofern `allowDuplicate = false`) |
| `GetAll()` / `Get(Guid id)` | `public` | Liefert Task-Informationen |
| `TryCancel(Guid id)` | `public` | Markiert laufenden Task als `Cancelled` |
| `TryRemoveQueued(Guid id)` | `public` | Entfernt wartenden Task aus Store/Queue |
| `TryDequeueNext(out Guid id)` | `public` | Holt nächste Task-ID aus Queue |
| `UpdateTaskInfo(BackgroundTaskInfo info)` | `public` | Persistiert Status-/Fortschrittsupdate im In-Memory-Store |

Abonnierte Events: keine.
Publizierte Events: keine.

## `FinanceManager.Application.BackgroundTaskRunner`
Datei: `FinanceManager.Application/BackgroundTaskRunner.cs`

| Methode | Sichtbarkeit | Kurzbeschreibung |
|---------|-------------|------------------|
| `ExecuteAsync(CancellationToken stoppingToken)` | `protected` | Hintergrundschleife: dequeue, Executor wählen, Statusübergänge (`Queued`→`Running`→`Completed/Failed/Cancelled`) und Progress-Updates |

Abonnierte Events: keine.
Publizierte Events: keine.

Querverweise:
- Nutzt alle registrierten `IBackgroundTaskExecutor` inklusive `DemoDataTaskExecutor`.

## `FinanceManager.Web.Components.AuthRedirect`
Datei: `FinanceManager.Web/Components/AuthRedirect.razor`

| Methode | Sichtbarkeit | Kurzbeschreibung |
|---------|-------------|------------------|
| `OnInitialized()` | `protected` | Registriert Handler für Navigation und `ApiClient.AuthenticationRequired` |
| `CheckRedirectAsync(string uri)` | `private` | Leitet unauthentifizierte Nutzer zu `/register` (wenn keine User existieren) oder `/login` weiter |
| `RedirectToLoginAsync(string? explicitReturnUrl)` | `private` | Login-/Register-Redirect mit ReturnUrl-Handling |
| `Dispose()` | `public` | Deregistriert Event-Handler |

Abonnierte Events:
- `NavigationManager.LocationChanged`
- `ApiClient.AuthenticationRequired`

Publizierte Events: keine.

Querverweise:
- Wird in `Components/App.razor` global eingebunden.
- Nutzt `IUserReadService.HasAnyUsersAsync` als Erstbenutzer-Check.

## `FinanceManager.Web.Components.Pages.Register`
Datei: `FinanceManager.Web/Components/Pages/Register.razor`

| Methode | Sichtbarkeit | Kurzbeschreibung |
|---------|-------------|------------------|
| `RegisterAsync()` | `private` | Baut `RegisterRequest(..., CreateDemoData)` und ruft `Api.Auth_RegisterAsync` auf |

Abonnierte Events: keine.
Publizierte Events: keine.

Ist-Befund:
- Checkbox vorhanden: `InputCheckbox id="create-demo-data" @bind-Value="_model.CreateDemoData"`.
- Label aus Resource-Key `Checkbox_CreateDemoData` (`Demodaten anlegen` / `Create demo data`).

## `FinanceManager.Web.Components.BackgroundTaskStatusPanel`
Datei: `FinanceManager.Web/Components/BackgroundTaskStatusPanel.razor`

| Methode | Sichtbarkeit | Kurzbeschreibung |
|---------|-------------|------------------|
| `OnAfterRenderAsync(bool firstRender)` | `protected` | Startet Polling |
| `PollLoopAsync(CancellationToken ct)` | `private` | Pollt `/api/background-tasks/active` zyklisch |
| `LoadTasksAsync(CancellationToken ct)` | `private` | Lädt aktive/queued Tasks und aktualisiert Anzeige |
| `CancelTaskAsync(Guid id)` / `RemoveQueuedAsync(Guid id)` | `public` | Führt Abbruch/Entfernung über API aus |

Abonnierte Events: keine.
Publizierte Events: keine.

Querverweise:
- Wird in `Components/Pages/Home.razor` ohne `AllowedTypes` eingebunden, dadurch generische Anzeige aller aktiven Tasks (inkl. DemoData-Task).
