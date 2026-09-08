# Umsetzungsplan: Demo-Daten

## Übersicht

Die Demo-Daten-Anforderung erweitert den Erstregistrierungs- und Login-Flow um einen optionalen Hintergrundjob, der für den ersten Benutzer vollständige Demo-Daten erzeugt. Dabei bleiben das vorhandene `BackgroundTaskInfo`/`BackgroundTaskContext`-Modell und die bestehenden Business-Services unverändert; nur die First-User-Logik, die Seed-Generierung im `DemoDataService` und die Startseiten-Fortschrittsanzeige werden erweitert. Betroffene Bereiche sind der Registrierungsfluss (`Register.razor`, `AuthRedirect`, `UserAuthService`), die Demo-Daten-Orchestrierung (`DemoDataService`, `DemoDataTaskExecutor`), das allgemeine Background-Task-Statusmodell und die Startseite.

## Designentscheidungen

| Komponente / Bereich | Gewählter Ansatz | Begründung |
|----------------------|-----------------|------------|
| Erstbenutzer-Checkbox | Nur für den ersten Benutzer sichtbar und wirksam; bei späteren Registrierungen nicht sichtbar bzw. ignoriert | Die Anforderung ist explizit: Die Checkbox ist nur für den ersten Benutzer relevant und wirksam. |
| Fortschrittsmodell | Bestehendes `BackgroundTaskInfo`/`BackgroundTaskContext`-Modell unverändert nutzen; keine neuen Progress-Modelle einführen | Die Anforderung bestätigt das vorhandene Modell und verlangt ausdrücklich, kein neues Progress-Modell zu ergänzen. |
| Datensatz- und Buchungsreihenfolge | Deterministische Reihenfolge nach Monat, Tag, Kontakt/Gruppenbezug, Sparplan und Typ | Reproduzierbare Abfolge ist notwendig für validierte Testfälle, Snapshots und Nachvollziehbarkeit. |
| Zufallswerte | Reproduzierbare Zufallswerte mit festem Seed im `DemoDataService` | Die Demo-Daten müssen realistisch sein, aber auch in Tests reproduzierbar und deterministisch verlaufen. |
| Monatsgrenzen | Lokale Hilfslogik im `DemoDataService` für ersten/letzten Werktag und Monatsgrenzen | Die Anforderung sagt ausdrücklich, dass diese Berechnungen lokal in der Demo-Logik erfolgen. |
| Preisimport | Bestehende Importfunktion für Wertpapierkurse verwenden und keine eigene Inline-Generierung etablieren | Die Anforderung verlangt den regulären Importpfad, damit die Demo-Daten den Produktivfluss exakt abbilden. |

## Programmabläufe

### 1. Erstregistrierung mit optionalem Demo-Data-Job

1. `AuthRedirect` erkennt beim Start ohne Benutzer und leitet auf `/register` weiter.
2. `Register.razor` rendert die Checkbox `CreateDemoData` mit dem Label `Demodaten anlegen` und setzt den Wert in `RegisterRequest.CreateDemoData`.
3. `UserAuthService.RegisterAsync` prüft den First-User-Status und akzeptiert das `CreateDemoData`-Flag nur dann.
4. Bei `CreateDemoData == true` und `isFirst == true` wird ein `BackgroundTaskType.CreateDemoData`-Task in den allgemeinen Task-Manager eingereiht.
5. Spätere Registrierungen besitzen keinen Effekt auf den Demo-Data-Task; die Checkbox ist dort nicht sichtbar bzw. wirksam.
6. Der Registrierungsflow selbst bleibt unverändert; nur der erste Benutzer kann die Demo-Daten optional aktivieren.

Beteiligte Klassen/Komponenten: `AuthRedirect`, `Register.razor`, `RegisterRequest`, `UserAuthService`, `IBackgroundTaskManager`, `BackgroundTaskType`.

### 2. Ausführung des Demo-Daten-Background-Tasks

