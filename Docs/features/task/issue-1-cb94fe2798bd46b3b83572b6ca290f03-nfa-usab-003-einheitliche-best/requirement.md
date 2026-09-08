# Anforderung: NFA-USAB-003 – Einheitliche Bestätigungsabfragen für Aktionsbuttons + globale Option zur Deaktivierung

## Metadaten

| Feld | Wert |
|------|------|
| Aufgaben-ID | cb94fe27-98bd-46b3-b835-72b6ca290f03 |
| Issue | #1 |
| Branch | `task/issue-1-cb94fe2798bd46b3b83572b6ca290f03-nfa-usab-003-einheitliche-best` |
| Erstellt | 2026-09-07 |
| Kurztitel | Einführung konsistenter Bestätigungsdialoge für kritische Aktionen inkl. globaler Nutzer-Einstellung zum Unterbinden |

## Ziel / Problem

Aktuell ist uneinheitlich oder gar nicht sichergestellt, dass der Benutzer vor kritischen, destruktiven oder irreversiblen Aktionen eine Bestätigung abgeben muss. Dies kann zu Fehlbedienungen und Datenverlust führen. Gleichzeitig wünschen manche Benutzer eine schnellere Bedienung ohne wiederholte Nachfragen.

## Nutzerstory

Als Anwender möchte ich vor kritischen oder irreversiblen Aktionen einen klaren Bestätigungsdialog erhalten, um unbeabsichtigte Änderungen zu vermeiden. Gleichzeitig möchte ich (optional) zentral einstellen können, dass solche Bestätigungen unterdrückt werden, wenn ich mir der Auswirkungen bewusst bin.

## Geltungsbereich

Alle interaktiven Aktionsbuttons in der Anwendung:

- Icon-Buttons auf Detailseiten
- Aktionsleisten auf Detailseiten
- Massenaktionen auf Übersichtsseiten

## Klassen von Aktionen (Richtlinie)

### Bestätigung erforderlich

1. Destruktiv: Löschen, Archivieren, Zurücksetzen, Entfernen, Abbrechen irreversibler Prozesse.
2. Irreversibel: Verbuchen / Finalisieren / Abschluss, Statuswechsel ohne Rückkehr.
3. Datenmanipulation mit großen Auswirkungen: Massenklassifikation, Zusammenführen (z. B. Kontakte).
4. Sicherheits-/Freigabeaktionen: Zuweisung oder Entzug sensibler Zugriffsrechte (falls vorhanden).
5. Kontextveränderung mit potenziell hohem Aufwand bei Rückgängigmachung.

### Bestätigung NICHT erforderlich

- Rein lesende Aktionen (Anzeigen, Navigieren).
- Editieren (Öffnen des Formulars) ohne persistierende Änderung.
- Speichern mit Undo-/Abbrechen-Möglichkeit vor finalem Commit (falls reversibel).

## Beispiele bestätigungspflichtiger Aktionen

- Statement Draft final verbuchen / abschließen
- Statement Draft löschen
- Buchung aus Entwurf entfernen
- Sicherheitsobjekt (Security) löschen / archivieren
- Savings Plan löschen / beenden
- Kontakt löschen / Kontakt zusammenführen (Merge)
- Klassifikations-Massenanwendung starten
- Rückgängig machen einer Klassifikation (falls irreversibel)
- Persistenter Import ausführen (falls nicht rücksetzbar)

## Globale Einstellung

### Neue Option auf der Einrichtungsseite (Settings / Preferences)

- Bezeichnung (de): "Bestätigungsdialoge anzeigen"
- Typ: Boolean
- Default: `true`
- Verhalten: Wenn `false`, werden alle Bestätigungsdialoge übersprungen (hart).

### Persistenz

- Pro Benutzer (User Preference) – nicht systemweit.
- Speicherung in bestehender User-Einstellungsstruktur (falls vorhanden) oder neue Tabelle `UserPreferences`:
  - `UserId` (PK / FK Users)
  - `ShowConfirmations` (bit, not null, default 1)
  - `RowVersion` (rowversion) für Concurrency
- Falls kein Eintrag existiert → Default `true`.

## Funktionales Verhalten

1. Benutzer klickt auf eine bestätigungspflichtige Aktion.
2. System prüft die globale Einstellung:
   - `true` → Modalen Bestätigungsdialog anzeigen.
   - `false` → Aktion sofort ausführen.
3. Der Dialog enthält:
   - Titel (kontextabhängig, z. B. "Löschen bestätigen")
   - Kurzbeschreibung / Auswirkung (z. B. "Der Eintrag wird dauerhaft entfernt.")
   - Primär-Button "Bestätigen" + Sekundär-Button "Abbrechen"
   - Keine Checkbox "Diese Bestätigungen künftig unterdrücken" (Out of Scope)
4. "Abbrechen" → keine Aktion.
5. "Bestätigen" → Aktion ausführen, Fehler über bestehende Fehlerbehandlung.

## Nicht-funktionale Anforderungen

- Einheitliche API / Service-Aufruf (kein Copy-Paste von Dialogmarkup).
- Lokalisierung aller Texte (de / en).
- Keine Inline-Hardcodierung von Beschriftungen → Ressourcen.
- Barrierefreiheit: Fokus-Management (initialer Fokus auf "Abbrechen" als sicherer Standard).
- Testbarkeit: Service abstrahiert, in Unit Tests mockbar ohne UI.
- Logging: Nur tatsächliche Ausführung (nicht das reine Öffnen des Dialogs) als Information loggen. Keine sensiblen Daten.

