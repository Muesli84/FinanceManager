# FA-REP-006: Umsatz?Auswertungsgraphen f�r alle Entit�ten mit Postings

## Kurzbeschreibung
Auf den Detailseiten von Bankkonten, Kontakten, Sparpl�nen und Wertpapieren sollen die zugeh�rigen Ums�tze (Postings) als Balkendiagramm visualisiert werden. Der Nutzer kann das Aggregationsintervall (Monat, Quartal, Halbjahr, Jahr) umschalten. Die Darstellung erfolgt �ber eine wiederverwendbare Blazor-Komponente.

## Ziele / Nutzen
- Schnelle visuelle Einsch�tzung von Verlauf / Intensit�t der Bewegungen.
- Einheitliches UX-Pattern f�r verschiedene Entit�tstypen (Konto, Kontakt, Sparplan, Wertpapier/Security).
- Grundlage f�r sp�tere Erweiterungen (Tooltips, Drill?Down, Vergleich Vorjahr, Trendlinien).

## Geltungsbereich
Detailseiten folgender Entit�ten:
1. Account (Bankkonto)
2. Contact (Kontakt) � nur Postings mit Kind=Contact
3. SavingsPlan � Postings Kind=SavingsPlan
4. Security (Wertpapier) � Postings Kind=Security (Summe der Betr�ge, zus�tzliche Menge zun�chst nicht visualisiert)

## Fachliche Anforderungen
| Nr | Beschreibung |
|----|--------------|
| FA-REP-006-01 | Balkendiagramm zeigt aggregierte Summe der Betr�ge pro Intervall (Saldo: einfache Summe; negative Werte nach unten). |
| FA-REP-006-02 | Umschaltbare Intervalle: Monat, Quartal, Halbjahr, Jahr. |
| FA-REP-006-03 | Standardintervall: Monat. Auswahl persistiert nur im UI-State (kein Server-Storage). |
| FA-REP-006-04 | Leere Perioden (ohne Postings) erscheinen mit Wert 0 (Balkenh�he = 0) � optional, Minimale Umsetzung: Nur vorhandene Perioden. Entscheidung: Zun�chst nur vorhandene Perioden (Out-of-Scope Auff�llen). |
| FA-REP-006-05 | Negativbetr�ge nach unten, Positivbetr�ge nach oben (getrennte H�lfte des Graphen). |
| FA-REP-006-06 | Tooltip / Title Attribut zeigt Zeitraum + Betrag (lokal formatiert). |
| FA-REP-006-07 | Aggregation basiert auf Buchungsdatum (BookingDate). |
| FA-REP-006-08 | Paging nicht erforderlich; maximal 5 Jahre r�ckw�rts laden (konfigurierbar). Default Scope: Letzte 24 Monate f�r Monatsansicht, sonst dynamisch per Intervall. |
| FA-REP-006-09 | Keine separate API je Intervall; API erh�lt Parameter `period` (Month|Quarter|HalfYear|Year). |
| FA-REP-006-10 | Wechsel des Intervalls l�dt Daten neu (Spinner / Skeleton). |

## Nicht-funktionale Anforderungen
- NFA-REP-006-01: Rendering performant (nur einfache SVG / Div Balken; kein schweres Charting-Framework jetzt).
- NFA-REP-006-02: Responsives Layout (Balken schrumpfen; horizontales Scrollen vermeiden; bei sehr vielen Perioden ggf. komprimierte Darstellung � initial: Overflow X auto zul�ssig).
- NFA-REP-006-03: Lokalisierte Datumsformatierung & Nummern.
- NFA-REP-006-04: Wiederverwendbare Komponente ohne Businesslogik (nur Anzeige / Intervallsteuerung). Datenbeschaffung �ber aufrufende Seite / Callback.
- NFA-REP-006-05: Barrierefreiheit: Titel/Texte f�r Screenreader (aria-label pro Balken mit Zeitraum & Betrag).

## Datenmodell / API
Erweiterung vorhandener Aggregations- oder Endpunkt-Strategie (falls nicht vorhanden):
`GET /api/{entityType}/{id}/aggregates?period={Month|Quarter|HalfYear|Year}`

R�ckgabe (JSON Array):
```json
[
  { "periodStart": "2025-01-01", "amount": 123.45 }
]
```
Optional: `periodLabel` kann clientseitig formatiert werden.

Erforderliche Endpunkte (falls nicht existent):
- Accounts: Bereits vorhanden f�r Month/Quarter/Year ? erweitern um HalfYear (PeriodEnum erg�nzen).
- Contacts: Neu `/api/contacts/{id}/aggregates`.
- SavingsPlans: Neu `/api/savings-plans/{id}/aggregates`.
- Securities: Neu `/api/securities/{id}/aggregates`.

