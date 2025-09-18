# Background Task Queue – Umsetzungsstand

## Erledigt
- **Shared Enums und Records:**
  - `BackgroundTaskType`, `BackgroundTaskStatus`, `BackgroundTaskInfo` in `FinanceManager.Shared/Dtos` angelegt.
- **BackgroundTaskManager:**
  - Threadsafe InMemory-Manager mit Queue, Dictionary und Semaphore implementiert.
- **BackgroundTaskRunner:**
  - HostedService für Task-Ausführung und Fortschritts-Handling erstellt.
- **Koordinatoren-Migration:**
  - Klassifizierung, Buchung und BackupRestore als `IBackgroundTaskExecutor` umgesetzt.
- **API-Controller:**
  - Neue Endpunkte für Aufgabenverwaltung (`BackgroundTasksController`) angelegt.
- **UI-Komponente:**
  - `BackgroundTaskStatusPanel.razor` erstellt und in `StatementDrafts.razor` integriert.

## Noch offen / ToDo
- **API-Adapter:**
  - Kompatibilitäts-Endpunkte für alte Buttons/Statusboxen bereitstellen (Obsolete markieren).
- **UI-Integration:**
  - Einbindung der Task-Panel-Komponente in weitere Seiten (z.B. `Setup.razor` für Backup Restore).
- **Lokalisierung:**
  - Ressourcentexte für Task-Status und Panel in `Resources` (de/en) ergänzen.
- **Unit Tests:**
  - Tests für TaskManager und Runner (Idempotenz, Cancel, Queue Order, Progress).
- **Alte Koordinatoren:**
  - Entfernen oder zu Adapter/Obsolete downgraden.
- **Dokumentation:**
  - Anforderungsstatus und technische Doku aktualisieren.
- **Projekt-Setup:**
  - TargetFramework auf .NET 8.0 setzen, NuGet-Paket `Microsoft.Extensions.Logging.Abstractions` installieren.

## Hinweise
- Nach Umstellung auf .NET 8.0 kann das Logging-Paket per NuGet installiert werden:
  ```sh
  dotnet add FinanceManager.Application package Microsoft.Extensions.Logging.Abstractions
  ```
- Die neuen Endpunkte sind unter `/api/background-tasks` verfügbar.
- Die UI-Komponente pollt alle 2s und zeigt Status, Fortschritt, Warteschlange und Fehler an.

---
Letzter Stand: 18.09.2025
