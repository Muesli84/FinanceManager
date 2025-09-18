# FA-AUSZ-016: Konfigurierbare Monatsbasierte Aufteilung von Kontoauszugs-Imports

## Kurzbeschreibung
Die bisherige Aufteilung eines Kontoauszug-Imports in mehrere Statement Drafts erfolgt ausschlie�lich auf Basis einer maximalen Anzahl von Eintr�gen pro Draft. Zuk�nftig soll � sobald eine (konfigurierbare) Schwellwert-Anzahl von Eintr�gen �berschritten w�rde � die Aufteilung prim�r monatsbasiert (nach Buchungsdatum) erfolgen. Damit werden Buchungszeitr�ume klar abgegrenzt und die Nachvollziehbarkeit erh�ht. Das Verhalten (Modus & Grenzwerte) soll der Anwender in einem neuen Register der Einrichtungs-/Setup-Seite konfigurieren k�nnen.

## Ziele / Nutzen
- Bessere fachliche Struktur: Ein Draft ? ein Kalendermonat.
- Vermeidung sehr gro�er Drafts bei Massenuploads.
- Reduzierte manuelle Nacharbeit zur periodischen Auswertung.
- Flexibilit�t durch Umschaltbarkeit der Strategie.

## Aktueller Zustand (IST)
- Import erstellt eine oder mehrere Drafts mit fortlaufender Bef�llung bis `MaxEntriesPerDraft` (fix / Code-Konstante oder Konfiguration).
- Monatliche Trennung nicht gew�hrleistet (Monatswechsel kann mitten im Draft liegen).

## Neuer Soll-Prozess
1. Datei wird eingelesen und die Einzelbewegungen (Entries) extrahiert.
2. Konfiguration des Users wird geladen:
   - Modus (Enum):
     - `FixedSize` (Legacy-Verhalten)
     - `Monthly` (Monatsaufteilung bei Bedarf)
     - `MonthlyOrFixed` (Monatsaufteilung sobald Schwellwert �berschritten w�rde � Standard)
   - `MaxEntriesPerDraft` (int, >0)
   - `MonthlySplitThreshold` (int, optional � Anzahl Eintr�ge ab der monatliche Splits erzwungen werden; Default = `MaxEntriesPerDraft`).
3. Iteration �ber sortierte Bewegungen (nach BookingDate asc):
   - Start eines Drafts mit erstem Eintrag.
   - Bei Modus = `FixedSize`: Split nur wenn Count == MaxEntriesPerDraft.
   - Bei Modus = `Monthly`: Split beim Monatwechsel (BookingDate.Month != current.Month).
   - Bei Modus = `MonthlyOrFixed`:
     - Wenn Monat wechselt ? Split.
     - Sonst wenn (aktueller Draft Count == MaxEntriesPerDraft) ? Split.
     - Oder wenn (aktueller Count + verbleibende Eintr�ge dieses Monats) > MonthlySplitThreshold ? vorzeitig splitten (alle restlichen Eintr�ge dieses Monats in neuen Draft?). Entscheidung: Vorzeitiges Splitten direkt beim �berschreiten, Rest des Monats in neuen Draft (einfaches Verhalten).
4. Draft-Metadaten (AccountName, OriginalFileName, CreatedUtc) wie bisher.
5. Persistierung in Transaktion.

## Funktionale Anforderungen
| Nr | Beschreibung |
|----|--------------|
| FA-AUSZ-016-01 | Neuer Konfigurationsbereich "Import-Aufteilung" im Setup (Registerkarte). |
| FA-AUSZ-016-02 | Einstellbare Werte: Modus (Dropdown), MaxEntriesPerDraft (Numeric), MonthlySplitThreshold (Numeric, sichtbar nur bei Modus "MonthlyOrFixed"). |
| FA-AUSZ-016-03 | Default-Werte f�r neue Benutzer: Modus=MonthlyOrFixed, MaxEntriesPerDraft=250, MonthlySplitThreshold=250. |
| FA-AUSZ-016-04 | Importlogik ber�cksichtigt Benutzereinstellungen pro Upload (User-Scope). |
| FA-AUSZ-016-05 | Validation: MaxEntriesPerDraft >= 20; MonthlySplitThreshold >= MaxEntriesPerDraft (falls gesetzt). |
| FA-AUSZ-016-06 | Logging: Info-Eintrag mit Modus, resultierender Anzahl Drafts, gr��te Draft-Gr��e. |
| FA-AUSZ-016-07 | UI-Hinweis nach Import: Anzahl erzeugter Drafts + verwendeter Modus (lokalisiert). |
| FA-AUSZ-016-08 | R�ckfallverhalten: Fehlt Konfiguration ? Defaults verwenden. |
| FA-AUSZ-016-09 | Performance: Kein mehrfaches Rescannen � einmalige Gruppierung / Streaming. |
| FA-AUSZ-016-10 | Einheitliche Reihenfolge der Drafts: Nach Monat aufsteigend, intern Entries nach Datum. |

