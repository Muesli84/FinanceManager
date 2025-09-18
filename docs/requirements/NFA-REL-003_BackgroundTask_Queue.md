# NFA-REL-003: Vereinheitlichte Hintergrundaufgaben-Verwaltung (Single Runner + Queue)

## Kurzbeschreibung
Bestehende langlaufende Hintergrundaktionen (Klassifizierung aller Kontoauszüge, Buchen aller Kontoauszüge, Backup-Wiederherstellung) sollen über eine einheitliche Aufgabenverwaltung (Task Manager) orchestriert werden. Es darf immer nur genau eine Aufgabe aktiv laufen. Weitere angeforderte Aufgaben werden in eine Warteschlange eingereiht und automatisch gestartet, sobald die laufende Aufgabe beendet ist. Das bisherige Look & Feel (Fortschrittsanzeige) wird konsolidiert; eine neue wiederverwendbare UI-Komponente zeigt Status, Fortschritt, verbleibende Warteschlangenposition sowie Fehler an.

## Ziele / Nutzen
- Einheitliche Infrastruktur statt individueller Koordinator-Implementierungen.
- Verhindern konkurrierender ressourcenintensiver Prozesse.
- Transparente Sicht für Anwender: Was läuft / was steht an?
- Erweiterbarkeit für zukünftige Aufgaben (Kurs-Nachberechnung, Aggregat-Rebuild, Re-Indizierung, Attachment-Migration, etc.).

## Scope (Phase 1)
Umstellung folgender Prozesse auf neue Infrastruktur:
1. Klassifizierung aller offenen Statement Drafts (bisher `ClassificationCoordinator`).
2. Massenbuchung aller Statement Drafts (bisher `BookingCoordinator`).
3. Backup Restore (bisher `BackupRestoreCoordinator`).

## Out-of-Scope (Phase 1)
- Parallele Ausführung mehrerer Aufgaben (bewusst ausgeschlossen).
- Persistente Wiederaufnahme nach Server-Neustart (Task-Lebenszyklus endet beim Neustart).
- Verteilte Ausführung (Single-Node Annahme).

## Funktionale Anforderungen
| Nr | Beschreibung |
|----|--------------|
| NFA-REL-003-01 | Neue zentrale Service-Schnittstelle zum Registrieren, Starten, Abbrechen & Abfragen von Aufgaben. |
| NFA-REL-003-02 | Max. eine aktive Aufgabe (Status = Running). |
| NFA-REL-003-03 | Weitere Startanforderungen landen FIFO in einer Warteschlange (Status = Queued). |
| NFA-REL-003-04 | Automatischer Start der nächsten Aufgabe nach Abschluss (Success/Failed/Cancelled). |
| NFA-REL-003-05 | Fortschrittsmodell: processed, total (optional), message, warnings, errors. |
| NFA-REL-003-06 | Anwender kann laufende Aufgabe abbrechen (CancellationToken ? Kooperatives Cancel). |
| NFA-REL-003-07 | Wartende Aufgabe kann vor Start vom Anwender entfernt werden (Queue Remove). |
| NFA-REL-003-08 | API liefert vollständige Liste (Active + Queue) und Detail zu aktiver Aufgabe. |
| NFA-REL-003-09 | Aufgaben erhalten stabilen Identifier (GUID) + Typ (Enum) + vom Benutzer initiierter Kontext (UserId). |
| NFA-REL-003-10 | Fehlerzustand speichert Fehlermeldung + optional StackTrace (intern; StackTrace nicht an UI weitergeben). |
| NFA-REL-003-11 | UI zeigt: aktueller Fortschritt (%), verbleibender Queue-Platz (Position), Status-Badge (Running / Queued / Completed / Failed / Cancelled). |
| NFA-REL-003-12 | Bei fehlender total-Angabe: indeterminierter Fortschrittsindikator. |
| NFA-REL-003-13 | Rückwärtskompatible API-Endpunkte für bestehende Buttons (Routen bleiben, intern Umleitung). |
| NFA-REL-003-14 | Doppeltes Anfordern identischer Aufgabe (gleicher Typ + Running/Queued bereits vorhanden) liefert bestehenden Task (Idempotenz). Konfigurierbar per Flag pro Task-Typ. |
| NFA-REL-003-15 | Laufende Aufgabe blockiert Start anderer – Benutzer erhält Info mit TaskId & Typ. |

