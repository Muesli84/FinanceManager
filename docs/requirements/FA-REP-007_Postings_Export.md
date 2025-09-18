# FA-REP-007: Export von Postenlisten (CSV & Excel)

## Kurzbeschreibung
Auf allen Seiten, die Posten (Postings) in Listenform darstellen (Konto, Kontakt, Sparplan, Wertpapier, ggf. Security-spezifische und SavingsPlan-Ansichten), soll ein Export der aktuell gefilterten Daten als CSV oder Excel-Datei angeboten werden.

## Ziel / Nutzen
- Schnelle externe Weiterverarbeitung (Analyse, Steuer, Reporting)
- Vereinheitlichter Exportmechanismus f�r alle Posting-Kontexte
- Minimierung manueller Kopierarbeit / Fehlerquellen

## Geltungsbereich
Export-Button (Icon + Tooltip) in folgenden Seiten/Listen:
- /postings/account/{accountId}
- /postings/contact/{contactId}
- /postings/savings-plan/{planId}
- /postings/security/{securityId}
(Erweiterbar auf zuk�nftige konsolidierte �bersichten)

## Funktionale Anforderungen
| Nr | Beschreibung |
|----|-------------|
| FA-REP-007-01 | Export ber�cksichtigt aktive Filter (Suche q, Datumsbereich from/to, weitere sp�tere Parameter). |
| FA-REP-007-02 | Zwei Formate: CSV (UTF-8 mit BOM) und Excel (XLSX). |
| FA-REP-007-03 | Maximale Anzahl exportierter Zeilen konfigurierbar (`Exports:MaxRows`, Default 50_000); �berschreitung ? Abbruch mit Fehlermeldung. |
| FA-REP-007-04 | Spaltensatz je Kontext konsistent (siehe Mapping unten). |
| FA-REP-007-05 | Dezimalzahlen und Datumswerte kulturunabh�ngig normalisiert (CSV: ISO-8601 f�r Datum, Dezimalpunkt / Excel: native Typen). |
| FA-REP-007-06 | Datei-Namenskonvention: `<Context>_<EntityNameOrId>_<yyyyMMddHHmm>.<ext>` (z.B. `Account_Girokonto_20250918T1430.csv`). Fallback bei unbekanntem Namen: Id. |
| FA-REP-007-07 | Leere Ergebnismenge ? trotzdem g�ltige Datei mit Headerzeile. |
| FA-REP-007-08 | Sicherheit: Nur eigene Postings exportierbar (Ownership-Pr�fung analog API-Listing). |
| FA-REP-007-09 | Fehlerf�lle liefern ProblemDetails (HTTP 400/404/413). |
| FA-REP-007-10 | Streaming Response (keine vollst�ndige Materialisierung in Speicher bei gro�en Datenmengen). |
| FA-REP-007-11 | UI zeigt Busy-Indikator w�hrend Generierung. |
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
- NFA-REP-007-03: Keine sensiblen / internen IDs zus�tzlich au�er explizit gelistet. 
- NFA-REP-007-04: Logging nur Metadaten (Zeilenanzahl, Kontext, Dauer); kein Inhalt. 

## API Endpunkte (neu)
GET `/api/postings/account/{accountId}/export?format=csv|xlsx&from=...&to=...&q=...`
GET `/api/postings/contact/{contactId}/export?...`
GET `/api/postings/savings-plan/{planId}/export?...`
GET `/api/postings/security/{securityId}/export?...`

Query-Parameter analog vorhandener Listen (skip/take entfallen; Export ignoriert Paging und holt alle passenden Zeilen bis MaxRows).

HTTP Status:
- 200: Datei Stream
- 400: Ung�ltiges Format / MaxRows �berschritten
- 404: Entit�t nicht gefunden / keine Berechtigung
- 413: (Optional) Falls sp�ter serverseitiger Schutz greift

## UI / Blazor
- Neuer Icon-Button (z.B. `download`) rechts in Action-Bar der Postings-Seiten mit Dropdown (CSV / Excel) oder kleines Kontext-Men�.
- W�hrend Export: Button disabled + Spinner (oder progress overlay minimal). 
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
- Keine M�glichkeit, fremde Datens�tze per Rate-Limit Umgehung zu exfiltrieren.

## Technischer Ansatz
Service `IPostingExportService`:
```csharp
public interface IPostingExportService
{
    IAsyncEnumerable<PostingExportRow> QueryAsync(PostingExportQuery query, CancellationToken ct);
    Task<(string ContentType, string FileName, Stream Content)> GenerateAsync(PostingExportQuery query, CancellationToken ct);
}
```
`PostingExportRow` flach; `PostingExportQuery` enth�lt Kontext (Kind + Id), Filter (q, from, to), Format, MaxRows.

CSV: Erstellung via `StreamWriter` + manuelles Escaping (" -> ""). Trennzeichen: `;` (DE) oder konfigurierbar? Entscheidung: Semikolon zur besseren Excel-Kompatibilit�t in DE; Out-of-Scope dynamisch.

Excel: Leichtgewichtiger Writer (z.B. ClosedXML oder Mini-OpenXML Utility). Falls neue Dependency: in Infrastruktur-Projekt hinzuf�gen. Alternativ zun�chst nur CSV (Fallback) wenn Abh�ngigkeit nicht gew�nscht � Entscheidung: Direkt XLSX unterst�tzen.

## Akzeptanzkriterien (Auszug)
1. Export reflektiert aktive Filter (q & Zeitraum). 
2. CSV in Excel / LibreOffice direkt lesbar (Umlaute korrekt). 
3. XLSX �ffnet ohne Reparaturhinweis. 
4. Negative Betr�ge korrekt mit Vorzeichen. 
5. MaxRows > tats�chliche Zeilen ? vollst�ndiger Export; bei �berschreitung ? Abbruch + Fehlermeldung. 
6. Leerer Filter ? vollst�ndige (begrenzte) Liste. 
7. Unbekanntes Format ? 400. 
8. Performance-Test (50k) innerhalb Ziel. 

## Risiken / Offene Fragen
- Library-Wahl f�r XLSX (ClosedXML vs. reines OpenXML) ? Performance / Footprint abw�gen.
- Zeichenkodierung CSV (BOM ja/nein) � Entscheidung: UTF-8 mit BOM f�r Windows-Kompatibilit�t.
- Dynamische Spaltenerweiterungen bei zuk�nftigen Features? (Jetzt strikt fix.)
- Filter f�r SecuritySubType n�tig? (Derzeit nein.)

## Out of Scope
- Aggregierte Exportformate (Pivot, Summen). 
- Signierte / verschl�sselte Dateien. 
- Geplanter Hintergrund-Job / asynchroner Large Export (sp�ter). 

## Umsetzungsschritte
1. Shared DTOs / Enums (PostingExportFormat). 
2. Service Implementierung + Tests (Edge: keine Daten, >MaxRows, Filter). 
3. Controller Endpunkte. 
4. UI: Button + Dropdown + Busy State. 
5. Lokalisierungstexte. 
6. Doku / Anforderungsstatus erg�nzen.

## Branch / Commit
- Branch: `feature/postings-export`
- Commit Prefix: `feat(postings-export): ...`

---
Letzte Aktualisierung: (Initial)
