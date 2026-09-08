← [Zurück zur Übersicht](index.md)

# Demo-Daten für den ersten Benutzer

## Zweck

Beim Start der Anwendung ohne vorhandenes Benutzerkonto öffnet der Nutzer die Registrierungsseite. Dort kann er mit der Checkbox `Demodaten anlegen` entscheiden, ob der erste Benutzer direkt einen vollständigen Demo-Datenbestand erstellen lassen möchte.

## Ablauf

1. `Register.razor` zeigt die Checkbox und sendet den Wert als `CreateDemoData` im `RegisterRequest` an den Server.
2. `AuthController.RegisterAsync(...)` mappt das Feld auf `RegisterUserCommand`.
3. `UserAuthService.RegisterAsync(...)` prüft, ob der Benutzer der erste registrierte Account ist und enqueued bei aktivierter Option einen neuen Task des Typs `CreateDemoData`.
4. `BackgroundTaskRunner` verarbeitet den Task über den registrierten `DemoDataTaskExecutor`.
5. Der Executor löst `IDemoDataService.CreateDemoDataAsync(userId, createPostings: true, ct)` aus und meldet den Fortschritt über die vorhandene Task-Anzeige.
6. Die Startseite zeigt die laufende Aufgabe über `BackgroundTaskStatusPanel` an, sofern sie aktiv ist.

## Technische Hinweise

- Die Checkbox ist standardmäßig deaktiviert, damit bestehende Registrierungen unverändert bleiben.
- Die Demo-Daten-Erzeugung wird nur für den ersten Benutzer ausgelöst; spätere Registrierungen nutzen die normalen Login-/Registrierungs- und Admin-Flows ohne Hintergrundtask.
- Die unterliegende Generierung nutzt die vorhandenen Anwendungsservices, sodass die erzeugten Daten denselben fachlichen Pfad durchlaufen wie manuell erstellte Daten.
