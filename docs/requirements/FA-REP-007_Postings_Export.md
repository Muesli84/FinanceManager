# FA-REP-007: Export von Postenlisten (CSV & Excel)

## Kurzbeschreibung
Auf allen Seiten, die Posten (Postings) in Listenform darstellen (Konto, Kontakt, Sparplan, Wertpapier, ggf. Security-spezifische und SavingsPlan-Ansichten), soll ein Export der aktuell gefilterten Daten als CSV oder Excel-Datei angeboten werden.

## Ziel / Nutzen
- Schnelle externe Weiterverarbeitung (Analyse, Steuer, Reporting)
- Vereinheitlichter Exportmechanismus für alle Posting-Kontexte
- Minimierung manueller Kopierarbeit / Fehlerquellen

## Geltungsbereich
Export-Button (Icon + Tooltip) in folgenden Seiten/Listen:
- /postings/account/{accountId}
- /postings/contact/{contactId}
- /postings/savings-plan/{planId}
- /postings/security/{securityId}
(Erweiterbar auf zukünftige konsolidierte Übersichten)

## Funktionale Anforderungen
| Nr | Beschreibung |
|----|-------------|
| FA-REP-007-01 | Export berücksichtigt aktive Filter (Suche q, Datumsbereich from/to, weitere spätere Parameter). |
| FA-REP-007-02 | Zwei Formate: CSV (UTF-8 mit BOM) und Excel (XLSX). |
| FA-REP-007-03 | Maximale Anzahl exportierter Zeilen konfigurierbar (`Exports:MaxRows`, Default 50_000); Überschreitung ? Abbruch mit Fehlermeldung. |
| FA-REP-007-04 | Spaltensatz je Kontext konsistent (siehe Mapping unten). |
| FA-REP-007-05 | Dezimalzahlen und Datumswerte kulturunabhängig normalisiert (CSV: ISO-8601 für Datum, Dezimalpunkt / Excel: native Typen). |
| FA-REP-007-06 | Datei-Namenskonvention: `<Context>_<EntityNameOrId>_<yyyyMMddHHmm>.<ext>` (z.B. `Account_Girokonto_20250918T1430.csv`). Fallback bei unbekanntem Namen: Id. |
| FA-REP-007-07 | Leere Ergebnismenge ? trotzdem gültige Datei mit Headerzeile. |
| FA-REP-007-08 | Sicherheit: Nur eigene Postings exportierbar (Ownership-Prüfung analog API-Listing). |
| FA-REP-007-09 | Fehlerfälle liefern ProblemDetails (HTTP 400/404/413). |
| FA-REP-007-10 | Streaming Response (keine vollständige Materialisierung in Speicher bei großen Datenmengen). |
| FA-REP-007-11 | UI zeigt Busy-Indikator während Generierung. |
| FA-REP-007-12 | Exportaktion ist deaktiviert, wenn aktuell noch Daten nachgeladen werden (infinite scroll Ladevorgang aktiv). |

## Spalten-Mapping
Gemeinsamer Basissatz (alle):
1. BookingDate (ISO 8601: yyyy-MM-dd)
2. Amount (numerisch, Vorzeichen beibehalten)
3. Kind (Bank|Contact|SavingsPlan|Security)
4. Subject
5. RecipientName
6. Description
7. AccountId
8. ContactId
9. SavingsPlanId
10. SecurityId
11. SecuritySubType (optional; leer wenn n/a)
12. Quantity (nur Security; sonst leer)

Reihenfolge fix; fehlende Werte leer.

## Nicht-funktionale Anforderungen
- NFA-REP-007-01: Performance: Erzeugung 50k Zeilen < 3s auf Standardumgebung (Richtwert). 
- NFA-REP-007-02: Speicher: Streaming / `IAsyncEnumerable` zur Reduktion des Peak Memory (< 50 MB bei 50k Zeilen). 
- NFA-REP-007-03: Keine sensiblen / internen IDs zusätzlich außer explizit gelistet. 
- NFA-REP-007-04: Logging nur Metadaten (Zeilenanzahl, Kontext, Dauer); kein Inhalt. 

## API Endpunkte (neu)
GET `/api/postings/account/{accountId}/export?format=csv|xlsx&from=...&to=...&q=...`
GET `/api/postings/contact/{contactId}/export?...`
GET `/api/postings/savings-plan/{planId}/export?...`
GET `/api/postings/security/{securityId}/export?...`

