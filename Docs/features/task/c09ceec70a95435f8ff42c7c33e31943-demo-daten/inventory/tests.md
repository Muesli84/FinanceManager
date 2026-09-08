## Testklassen

### `ApiClientDemoDataTests`
Datei: `FinanceManager.Tests.Integration/ApiClient/ApiClientDemoDataTests.cs`

- `Users_CreateDemoData_Should_ReturnAccepted` — registriert Benutzer, ruft `Users_CreateDemoDataAsync` auf und prüft nur, dass mindestens 3 Konten (`>= 1 Giro + 2 Savings`) entstanden sind.

### `ApiClientBackupsWithDemoDataTests`
Datei: `FinanceManager.Tests.Integration/ApiClient/ApiClientBackupsWithDemoDataTests.cs`

- `Backup_With_DemoData_Restore_Removes_NewlyCreatedContact` — nutzt `IDemoDataService.CreateDemoDataAsync(userId, true, ...)` zum Seeden, erstellt Snapshot, führt Backup/Restore aus und prüft vollständige Wiederherstellung inkl. Entfernen nachträglich angelegter Daten.

### `UserAuthServiceTests`
Datei: `FinanceManager.Tests/Auth/UserAuthServiceTests.cs`

- `RegisterAsync_ShouldCreateFirstUserAsAdmin_WhenNoUsersExist` — testet Erstregistrierung/Admin-Flag.
- Weitere Tests prüfen Login/Registrierung allgemein (Duplicate Username, Pflichtfelder, Lockout, Token), aber **nicht** spezifisch das Queueing von `BackgroundTaskType.CreateDemoData`.

### `ApiClientBackgroundTasksTests`
Datei: `FinanceManager.Tests.Integration/ApiClient/ApiClientBackgroundTasksTests.cs`

- `Enqueue_RebuildAggregates_ShouldReturnTaskInfo_AndStatusEndpointsWork` — testet generische Background-Task-API am Beispiel `RebuildAggregates`.
- `CancelOrRemove_ShouldReturnNoContentOrFalse` — testet Abbruch/Entfernung queued/running Task.
- Kein dedizierter Test für `CreateDemoData`-Tasklauf oder dessen Progress im UI.

### `EnumCoverageTests`
Datei: `FinanceManager.Tests/Common/EnumCoverageTests.cs`

- `BackgroundTaskType_Values` — enthält `CreateDemoData` als Enum-Abdeckung.
- `BackgroundTaskStatus_Values` — deckt Statuswerte (`Queued`, `Running`, `Completed`, `Failed`, `Cancelled`) ab.

## Hilfsmethoden

### `ApiClientDemoDataTests`
- `CreateClient` — erstellt API-Client gegen Testserver.
- `EnsureAuthenticatedAsync` — Registrierung als Test-Login-Vorbereitung.

### `ApiClientBackupsWithDemoDataTests`
- `RegisterAndAuthenticateAsync` — erstellt authentifizierten Testnutzer.
- `CaptureSnapshotAsync` — liest den vollständigen Benutzerdatenbestand (Kontakte, Konten, Sparpläne, Wertpapiere, Kurse, Auszüge, Budgets, Attachments, usw.).
- `CompareSnapshots` / `AlignBeforeSnapshot` / `SortSnapshot` — normalisieren/gleichen IDs und vergleichen Vorher-/Nachher-Zustände deterministisch.
- `CalculateAggregatesAsync` — berechnet fachliche Summen über Kontakte/Sparpläne/Wertpapiere/Konten als zusätzliche Integritätsprüfung.

## Sichtbare Testlücken zur Anforderung

- Keine Tests, die die in der Anforderung geforderte vollständige Demo-Entity-Liste (konkrete Kontakte, Budgets, Sparpläne, Wertpapiere) überprüfen.
- Keine Tests für werktägliche Kursgenerierung über 2 Jahre oder für Import über `ISecurityPriceImportService`.
- Keine Tests für die geforderte 24-Monats-Buchungslogik inkl. „aktueller Monat ungebucht“ gemäß Detailregeln.
- Keine UI-Tests, die die Fortschrittsdarstellung des Demo-Background-Tasks auf der Startseite verifizieren.
