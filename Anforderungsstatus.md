# Mapping: Anforderungen zu Implementierung (FinanceManager)

Dieses Dokument zeigt, wie die Anforderungen aus dem Anforderungskatalog im aktuellen Code umgesetzt sind. Es dient als Übersicht für den Projektstand und zur Identifikation offener Punkte.

| Nr.              | Anforderung (Kurzbeschreibung)                                          | Implementierung im Code / Stand                                                                                                                                        | Status |
|------------------|-------------------------------------------------------------------------|------------------------------------------------------------------------------------------------------------------------------------------------------------------------|--------|
| FA-KTO-001       | Beliebig viele Bankkonten anlegen                                       | AccountService, AccountsController, UI                                                                                                                                 | ✔      |
| FA-KTO-002       | Kontotyp (Giro/Spar)                                                    | AccountService, Domain.AccountType Enum                                                                                                                                | ✔      |
| FA-KTO-003       | Automatische Bankkontakt-Anlage                                         | AccountService, ContactService                                                                                                                                         | ✔      |
| FA-KTO-004       | Konto teilen mit anderen Benutzern                                      | (Teilen-Logik noch offen)                                                                                                                                              | ✖      |
| FA-AUSZ-001      | Kontoauszugdateien einlesen (CSV, PDF)                                  | StatementDraftService, FileReader                                                                                                                                      | ✔      |
| FA-AUSZ-002      | Für jeden Import wird ein Buch.-Blatt erzeugt                           | StatementDraftService, Domain.StatementDraft                                                                                                                           | ✔      |
| FA-AUSZ-003      | Buch.-Blatt Einträge bearbeiten, ergänzen, löschen                      | StatementDraftService, UI (Detail + Editiermodus); zusätzlich Archivierungs-Flag pro Eintrag für Sparpläne                                                             | ✔      |
| FA-AUSZ-004      | Beim Buchen entstehen Bankposten                                        | StatementDraftService.BookAsync → `PostingKind.Bank`                                                                                                                   | ✔      |
| FA-AUSZ-005      | Duplikatserkennung beim Import                                          | StatementDraftService: Duplikatprüfung                                                                                                                                 | ✔      |
| FA-AUSZ-006      | Kostenneutral bei eigenen Kontakten                                     | StatementDraftService: Status-/CostNeutral-Logik                                                                                                                       | ✔      |
| FA-AUSZ-007      | Kontaktposten beim Buchen entstehen                                     | StatementDraftService.BookAsync → `PostingKind.Contact`                                                                                                                | ✔      |
| FA-AUSZ-008      | Empfänger muss Kontakt zugeordnet werden                                | StatementDraftService, UI                                                                                                                                              | ✔      |
| FA-AUSZ-009      | Wertpapierzuordnung bei eigener Bank                                    | UI & API; PDF-Detailimport (ING) erweitert; Depot-/Positionslogik offen                                                                                                | ~      |
| FA-AUSZ-010      | PDF-Parsing mit Tabellenextraktion                                      | ING_StatementFileReader, Barclays_StatementFileReader, erweiterbar                                                                                                     | ✔      |
| FA-AUSZ-011      | Import-Pipeline mit Format-Strategie                                    | StatementDraftService, Reader-Interface                                                                                                                                | ✔      |
| FA-AUSZ-012      | Anzeige Gesamtbetrag verknüpfter Aufteilungs-Auszüge im Eintrag         | StatementDraftsController GetEntry (SplitSum/Difference); EntryDetail UI                                                                                               | ✔      |
| FA-AUSZ-013      | Status offen bei Zahlungsintermediär bis vollständig gesplittet         | StatementDraftService: TryAutoAssignContact & ReevaluateParentEntryStatusAsync                                                                                         | ✔      |
| FA-AUSZ-014      | Originaldatei speichern & Download / Inline-Ansicht                     | StatementDraft: OriginalFileContent; Controller `/file`; Viewer im Detail                                                                                              | ✔      |
| FA-AUSZ-015      | Massenbuchung Kontoauszüge (inkl. optionaler Einzelbuchung pro Eintrag) | BackgroundTask (BookingTaskExecutor + BackgroundTaskManager), Endpoint `/api/statement-drafts/book-all`, UI Dialog, Fortschritt Panel                                  | ✔      |
| FA-AUSZ-016      | Konfigurierbare Monatsbasierte Aufteilung von Kontoauszugs-Imports      | Siehe Unteranforderungen -01..-10 / NFA -01..-04                                                                                                                       | (∑)    |
| FA-AUSZ-016-01   | Neuer Konfigurationsbereich "Import-Aufteilung" im Setup (Registerkarte) | Tab + UI-Komponente `SetupImportSplitTab.razor` integriert in `Setup.razor`                                                                                            | ✔      |
| FA-AUSZ-016-02   | Einstellbare Werte (Modus, MaxEntriesPerDraft, MonthlySplitThreshold)    | UI Form (`SetupImportSplitTab.razor`), DTO `ImportSplitSettingsDto`, API `UserImportSplitSettingsController`, Entity-Felder `User` + Migration                          | ✔      |
| FA-AUSZ-016-03   | Default-Werte für neue Benutzer                                         | Defaults im `User`-Konstruktor (Mode=MonthlyOrFixed, Max=250, Threshold=250)                                                                                            | ✔      |
| FA-AUSZ-016-04   | Importlogik berücksichtigt Benutzereinstellungen                        | Split-Algorithmus in `StatementDraftService.CreateDraftAsync` (Monthly / Fixed / Hybrid via Threshold)                                                                 | ✔      |
| FA-AUSZ-016-05   | Validierung (Grenzwerte / Abhängigkeit Threshold)                       | UI-Validierung (SetupImportSplitTab) + API Validierung (Range + Schwellenprüfung) + Domänenprüfung in `User.SetImportSplitSettings`                                    | ✔      |
| FA-AUSZ-016-06   | Logging (Modus, Draft-Anzahl, größte Draft-Größe)                       | Informations-Log in `StatementDraftService.CreateDraftAsync` (Mode, UseMonthly, Movements, DraftCount, MaxPerDraft, LargestDraftSize, Threshold, File)                 | ✔      |
| FA-AUSZ-016-07   | UI-Hinweis nach Import (Count + Modus, lokalisiert)                     | Erweiterung Import-Result DTO & Notification fehlt                                                                                                                     | ✖      |
| FA-AUSZ-016-08   | Rückfallverhalten bei fehlender Konfiguration (Defaults)                | Fallback im Code (`CreateDraftAsync` nutzt Defaults bei fehlenden UserSettings)                                                                                        | ✔      |
| FA-AUSZ-016-09   | Performance (einmaliges Streaming / keine Mehrfach-Scans)               | Ein Durchlauf + Sort (O(n log n)); kein separater Performance-Nachweis / Optimierung auf reines Streaming                                                              | ~      |
| FA-AUSZ-016-10   | Reihenfolge der Drafts nach Monat / Entries nach Datum                  | Sortierung in `CreateDraftAsync` (BookingDate, danach Subject); Monats-/Teil-Kennzeichnung                                                                             | ✔      |
| NFA-AUSZ-016-01  | Serverseitige User-Konfiguration (UserPreferences erweitern)            | Felder am `User`, Migration `FixUserImportSplitSettings` (Idempotent), EF-Konfiguration                                                                                | ✔      |
| NFA-AUSZ-016-02  | Erweiterbar für künftige Strategien                                    | Strategy/Resolver Pattern noch nicht extrahiert (Logik monolithisch im Service)                                                                                        | ✖      |
| NFA-AUSZ-016-03  | Unit Tests für Splitalgorithmus                                        | Tests `StatementDraftImportSplitTests` (Fixed, Monthly, Hybrid, Threshold)                                                                                             | ✔      |
| NFA-AUSZ-016-04  | O(n) Laufzeit bestätigt                                                | Noch keine Messung / Benchmark; Algorithmus konzeptionell linear außer Sortierung                                                                                      | ~      |
| FA-BACK-001      | Backup erstellen                                                        | BackupService.CreateAsync, BackupsController POST, UI                                                                                                                  | ✔      |
| FA-BACK-002      | Backups auflisten & herunterladen                                       | BackupService.List/OpenDownload, BackupsController GET + Download                                                                                                      | ✔      |
| FA-BACK-003      | Backup hochladen                                                        | BackupsController `/upload` (NDJSON/ZIP), BackupService.UploadAsync                                                                                                    | ✔      |
| FA-BACK-004      | Backup wiederherstellen (asynchron) mit Fortschrittsanzeige             | BackgroundTaskManager + BackupRestoreTaskExecutor + BackgroundTasksController + UI Panel                                                                               | ✔      |
| FA-BACK-005      | Backup löschen                                                          | BackupService.DeleteAsync, BackupsController DELETE                                                                                                                    | ✔      |
| FA-KON-001       | Kontakte verwalten (CRUD)                                               | ContactService, ContactsController, UI                                                                                                                                 | ✔      |
| FA-KON-002       | Kontakte können Kategorie zugeordnet werden                             | ContactService, UI                                                                                                                                                      | ✔      |
| FA-KON-003       | Anwender als Kontakt angelegt                                           | ContactService, Initialisierung                                                                                                                                         | ✔      |
| FA-KON-004       | Aliasnamen mit Wildcards pflegen                                        | ContactService, UI, StatementDraftService                                                                                                                              | ✔      |
| FA-KON-005       | Kontakte verschmelzen (Merge)                                           | ContactService.MergeAsync, ContactsController, ContactMergeDialog                                                                                                      | ✔      |
| FA-AUTO-001      | Aliasnamen für automatische Kontaktzuordnung                            | StatementDraftService: Alias-Matching                                                                                                                                  | ✔      |
| FA-AUTO-002      | Duplikate werden ausgelassen                                            | StatementDraftService: Duplikatprüfung                                                                                                                                 | ✔      |
| FA-SPAR-001      | Sparpläne verwalten (CRUD)                                              | SavingsPlanService, SavingsPlansController, UI                                                                                                                         | ✔      |
| FA-SPAR-002      | Sparplan-Typen                                                          | SavingsPlanType (OneTime, Recurring, Open)                                                                                                                             | ✔      |
| FA-SPAR-003      | Wiederkehrende Intervalle                                               | Buchung verschiebt Fälligkeit für Recurring-Pläne                                                                                                                      | ~      |
| FA-SPAR-004      | Automatische Sparplanvorschläge                                         | Noch nicht implementiert                                                                                                                                               | ✖      |
| FA-SPAR-005      | Manuelle Änderung Sparplanvorschlag                                     | Noch nicht implementiert                                                                                                                                               | ✖      |
| FA-SPAR-006      | Statusanzeige Sparziel erreicht                                         | AnalyzeAsync + Hinweise (Ziel erreicht / fällige Pläne)                                                                                                                | ~      |
| FA-SPAR-007      | Anzeige fehlender Buchungen zum Ziel                                    | Noch nicht implementiert                                                                                                                                               | ✖      |
| FA-SPAR-008      | Prognose Zielverfehlung                                                 | AnalyzeAsync (Durchschnitt/Erfordernis)                                                                                                                                | ✔      |
| FA-SPAR-009      | Archivierung bei Ausbuchung                                             | Archivierungs-Flag + Validate + Auto-Archiv bei saldo=0                                                                                                                 | ✔      |
| FA-SPAR-010      | Sparplan aus Rückzahlung/Kredit                                         | Noch nicht implementiert                                                                                                                                               | ✖      |
| FA-SPAR-011      | Sparplanposten bei Buchung                                              | StatementDraftService.BookAsync → `PostingKind.SavingsPlan`                                                                                                            | ✔      |
| FA-SPAR-012      | Umschalten aktive/archivierte Sparpläne                                 | Noch nicht implementiert                                                                                                                                               | ✖      |
| FA-WERT-001      | Wertpapiere verwalten                                                   | SecurityService, SecuritiesController, UI                                                                                                                              | ✔      |
| FA-WERT-002      | Wertpapiertransaktionen                                                 | Import-/Buchungslogik inkl. PDF-Parsing (Steuern/Fees), Depot-/Positionslogik offen                                                                                    | ~      |
| FA-WERT-003      | Wertpapierposten bei Buchung                                            | StatementDraftService.BookAsync → `PostingKind.Security` (Trade/Fee/Tax)                                                                                               | ✔      |
| FA-WERT-004      | Kursabruf AlphaVantage API                                              | SecurityPriceWorker (täglich, Rate-Limit-Erkennung, Skip Wochenende)                                                                                                   | ~      |
| FA-WERT-005      | Historische Kurse nachholen                                             | Initialer Backfill + inkrementell                                                                                                                                       | ~      |
| FA-WERT-006      | API-Limit erkennen/beachten                                             | Worker wertet `Note/Information` aus, Backoff bis Folgetag                                                                                                             | ✔      |
| FA-WERT-007      | Speicherung Kursposten                                                  | Entity `SecurityPrice` + Unique-Index + Persistierung                                                                                                                   | ✔      |
| FA-WERT-008      | Renditeberechnung                                                       | Noch nicht implementiert                                                                                                                                               | ✖      |
| FA-WERT-009      | Kursliste im UI (Infinite Scroll)                                       | `SecurityPrices.razor` + API                                                                                                                                           | ✔      |
| FA-REP-001       | Buchungsaggregation (Monat, Quartal, Jahr)                              | Noch nicht implementiert                                                                                                                                               | ✖      |
| FA-REP-002       | Year-To-Date Berechnung                                                 | Noch nicht implementiert                                                                                                                                               | ✖      |
| FA-REP-003       | Vergleich mit Vorjahr                                                   | Noch nicht implementiert                                                                                                                                               | ✖      |
| FA-REP-004       | GuV pro Monat/Kategorie                                                 | Noch nicht implementiert                                                                                                                                               | ✖      |
| FA-REP-005       | Renditekennzahlen Wertpapiere                                           | Noch nicht implementiert                                                                                                                                               | ✖      |
| FA-REP-006       | Umsatz‑Auswertungsgraphen für alle Entitäten mit Postings               | Noch nicht implementiert                                                                                                                                               | ✖      |
| FA-REP-007       | Export von Postenlisten (CSV & Excel)                                   | Noch nicht implementiert                                                                                                                                               | ✖      |
| FA-KPI-001       | Dividenden aktueller Monat                                              | Noch nicht implementiert                                                                                                                                               | ✖      |
| FA-KPI-002       | Dividenden aktuelles Jahr                                               | Noch nicht implementiert                                                                                                                                               | ✖      |
| FA-KPI-003       | Einnahmen/Ausgaben pro Monat                                            | Erste Graphen Bankkontodetail                                                                                                                                          | ~      |
| FA-KPI-004       | Gesamtdepotrendite aktuelles Jahr                                       | Noch nicht implementiert                                                                                                                                               | ✖      |
| FA-KPI-005       | KPI-Fallback Jahresanfang                                               | Noch nicht implementiert                                                                                                                                               | ✖      |
| FA-UI-001        | Suchfeld in Listen                                                      | Kontakte + Merge umgesetzt, andere Listen teils offen                                                                                                                  | ~      |
| FA-UI-002        | Live-Filterung                                                          | Debounce Kontakte-Liste (Name)                                                                                                                                         | ~      |
| FA-API-001       | Web API für alle Entitäten                                              | Controller (Accounts, Contacts, Statements, Securities, etc.)                                                                                                          | ✔      |
| FA-API-002       | Suchkriterien für API                                                   | Kontakte: type + q; weitere Entitäten partiell                                                                                                                         | ~      |
| FA-API-003       | Authentifizierung & Autorisierung                                       | JWT + `[Authorize]`                                                                                                                                                     | ✔      |
| FA-API-004       | Rate Limiting/Caching Kursabfragen                                      | Worker-seitig (AlphaVantage) – Gateway-Limit offen                                                                                                                     | ~      |
| FA-AUTH-001      | Anmeldung erforderlich                                                  | JWT Auth Pipeline                                                                                                                                                       | ✔      |
| FA-AUTH-002      | JWT-Token bei Anmeldung                                                 | JwtBearer                                                                                                                                                               | ✔      |
| FA-AUTH-003      | Token im Authorization Header                                           | JwtBearer                                                                                                                                                               | ✔      |
| FA-AUTH-004      | Datenzugriff auf Benutzer gescoped                                      | Services & Controller filtern nach UserId                                                                                                                               | ✔      |
| FA-AUTH-005      | Konto teilen mit Benutzer                                               | Noch nicht implementiert                                                                                                                                               | ✖      |
| FA-AUTH-006      | Schreibrechte konfigurierbar                                            | Noch nicht implementiert                                                                                                                                               | ✖      |
| FA-AUTH-007      | Entzug von Freigaben                                                    | Noch nicht implementiert                                                                                                                                               | ✖      |
| FA-AUTH-008      | Passworthashing                                                         | PBKDF2 (Ziel: Argon2id/bcrypt)                                                                                                                                          | ✔      |
| FA-AUTH-009      | Admin-Oberfläche Benutzerverwaltung                                     | AdminUsersController + `Users.razor`                                                                                                                                    | ✔      |
| FA-AUTH-010      | Erster Benutzer = Admin                                                 | Noch nicht implementiert                                                                                                                                               | ✖      |
| FA-AUTH-011      | Admin kann Benutzer bearbeiten/löschen                                  | Update/Delete Endpoints + UI                                                                                                                                            | ✔      |
| FA-AUTH-012      | Löschen entfernt private Daten                                          | Noch nicht implementiert                                                                                                                                               | ✖      |
| FA-AUTH-013      | Admin kann Sperren aufheben                                             | Unlock Endpoint + UI                                                                                                                                                   | ✔      |
| FA-I18N-001      | UI-Texte in Deutsch/Englisch                                            | Ressourcenstruktur, Localizer                                                                                                                                           | ✔      |
| FA-I18N-002      | Sprache im Profil einstellen                                            | Noch nicht implementiert                                                                                                                                               | ✖      |
| FA-I18N-003      | Fallback auf Browser/Systemsprache                                      | RequestLocalizationOptions                                                                                                                                              | ✔      |
| FA-I18N-004      | Texte über Resource-Dateien verwaltet                                   | Resx-Konzept vorhanden                                                                                                                                                  | ✔      |
| FA-I18N-005      | Sprachwechsel ohne Neuanmeldung                                         | Noch nicht implementiert                                                                                                                                               | ✖      |
| FA-I18N-006      | Datums-/Zahlen-/Währungsformate                                         | CultureInfo-Konfiguration                                                                                                                                               | ✔      |
| NFA-PERF-001     | Import Performance                                                      | Noch nicht validiert                                                                                                                                                    | ~      |
| NFA-PERF-002     | KPI Performance                                                         | Noch nicht validiert                                                                                                                                                    | ~      |
| NFA-SEC-001      | Endpunkte geschützt                                                     | `[Authorize]` + JWT                                                                                                                                                     | ✔      |
| NFA-SEC-002      | Idempotenz bei Verbuchung                                               | Duplikatprüfung beim Import/Buchen                                                                                                                                      | ✔      |
| NFA-SEC-003      | Access Scope Enforcement                                                | UserId-Scoping durchgängig                                                                                                                                              | ✔      |
| NFA-SEC-004      | Passwort-Hashing (Argon2id/bcrypt)                                      | Ziel noch offen (aktuell PBKDF2)                                                                                                                                        | ✖      |
| NFA-SEC-005      | Token-Lebensdauer/Refresh-Token                                         | Noch nicht implementiert                                                                                                                                                | ✖      |
| NFA-SEC-006      | Audit Logging sicherheitsrelevanter Aktionen                            | Noch nicht implementiert                                                                                                                                                | ✖      |
| NFA-SEC-007      | Login-Sperre nach Fehlversuchen                                         | Noch nicht implementiert                                                                                                                                                | ✖      |
| NFA-SEC-008      | Admin-Audit Logging                                                     | Noch nicht implementiert                                                                                                                                                | ✖      |
| NFA-REL-001      | Fehler beim Kursabruf blockiert Hauptfunktionen nicht                   | Worker fängt Fehler / Rate-Limit ab                                                                                                                                     | ✔      |
| NFA-REL-002      | Langläufer im Hintergrund mit Fortschritt (Restore)                     | BackgroundTaskManager + Executor (BackupRestoreTaskExecutor) + Panel                                                                                                    | ✔      |
| NFA-REL-003      | Einheitliche Background-Task Queue (Classify, Mass Booking, Restore)    | BackgroundTaskManager + BackgroundTaskRunner + Executors + API + UI Panel                                                                                               | ✔      |
| NFA-USAB-001     | Responsive UI                                                           | Teilweise umgesetzt (Tabellen/Layouts optimiert)                                                                                                                        | ~      |
| NFA-USAB-002     | Einheitliche Aktions‑Symbole via Sprite                                 | `wwwroot/icons/sprite.svg`                                                                                                                                             | ✔      |
| NFA-USAB-003     | Bestätigungsdialoge kritische Aktionen + globale Abschalt-Option        | Konzept (IConfirmationService + Dialog) offen                                                                                                                           | ✖      |
| NFA-ARCH-001     | Trennung Domäne/Präsentation                                            | Separierte Layer (Domain, Application, Infrastructure, Web)                                                                                                             | ✔      |
| NFA-LOG-001      | Zentrales Logging                                                       | Serilog Pipeline                                                                                                                                                       | ✔      |
| NFA-I18N-001     | Zwei Sprachen, Fallback                                                 | Lokalisierung de/en + Fallback                                                                                                                                          | ✔      |
| NFA-DATA-001     | Zeitreihen effizient gespeichert                                        | Noch ausstehend (optimierte Speicherung / Aggregationen)                                                                                                                | ✖      |
| NFA-PRIV-001     | Lokale Speicherung, keine Weitergabe                                    | Keine externe Weitergabe implementiert                                                                                                                                  | ✔      |