Enum (Shared DTO) erweitern / neu: `AggregationPeriod { Month=0, Quarter=1, HalfYear=2, Year=3 }`.

## Aggregationslogik (Server)
- Basis: Tabelle `Postings` (Kind + EntityId Filter je Kontext).
- Periodenstart-Berechnung:
  - Month: 1. Tag des Monats.
  - Quarter: 1. Tag (Month = ((m-1)/3)*3+1).
  - HalfYear: 01.01 oder 01.07.
  - Year: 01.01.
- GroupBy (PeriodStart) ? Sum(Amount).
- Sortierung ascending nach PeriodStart.
- Limit Zeitraum (Konfiguration, z.B. `Aggregation:MaxYearsBack` default 5) ? Filter `BookingDate >= Today.AddYears(-MaxYearsBack)`.

## Blazor UI Komponente
`Components/Shared/AggregateBarChart.razor`
Parameter:
```csharp
[Parameter] public IReadOnlyList<AggregatePoint>? Data { get; set; }
[Parameter] public string? Title { get; set; }
[Parameter] public AggregationPeriod Period { get; set; }
[Parameter] public EventCallback<AggregationPeriod> PeriodChanged { get; set; }
[Parameter] public bool IsLoading { get; set; }
[Parameter] public string PositiveColor { get; set; } = "#2d6cdf";
[Parameter] public string NegativeColor { get; set; } = "#c94";
[Parameter] public string AxisColor { get; set; } = "#555";
```
ViewModel / DTO:
```csharp
public sealed record AggregatePoint(DateTime PeriodStart, decimal Amount);
```
Features:
- DropDown f�r Period (Month, Quarter, HalfYear, Year) ? PeriodChanged.
- Berechnung max Abs(Amount) ? Skalierung.
- Zwei vertikale H�lften (positiv/negativ) wie bereits bei Accounts (Wiederverwendung Logik extrahieren).
- Fallback Anzeige bei leerer Liste (lokalisierter Text `NoData`).

## Lokalisierung
Neue Ressourcenschl�ssel (de/en):
- Chart_Title_Account / Contact / SavingsPlan / Security (optional spezifisch oder dynamisch).
- Chart_Period_Month, Chart_Period_Quarter, Chart_Period_HalfYear, Chart_Period_Year
- Chart_NoData
- Chart_Loading

## Akzeptanzkriterien
1. Umschalten der Intervalle l�dt neue Daten (sichtbar durch Spinner) und aktualisiert Balken.
2. Negative Betr�ge werden unterhalb der Nulllinie dargestellt.
3. HalfYear funktioniert analog (zwei Balken pro Jahr bei Daten vorhanden).
4. Kein JavaScript-Fehler im Browser (reines Blazor/SVG/Div Layout).
5. Screenreader liest jeden Balken (Aria-Label mit Zeitraum & Betrag).
6. Zeitraumgrenze (MaxYearsBack) wird eingehalten � �ltere Postings werden nicht aggregiert.
7. Wechsel der Seite (Navigieren zu anderer Entit�t) isoliert Zustand (Intervall = Default Monat).

## Risiken / Offene Punkte
- Performance bei sehr vielen Perioden gering (Mit Limit mitigiert).
- Unterschiedliche Aggregationsendpunkte ? erw�gen zentralen Aggregationsservice (Refactoring sp�ter).
- Halbjahresaggregation neu � sicherstellen, dass Enum kompatibel bleibt (Migration Frontend Ressourcen).

## Erweiterungen (Out of Scope Jetzt)
- Vergleich Vorjahr (Doppelte Balken nebeneinander).
- Tooltip mit kumuliertem Verlauf / gleitendem Durchschnitt.
- Export (CSV / PNG Snapshot).
- Stacked Bars nach Kategorien.

## Umsetzungsschritte (vorschlag)
1. Shared Enum + DTO erweitern (Shared Projekt).
2. Infrastruktur: Aggregationslogik in dedizierten Service extrahieren `IPostingAggregationService` (Filter by kind + id + period + yearsBack) ? Wiederverwendung f�r alle Endpunkte.
3. Neue API-Controller oder vorhandene erweitern (Accounts bereits vorhanden; weitere analog).
4. Unit Tests Service (PeriodStart Berechnung, Grenzen, Negative Summen).
5. UI Komponente implementieren (Refactor existierende Account-Darstellung ? Nutzung Komponente).
6. Detailseiten anpassen (AccountDetail refactor, neue Abschnitte bei Contact/SavingsPlan/Security).
7. Ressourcenschl�ssel / Lokalisierung.
8. Accessibility Check.

## Branch / Commit
- Branch: `feature/rep-graphs`  
- Commit Prefix: `feat(rep-graphs): ...`

---
Letzte Aktualisierung: (Initial)