## `RegisterUserCommand`
Datei: `FinanceManager.Application/Users/UserDtos.cs`

| Eigenschaft | Typ | Beschreibung / Zweck |
|-------------|-----|----------------------|
| `Username` | `string` | Benutzername für Registrierung |
| `Password` | `string` | Passwort für Registrierung |
| `PreferredLanguage` | `string?` | Optionale Sprachpräferenz |
| `TimeZoneId` | `string?` | Optionale Zeitzone |
| `CreateDemoData` | `bool` | Flag, ob bei Erstregistrierung Demo-Daten-Task gestartet werden soll |

## `RegisterRequest`
Datei: `FinanceManager.Shared/Dtos/Users/RegisterRequest.cs`

| Eigenschaft | Typ | Beschreibung / Zweck |
|-------------|-----|----------------------|
| `Username` | `string` | Benutzername (mit Validation-Attributen) |
| `Password` | `string` | Passwort (mit Validation-Attributen) |
| `PreferredLanguage` | `string?` | Optionale Sprache aus UI |
| `TimeZoneId` | `string?` | Optionale Zeitzone aus UI |
| `CreateDemoData` | `bool` | Transportiert Checkbox-Wert aus Registrierung |

## `DemoRequest`
Datei: `FinanceManager.Shared/Dtos/Users/DemoRequest.cs`

| Eigenschaft | Typ | Beschreibung / Zweck |
|-------------|-----|----------------------|
| `createPostings` | `bool` | Steuert, ob Demo-Daten mit Buchungen/Kontoauszugsentwürfen erstellt werden |

## `BackgroundTaskInfo`
Datei: `FinanceManager.Shared/Dtos/Admin/BackgroundTaskInfo.cs`

| Eigenschaft | Typ | Beschreibung / Zweck |
|-------------|-----|----------------------|
| `Id` | `Guid` | Eindeutige Task-ID |
| `Type` | `BackgroundTaskType` | Fachlicher Task-Typ (u. a. `CreateDemoData`) |
| `UserId` | `Guid` | Besitzer des Tasks |
| `EnqueuedUtc` | `DateTime` | Einreihungszeitpunkt |
| `Status` | `BackgroundTaskStatus` | Laufzustand (`Queued`, `Running`, …) |
| `Processed` / `Total` | `int?` | Primäre Fortschrittszähler |
| `Message` | `string?` | Primäre Statusmeldung |
| `Warnings` / `Errors` | `int` | Warn-/Fehlerzähler |
| `ErrorDetail` | `string?` | Fehlerdetails bei Fehlerfall |
| `StartedUtc` / `FinishedUtc` | `DateTime?` | Start-/Endezeit |
| `Payload` | `string?` | Optional serialisierte Zusatzdaten |
| `Processed2` / `Total2` / `Message2` | `int?` / `int?` / `string?` | Sekundäre Fortschrittsinformationen |

## `BackgroundTaskContext`
Datei: `FinanceManager.Application/BackgroundTaskRunner.cs`

| Eigenschaft | Typ | Beschreibung / Zweck |
|-------------|-----|----------------------|
| `TaskId` | `Guid` | Laufende Task-ID |
| `UserId` | `Guid` | Benutzerkontext |
| `Payload` | `object?` | Optionaler Payload |
| `ReportProgress` | `Action<int,int?,string?,int,int>` | Callback, mit dem Executor Progress in den Task-Status schreibt |