**Legende:**  
✔ = umgesetzt / vorhanden  
✖ = offen / noch nicht implementiert  
~ = teilweise umgesetzt / in Arbeit  

Änderungen (19.09.2025) – Ergänzung 4:
- FA-AUSZ-016-06 umgesetzt: Logging der Split-Metriken (Mode, UseMonthly, Movements, DraftCount, MaxPerDraft, LargestDraftSize, Threshold, File) bei Draft-Erstellung.

Änderungen (19.09.2025) – Ergänzung 3:
- FA-AUSZ-016 Unteranforderungen aktualisiert: Implementierung UI + Persistenz + Logik + Validierung + Defaults produktiv.
- FA-AUSZ-016-04 (Importlogik) jetzt umgesetzt: Splitting in `StatementDraftService.CreateDraftAsync` inkl. Hybrid-Entscheidung anhand Threshold.
- FA-AUSZ-016-05 Validierung vollständig (UI + API + Domain) → ✔.
- FA-AUSZ-016-08 Fallback-Mechanik aktiv (Defaults bei fehlenden UserSettings) → ✔.
- FA-AUSZ-016-10 Sortier-/Monatslogik implementiert → ✔.
- NFA-AUSZ-016-01 (Serverseitige Konfiguration) umgesetzt (Migration + Felder) → ✔.
- NFA-AUSZ-016-03 Unit Tests (`StatementDraftImportSplitTests`) hinzugefügt → ✔.
- Performance / Streaming (FA-AUSZ-016-09, NFA-AUSZ-016-04) noch nicht final optimiert / gemessen → ~ belassen.
- Logging & UI-Hinweis (FA-AUSZ-016-06 / -07) weiterhin offen.

