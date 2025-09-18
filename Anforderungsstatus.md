# Mapping: Anforderungen zu Implementierung (FinanceManager)

Dieses Dokument zeigt, wie die Anforderungen aus dem Anforderungskatalog im aktuellen Code umgesetzt sind. Es dient als Übersicht für den Projektstand und zur Identifikation offener Punkte.

| Nr.         | Anforderung (Kurzbeschreibung)                                          | Implementierung im Code / Stand                                                      | Status |
|-------------|-------------------------------------------------------------------------|--------------------------------------------------------------------------------------|--------|
| FA-KTO-001  | Beliebig viele Bankkonten anlegen                                       | AccountService, AccountsController, UI                                               | ✔      |
| FA-KTO-002  | Kontotyp (Giro/Spar)                                                    | AccountService, Domain.AccountType Enum                                              | ✔      |
| FA-KTO-003  | Automatische Bankkontakt-Anlage                                         | AccountService, ContactService                                                       | ✔      |
| FA-KTO-004  | Konto teilen mit anderen Benutzern                                      | (Teilen-Logik noch offen)                                                            | ✖      |
| FA-AUSZ-001 | Kontoauszugdateien einlesen (CSV, PDF)                                  | StatementDraftService, FileReader                                                    | ✔      |
| FA-AUSZ-002 | Für jeden Import wird ein Buch.-Blatt erzeugt                           | StatementDraftService, Domain.StatementDraft                                         | ✔      |
| FA-AUSZ-003 | Buch.-Blatt Einträge bearbeiten, ergänzen, löschen                      | StatementDraftService, UI (Detail + Editiermodus); zusätzlich Archivierungs-Flag pro Eintrag für Sparpläne | ✔      |
| FA-AUSZ-004 | Beim Buchen entstehen Bankposten                                        | StatementDraftService.BookAsync → `PostingKind.Bank`                                 | ✔      |
| FA-AUSZ-005 | Duplikatserkennung beim Import                                          | StatementDraftService: Duplikatprüfung                                               | ✔      |
| FA-AUSZ-006 | Kostenneutral bei eigenen Kontakten                                     | StatementDraftService: Status-/CostNeutral-Logik                                     | ✔      |
| FA-AUSZ-007 | Kontaktposten beim Buchen entstehen                                     | StatementDraftService.BookAsync → `PostingKind.Contact`                              | ✔      |
| FA-AUSZ-008 | Empfänger muss Kontakt zugeordnet werden                                | StatementDraftService, UI                                                            | ✔      |
| FA-AUSZ-009 | Wertpapierzuordnung bei eigener Bank                                    | UI & API: Auswahl, Neuanlage und Zuordnung in StatementDraftEntryDetail; PDF-Detailimport (ING) erkennt Dividende/Kauf/Verkauf, ISIN, Nominale (inkl. „Stück“ vor/nach Zahl), Ordernummer, Steuern und Provision; automatische Übernahme von Steuer/Provision in den Eintrag; Security-Postings (Trade/Fee/Tax) werden erzeugt; Menge bei Dividende optional; Validierung angepasst. Positions-/Depotlogik weiter offen. | ~      |
| FA-AUSZ-010 | PDF-Parsing mit Tabellenextraktion                                      | ING_StatementFileReader, Barclays_StatementFileReader, erweiterbar                   | ✔      |
| FA-AUSZ-011 | Import-Pipeline mit Format-Strategie                                    | StatementDraftService, Reader-Interface                                              | ✔      |
| FA-AUSZ-012 | Anzeige Gesamtbetrag verknüpfter Aufteilungs-Auszüge im Eintrag         | StatementDraftsController GetEntry: SplitSum/Difference; EntryDetail UI Amount-Zeile | ✔      |
| FA-AUSZ-013 | Status offen bei Zahlungsintermediär bis vollständig gesplittet         | StatementDraftService: TryAutoAssignContact & ReevaluateParentEntryStatusAsync       | ✔      |
| FA-AUSZ-014 | Originaldatei speichern & Download / Inline-Ansicht                     | StatementDraft: OriginalFileContent; Controller /file Endpoint; Detail-Viewer        | ✔      |
| FA-AUSZ-015 | Massenbuchung Kontoauszüge (inkl. optionaler Einzelbuchung pro Eintrag) | BookingCoordinator, StatementDraftsController (`/api/statement-drafts/book-all`), UI `StatementDrafts.razor` Dialogoption „Einträge einzeln buchen“ – nur Einträge mit Warnung/Fehler bleiben offen | ✔      |
| FA-AUSZ-016 | Konfigurierbare Monatsbasierte Aufteilung von Kontoauszugs-Imports      | Noch nicht implementiert                                                             | ✖      |
| FA-BACK-001 | Backup erstellen                                                        | BackupService.CreateAsync, BackupsController POST, UI `Setup.razor`                  | ✔      |
| FA-BACK-002 | Backups auflisten & herunterladen                                       | BackupService.List/OpenDownload, BackupsController GET/Download, UI `Setup.razor`    | ✔      |
| FA-BACK-003 | Backup hochladen                                                        | BackupsController `/upload` (NDJSON/ZIP), BackupService.UploadAsync, UI `Setup.razor`| ✔      |
| FA-BACK-004 | Backup wiederherstellen (asynchron) mit Fortschrittsanzeige             | BackupRestoreCoordinator (Hintergrund), BackupsController Start/Status/Cancel, UI Fortschrittsbox in `Setup.razor` | ✔      |
| FA-BACK-005 | Backup löschen                                                          | BackupService.DeleteAsync, BackupsController DELETE, UI `Setup.razor`                | ✔      |
| FA-KON-001  | Kontakte verwalten (CRUD)                                               | ContactService, ContactsController, UI                                               | ✔      |
| FA-KON-002  | Kontakte können Kategorie zugeordnet werden                             | ContactService, UI                                                                   | ✔      |
| FA-KON-003  | Anwender als Kontakt angelegt                                           | ContactService, Initialisierung                                                      | ✔      |
| FA-KON-004  | Aliasnamen mit Wildcards pflegen                                        | ContactService, UI, StatementDraftService                                            | ✔      |
| FA-KON-005  | Kontakte verschmelzen (Merge)                                           | ContactService.MergeAsync, ContactsController, ContactMergeDialog                    | ✔      |
| FA-AUTO-001 | Aliasnamen für automatische Kontaktzuordnung                            | StatementDraftService: Alias-Matching                                                | ✔      |
| FA-AUTO-002 | Duplikate werden ausgelassen                                            | StatementDraftService: Duplikatprüfung                                               | ✔      |
| FA-SPAR-001 | Sparpläne verwalten (CRUD)                                              | SavingsPlanService, SavingsPlansController, UI (Liste, Detail), Neuanlage & Auto‑Zuordnung aus Kontoauszugseintrag | ✔      |
| FA-SPAR-002 | Sparplan-Typen                                                          | SavingsPlanType (OneTime, Recurring, Open) inkl. UI/Service                          | ✔      |
| FA-SPAR-003 | Wiederkehrende Intervalle                                               | Bei Buchung wird Fälligkeit für wiederkehrende Pläne (Type=Recurring) um Intervall verlängert | ~      |
| FA-SPAR-004 | Automatische Sparplanvorschläge                                         | Noch nicht implementiert                                                             | ✖      |
| FA-SPAR-005 | Manuelle Änderung Sparplanvorschlag                                     | Noch nicht implementiert                                                             | ✖      |
| FA-SPAR-006 | Statusanzeige Sparziel erreicht                                         | SavingsPlanService.AnalyzeAsync liefert Kennzahlen; Kontoauszugs‑Prüfung: Infos bei Ziel erreicht sowie Hinweis auf fällige Pläne (ohne Monatsbuchung, nicht in offenem Auszug) | ~      |
| FA-SPAR-007 | Anzeige fehlender Buchungen zum Ziel                                    | Noch nicht implementiert                                                             | ✖      |
| FA-SPAR-008 | Prognose Zielverfehlung                                                 | SavingsPlanService.AnalyzeAsync (Durchschnitt/Erfordernis)                           | ✔      |
| FA-SPAR-009 | Archivierung bei Ausbuchung                                             | Kontoauszugseintrag: Flag „Sparplan nach Buchung archivieren“; Validate prüft exakte Ausbuchung (`SAVINGSPLAN_ARCHIVE_MISMATCH` bei Abweichung); Book archiviert bei Saldo=0 und meldet `SAVINGSPLAN_ARCHIVED` als Information | ✔      |
| FA-SPAR-010 | Sparplan aus Rückzahlung/Kredit                                         | Noch nicht implementiert                                                             | ✖      |
| FA-SPAR-011 | Sparplanposten bei Buchung                                              | StatementDraftService.BookEntryAsync → `PostingKind.SavingsPlan` (negierter Betrag)  | ✔      |
| FA-SPAR-012 | Umschalten aktive/archivierte Sparpläne                                 | Noch nicht implementiert                                                             | ✖      |
| FA-WERT-001 | Wertpapiere verwalten                                                   | SecurityService, SecuritiesController, UI (Liste, Detail, Kategorien), Erstellung & Rücksprung aus Kontoauszug | ✔      |
| FA-WERT-002 | Wertpapiertransaktionen                                                 | Transaktionstyp und Menge werden bei Buchung erfasst (Buy +, Sell −, Dividend ohne Menge); aus PDF-Details (ING) werden Kauf/Verkauf/Dividende, Nominale, Steuern und Provision erkannt und übernommen. Positions-/Depot-/FIFO‑Logik noch offen. | ~      |
| FA-WERT-003 | Wertpapierposten bei Buchung                                            | StatementDraftService.BookAsync → `PostingKind.Security` (Trade/Fee/Tax)             | ✔      |
| FA-WERT-004 | Kursabruf AlphaVantage API                                              | Hintergrund‑Worker ruft tägliche Kurse (TIME_SERIES_DAILY) ab; optionaler API‑Key (ohne Key inaktiv); Erkennung des Request‑Limits (Backoff bis Folgetag); Abruf 1×/Tag bis Vortag; Wochenenden übersprungen | ~      |
| FA-WERT-005 | Historische Kurse nachholen                                             | Initiales Backfill (bis ca. 2 Jahre, dann inkrementell seit letztem Eintrag)         | ~      |
| FA-WERT-006 | API-Limit erkennen/beachten                                             | Worker erkennt Note/Information und pausiert bis zum nächsten Tag                    | ✔      |
| FA-WERT-007 | Speicherung Kursposten                                                  | Entität `SecurityPrice`, Unique‑Index (SecurityId+Date), Persistierung via Worker    | ✔      |
| FA-WERT-008 | Renditeberechnung                                                       | Noch nicht implementiert                                                             | ✖      |
| FA-WERT-009 | Kursliste im UI (Infinite Scroll)                                       | Seite `SecurityPrices.razor` + API `SecurityPricesController`                        | ✔      |
| FA-REP-001  | Buchungsaggregation (Monat, Quartal, Jahr)                              | Noch nicht implementiert                                                             | ✖      |
| FA-REP-002  | Year-To-Date Berechnung                                                 | Noch nicht implementiert                                                             | ✖      |
| FA-REP-003  | Vergleich mit Vorjahr                                                   | Noch nicht implementiert                                                             | ✖      |
| FA-REP-004  | GuV pro Monat/Kategorie                                                 | Noch nicht implementiert                                                             | ✖      |
| FA-REP-005  | Renditekennzahlen Wertpapiere                                           | Noch nicht implementiert                                                             | ✖      |
| FA-REP-006  | Umsatz‑Auswertungsgraphen für alle Entitäten mit Postings               | Noch nicht implementiert                                                             | ✖      |
| FA-REP-007  | Export von Postenlisten (CSV & Excel)                                   | Noch nicht implementiert                                                             | ✖      |
| FA-KPI-001  | Dividenden aktueller Monat                                              | Noch nicht implementiert                                                             | ✖      |
| FA-KPI-002  | Dividenden aktuelles Jahr                                               | Noch nicht implementiert                                                             | ✖      |
| FA-KPI-003  | Einnahmen/Ausgaben pro Monat                                            | Erste Graphen auf Bankkontodetailseite                                               | ~      |
| FA-KPI-004  | Gesamtdepotrendite aktuelles Jahr                                       | Noch nicht implementiert                                                             | ✖      |
| FA-KPI-005  | KPI-Fallback Jahresanfang                                               | Noch nicht implementiert                                                             | ✖      |
| FA-UI-001   | Suchfeld in Listen                                                      | Kontakte-Liste + Merge-Dialog implementiert; andere Listen teils offen               | ~      |
| FA-UI-002   | Live-Filterung                                                          | Debounce Filter Kontakte-Liste umgesetzt (Name)                                      | ~      |
| FA-API-001  | Web API für alle Entitäten                                              | Controller für Konten, Kontakte, Auszüge                                             | ✔      |
| FA-API-002  | Suchkriterien für API                                                   | Kontakte: type + q Filter ergänzt; weitere Entitäten offen                           | ~      |
| FA-API-003  | Authentifizierung & Autorisierung                                       | Program.cs, Controller `[Authorize]`                                                 | ✔      |
| FA-API-004  | Rate Limiting/Caching Kursabfragen                                      | Rate‑Limit Handling in Worker integriert (AlphaVantage); API‑Gateway‑Limiting offen  | ~      |
| FA-AUTH-001 | Anmeldung erforderlich                                                  | Program.cs, Controller `[Authorize]`                                                 | ✔      |
| FA-AUTH-002 | JWT-Token bei Anmeldung                                                 | Program.cs, JwtBearer                                                                | ✔      |
| FA-AUTH-003 | Token im Authorization Header                                           | Program.cs, JwtBearer                                                                | ✔      |
| FA-AUTH-004 | Datenzugriff auf Benutzer gescoped                                      | Services, Controller: UserId-Scoping                                                 | ✔      |
| FA-AUTH-005 | Konto teilen mit Benutzer                                               | Noch nicht implementiert                                                             | ✖      |
| FA-AUTH-006 | Schreibrechte konfigurierbar                                            | Noch nicht implementiert                                                             | ✖      |
| FA-AUTH-007 | Entzug von Freigaben                                                    | Noch nicht implementiert                                                             | ✖      |
| FA-AUTH-008 | Passworthashing                                                         | Implementiert via `Pbkdf2PasswordHasher`; Registrierung in DI                        | ✔      |
| FA-AUTH-009 | Admin-Oberfläche Benutzerverwaltung                                     | AdminUsersController, UI `Users.razor`                                               | ✔      |
| FA-AUTH-010 | Erster Benutzer = Admin                                                 | Noch nicht implementiert                                                             | ✖      |
| FA-AUTH-011 | Admin kann Benutzer bearbeiten/löschen                                  | AdminUsersController: Update/Delete; UI `Users.razor`                                | ✔      |
| FA-AUTH-012 | Löschen entfernt private Daten                                          | Noch nicht implementiert                                                             | ✖      |
| FA-AUTH-013 | Admin kann Sperren aufheben                                             | AdminUsersController Unlock; UI `Users.razor`                                        | ✔      |
| FA-I18N-001 | UI-Texte in Deutsch/Englisch                                            | Ressourcenstruktur, Program.cs                                                       | ✔      |
| FA-I18N-002 | Sprache im Profil einstellen                                            | Noch nicht implementiert                                                             | ✖      |
| FA-I18N-003 | Fallback auf Browser/Systemsprache                                      | Program.cs: SupportedCultures                                                        | ✔      |
| FA-I18N-004 | Texte über Resource-Dateien verwaltet                                   | Ressourcenstruktur                                                                   | ✔      |
| FA-I18N-005 | Sprachwechsel ohne Neuanmeldung                                         | Noch nicht implementiert                                                             | ✖      |
| FA-I18N-006 | Datums-/Zahlen-/Währungsformate                                         | Program.cs, UI: CultureInfo                                                          | ✔      |
| NFA-PERF-001| Import Performance                                                      | Noch nicht validiert                                                                 | ~      |
| NFA-PERF-002| KPI Performance                                                         | Noch nicht validiert                                                                 | ~      |
| NFA-SEC-001 | Endpunkte geschützt                                                     | Program.cs, Controller `[Authorize]`                                                 | ✔      |
| NFA-SEC-002 | Idempotenz bei Verbuchung                                               | StatementDraftService: Duplikatprüfung                                               | ✔      |
| NFA-SEC-003 | Access Scope Enforcement                                                | Services, Controller: UserId-Scoping                                                 | ✔      |
| NFA-SEC-004 | Passwort-Hashing (Argon2id/bcrypt)                                      | Ziel bleibt Argon2id/bcrypt; aktuell PBKDF2 im Einsatz                               | ✖      |
| NFA-SEC-005 | Token-Lebensdauer/Refresh-Token                                         | Noch nicht implementiert                                                             | ✖      |
| NFA-SEC-006 | Audit Logging sicherheitsrelevanter Aktionen                            | Noch nicht implementiert                                                             | ✖      |
| NFA-SEC-007 | Login-Sperre nach Fehlversuchen                                         | Noch nicht implementiert                                                             | ✖      |
| NFA-SEC-008 | Admin-Audit Logging                                                     | Noch nicht implementiert                                                             | ✖      |
| NFA-REL-001 | Fehler beim Kursabruf blockiert Hauptfunktionen nicht                   | Worker isoliert, Fehler/Rate‑Limit werden gefangen und verhindern keine UI/API       | ✔      |
| NFA-REL-002 | Langläufer im Hintergrund mit Fortschritt (Restore)                     | BackupRestoreCoordinator + REST Endpunkte + UI Fortschrittsanzeige                   | ✔      |
| NFA-REL-003 | Vereinheitlichte Hintergrundaufgaben-Verwaltung (Single Runner + Queue) | Noch nicht implementiert                                                             | ✖      |
| NFA-USAB-001| Responsive UI                                                           | UI: Blazor, Responsive Design teilweise                                              | ~      |
| NFA-USAB-002| Einheitliche Aktions‑Symbole via Sprite                                 | `wwwroot/icons/sprite.svg` gepflegt (u.a. play, download, close ergänzt)             | ✔      |
| NFA-USAB-003| Bestätigungsdialoge für kritische Aktionen + globale Abschalt-Option    | Noch nicht implementiert (geplanter zentraler `IConfirmationService`, Dialog-Component, UserPreference `ShowConfirmations`) | ✖      |
| NFA-ARCH-001| Trennung Domäne/Präsentation                                            | Shared Library, Blazor, Services                                                     | ✔      |
| NFA-LOG-001 | Zentrales Logging                                                       | Program.cs, Serilog                                                                  | ✔      |
| NFA-I18N-001| Zwei Sprachen, Fallback                                                 | Program.cs, Ressourcenstruktur                                                       | ✔      |
| NFA-DATA-001| Zeitreihen effizient gespeichert                                        | Noch nicht implementiert                                                             | ✖      |
| NFA-PRIV-001| Lokale Speicherung, keine Weitergabe                                    | Program.cs, keine Weitergabe                                                         | ✔      |