1. `DemoDataTaskExecutor.ExecuteAsync` liest `context.UserId` und startet `IDemoDataService.CreateDemoDataAsync(userId, createPostings: true, ct)`.
2. `DemoDataService` baut die komplette Datenmenge über die vorhandenen Business-Services auf: Kontakte, Gruppen, Bankkonten, Sparpläne, Budgets, Wertpapiere, Kurse, Kontoauszüge und Buchungen.
3. Der Task meldet Teilfortschritte und Gesamtstatus über das vorhandene `BackgroundTaskInfo`/`ReportProgress`-Mechanismus.
4. Erfolgs- und Fehlerpfade laufen über den generischen `BackgroundTaskRunner` und bleiben konsistent mit anderen Hintergrundjobs.
5. Die Startseite zeigt den Fortschritt im regulären UI-Mechanismus an, ohne ein neues Statusmodell einzuführen.

Beteiligte Klassen/Komponenten: `DemoDataTaskExecutor`, `IDemoDataService`, `DemoDataService`, `BackgroundTaskRunner`, `BackgroundTaskInfo`, `BackgroundTaskContext`.

### 3. Generierung der 24-Monats-Datenbasis

1. `DemoDataService` setzt den Referenzstichtag auf den ersten Tag des aktuellen Monats und berechnet die relevanten Monatsfenster der letzten 24 Monate.
2. Die festen Kontakte, Gruppen, Bankkontakte, Bankkonten, Self- und Arbeitgeber-/Versicherungsbeziehungen werden in determiniertem Ablauf angelegt.
3. Sparpläne, Budgets, Regeln und Kontenzuordnungen werden nach der fachlichen Spezifikation mit deterministischer Reihenfolge und festen Seed-Werten erzeugt.
4. Für `USHSIV-MSCI WLD` und `Inländische Post AG` werden Metadaten, historischer 2-Jahres-Kursverlauf und Import-Logik über die vorhandene Security-Importpipeline aufgebaut.
5. Die Kontoauszug- und Buchungslogik durchläuft alle Monate der 24-Monats-Sicht, weist aber den aktuellen Monat bewusst als ungebucht aus.
6. Sonderregeln wie Gehalt, Miete, Versicherungsbeiträge, Rückstellungen, Kartenzahlungen, Wertpapierkäufe und Dividendenzahlungen werden in einer fachlich definierten Reihenfolge angelegt.

Beteiligte Klassen/Komponenten: `DemoDataService`, Domain-/Application-Services für Kontakte, Konten, Sparpläne, Budgets, Wertpapiere, Auszüge und Buchungen.

### 4. Fortschrittsanzeige auf der Startseite

1. `BackgroundTaskStatusPanel` bleibt der generische Sicht für aktive Hintergrundjobs.
2. `Home.razor` zeigt den laufenden `CreateDemoData`-Task identisch zu anderen Backgroundjobs in der regulären Statusanzeige.
3. `DemoDataService` und `DemoDataTaskExecutor` melden Teilfortschritte über dieselbe generische Statuslösung, damit die Startseite die laufende Generierung nachvollziehbar zeigt.
4. Die Anzeige bleibt unabhängig von der internen Datenquelle und für den Benutzer sichtbar und konsistent.

Beteiligte Klassen/Komponenten: `BackgroundTaskStatusPanel`, `Home.razor`, `DemoDataTaskExecutor`, `BackgroundTaskRunner`, `BackgroundTaskInfo`.

## Neue Klassen

Keine zwingend erforderlichen neuen Klassen. Die Umsetzung erfolgt durch Erweiterung der vorhandenen Orchestrierungs- und Background-Komponenten. Wenn die Fachlogik in `DemoDataService` sehr groß wird, kann eine kleine lokale Hilfsklasse für deterministische Monats-/Werktagsberechnung ergänzt werden, aber sie ist keine fachliche Anforderung und optional.

## Änderungen an bestehenden Klassen

### `FinanceManager.Infrastructure.Demo.DemoDataService` (Klasse)