## Nicht-funktionale Anforderungen
- NFA-REL-003-16: Threadsafe (ConcurrentQueue / Channel).
- NFA-REL-003-17: Kein Busy-Waiting – Hintergrundkonsument (HostedService) wartet signalisierend (`SemaphoreSlim` oder ChannelReader).
- NFA-REL-003-18: Speicher-Footprint gering: Task-Metadaten im RAM (keine DB-Persistenz Phase 1).
- NFA-REL-003-19: Logging strukturiert (TaskType, TaskId, Statuswechsel, Dauer ms, Result). Kein Log-Spam per Iteration.
- NFA-REL-003-20: Cancellation reagiert < 1s (Polling in Implementierungen falls nötig anpassen).

## Domänenmodell (In-Memory)
```csharp
public enum BackgroundTaskType { ClassifyAllDrafts, BookAllDrafts, BackupRestore }
public enum BackgroundTaskStatus { Queued, Running, Completed, Failed, Cancelled }

public sealed record BackgroundTaskInfo(
    Guid Id,
    BackgroundTaskType Type,
    Guid UserId,
    DateTime EnqueuedUtc,
    BackgroundTaskStatus Status,
    int? Processed,
    int? Total,
    string? Message,
    int Warnings,
    int Errors,
    string? ErrorDetail,
    DateTime? StartedUtc,
    DateTime? FinishedUtc
);
```

## Service-Schnittstellen (Vorschlag)
```csharp
public interface IBackgroundTaskManager
{
    BackgroundTaskInfo Enqueue(BackgroundTaskType type, Guid userId, object? payload = null, bool allowDuplicate = false);
    IReadOnlyList<BackgroundTaskInfo> GetAll();
    BackgroundTaskInfo? Get(Guid id);
    bool TryCancel(Guid id);
    bool TryRemoveQueued(Guid id);
}

public interface IBackgroundTaskExecutor // implementiert pro Task-Typ
{
    BackgroundTaskType Type { get; }
    Task ExecuteAsync(BackgroundTaskContext context, CancellationToken ct);
}

public sealed class BackgroundTaskContext
{
    public Guid TaskId { get; }
    public Guid UserId { get; }
    public object? Payload { get; }
    public Action<int,int?,string?,int,int> ReportProgress { get; }
    // ... ctor etc.
}
```

Hosted Service `BackgroundTaskRunner`:
- Wartet auf neue Aufgaben ? setzt Status=Running ? Stopwatch ? ExecuteAsync
- Fortschrittsupdates via `ReportProgress`
- Abschluss: Status setzen + Logging + nächste Aufgabe

## Migration bestehender Koordinatoren
| Alt | Neu |
|-----|-----|
| `ClassificationCoordinator` | Implementierung `IBackgroundTaskExecutor` (TaskType=ClassifyAllDrafts) |
| `BookingCoordinator` | Executor (TaskType=BookAllDrafts) |
| `BackupRestoreCoordinator` | Executor (TaskType=BackupRestore) |

Bestehende Controller-Endpunkte ändern Aufruf auf `IBackgroundTaskManager.Enqueue(...)` und geben TaskId + initial Info zurück. Status-Endpunkte vereinheitlichen (`GET /api/background-tasks/active` + `GET /api/background-tasks` + `GET /api/background-tasks/{id}`).

Kompatibilitäts-Endpunkte (deprecated) behalten Rückgabeformate bis UI migriert.

## API (neu)
| Methode | Route | Beschreibung |
|---------|-------|--------------|
| POST | `/api/background-tasks/{type}` | Start (oder Idempotenz-Rückgabe) einer Aufgabe |
| GET | `/api/background-tasks/active` | Aktive + Queue Übersicht |
| GET | `/api/background-tasks/{id}` | Detail |
| DELETE | `/api/background-tasks/{id}` | Cancel (Running) oder Remove (Queued) |

Security: `[Authorize]` – User sieht nur eigene Tasks (oder Admin alle; Erweiterung später). Phase 1: User-Scope.

## UI / Blazor
Neue wiederverwendbare Komponente: `BackgroundTaskStatusPanel.razor`:
- Parameter: `TaskListEndpoint`, PollInterval (z.B. 2s)
- Darstellung:
  - Section "Aktive Aufgabe" (Fortschrittsbalken, Message, Cancel-Button)
  - Section "Warteschlange" (Liste TaskType, Enqueue-Zeit, Position, Remove-Button)
  - Badges für Status
  - Icon je TaskType (reuse sprite.svg – z.B. classify, post, backup)