**Legende:**  
✔ = umgesetzt / vorhanden  
✖ = offen / noch nicht implementiert  
~ = teilweise umgesetzt / in Arbeit  

Änderungen (18.09.2025):
- Neu: NFA-USAB-003 hinzugefügt: Einheitliche Bestätigungsdialoge für kritische/irreversible Aktionen inkl. globaler Benutzerpräferenz zum Unterdrücken (Default aktiv). Umsetzung: geplanter `IConfirmationService`, zentrales `ConfirmDialog` Component, UserPreference `ShowConfirmations`.

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
- UX: Rückkehrpfad nach Öffnen eines Kontakts aus Kontoauszugseintrag via `returnUrl` (kein eigener Requirement-Eintrag, inkrementelle Verbesserung von FA-KON-001 / FA-AUSZ-003).
- FA-KPI-003 von ✖ auf ~: Erste Graphen auf Bankkontodetailseite (Einnahmen/Ausgaben pro Monat).

Änderungen (15.09.2025):
- FA-WERT-004 von ✖ auf ~: Hintergrund‑Worker (optional via API‑Key) ruft tägliche Kurse ab; Rate‑Limit erkannt (Backoff bis Folgetag); keine Anfragen ohne Key; Abruf 1×/Tag bis Vortag; Wochenende übersprungen.
- FA-WERT-005 von ✖ auf ~: Initiales Backfill (bis ca. 2 Jahre, dann inkrementell seit letztem Eintrag).
- FA-WERT-006 von ✖ auf ✔: Rate‑Limit-Erkennung implementiert.
- FA-WERT-007 von ✖ auf ✔: Speicherung Kursposten (`SecurityPrice`).
- FA-WERT-009 neu: Kursliste im UI (Infinite Scroll) inkl. API.
- NFA-REL-001 von ✖ auf ✔: Kursabruffehler blockieren die Hauptfunktionen nicht.
- README angepasst: AlphaVantage‑Key optional; ohne Key keine Kursabrufe.
- FA-WERT-002/FA-AUSZ-009: Mengenlogik und Tests ergänzt (siehe Änderungen gleicher Tag).