Änderungen (19.09.2025) – Ergänzung 2:
- FA-AUSZ-016 in funktionale (FA-AUSZ-016-01 .. -10) und nicht-funktionale (NFA-AUSZ-016-01 .. -04) Unteranforderungen zerlegt.
- Alle Teilanforderungen aktuell offen (Planungsphase). Architekturansatz: Strategy-Resolver + spezifische Partitionierungs-Implementierungen (FixedSize, Monthly, MonthlyOrFixed).
- Datenmodell-Erweiterung (Variante A: `UserPreferences`) + Migration noch nicht gestartet.

Änderungen (19.09.2025):
- Hintergrundaufgaben vereinheitlicht (Legacy *Coordinator* entfernt) → Neuer Requirement NFA-REL-003 auf ✔.
- FA-BACK-004 & FA-AUSZ-015 auf neue Architektur (BackgroundTaskManager + Executors + Panel) aktualisiert.
- Lokalisierung der Task-Executor-Meldungen (de/en Resx) ergänzt.
- Verweis auf veralteten BackupRestoreCoordinator entfernt.
- BackgroundTaskStatusPanel angepasst: Sichtbarkeit gesteuert über `AllowedTypes`, Darstellung zeigt bei Sichtbarkeit nun alle aktiven/queued Tasks (kein Inhaltsfilter). Ergänzende Unit-Tests `BackgroundTasksControllerTests` (Idempotenz, User-Scope, Cancel/Remove) hinzugefügt (NFA-REL-003 unverändert ✔).

