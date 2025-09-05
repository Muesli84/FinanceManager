# Mapping: Anforderungen zu Implementierung (FinanceManager)

Dieses Dokument zeigt, wie die Anforderungen aus dem Anforderungskatalog im aktuellen Code umgesetzt sind. Es dient als Übersicht für den Projektstand und zur Identifikation offener Punkte.

| Nr.         | Anforderung (Kurzbeschreibung)                                  | Implementierung im Code / Stand                                                      | Status |
|-------------|-----------------------------------------------------------------|--------------------------------------------------------------------------------------|--------|
| FA-KTO-001  | Beliebig viele Bankkonten anlegen                               | AccountService, AccountsController, UI                                               | ✔      |
| FA-KTO-002  | Kontotyp (Giro/Spar)                                            | AccountService, Domain.AccountType Enum                                              | ✔      |
| FA-KTO-003  | Automatische Bankkontakt-Anlage                                 | AccountService, ContactService                                                       | ✔      |
| FA-KTO-004  | Konto teilen mit anderen Benutzern                              | (Teilen-Logik noch offen)                                                            | ✖      |
| FA-AUSZ-001 | Kontoauszugdateien einlesen (CSV, PDF)                          | StatementDraftService, FileReader                                                    | ✔      |
| FA-AUSZ-002 | Für jeden Import wird ein Buch.-Blatt erzeugt                   | StatementDraftService, Domain.StatementDraft                                         | ✔      |
| FA-AUSZ-003 | Buch.-Blatt Einträge bearbeiten, ergänzen, löschen              | StatementDraftService, UI (Detail + Editiermodus)                                    | ✔      |
| FA-AUSZ-004 | Beim Buchen entstehen Bankposten                                | Noch nicht implementiert                                                             | ✖      |
| FA-AUSZ-005 | Duplikatserkennung beim Import                                  | StatementDraftService: Duplikatprüfung                                               | ✔      |
| FA-AUSZ-006 | Kostenneutral bei eigenen Kontakten                             | StatementDraftService: Status-/CostNeutral-Logik                                     | ✔      |
| FA-AUSZ-007 | Kontaktposten beim Buchen entstehen                             | Noch nicht implementiert                                                             | ✖      |
| FA-AUSZ-008 | Empfänger muss Kontakt zugeordnet werden                        | StatementDraftService, UI                                                            | ✔      |
| FA-AUSZ-009 | Wertpapierzuordnung bei eigener Bank                            | UI & API: Auswahl, Neuanlage und Zuordnung in StatementDraftEntryDetail; Persistierung via /security Endpoint. Buchungs-/Transaktionslogik (Positions-/Depotbuchungen) noch offen. | ~      |
| FA-AUSZ-010 | PDF-Parsing mit Tabellenextraktion                              | ING_StatementFileReader, Barclays_StatementFileReader, erweiterbar                   | ✔      |
| FA-AUSZ-011 | Import-Pipeline mit Format-Strategie                            | StatementDraftService, Reader-Interface                                              | ✔      |
| FA-AUSZ-012 | Anzeige Gesamtbetrag verknüpfter Aufteilungs-Auszüge im Eintrag | StatementDraftsController GetEntry: SplitSum/Difference; EntryDetail UI Amount-Zeile | ✔      |
| FA-AUSZ-013 | Status offen bei Zahlungsintermediär bis vollständig gesplittet | StatementDraftService: TryAutoAssignContact & ReevaluateParentEntryStatusAsync       | ✔      |
| FA-AUSZ-014 | Originaldatei speichern & Download / Inline-Ansicht             | StatementDraft: OriginalFileContent; Controller /file Endpoint; Detail-Viewer        | ✔      |
| FA-KON-001  | Kontakte verwalten (CRUD)                                       | ContactService, ContactsController, UI                                               | ✔      |
| FA-KON-002  | Kontakte können Kategorie zugeordnet werden                     | ContactService, UI                                                                   | ✔      |
| FA-KON-003  | Anwender als Kontakt angelegt                                   | ContactService, Initialisierung                                                      | ✔      |
| FA-KON-004  | Aliasnamen mit Wildcards pflegen                                | ContactService, UI, StatementDraftService                                            | ✔      |
| FA-KON-005  | Kontakte verschmelzen (Merge)                                   | ContactService.MergeAsync, ContactsController, ContactMergeDialog                    | ✔      |
| FA-AUTO-001 | Aliasnamen für automatische Kontaktzuordnung                    | StatementDraftService: Alias-Matching                                                | ✔      |
| FA-AUTO-002 | Duplikate werden ausgelassen                                    | StatementDraftService: Duplikatprüfung                                               | ✔      |
| FA-SPAR-001 | Sparpläne verwalten (CRUD)                                      | SavingsPlanService, SavingPlanController, IO                                         | ✔      |
| FA-SPAR-002 | Sparplan-Typen                                                  | Noch nicht implementiert                                                             | ✖      |
| FA-SPAR-003 | Wiederkehrende Intervalle                                       | Noch nicht implementiert                                                             | ✖      |
| FA-SPAR-004 | Automatische Sparplanvorschläge                                 | Noch nicht implementiert                                                             | ✖      |
| FA-SPAR-005 | Manuelle Änderung Sparplanvorschlag                             | Noch nicht implementiert                                                             | ✖      |
| FA-SPAR-006 | Statusanzeige Sparziel erreicht                                 | Noch nicht implementiert                                                             | ✖      |
| FA-SPAR-007 | Anzeige fehlender Buchungen zum Ziel                            | Noch nicht implementiert                                                             | ✖      |
| FA-SPAR-008 | Prognose Zielverfehlung                                         | Noch nicht implementiert                                                             | ✖      |
| FA-SPAR-009 | Archivierung bei Ausbuchung                                     | Noch nicht implementiert                                                             | ✖      |
| FA-SPAR-010 | Sparplan aus Rückzahlung/Kredit                                 | Noch nicht implementiert                                                             | ✖      |
| FA-SPAR-011 | Sparplanposten bei Buchung                                      | Noch nicht implementiert                                                             | ✖      |
| FA-SPAR-012 | Umschalten aktive/archivierte Sparpläne                         | Noch nicht implementiert                                                             | ✖      |
| FA-WERT-001 | Wertpapiere verwalten                                           | SecurityService, SecuritiesController, UI (Liste, Detail, Kategorien), Erstellung & Rücksprung aus Kontoauszug | ✔      |
| FA-WERT-002 | Wertpapiertransaktionen                                         | Noch nicht implementiert                                                             | ✖      |
| FA-WERT-003 | Wertpapierposten bei Buchung                                    | Noch nicht implementiert                                                             | ✖      |
| FA-WERT-004 | Kursabruf AlphaVantage API                                      | Noch nicht implementiert                                                             | ✖      |
| FA-WERT-005 | Historische Kurse nachholen                                     | Noch nicht implementiert                                                             | ✖      |
| FA-WERT-006 | API-Limit erkennen/beachten                                     | Noch nicht implementiert                                                             | ✖      |
| FA-WERT-007 | Speicherung Kursposten                                          | Noch nicht implementiert                                                             | ✖      |
| FA-WERT-008 | Renditeberechnung                                               | Noch nicht implementiert                                                             | ✖      |
| FA-REP-001  | Buchungsaggregation (Monat, Quartal, Jahr)                      | Noch nicht implementiert                                                             | ✖      |
| FA-REP-002  | Year-To-Date Berechnung                                         | Noch nicht implementiert                                                             | ✖      |
| FA-REP-003  | Vergleich mit Vorjahr                                           | Noch nicht implementiert                                                             | ✖      |
| FA-REP-004  | GuV pro Monat/Kategorie                                         | Noch nicht implementiert                                                             | ✖      |
| FA-REP-005  | Renditekennzahlen Wertpapiere                                   | Noch nicht implementiert                                                             | ✖      |
| FA-KPI-001  | Dividenden aktueller Monat                                      | Noch nicht implementiert                                                             | ✖      |
| FA-KPI-002  | Dividenden aktuelles Jahr                                       | Noch nicht implementiert                                                             | ✖      |
| FA-KPI-003  | Einnahmen/Ausgaben pro Monat                                    | Noch nicht implementiert                                                             | ✖      |
| FA-KPI-004  | Gesamtdepotrendite aktuelles Jahr                               | Noch nicht implementiert                                                             | ✖      |
| FA-KPI-005  | KPI-Fallback Jahresanfang                                       | Noch nicht implementiert                                                             | ✖      |
| FA-UI-001   | Suchfeld in Listen                                              | Kontakte-Liste + Merge-Dialog implementiert; andere Listen teils offen               | ~      |
| FA-UI-002   | Live-Filterung                                                  | Debounce Filter Kontakte-Liste umgesetzt (Name)                                      | ~      |
| FA-API-001  | Web API für alle Entitäten                                      | Controller für Konten, Kontakte, Auszüge                                             | ✔      |
| FA-API-002  | Suchkriterien für API                                           | Kontakte: type + q Filter ergänzt; weitere Entitäten offen                           | ~      |
| FA-API-003  | Authentifizierung & Autorisierung                               | Program.cs, Controller `[Authorize]`                                                 | ✔      |
| FA-API-004  | Rate Limiting/Caching Kursabfragen                              | Noch nicht implementiert                                                             | ✖      |
| FA-AUTH-001 | Anmeldung erforderlich                                          | Program.cs, Controller `[Authorize]`                                                 | ✔      |
| FA-AUTH-002 | JWT-Token bei Anmeldung                                         | Program.cs, JwtBearer                                                                | ✔      |
| FA-AUTH-003 | Token im Authorization Header                                   | Program.cs, JwtBearer                                                                | ✔      |
| FA-AUTH-004 | Datenzugriff auf Benutzer gescoped                              | Services, Controller: UserId-Scoping                                                 | ✔      |
| FA-AUTH-005 | Konto teilen mit Benutzer                                       | Noch nicht implementiert                                                             | ✖      |
| FA-AUTH-006 | Schreibrechte konfigurierbar                                    | Noch nicht implementiert                                                             | ✖      |
| FA-AUTH-007 | Entzug von Freigaben                                            | Noch nicht implementiert                                                             | ✖      |
| FA-AUTH-008 | Passworthashing                                                 | Noch nicht implementiert (nur JWT)                                                   | ✖      |
| FA-AUTH-009 | Admin-Oberfläche Benutzerverwaltung                             | Noch nicht implementiert                                                             | ✖      |
| FA-AUTH-010 | Erster Benutzer = Admin                                         | Noch nicht implementiert                                                             | ✖      |
| FA-AUTH-011 | Admin kann Benutzer bearbeiten/löschen                          | Noch nicht implementiert                                                             | ✖      |
| FA-AUTH-012 | Löschen entfernt private Daten                                  | Noch nicht implementiert                                                             | ✖      |
| FA-AUTH-013 | Admin kann Sperren aufheben                                     | Noch nicht implementiert                                                             | ✖      |
| FA-I18N-001 | UI-Texte in Deutsch/Englisch                                    | Ressourcenstruktur, Program.cs                                                       | ✔      |
| FA-I18N-002 | Sprache im Profil einstellen                                    | Noch nicht implementiert                                                             | ✖      |
| FA-I18N-003 | Fallback auf Browser/Systemsprache                              | Program.cs: SupportedCultures                                                        | ✔      |
| FA-I18N-004 | Texte über Resource-Dateien verwaltet                           | Ressourcenstruktur                                                                   | ✔      |
| FA-I18N-005 | Sprachwechsel ohne Neuanmeldung                                 | Noch nicht implementiert                                                             | ✖      |
| FA-I18N-006 | Datums-/Zahlen-/Währungsformate                                 | Program.cs, UI: CultureInfo                                                          | ✔      |
| NFA-PERF-001| Import Performance                                              | Noch nicht validiert                                                                 | ~      |
| NFA-PERF-002| KPI Performance                                                 | Noch nicht validiert                                                                 | ~      |
| NFA-SEC-001 | Endpunkte geschützt                                             | Program.cs, Controller `[Authorize]`                                                 | ✔      |
| NFA-SEC-002 | Idempotenz bei Verbuchung                                       | StatementDraftService: Duplikatprüfung                                               | ✔      |
| NFA-SEC-003 | Access Scope Enforcement                                        | Services, Controller: UserId-Scoping                                                 | ✔      |
| NFA-SEC-004 | Passwort-Hashing (Argon2id/bcrypt)                              | Noch nicht implementiert                                                             | ✖      |
| NFA-SEC-005 | Token-Lebensdauer/Refresh-Token                                 | Noch nicht implementiert                                                             | ✖      |
| NFA-SEC-006 | Audit Logging sicherheitsrelevanter Aktionen                    | Noch nicht implementiert                                                             | ✖      |
| NFA-SEC-007 | Login-Sperre nach Fehlversuchen                                 | Noch nicht implementiert                                                             | ✖      |
| NFA-SEC-008 | Admin-Audit Logging                                             | Noch nicht implementiert                                                             | ✖      |
| NFA-REL-001 | Fehler beim Kursabruf blockiert Hauptfunktionen nicht           | Noch nicht implementiert                                                             | ✖      |
| NFA-USAB-001| Responsive UI                                                   | UI: Blazor, Responsive Design teilweise                                              | ~      |
| NFA-ARCH-001| Trennung Domäne/Präsentation                                   | Shared Library, Blazor, Services                                                     | ✔      |
| NFA-LOG-001 | Zentrales Logging                                               | Program.cs, Serilog                                                                  | ✔      |
| NFA-I18N-001| Zwei Sprachen, Fallback                                         | Program.cs, Ressourcenstruktur                                                       | ✔      |
| NFA-DATA-001| Zeitreihen effizient gespeichert                                | Noch nicht implementiert                                                             | ✖      |
| NFA-PRIV-001| Lokale Speicherung, keine Weitergabe                            | Program.cs, keine Weitergabe                                                         | ✔      |

**Legende:**  
✔ = umgesetzt / vorhanden  
✖ = offen / noch nicht implementiert  
~ = teilweise umgesetzt / in Arbeit  

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

*Letzte Aktualisierung: 05.09.2025*