- Indeterminierter Progress wenn Total null.
- Lokalisierungs-Schlüssel:
  - Bgt_Title, Bgt_ActiveNone, Bgt_QueuedTitle, Bgt_Cancel, Bgt_Remove, Bgt_Status_Running, Bgt_Status_Queued, Bgt_Status_Completed, Bgt_Status_Failed, Bgt_Status_Cancelled, Bgt_Warnings, Bgt_Errors, Bgt_Position

Einbindung:
- `StatementDrafts.razor` (ersetzt bisherige Klassifizierungs- / Buchungsstatusboxen)
- `Setup.razor` (Backup Restore Abschnitt ersetzen)

## Ablauf (Beispiel)
1. User klickt "Alle klassifizieren" ? POST `/api/background-tasks/ClassifyAllDrafts` ? Antwort TaskInfo Id.
2. Panel pollt ? zeigt laufende Aufgabe.
3. User startet währenddessen "Alle buchen" ? Task landet Queue (Status Queued, position 1).
4. Klassifizierung fertig ? Runner nimmt Buchen aus Queue.
5. User kann queued Booking entfernen ? wird nicht ausgeführt.

## Fehlerszenarien
- Executor wirft Exception ? Status=Failed; ErrorDetail intern gespeichert; UI zeigt generische Fehlermeldung (lokalisiert). Restart manuell möglich (erneutes Enqueue).
- Cancel: Executor soll ct beachten, Zwischenstände nicht persistieren bzw. konsistent abbrechen.

## Logging
- Start: `Information` ? {TaskId, Type, UserId}
- Progress (nur relevante Milestones – optional jede 5% Änderung) auf `Debug`.
- End: `Information` ? {TaskId, Type, DurationMs, ResultStatus, Processed, Total, Warnings, Errors}
- Fail: zusätzlich `Error` (Exception Message, TaskId, Type)

## Akzeptanzkriterien (Auszug)
1. Gleichzeitiges Starten von 3 Tasks ? 1 Running, 2 Queued in korrekt angezeigter Reihenfolge.
2. Cancel beendet laufende Aufgabe binnen < 1s und startet nächste aus Queue.
3. Entfernen eines queued Tasks verhindert dessen Ausführung dauerhaft.
4. Fortschrittsbalken zeigt relative Prozent (Processed/Total) oder Animation bei unbekanntem Total.
5. Idempotenter Aufruf (gleicher TaskType während Running/Queued) liefert bestehende TaskId zurück (wenn allowDuplicate=false).
6. Fehlerhafte Aufgabe markiert als Failed; nächste queued startet automatisch.
7. UI-Komponente wiederverwendet gleiche Darstellung für alle drei Task-Typen.

## Erweiterungen (Future)
- Persistenz (DB) + Recovery nach Neustart.
- Parallelitäts-Level > 1 konfigurierbar.
- Prioritäten (High/Normal/Low) statt simple FIFO.
- Broadcast Echtzeit (SignalR) statt Polling.
- Multi-Node Koordination (Distributed Lock / Leader Election).

## Risiken
- Fehlende Persistenz ? Verlust laufender Queue bei Neustart (akzeptiert Phase 1).
- Executor muss konsequent cancellation-fähig sein – Nacharbeit in bestehendem Code nötig.

## Umsetzungsschritte
1. Shared Enums / Records hinzufügen.
2. Implement `BackgroundTaskManager` (InMemory + thread-safe Queue + Dictionary für Tasks + Semaphore).
3. Implement `BackgroundTaskRunner` HostedService.
4. Migrate Classification/Booking/Backup Restore auf Executors.
5. Neue API Controller + Routen (Adapter für alte Endpoints bereitstellen -> Obsolete Markierungen).
6. UI Komponente + Integration in Seiten.
7. Lokalisierungstexte de/en.
8. Unit Tests (Manager, Runner – mittels Fake Executor; Idempotenz, Cancel, Queue Order, Progress Reporting).
9. Entfernen / Downgraden alter Koordinator-Singletons nach Migration.
10. Doku + Anforderungsstatus ergänzen.

## Branch / Commit
- Branch: `feature/background-task-queue`
- Commit Prefix: `feat(tasks): ...`

---
Letzte Aktualisierung: (Initial)