Änderungen (18.09.2025):
- Neu: NFA-USAB-003 hinzugefügt: Einheitliche Bestätigungsdialoge für kritische/irreversibile Aktionen inkl. globaler Benutzerpräferenz zum Unterdrücken (Default aktiv). Umsetzung: geplanter `IConfirmationService`, zentrales `ConfirmDialog` Component, UserPreference `ShowConfirmations`.

Änderungen (17.09.2025):
- I18N Clean-up: `Home.razor` (Import-Button, KPI-Platzhalter) vollständig lokalisiert; neue Ressourcen-Schlüssel ergänzt.
- I18N Clean-up: `SavingsPlanEdit.razor` vollständig lokalisiert (Aktionen, Felder inkl. Vertragsnummer, Analyse-Panel inkl. Status/Labels/Tooltips); passende `*.resx`-Einträge in DE/EN ergänzt.

Änderungen (16.09.2025):
- PDF-Detailimport (ING) erweitert: Erkennung Kauf/Verkauf/Dividende, Ordernummer, Provision, robustere Betrags-/Vorzeichenlogik (inkl. „EUR - 1,67“), Nominale auch mit „Stück“ vor/nach der Zahl. Steuern (KESt/SolZ/KiSt) und Provision werden übernommen. Automatische Zuordnung von Steuer/Provision in Draft-Einträge bei Details-Import.
- FA-AUSZ-009 Beschreibung aktualisiert (PDF-Details, automatische Übernahme). Status bleibt ~ wegen offener Depot-/Positionslogik.
- FA-WERT-002 Beschreibung aktualisiert (Ableitung Transaktionstyp/Menge/Gebühren/Steuern aus PDF). Status bleibt ~.
- Backup-Funktionalität ergänzt: Erstellen, Auflisten/Download, Upload, asynchrones Wiederherstellen mit Fortschrittsanzeige, Löschen (BackupsController, BackupService, `Setup.razor`, `BackupRestoreCoordinator`).
- UI: SVG-Sprite um fehlende Aktionssymbole erweitert (`play`, `download`, `close`).
- Admin-Benutzerverwaltung implementiert: AdminUsersController + `Users.razor` (Bearbeiten/Löschen/Passwort zurücksetzen/Entsperren). Anforderungen FA-AUTH-009/011/013 auf ✔.
- Sicherheit: Passwort-Hashing via PBKDF2 implementiert (FA-AUTH-008 ✔). Langfristiges Ziel weiterhin Argon2id/bcrypt (NFA-SEC-004 bleibt ✖).