## Vorgeschlagener technischer Ansatz

1. Neues Interface `IConfirmationService` im Web-/UI-Layer:

   ```csharp
   public interface IConfirmationService
   {
       Task<bool> ConfirmAsync(ConfirmationRequest request, CancellationToken ct = default);
   }

   public sealed record ConfirmationRequest(
       string TitleResourceKey,
       string MessageResourceKey,
       string? ContextId = null,
       ConfirmationSeverity Severity = ConfirmationSeverity.Default);
   ```

2. Implementierung `ConfirmationService`:
   - Prüft aktuell eingeloggten User über `UserSettingsProvider` → `ShowConfirmations`?
   - Wenn `false` → `Task.FromResult(true)`
   - Sonst öffnet zentrales `ConfirmDialog.razor`-Component.

3. Neues Component `ConfirmDialog.razor`:
   - Parameter für Titel, Message, Severity (Icon / Farbe).
   - Standardisierte Buttons: "Bestätigen" (Primär), "Abbrechen".

4. Blazor-Integration:
   - Verwendung über DI: `var confirmed = await _confirmationService.ConfirmAsync(request, ct);`
   - Aktionsbuttons rufen erst Service auf, dann bei `true` eigentliche Service-/Controller-Operation.

5. Einstellungsseite:
   - Binding an `UserPreferenceViewModel.ShowConfirmations`.
   - Speichern über `IUserPreferenceService.UpdateAsync()`.

6. Server / Persistence:
   - Migration für `UserPreferences` (falls nicht vorhanden).
   - Laden beim Login in Claims / Scoped Cache oder OnDemand-Service-Lookup.
   - Optional: HTTP-Endpoint `GET /api/user/preferences`, `PUT /api/user/preferences`.

## Ressourcen-Keys (Vorschläge)

- `Confirmation_Delete_Title`
- `Confirmation_Delete_Message`
- `Confirmation_Finalize_Title`
- `Confirmation_Finalize_Message`
- `Confirmation_Merge_Title`
- `Confirmation_Merge_Message`
- `Settings_ShowConfirmations_Label`
- `Settings_ShowConfirmations_Description`

## Akzeptanzkriterien

1. Standard: Neuer Benutzer erhält Bestätigungsdialoge (kein Vorhandensein einer Präferenz → `true`).
2. Deaktiviert der Benutzer die Option und speichert → danach erscheinen keine Bestätigungsdialoge mehr für abgedeckte Aktionen.
3. Wird eine kritische Aktion bei aktivierten Dialogen abgebrochen → keine Änderung tritt ein.
4. Wird bestätigt → Aktion wird vollständig ausgeführt (bestehender Flow unverändert).
5. Alle kritischen Aktionen nutzen den zentralen Service – kein dupliziertes Markup.
6. Lokalisierung funktioniert (Fallback `de`, falls `en` fehlt).
7. Accessibility: Fokus nach Öffnen auf "Abbrechen"-Button.
8. Unit Tests (mindestens):
   - `ConfirmationService` returns `true` ohne UI wenn `ShowConfirmations=false`.
   - `ConfirmationService` ruft Dialogmechanismus auf wenn `ShowConfirmations=true`.
   - `UserPreferences` speichern / laden (Repository / Service).
9. Kein Bestätigungsdialog bei rein lesenden Aktionen.
10. Performance: Kein wahrnehmbarer Mehraufwand (Dialog lazy gerendert).

## Tests (Vorgeschlagen)

- Unit: `ConfirmationServiceTests`
- Unit: `UserPreferenceServiceTests`
- Integration: API PUT/GET Preferences
- UI (Komponenten-Test): `ConfirmDialog` Render + Callback
- Smoke (manuell): Delete / Merge / Finalize mit an/aus Option

## Migrationsschritte

1. Add Migration: `YYYYMMDDHHmm_AddUserPreferences`
2. Update Database
3. Optional: Seed nichts (Default greift über Code).

## Risiken / Hinweise

- Scope Creep (pro Aktion eigene Toggles) → bewusst ausgeschlossen.
- Race Condition bei parallelem Tab: Änderung der Einstellung wirkt erst auf nachfolgende Bestätigungen (akzeptabel).
- Sicherstellen, dass wirklich alle relevanten Buttons inventarisiert werden (Review Liste im PR).

## Deliverables

- Migration + Modell
- Service + Interface + Tests
- Razor Component `ConfirmDialog`
- Erweiterung Einstellungsseite
- Anpassungen aller relevanten Aktionsbuttons
- Ressourcen-Einträge
- Aktualisierung der Programmierrichtlinien (Abschnitt 15 Hinweis auf zentralen Bestätigungsservice)

## Out of Scope

- Granulare (pro Aktion) Deaktivierung
- Zeitlich begrenzte Unterdrückung
- Audit-Trail für verweigerte Bestätigungen
- Mobile spezifische UI-Anpassungen

## Offene Fragen (für Refinement / Bestandsaufnahme)

1. Existiert bereits ein User-Settings-Konzept oder ist eine neue Tabelle nötig?
2. Sollen Massenaktionen immer bestätigt werden, auch bei deaktivierten globalen Dialogen? (Aktuell: Nein, globale Option gilt universell.)
3. Benötigen wir Severity-Stufen (Warnung vs. Kritisch) für unterschiedliche Icons? (Empfehlung: Ja, minimale Enum.)
