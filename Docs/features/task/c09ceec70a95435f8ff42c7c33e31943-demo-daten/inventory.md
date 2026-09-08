# Bestandsaufnahme: Vollständige Demo-Daten-Anforderung

Analysiert wurde der bestehende Demo-Daten- und Erstregistrierungs-Flow in `FinanceManager.Application`, `FinanceManager.Infrastructure`, `FinanceManager.Web` sowie die vorhandenen Tests. Fokus war die Ist-Implementierung gegenüber der Anforderung zur vollständigen Anlage von Demo-Daten inkl. Background-Progress.

## Zusammenfassung

- `IDemoDataService` ist vorhanden und produktiv registriert (`FinanceManager.Infrastructure.Demo.DemoDataService`), wird aber fachlich nur mit einem **reduzierten Demo-Datensatz** umgesetzt.
- Der Erstbenutzer-Flow ist technisch vorhanden:
  - Redirect auf Registrierung bei leerem System (`AuthRedirect`, `Login.razor`),
  - Checkbox „Demodaten anlegen“ in `Register.razor`,
  - Queueing eines Background-Tasks nur für den ersten Benutzer in `UserAuthService.RegisterAsync`.
- Background-Task-Integration ist vorhanden (`BackgroundTaskType.CreateDemoData`, `DemoDataTaskExecutor`, `BackgroundTaskRunner`, `BackgroundTaskStatusPanel` auf Home).
- Fortschrittsmeldung für Demo-Daten ist nur grob (0/1 → 1/1), **kein fachlicher Teilfortschritt** für die lange 24-Monats-Anlage.
- Die aktuelle Demo-Daten-Erzeugung weicht stark von der Anforderung ab:
  - deutlich weniger Kontakte/Konten/Sparpläne/Budgets,
  - nur ein Wertpapier (`MSCI World`) statt zwei geforderter Papiere,
  - Kursdaten monatlich (24 Punkte) statt werktäglich (2 Jahre),
  - kein Import über den bestehenden Preis-Import-Mechanismus,
  - keine vollständige Kontoauszug-/Buchungslogik über 24 Monate gemäß fachlichen Regeln.
- Es existiert zusätzlich eine zweite, nicht eingebundene Klasse `FinanceManager.Infrastructure.Auth.DemoDataService` (nur ContactCategory-Seeding), ohne erkennbare Verwendung.

## Details

- [Datenmodell](inventory/models.md)
- [Logik](inventory/logic.md)
- [Enums](inventory/enums.md)
- [Interfaces](inventory/interfaces.md)
- [Tests](inventory/tests.md)