Query-Parameter analog vorhandener Listen (skip/take entfallen; Export ignoriert Paging und holt alle passenden Zeilen bis MaxRows).

HTTP Status:
- 200: Datei Stream
- 400: Ungültiges Format / MaxRows überschritten
- 404: Entität nicht gefunden / keine Berechtigung
- 413: (Optional) Falls später serverseitiger Schutz greift

## UI / Blazor
- Neuer Icon-Button (z.B. `download`) rechts in Action-Bar der Postings-Seiten mit Dropdown (CSV / Excel) oder kleines Kontext-Menü.
- Während Export: Button disabled + Spinner (oder progress overlay minimal). 
- Fehleranzeige in bestehendem Meldungsbereich (Text lokalisierbar).

## Lokalisierung (Keys Vorschlag)
- Export_ButtonTooltip
- Export_Format_Csv
- Export_Format_Excel
- Export_Error_MaxRows
- Export_Error_InvalidFormat
- Export_Empty (optional Hinweis nach Download wenn leer)

## Sicherheit
- Gleiche Ownership-Checks wie beim normalen GET.
- Keine Möglichkeit, fremde Datensätze per Rate-Limit Umgehung zu exfiltrieren.

## Technischer Ansatz
Service `IPostingExportService`:
```csharp
public interface IPostingExportService
{
    IAsyncEnumerable<PostingExportRow> QueryAsync(PostingExportQuery query, CancellationToken ct);
    Task<(string ContentType, string FileName, Stream Content)> GenerateAsync(PostingExportQuery query, CancellationToken ct);
}
```
`PostingExportRow` flach; `PostingExportQuery` enthält Kontext (Kind + Id), Filter (q, from, to), Format, MaxRows.

CSV: Erstellung via `StreamWriter` + manuelles Escaping (" -> ""). Trennzeichen: `;` (DE) oder konfigurierbar? Entscheidung: Semikolon zur besseren Excel-Kompatibilität in DE; Out-of-Scope dynamisch.

Excel: Leichtgewichtiger Writer (z.B. ClosedXML oder Mini-OpenXML Utility). Falls neue Dependency: in Infrastruktur-Projekt hinzufügen. Alternativ zunächst nur CSV (Fallback) wenn Abhängigkeit nicht gewünscht – Entscheidung: Direkt XLSX unterstützen.

## Akzeptanzkriterien (Auszug)
1. Export reflektiert aktive Filter (q & Zeitraum). 
2. CSV in Excel / LibreOffice direkt lesbar (Umlaute korrekt). 
3. XLSX öffnet ohne Reparaturhinweis. 
4. Negative Beträge korrekt mit Vorzeichen. 
5. MaxRows > tatsächliche Zeilen ? vollständiger Export; bei Überschreitung ? Abbruch + Fehlermeldung. 
6. Leerer Filter ? vollständige (begrenzte) Liste. 
7. Unbekanntes Format ? 400. 
8. Performance-Test (50k) innerhalb Ziel. 

## Risiken / Offene Fragen
- Library-Wahl für XLSX (ClosedXML vs. reines OpenXML) ? Performance / Footprint abwägen.
- Zeichenkodierung CSV (BOM ja/nein) – Entscheidung: UTF-8 mit BOM für Windows-Kompatibilität.
- Dynamische Spaltenerweiterungen bei zukünftigen Features? (Jetzt strikt fix.)
- Filter für SecuritySubType nötig? (Derzeit nein.)

## Out of Scope
- Aggregierte Exportformate (Pivot, Summen). 
- Signierte / verschlüsselte Dateien. 
- Geplanter Hintergrund-Job / asynchroner Large Export (später). 

## Umsetzungsschritte
1. Shared DTOs / Enums (PostingExportFormat). 
2. Service Implementierung + Tests (Edge: keine Daten, >MaxRows, Filter). 
3. Controller Endpunkte. 
4. UI: Button + Dropdown + Busy State. 
5. Lokalisierungstexte. 
6. Doku / Anforderungsstatus ergänzen.

## Branch / Commit
- Branch: `feature/postings-export`
- Commit Prefix: `feat(postings-export): ...`

---
Letzte Aktualisierung: (Initial)