Änderungen (15.09.2025) – Ergänzung 2:
- NEU: FA-AUSZ-015 Massenbuchung mit optionaler Einzelbuchung pro Eintrag (UI Dialogoption „Einträge einzeln buchen“). Backend: BookingCoordinator erweitert; Controller & UI aktualisiert.
- UX: Rückkehrpfad nach Öffnen eines Kontakts aus Kontoauszugseintrag via `returnUrl`.
- FA-KPI-003 von ✖ auf ~: Erste Graphen auf Bankkontodetailseite (Einnahmen/Ausgaben pro Monat).

Änderungen (15.09.2025):
- FA-WERT-004 von ✖ auf ~: Hintergrund‑Worker (optional via API‑Key) ruft tägliche Kurse ab; Rate‑Limit erkannt (Backoff bis Folgetag); keine Anfragen ohne Key; Abruf 1×/Tag bis Vortag; Wochenende übersprungen.
- FA-WERT-005 von ✖ auf ~: Initiales Backfill (bis ca. 2 Jahre, dann inkrementell seit letztem Eintrag).
- FA-WERT-006 von ✖ auf ✔: Rate‑Limit-Erkennung implementiert.
- FA-WERT-007 von ✖ auf ✔: Speicherung Kursposten (`SecurityPrice`).
- FA-WERT-009 neu: Kursliste im UI (Infinite Scroll) inkl. API.
- NFA-REL-001 von ✖ auf ✔: Kursabruffehler blockieren die Hauptfunktionen nicht.
- README angepasst: AlphaVantage‑Key optional; ohne Key keine Kursabrufe.
- FA-WERT-002/FA-AUSZ-009: Mengenlogik und Tests ergänzt.

