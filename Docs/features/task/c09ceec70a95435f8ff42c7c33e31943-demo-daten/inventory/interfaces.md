## `IDemoDataService`
Datei: `FinanceManager.Application/Demo/IDemoDataService.cs`

| Methode | Parameter | Rückgabewert | Zweck |
|---------|-----------|--------------|-------|
| `CreateDemoDataAsync` | `Guid userId`, `bool createPostings`, `CancellationToken ct` | `Task` | Erzeugt Demo-Daten für einen Benutzer (optional inkl. Buchungen) |

## `IBackgroundTaskManager`
Datei: `FinanceManager.Application/BackgroundTaskManager.cs`

| Methode | Parameter | Rückgabewert | Zweck |
|---------|-----------|--------------|-------|
| `Enqueue` | `BackgroundTaskType type`, `Guid userId`, `object? payload = null`, `bool allowDuplicate = false` | `BackgroundTaskInfo` | Neue Background-Aufgabe einreihen |
| `GetAll` | – | `IReadOnlyList<BackgroundTaskInfo>` | Alle bekannten Tasks lesen |
| `Get` | `Guid id` | `BackgroundTaskInfo?` | Einzelnen Task lesen |
| `TryCancel` | `Guid id` | `bool` | Laufenden Task abbrechen |
| `TryRemoveQueued` | `Guid id` | `bool` | Wartenden Task entfernen |
| `TryDequeueNext` | `out Guid id` | `bool` | Nächsten Task für Runner entnehmen |
| `UpdateTaskInfo` | `BackgroundTaskInfo info` | `void` | Status/Progress eines Tasks aktualisieren |
| `Semaphore` | – | `SemaphoreSlim` | Synchronisationsprimitive für Runner |

## `IBackgroundTaskExecutor`
Datei: `FinanceManager.Application/BackgroundTaskRunner.cs`

| Methode | Parameter | Rückgabewert | Zweck |
|---------|-----------|--------------|-------|
| `Type` | – | `BackgroundTaskType` | Gibt den unterstützten Task-Typ an |
| `ExecuteAsync` | `BackgroundTaskContext context`, `CancellationToken ct` | `Task` | Führt die eigentliche Task-Logik aus |

## `IUserAuthService`
Datei: `FinanceManager.Application/Users/IUserAuthService.cs`

| Methode | Parameter | Rückgabewert | Zweck |
|---------|-----------|--------------|-------|
| `RegisterAsync` | `RegisterUserCommand command`, `CancellationToken ct` | `Task<Result<AuthResult>>` | Registrierung inkl. Token-Ausgabe |
| `LoginAsync` | `LoginCommand command`, `CancellationToken ct` | `Task<Result<AuthResult>>` | Login inkl. Token-Ausgabe |

## `IUserReadService`
Datei: `FinanceManager.Application/Users/IUserReadService.cs`

| Methode | Parameter | Rückgabewert | Zweck |
|---------|-----------|--------------|-------|
| `HasAnyUsersAsync` | `CancellationToken ct` | `Task<bool>` | Prüft, ob mindestens ein Benutzer existiert (First-User-Flow) |

## `ISecurityPriceImportService`
Datei: `FinanceManager.Application/Securities/ISecurityPriceImportService.cs`

| Methode | Parameter | Rückgabewert | Zweck |
|---------|-----------|--------------|-------|
| `CanHandle` | `SecurityPriceImportContext context` | `bool` | Prüft, ob Provider/Format unterstützt wird |
| `ImportAsync` | `Guid ownerUserId`, `Guid securityId`, `Stream stream`, `SecurityPriceImportContext context`, `CancellationToken ct` | `Task<SecurityPriceImportResultDto>` | Importiert Kursdaten über bestehenden Importpfad |

## `ISecurityPriceImportServiceFactory`
Datei: `FinanceManager.Application/Securities/ISecurityPriceImportServiceFactory.cs`

| Methode | Parameter | Rückgabewert | Zweck |
|---------|-----------|--------------|-------|
| `Resolve` | `SecurityPriceImportContext context` | `ISecurityPriceImportService` | Liefert zuständigen Import-Service |
| `TryResolveByContent` | `SecurityPriceImportContext context`, `byte[] content`, `out ISecurityPriceImportService? service`, `out SecurityPriceImportInspectionResult? inspection` | `bool` | Ermittelt Service über Inhaltsanalyse |