Änderungen (13.09.2025):
- FA-SPAR-002 von ✖ auf ✔: Sparplan-Typen (OneTime, Recurring, Open) in DTO/Domain + UI/Service nutzbar.
- FA-SPAR-003 von ✖ auf ~: Bei Buchung wird bei wiederkehrenden Sparplänen das Fälligkeitsdatum um das Intervall erhöht.
- FA-SPAR-006 Beschreibung erweitert: Kontoauszugs‑Prüfung meldet zusätzlich fällige Pläne (ohne Monatsbuchung, nicht in offenem Auszug) als Information.
- FA-SPAR-001 erweitert: Neuanlage eines Sparplans direkt aus Kontoauszugseintrag inkl. automatischer Zuordnung und Rücksprung.
- FA-SPAR-009 von ✖ auf ✔: Archivierung bei Ausbuchung via Archivierungs-Flag am Kontoauszugseintrag; Validierung `SAVINGSPLAN_ARCHIVE_MISMATCH` bei Abweichung; Archivierung und Info `SAVINGSPLAN_ARCHIVED` bei Buchung.
- Tests ergänzt: Unit-Tests für Fehlfall (Mismatch) und Erfolgsfall (Archivierung) der Archivierungsfunktion.

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

*Letzte Aktualisierung: 18.09.2025*