Änderungen (13.09.2025):
- FA-SPAR-002 von ✖ auf ✔: Sparplan-Typen (OneTime, Recurring, Open) in DTO/Domain + UI/Service nutzbar.
- FA-SPAR-003 von ✖ auf ~: Bei Buchung wird bei wiederkehrenden Sparplänen das Fälligkeitsdatum um das Intervall erhöht.
- FA-SPAR-006 Beschreibung erweitert: Kontoauszugs‑Prüfung meldet zusätzlich fällige Pläne (ohne Monatsbuchung, nicht in offenem Auszug) als Information.
- FA-SPAR-001 erweitert: Neuanlage eines Sparplans direkt aus Kontoauszugseintrag inkl. automatischer Zuordnung und Rücksprung.
- FA-SPAR-009 von ✖ auf ✔: Archivierung bei Ausbuchung via Archivierungs-Flag am Kontoauszugseintrag; Validierung `SAVINGSPLAN_ARCHIVE_MISMATCH` bei Abweichung; Archivierung und Info `SAVINGSPLAN_ARCHIVED` bei Buchung.
- Tests ergänzt: Unit-Tests für Fehlfall (Mismatch) und Erfolgsfall (Archivierungsfunktion).

Änderungen (12.09.2025):
- FA-AUSZ-004 von ✖ auf ✔: Bankposten werden beim Buchen erzeugt (`PostingKind.Bank`).
- FA-AUSZ-007 von ✖ auf ✔: Kontaktposten werden beim Buchen erzeugt (`PostingKind.Contact`).
- FA-WERT-003 von ✖ auf ✔: Wertpapier-Postings (Trade/Fee/Tax) beim Buchen (`PostingKind.Security`).
- FA-SPAR-011 von ✖ auf ✔: Sparplan-Postings beim Buchen (`PostingKind.SavingsPlan`).
- FA-AUSZ-009 Beschreibung ergänzt: Security-Postings werden erzeugt; Depot-/Positionslogik weiterhin offen (Status bleibt ~).
- FA-SPAR-006 von ✖ auf ~: SavingsPlanService.AnalyzeAsync liefert Status; zusätzlich Informationsmeldung in der Kontoauszugs‑Prüfung (Ziel erreicht).