## Nicht-funktionale Anforderungen
- NFA-AUSZ-016-01: Konfiguration serverseitig pro User speicherbar (Tabelle `UserPreferences` erweitern oder neue Tabelle `UserImportSettings`).
- NFA-AUSZ-016-02: Erweiterbar f�r k�nftige Strategien (z.B. Quartalsweise, Wochenweise).
- NFA-AUSZ-016-03: Unit Tests f�r Kern-Splitalgorithmus (alle Modi, Grenzf�lle: Monatswechsel an Draft-Grenze, exakt Threshold, > Threshold, nur ein Monat, mehrere Monate, unsortierte Input -> Sortierung sicherstellen).
- NFA-AUSZ-016-04: Kein signifikanter Mehraufwand (O(n) mit n = Eintr�ge). 

## Datenmodell / Persistenz
Erweiterung (Variante A) `UserPreferences` um Spalten:
- ImportSplitMode (smallint)
- ImportMaxEntriesPerDraft (int)
- ImportMonthlySplitThreshold (int nullable)

Alternativ (Variante B) neue Tabelle `UserImportSettings` (FK UserId, RowVersion). Entscheidung: Variante A (geringere Komplexit�t) � falls Tabelle schon existiert; sonst B.

Enum:
```csharp
public enum ImportSplitMode : short
{
    FixedSize = 0,
    Monthly = 1,
    MonthlyOrFixed = 2
}
```

## UI / Blazor
Neue Registerkarte in `Setup.razor` (z.B. Tabs): "Import-Aufteilung".
Formularelemente:
- Select (Modus) ? OnChange UI aktualisiert.
- Numeric Input: MaxEntriesPerDraft.
- Numeric Input: MonthlySplitThreshold (nur sichtbar bei MonthlyOrFixed).
- Speichern-Button (disabled bei Unver�ndert / Ung�ltig).
- Validierung / Fehlermeldungen (lokalisiert).

## Lokalisierung (Beispiele)
- ImportSplit_Title
- ImportSplit_Mode_Label
- ImportSplit_Mode_FixedSize
- ImportSplit_Mode_Monthly
- ImportSplit_Mode_MonthlyOrFixed
- ImportSplit_MaxEntries
- ImportSplit_MonthlyThreshold
- ImportSplit_SaveSuccess
- ImportSplit_SaveError
- ImportSplit_InvalidThreshold
- ImportSplit_ResultSummary ("{count} Ausz�ge erzeugt (Modus: {mode})")

## Edge Cases / Logikdetails
- Alle Eintr�ge im selben Monat und Anzahl < MaxEntriesPerDraft ? Genau ein Draft.
- Monatswechsel bei niedrigem Volumen < Threshold ? mehrere Drafts (ein Draft pro Monat) im Monthly / MonthlyOrFixed Modus.
- Extrem viele Eintr�ge in einem Monat > Threshold ? Splits auch innerhalb des Monats durch Fixed-Anteile? (Aktuelle Vorgabe: Nur Split beim Erreichen MaxEntriesPerDraft; Threshold dient nur zur Entscheidung, ob Monatsmodus angewendet wird. Nicht zus�tzliche interne Splits.)
- Zeitstempel ohne BookingDate? ? Eintr�ge ohne Datum (sollte nicht vorkommen) -> validierungsfehler fr�her in Pipeline.

## Akzeptanzkriterien (Auszug)
1. Benutzer mit Default-Einstellungen l�dt Datei mit 600 Eintr�gen �ber 3 Monate ? Ergebnis: 3 Drafts (monatsweise), keine �berschreitung pro Draft.
2. Modus FixedSize, Max=250, 600 Eintr�ge, 3 Monate gemischt ? Drafts nach Gr��e (250/250/100), Monatsgrenzen ignoriert.
3. Modus Monthly, 2 Monate, je 40 Eintr�ge, Max=250 ? 2 Drafts (je Monat). 
4. Modus MonthlyOrFixed, 1 Monat, 600 Eintr�ge, Max=250, Threshold=250 ? Fixed-Splitting greift (250/250/100) � Monatsmodus irrelev. 
5. Ung�ltige Eingabe (Threshold < Max) ? Validierungsfehler.
6. Fehlende Konfiguration ? Default greift still.
7. Logging enth�lt Modus + DraftCount.

## Risiken / Offene Fragen
- Interpretation Threshold bei sehr gro�en Einzel-Monaten � Erweiterung sp�ter (z.B. zus�tzliche Binnen-Splits nach Monat + Gr��e). 
- Migration bestehender UserPreferences n�tig (Default-Spalten mit Werten). 

## Umsetzungsschritte
1. Enum + Preferences erweitern + Migration.
2. Service: `IImportSplitStrategyResolver` + Implementierungen (FixedSize, Monthly, MonthlyOrFixed).
3. Anpassung StatementDraftService.CreateDraftAsync Pipeline (Vorverarbeitung der Entry-Liste -> Partitionierung -> Draft-Erzeugung).
4. Setup UI erweitern (Register, Formular, API Endpunkte GET/PUT user import settings).
5. Lokalisierungsschl�ssel anlegen (de/en).
6. Unit Tests f�r Splitting.
7. Dokumentation & Anforderungsstatus aktualisieren.

## Branch / Commit
- Branch: `feature/import-monthly-split`
- Commit Prefix: `feat(import): monthly split option`

---
Letzte Aktualisierung: (Initial)