- Neue Eigenschaften: `DemoDataSeedSettings` (optional, falls zentrale Seed-/Zeitwerte in einer Hilfsstruktur zusammengefasst werden sollen) — zentrale fachliche Seed- und Monatsgrenzen.
- Neue Methoden: `CreateDemoDataSetAsync`, `CreateMonthlyPostingPlanAsync`, `CreateSecurityPriceHistoryAsync`, `GetFirstBusinessDayOfMonth`, `GetLastBusinessDayOfMonth`, `BuildDeterministicSeed` — fachliche Unterteilung der 24-Monats-Logik, Werktagsberechnung und reproduzierbare Zufallsbehandlung.
- Geänderte Methoden: `CreateDemoDataAsync` — erweitert um komplette 24-Monats-Logik, deterministische Reihenfolge, lokale Monats-/Werktagslogik und Nutzung des bestehenden Security-Importpfads.
- Neue Events: keine.
- Neue Event-Handler: keine.

### `FinanceManager.Infrastructure.Auth.UserAuthService` (Klasse)

- Neue Eigenschaften: keine.
- Neue Methoden: `ShouldCreateDemoDataForFirstUser` (optional, privat) — trennt First-User-Prüfung und Checkbox-Logik klar ab.
- Geänderte Methoden: `RegisterAsync` — prüft nur beim ersten Benutzer das Flag und enqueues nur dann den Demo-Task.
- Neue Events: keine.
- Neue Event-Handler: keine.

### `FinanceManager.Web.Components.Pages.Register` (Razor-Komponente)

- Neue Eigenschaften: keine zusätzlichen; das vorhandene `CreateDemoData`-Flag bleibt nur im First-User-Flow wirksam.
- Neue Methoden: keine.
- Geänderte Methoden: `RegisterAsync` — Transport des Flags nur für den Erstbenutzer sicherstellen.
- Neue Events: keine.
- Neue Event-Handler: keine.

### `FinanceManager.Web.Components.AuthRedirect` (Komponente)

- Neue Eigenschaften: keine.
- Neue Methoden: keine.
- Geänderte Methoden: `CheckRedirectAsync` — auf First-User-Check und Redirect auf `/register` fokussieren.
- Neue Events: keine.
- Neue Event-Handler: keine.

### `FinanceManager.Web.Components.BackgroundTaskStatusPanel` (Komponente)

- Neue Eigenschaften: keine.
- Neue Methoden: optional `RenderProgressState` oder `FilterActiveDemoTask` — nur falls die generische Anzeige für den Demo-Task zusätzliche visuelle Klarheit benötigt.
- Geänderte Methoden: `LoadTasksAsync` / `PollLoopAsync` — bestehende generische Anzeige für Teilfortschritte des Demo-Tasks sauber nutzen.
- Neue Events: keine.
- Neue Event-Handler: keine.

### `FinanceManager.Application.BackgroundTaskRunner` / `BackgroundTaskContext` (System)

- Neue Eigenschaften: keine.
- Neue Methoden: keine neuen Modelle; vorhandene Status-/Fortschrittsfelder werden detaillierter genutzt.
- Geänderte Methoden: `ExecuteAsync` — Teilfortschritte im bestehenden Modell präziser melden, ohne das Modell zu erweitern.
- Neue Events: keine.
- Neue Event-Handler: keine.

## Datenbankmigrationen

Keine.

## Validierungsregeln

| Feld / Objekt | Regel | Fehlerfall |
|---------------|-------|------------|
| `RegisterRequest.CreateDemoData` | Boolesches Flag, standardmäßig `false`, nur im First-User-Flow sichtbar und wirksam | Kein Fehlerfall in der UI; falscher Wert wird als `false` behandelt. |
| First-User-Prüfung | Nur wenn `HasAnyUsersAsync == false` darf ein `CreateDemoData`-Task enqueued werden | Wenn kein Erstbenutzer existiert, wird kein Task erzeugt. |
| Datumslogik | Für den aktuellen Monat werden keine Auszüge gebucht; nur verarbeitete historische Monate und der aktuelle Monat ohne Auszugseingang sind relevant | Inkonsistente Monatsdaten würden die fachliche Logik verletzen. |
| Reihenfolge | Datensätze und Buchungen müssen in einer festen Reihenfolge erzeugt werden | Tests und Snapshots werden unzuverlässig und nicht reproduzierbar. |
| Zufallswerte | Zufallsbereiche sind begrenzt und durch festen Seed reproduzierbar | Die Daten sind nicht deterministisch und Testläufe sind nicht reproduzierbar. |
| Kurs-/Importlogik | Für die Wertpapiere muss der vorhandene Security-Importpfad verwendet werden; kein eigener Inline-Importpfad | Die Demo-Daten würden nicht den produktiven Importpfad abbilden. |