Änderungen (05.09.2025):
- FA-AUSZ-009 von ✖ auf ~: UI & API für Wertpapierauswahl, direkte Neuanlage und Zuordnung aus Kontoauszugseintrag implementiert. Depot-/Transaktionsverbuchung noch offen.
- FA-WERT-001 Beschreibung präzisiert (Liste, Detail, Kategorien, Rücksprung aus Kontoauszug).
- Neuer Aktionsbutton in Kontoauszugseintrag zum Öffnen der Wertpapierdetailseite + SVG-Symbol (security) ergänzt.

Änderungen (04.09.2025):
- Neu: FA-AUSZ-012 Anzeige des Gesamtbetrags eines verknüpften Aufteilungs-Auszugs im Eintrag.
- Neu: FA-AUSZ-013 Status-Logik für Zahlungsintermediäre (offen bis vollständige Aufteilung).
- Neu: FA-AUSZ-014 Speicherung & Download/Inline-Anzeige der Original-Importdatei.
- Erweiterung: FA-AUSZ-003 jetzt inkl. Editiermodus für Kerndaten (BookingDate, Amount, Subject etc.).
- Neu: FA-UI-001 Suchfeld in Listen (Kontakte-Liste + Merge-Dialog umgesetzt, andere Listen teils offen).
- Neu: FA-WERT-001 Grundlegende Wertpapierverwaltung (CRUD) implementiert.
- Neu: FA-API-002 Suchkriterien für API (Kontakte: type + q Filter ergänzt; weitere Entitäten offen).
- Neu: NFA-USAB-001 Responsive UI (Blazor, Responsive Design teilweise umgesetzt).

*Letzte Aktualisierung: 19.09.2025 (Ergänzung 4)*