← [Zurück zur Übersicht](index.md)

# Demo-Daten für den ersten Benutzer

## Zweck

Beim ersten Start ohne vorhandenes Benutzerkonto wird auf die Registrierungsseite geleitet. Dort kann der erste Benutzer über die Checkbox `Demodaten anlegen` entscheiden, ob nach der Registrierung ein Hintergrundtask den vollständigen Demo-Datenbestand erzeugen soll.

## Sichtbarer Ablauf für Anwender

1. Ohne vorhandenen Benutzer wird über `AuthRedirect` auf `/register` umgeleitet.
2. Auf der Registrierungsseite wird die Checkbox `Demodaten anlegen` nur im Erstbenutzer-Fall angezeigt.
3. Nach erfolgreicher Registrierung startet bei aktivierter Checkbox ein Hintergrundtask.
4. Auf der Startseite zeigt das bestehende `BackgroundTaskStatusPanel` den laufenden Task inklusive Fortschrittsmeldung an.

## Technischer Ablauf

1. `Register.razor` sendet `CreateDemoData` im `RegisterRequest`, aber nur als `_isFirstUser && _model.CreateDemoData`.
2. `AuthController.RegisterAsync(...)` mappt auf `RegisterUserCommand(..., request.CreateDemoData)`.
3. `UserAuthService.RegisterAsync(...)` prüft den Erstbenutzerstatus (`isFirst`) und queued nur bei `ShouldCreateDemoDataForFirstUser(...)` den Task `BackgroundTaskType.CreateDemoData`.
4. `BackgroundTaskRunner` verarbeitet den Task über `DemoDataTaskExecutor`.
5. `DemoDataTaskExecutor.ExecuteAsync(...)` ruft `IDemoDataService.CreateDemoDataAsync(context.UserId, createPostings: true, ct)` auf und setzt Statusmeldungen über `context.ReportProgress(...)`.
6. `BackgroundTaskStatusPanel` pollt `/api/background-tasks/active` und rendert Typ, Status, Zähler und Meldung.

## Umfang der erzeugten Demo-Daten

Datumsbasis ist der erste Tag des aktuellen Monats (`referenceMonthStart`). Die Erzeugung umfasst 24 Monate (aktueller Monat inklusive), wobei nur vergangene Monate direkt gebucht werden.

### Kontakte und Gruppen

`DemoDataService.CreateDemoDataSetAsync(...)` legt folgende Kontaktgruppen an:
- `Banken`
- `Arbeit`
- `Versicherungen`
- `Dienstleister`
- `Supermärkte & Einzelhandel`
- `Bäckereien & Cafés`

Angelegte Kontakte:
- Banken: `Musterbank Nord`, `Musterbank Süd`
- Organisationen/Personen: `Arbeitgeber GmbH`, `Zentrial Versicherung`, `SDAC`, `Sabbel Lüchtenhausen`
- Märkte: `Adli`, `Didl`, `Adeka`
- Bäckereien: `Bäckerei Kramphove`, `Bäckerei Feiping`, `Bäckerei Schlonz`
- Self-Kontakt: vorhandener `ContactType.Self` wird genutzt oder als `Self` angelegt

### Konten

`DemoDataService` erstellt drei Konten:
- `Girokonto` (`AccountType.Giro`, IBAN `DE12500105170648489890`)
- `Sparkonto Rücklagen` (`AccountType.Savings`, IBAN `DE44500105175407324931`)
- `Sparkonto Urlaub` (`AccountType.Savings`, IBAN `DE21500105176123456789`)

### Sparpläne

- `SDAC Gebühr` (`SavingsPlanType.Recurring`, 99,00 €, jährlich, Ziel nächster 01.01., Kategorie `Wiederkehrende Ausgaben`, mit Vertragsnummer)
- `Hausratversicherung` (`SavingsPlanType.Recurring`, 62,60 €, jährlich, Ziel nächster 01.12., Kategorie `Wiederkehrende Ausgaben`, mit Vertragsnummer)
- `Auto` (`SavingsPlanType.OneTime`, 14.000,00 €, Ziel in 10 Jahren am 06.07., Kategorie `Anlage`)
- `Urlaub` (`SavingsPlanType.Open`, ohne Zielbetrag)

### Budgets

Budgetkategorien:
- `Arbeit`
- `Versicherungen`
- `Wohnen`
- `Einkaufen & Verpflegung`