## Konfigurationsänderungen

Keine zusätzlichen Konfigurationswerte erforderlich.

## Seiteneffekte und Risiken

- Erstbenutzer-Fluss: Die erste Registrierung wird durch das Demo-Task-Queueing länger; der Login-/Startseiten-Flow muss robust gegen Parallelität sein.
- Background-Task-Status: Andere aktive Tasks werden in derselben Anzeige sichtbar; Teilfortschritte müssen sauber formatiert werden, damit die Anzeige nicht nur 0/1 zeigt.
- Demo-Datenerzeugung: Die vollständige 24-Monats-Seed-Logik ist umfangreich und beeinflusst Backups, Snapshots und Restore-Tests.
- Kurs-/Importlogik: Die Verwendung des vorhandenen Importpfads kann bestehende Security-Import- und Backup-Tests beeinflussen; die Reihenfolge und Zeitbasis müssen deterministisch sein.
- Monatslogik: Fehler bei erster/letzter Werktagsberechnung oder beim Überspringen des aktuellen Monats führen zu fachlich falschen Datensätzen.

## Umsetzungsreihenfolge

1. **First-User-Flow und Checkbox festigen**
   - Voraussetzungen: Bestehende `Register.razor`, `RegisterRequest`, `UserAuthService`, `AuthRedirect`, `IBackgroundTaskManager` und `BackgroundTaskType.CreateDemoData` im Repo.
   - Beschreibung: Die Checkbox wird nur für den ersten Benutzer sichtbar und wirksam gemacht; der Task wird nur bei `CreateDemoData && isFirst` angelegt.

2. **Background-Task-Status für Demo-Daten präzisieren**
   - Voraussetzungen: `BackgroundTaskInfo`, `BackgroundTaskContext`, `BackgroundTaskRunner`, `DemoDataTaskExecutor` im vorhandenen Mechanismus vorhanden.
   - Beschreibung: Die Demo-Aufgabe meldet Teilfortschritte über das bestehende Statusmodell, ohne neue Progress-Modelle einzuführen.

3. **Deterministische Seed- und Monatslogik im `DemoDataService` festlegen**
   - Voraussetzungen: `IDemoDataService`-Schnittstelle und Business-Services für Kontakte, Konten, Sparpläne, Budgets, Wertpapiere und Auszüge im Repo.
   - Beschreibung: Feste Reihenfolge, Seed-basiertes Random, erster/letzter Werktag und Monatsgrenzen werden in die Demo-Logik eingebaut.

4. **Vollständige 24-Monats-Daten- und Buchungslogik implementieren**
   - Voraussetzungen: Fachliche Vorgaben für Kontakte, Bankkonten, Budgets, Sparpläne, Wertpapierdaten und Auszüge sind klar definiert.
   - Beschreibung: Die komplette Seed-Erzeugung läuft über die vorhandenen Business-Services mit historischer 24-Monats-Sicht und ohne Buchung des aktuellen Monats.

5. **Security-Importpfad für Wertpapierkurse einbinden**
   - Voraussetzungen: `ISecurityPriceImportService`, `ISecurityPriceImportServiceFactory`, Security-Service und Importpipeline im Repo vorhanden.
   - Beschreibung: Die Kursdaten der beiden Wertpapiere werden über den regulären Importpfad erzeugt; keine separate manuelle Inline-Generierung.

6. **UI-Fortschrittsanzeige an den generischen Task anpassen**
   - Voraussetzungen: `BackgroundTaskStatusPanel` und `Home.razor` vorhanden.
   - Beschreibung: Die Startseite zeigt den aktiven Demo-Task in der allgemeinen Background-Statusanzeige an.