Budgetzwecke und Regeln:
- `Gehalt`: monatlich +3642,50 €
- `Rückstellung Hausratversicherung`: monatlich -5,22 € und jährlich +62,64 € (Dezember)
- `Hausratversicherung`: jährlich -62,60 € (Dezember)
- `Rückstellung SDAC`: monatlich -8,25 € und jährlich +99,00 € (Januar)
- `SDAC`: jährlich -99,00 € (Januar)
- `Wohnungsmiete`: monatlich -845,00 €
- `Supermärkte & Einzelhandel`: `BudgetValuationType.TotalBudget`
- `Bäckereien & Cafés`: `BudgetValuationType.TotalBudget`
- Kategorie-Regel für `Einkaufen & Verpflegung`: monatlich -300,00 €

### Wertpapiere und Kurse

Angelegte Wertpapiere:
- `USHSIV-MSCI WLD` (`LU00ABACAD96`, `EUR`, Kategorie `ETF`, Region `Global`, Sektor `MSCI World`, Beschreibung `UShares MSCI World ETF`)
- `Inländische Post AG` (`DE0001112026`, `EUR`, Kategorie `Aktien`, Region `DE`, Sektor `Logistik`)

Kursverlauf:
- über `CreateSecurityPriceHistoryAsync(...)`
- Zeitraum: von `referenceMonthStart.AddYears(-2)` bis `referenceMonthStart`
- nur Werktage
- Startwerte: 11,36 bzw. 44,25
- tägliche Veränderung nach dem ersten Kurs: Faktor zwischen -0,5 % und +2,0 %
- Importpfad über bestehende Import-Infrastruktur (`ISecurityPriceImportServiceFactory`, `ImportAsync(...)`)

### Buchungen und Kontoauszüge (24 Monate)

`CreateMonthlyPostingPlanAsync(...)` erzeugt pro Monat drei Statement Drafts (Giro + 2x Sparkonto).

Regeln pro Monat:
- Gehalt auf Girokonto am letzten Werktag (im aktuellen Monat nur, wenn dieser Tag bereits erreicht ist)
- Rückstellung Hausratversicherung: -5,22 € Giro / +5,22 € Sparkonto Rücklagen
- Sparplan Urlaub: -50,00 € Giro / +50,00 € Sparkonto Urlaub
- Rückstellung SDAC: -8,25 € Giro / +8,25 € Sparkonto Rücklagen
- Wohnungsmiete: -845,00 € am ersten Werktag
- Kartenzahlungen für Märkte und Bäckereien: je Kontakt 1–2 Zahlungen pro Woche, Beträge 10,00 € bis 30,00 €

Zusatzregeln:
- Dezember: Auflösung Hausrat-Rückstellung (+62,64 € Giro / -62,64 € Sparkonto Rücklagen) und Versicherungsbeitrag -62,60 € (ab 16.12. auf nächsten Werktag verschoben)
- Januar: Auflösung SDAC-Rückstellung (+99,00 € Giro / -99,00 € Sparkonto Rücklagen)
- Monat 1 der 24-Monatsreihe: Kauf `USHSIV-MSCI WLD` über 2.000,00 €
- Jeder dritte Monat: Dividende `USHSIV-MSCI WLD` (Brutto zufällig 15,00 € bis 30,00 €, Steuer 25 %, Netto als Buchung)
- Monat 6: Kauf `Inländische Post AG` mit 62 Stück zum verfügbaren Kurs
- Ab Monat 6 jeweils im Mai: Dividende `Inländische Post AG` (4 % vom aktuellen Wert, 25 % Steuer)

Wichtig:
- Vergangene Monate werden gebucht (`BookDraftAsync(...)`).
- Für den aktuellen Monat bleiben die drei Drafts im Status Entwurf; es entstehen dort keine gebuchten Postings.

## Fortschritt und Backgroundtask-Anzeige

- Task-Typ: `BackgroundTaskType.CreateDemoData`
- Startmeldung: `Demo-Daten werden angelegt...`
- Erfolgsmeldung: `Demo-Daten wurden angelegt.`
- Abbruchmeldung: `Demo-Daten-Anlage abgebrochen.`

Die Anzeige erfolgt ohne Sonder-UI über das generische `BackgroundTaskStatusPanel` auf `Home.razor`.

## API-Bezug

- `POST /api/auth/register`: akzeptiert `RegisterRequest` mit Feld `createDemoData` (Standard `false`).
- `POST /api/users/demo/{userId}`: ermöglicht explizites Erzeugen von Demo-Daten für einen Benutzer über `DemoRequest.createPostings`.

## Einschränkungen

- Die Checkbox ist nur im Erstbenutzer-Flow sichtbar und wirksam.
- Zufallswerte sind zwar reproduzierbar (fester Seed), aber nicht fachlich frei konfigurierbar.
- Der aktuelle Monat enthält ungebuchte Drafts statt gebuchter Postings.