7. **Verifikation durch gezielte Tests**
   - Voraussetzungen: Testprojekt und vorhandene Auth-/Demo-/Background-Task-Tests im Repo.
   - Beschreibung: Neue Tests ergänzen die vorhandenen Integrationstests für Erstbenutzer, Seed-Daten und UI-Fortschritt.

## Tests

### Neue Tests

| Test / Hilfsmethode | Testklasse | Was wird geprüft / bereitgestellt? |
|--------------------|------------|-------------------------------------|
| `RegisterAsync_ShouldQueueDemoData_WhenFirstUserAndCreateDemoDataEnabled` | `UserAuthServiceTests` | Erstbenutzer mit aktivierter Checkbox erzeugt den `CreateDemoData`-Task; spätere Registrierungen bleiben unbeeinflusst. |
| `CreateDemoDataAsync_ShouldCreateCompleteSeedSet_WhenCreatePostingsTrue` | `DemoDataServiceTests` | Vollständiger 24-Monats-Datensatz mit Kontakten, Bankkonten, Sparplänen, Budgets, Wertpapieren, Kursen und Buchungen. |
| `CreateDemoDataAsync_ShouldSkipCurrentMonthPostings` | `DemoDataServiceTests` | Auszüge des aktuellen Monats werden nicht gebucht; die Monatsgrenzen funktionieren korrekt. |
| `CreateDemoDataAsync_ShouldUseDeterministicSeed` | `DemoDataServiceTests` | Zufallswerte und Reihenfolge sind reproduzierbar und damit testbar. |
| `CreateDemoDataAsync_ShouldUseSecurityPriceImportPath` | `DemoDataServiceTests` | Kurse werden über den vorhandenen Security-Importpfad erzeugt und nicht inline. |
| `Home_ShouldDisplayBackgroundProgress_ForCreateDemoDataTask` | `HomePageTests` / UI-Integration | Die Startseite zeigt die reguläre Background-Task-Fortschrittsanzeige für den Demo-Task. |
| `BuildDeterministicDemoSeed` (Hilfsmethode) | `DemoDataServiceTests` | Reproduzierbarer Seed-Builder für deterministische, fachlich realistische Daten. |

### Betroffene bestehende Tests

| Test / Testklasse | Grund der Anpassung |
|-------------------|---------------------|
| `UserAuthServiceTests` | Erstbenutzer-Flow und Checkbox-Queueing werden präziser abgesichert. |
| `ApiClientDemoDataTests` | Die vorhandenen Mindestprüfungen müssen auf den vollständigen 24-Monats-Datensatz und die neuen Regeln erweitert werden. |
| `ApiClientBackupsWithDemoDataTests` | Snapshots und Restore-Verhalten müssen auf den erweiterten Datensatz und deterministische Reihenfolge passen. |
| `ApiClientBackgroundTasksTests` | Background-Task-Status und Progress-Display für den `CreateDemoData`-Task werden ergänzt. |

### E2E-Tests (Pflicht)

| Szenario | Testdatei / Testklasse | Abgedecktes Akzeptanzkriterium |
|----------|------------------------|-------------------------------|
| Erstbenutzer registriert sich mit aktivierter Checkbox | `RegisterE2ETests` | Ohne vorhandenen Benutzer redirectet die App auf `/register`; die Checkbox ist sichtbar; beim Registrieren startet der Demo-Job. |
| Startseite zeigt laufenden Demo-Task an | `HomeE2ETests` | Während der Demo-Generierung ist der generische Background-Task-Fortschritt auf der Startseite sichtbar. |
| Erstbenutzer registriert sich ohne Checkbox | `RegisterE2ETests` | Ohne Aktivierung startet kein Demo-Task. |
| Spätere Registrierungen bleiben ohne Effekt | `AuthE2ETests` | Nach dem ersten Benutzer ist die Checkbox nicht wirksam; kein zusätzlicher Demo-Task wird gestartet. |

Welche bestehenden E2E-Tests müssen angepasst werden?

| Test / Testklasse | Grund der Anpassung |
|-------------------|---------------------|
| Keine identifizierten bestehenden E2E-Tests; neue Coverage ist erforderlich. |

## Offene Punkte

Keine.
