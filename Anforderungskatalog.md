# Anforderungskatalog

Version: 0.1 (Entwurf)
Datum: 2025-08-31
Autor: (auszuf�llen)

## 1. Ziel & Zweck
Die Solution dient der pers�nlichen Finanzverwaltung des Anwenders. Sie stellt Funktionen zur Verwaltung von Bankkonten, Kontoausz�gen, Kontakten, Sparpl�nen, Wertpapieren sowie Auswertungen & KPIs bereit. Die Blazor Server Anwendung (FinanceManager.Web) fungiert als UI und stellt zus�tzlich eine Web API bereit. (Geplante) Zusatz-Clients: .NET MAUI App (iOS). Gemeinsame Logik soll in einer Shared Library gekapselt werden.

## 2. System�bersicht
- Blazor Server (UI + API)
- (Geplant) .NET MAUI App f�r iOS
- Shared Library f�r Dom�nenmodelle, Services & Schnittstellen
- Externe Dienste: AlphaVantage (Kursdaten)

## 3. Stakeholder
- Endanwender (Privatperson)
- Entwickler / Maintainer

## 4. Begriffe (Glossar)
- Kontoauszug: Datei mit Buchungszeilen eines Bankkontos
- Buch.-Blatt: Tempor�re Sammlung eingelesener Buchungszeilen vor endg�ltiger Verbuchung
- Posten: Verbuchte Buchung (Bank-, Kontakt-, Sparplan- oder Wertpapierposten)
- Kontakt: Gegenpartei einer Buchung (Person, Bank, Institution, eigenes Konto)
- Sparplan: Zielgerichtetes oder offenes Sparen (einmalig, wiederkehrend oder offen)
- Wertpapier: Aktie oder Fonds (mit Transaktionen und Kursverlauf)
- KPIs: Kennzahlen�bersicht auf Startseite
- Aliasname: Alternativer Name/Pattern zur automatischen Kontaktzuordnung
- Benutzer / Anwender: Authentifizierte Person mit eigenem Datenbereich (und evtl. geteilten Konten)
- Token: Signiertes Authorisierungstoken (z.B. JWT) f�r API-Aufrufe

## 5. Funktionale Anforderungen
Nummerierung: FA-[DOM�NE]-[laufende Nummer]

### 5.1 Kontenverwaltung
- FA-KTO-001: System erm�glicht das Anlegen beliebig vieler Bankkonten.
- FA-KTO-002: Jedes Konto hat einen Typ (Girokonto | Sparkonto).
- FA-KTO-003: Automatische Anlage eines Kontakt-Eintrags f�r jede Bank; Bankkontakt kann mehreren Konten zugeordnet sein.
- FA-KTO-004: Bankkonten k�nnen mit anderen Benutzern geteilt werden (Lese-/Schreibrechte, Detailrechte zu definieren).

### 5.2 Kontoauszug Import & Buchungen
- FA-AUSZ-001: Einlesen von Kontoauszugdateien (aktuell unterst�tzte Formate: CSV, PDF; Erweiterbarkeit f�r weitere Formate vorgesehen).
- FA-AUSZ-002: F�r jeden Import wird ein Buch.-Blatt erzeugt.
- FA-AUSZ-003: Buch.-Blatt Eintr�ge k�nnen bearbeitet, erg�nzt, gel�scht werden bis zur Buchung.
- FA-AUSZ-004: Beim Buchen entstehen Bankposten (persistente Buchungen).
- FA-AUSZ-005: Bereits verbuchte Eintr�ge werden beim erneuten Einlesen erkannt und ignoriert (Duplikatserkennung).
- FA-AUSZ-006: Buchungen zwischen eigenen Kontakten (eigene Bankkonten) werden als kostenneutral markiert.
- FA-AUSZ-007: Beim Buchen entstehen gleichzeitig Kontaktposten.
- FA-AUSZ-008: Empf�nger eines Kontoauszugeintrags muss einem Kontakt zugeordnet werden (Pflicht).
- FA-AUSZ-009: Wenn Empf�nger die eigene Bank ist, kann (optional) eine Wertpapierzuordnung erfolgen (Transaktionstyp, Geb�hren, Steuern, Menge).
- FA-AUSZ-010: PDF-Parsing extrahiert Tabelle(n) mit Buchungszeilen (Heuristiken / Vorlagen pro Bank anlegbar).
- FA-AUSZ-011: Import-Pipeline nutzt Format-Strategie (Strategy Pattern) zur einfachen Erweiterung um neue Formate.

### 5.3 Kontakte & Kategorien
- FA-KON-001: Verwaltung von Kontakten (CRUD).
- FA-KON-002: Kontakte k�nnen einer Kategorie zugeordnet werden.
- FA-KON-003: Anwender wird automatisch als Kontakt angelegt (Initialisierung).
- FA-KON-004: Aliasnamen (mit Wildcards ? und *) k�nnen gepflegt werden zur automatischen Zuordnung beim Import.

### 5.4 Automatische Zuordnung
- FA-AUTO-001: Beim Import wird f�r jede Buchungszeile versucht, anhand Aliasnamen eine Kontaktzuordnung herzustellen.
- FA-AUTO-002: Bereits verbuchte Eintr�ge (Hash / Idempotenzkriterium) werden ausgelassen.

### 5.5 Sparpl�ne
- FA-SPAR-001: Verwaltung von Sparpl�nen (CRUD).
- FA-SPAR-002: Typen: einmalig (mit Zielbetrag + Zieldatum), wiederkehrend (Monatsbetrag + Intervall), offen (kein Zielbetrag/-datum).
- FA-SPAR-003: Wiederkehrende Intervalle: Monatlich, 2-monatlich, Quartalsweise, Halbj�hrlich, J�hrlich.
- FA-SPAR-004: Kontoauszugeintr�ge mit Empf�nger = eigenes Sparkonto werden bzgl. Sparpl�nen (Betreff-Matching) automatisch vorgeschlagen.
- FA-SPAR-005: Automatisch vorgeschlagene Zuordnung kann manuell ge�ndert werden.
- FA-SPAR-006: Erkennung, wenn Buchung ein Sparziel erreicht (Statusanzeige, Symbole in �bersicht & Eintragskarte).
- FA-SPAR-007: Anzeige, wenn 1 oder 2 weitere Buchungen zum Ziel fehlen.
- FA-SPAR-008: Anzeige, wenn Ziel voraussichtlich verfehlt wird (Prognose auf Basis bisheriger Frequenz / Betrag) � heuristische Logik zu definieren.
- FA-SPAR-009: Negative Buchung, die Sparplan komplett ausbucht, bietet Option zum Archivieren des Sparplans.
- FA-SPAR-010: Erstellung eines Sparplans aus einer Zahlung an Bankkontakt (Arten �R�ckzahlung / Kredit�hnlich�). Ziel ist Saldo-Ausgleich auf 0.
- FA-SPAR-011: Verbuchte Bewegungen erzeugen Sparplanposten.
- FA-SPAR-012: �bersicht erm�glicht Umschalten zwischen aktiven und archivierten Sparpl�nen.

### 5.6 Wertpapiere
- FA-WERT-001: Verwaltung von Wertpapieren (Aktien, Fonds) inkl. Stammdaten (Name, ISIN/Symbol, Typ).
- FA-WERT-002: Wertpapiertransaktionen: Kauf, Verkauf, Dividende/Zins inkl. Geb�hren & Steuern & St�ckzahl.
- FA-WERT-003: Beim Buchen eines Kontoauszugs mit Wertpapierzuordnung entstehen Wertpapierposten.
- FA-WERT-004: T�glicher Abruf der Kurse (Vortag) �ber AlphaVantage API.
- FA-WERT-005: Historische Kurse werden nachgeholt, falls L�cken bestehen.
- FA-WERT-006: Erkennung & Respektieren des API-Limits (Rate Limit) � keine weiteren Anfragen bis Tageswechsel.
- FA-WERT-007: Speicherung von Kursposten (Datum, Kurs, Quelle, Zeitstempel).
- FA-WERT-008: Berechnung Dividendenrendite, Kursrendite, Gesamtrendite pro Wertpapier.

### 5.7 Auswertungen & Berichte
- FA-REP-001: Aggregation von Buchungen (Posten) monats-, quartals- und jahresweise.
- FA-REP-002: Year-To-Date (YTD) Berechnungen.
- FA-REP-003: Optional Vergleich mit Vorjahreszeitraum.
- FA-REP-004: Gewinn-und-Verlustrechnung pro Monat gruppiert nach Kontaktkategorien / Kategorien.
- FA-REP-005: Renditekennzahlen f�r Wertpapiere (Dividende, Kurs, Gesamt) �ber Zeitr�ume.

### 5.8 KPI Dashboard (Startseite)
- FA-KPI-001: Anzeige Dividenden aktueller Monat.
- FA-KPI-002: Anzeige Dividenden aktuelles Jahr.
- FA-KPI-003: Einnahmen / Ausgaben pro Monat (Balkendiagramm).
- FA-KPI-004: Gesamtdepotrendite aktuelles Jahr.
- FA-KPI-005: Jahresanfang: Kennzahlen zeigen bis Vorjahresabschluss alte Jahreswerte, bis neue Daten vorliegen.

### 5.9 Suche & Filter
- FA-UI-001: In allen �bersichtslisten Pull-Down-Geste (oder UI-Interaktion) �ffnet Suchfeld.
- FA-UI-002: Live-Filterung der Liste nach Suchtext. Die Suchkritierien sind dom�nenspezifisch (z.B. Betreff, Kontaktname, Betrag bei Buchungen) und werden an die API �bergeben.

### 5.10 API & Integration
- FA-API-001: Web API Endpunkte f�r alle verwalteten Entit�ten (Konten, Kontakte, Sparpl�ne, Wertpapiere, Buchungsimport, Berichte).
- FA-API-002: Beim Abruf der Daten k�nnen Suchkriterien zum Einschr�nken des Ergebnisses �bergeben werden.
- FA-API-003: Authentifizierung & Autorisierung (Mechanismus offen � z.B. Identity / OIDC) � zu definieren.
- FA-API-004: Rate Limiting f�r externe Kursabfragen (AlphaVantage) + Caching.

### 5.11 Benutzer & Zugriffskontrolle
- FA-AUTH-001: Jeder Benutzer muss sich anmelden (Username + Passwort) um auf UI / API zuzugreifen.
- FA-AUTH-002: Bei erfolgreicher Anmeldung wird ein signiertes Authorisierungstoken (z.B. JWT) erstellt und dem Client zur�ckgegeben.
- FA-AUTH-003: Token wird bei allen API-Anfragen im Authorization Header (Bearer) �bertragen.
- FA-AUTH-004: S�mtliche Datenzugriffe (Lesen/�ndern) werden auf den angemeldeten Benutzer gescoped (mandanten�hnliche Isolation).
- FA-AUTH-005: Bankkonten k�nnen gezielt mit anderen Benutzern geteilt werden; geteilte Benutzer erhalten Zugriff auf zugeh�rige Bankposten und den Bankkontakt.
- FA-AUTH-006: Teilen eines Bankkontos impliziert Lesen der historischen Posten; Schreibrechte (Buchungen importieren) sind konfigurierbar (Rollen/Flags zu definieren).
- FA-AUTH-007: Entzug des Teilens entfernt den Zugriff r�ckwirkend (kein L�schen historischer Daten, nur Zugriff entzogen).
- FA-AUTH-008: Passworthashing (kein Klartext) � konkrete Algorithmen als NFA (siehe NFA-SEC-004).
- FA-AUTH-009: Administrationsoberfl�che f�r Benutzerverwaltung (nur f�r Administratoren sichtbar).
- FA-AUTH-010: Der erste registrierte Benutzer wird automatisch als Administrator markiert.
- FA-AUTH-011: Administratoren k�nnen Benutzerkonten anlegen, bearbeiten (Username �ndern, sperren/entsperren), l�schen.
- FA-AUTH-012: L�schen eines Benutzerkontos entfernt dessen private Daten (Konten/Posten/Sparpl�ne/Wertpapiere).
- FA-AUTH-013: Administratoren k�nnen tempor�re Sperren vorzeitig aufheben.

### 5.12 Internationalisierung & Lokalisierung
- FA-I18N-001: S�mtliche UI-Texte werden in Deutsch und Englisch bereitgestellt.
- FA-I18N-002: Benutzer kann bevorzugte Sprache in seinem Profil einstellen.
- FA-I18N-003: Fallback: Wenn keine Benutzersprache gesetzt ist, wird Browser-/Systemsprache verwendet; falls nicht unterst�tzt, Standard = Deutsch.
- FA-I18N-004: Texte werden �ber Resource-Dateien (resx) pro Sprache verwaltet; harte Strings im Code sind zu vermeiden.
- FA-I18N-005: Sprachwechsel im eingeloggten Zustand aktualisiert sichtbare Texte ohne Neuanmeldung (Dynamic Refresh).
- FA-I18N-006: Datums-, Zahlen- und W�hrungsformate folgen der aktiven UI-Kultur.

## 6. Nicht-funktionale Anforderungen
- NFA-PERF-001: Import von 1.000 Buchungszeilen < 10 Sekunden (Zielwert; Messpunkt nach Implementierung validieren).
- NFA-PERF-002: Dashboard-KPIs laden < 2 Sekunden bei Datenbestand < 50k Posten.
- NFA-SEC-001: Gesch�tzte Endpunkte nur f�r authentifizierten Benutzer.
- NFA-SEC-002: Schutz vor mehrfacher doppelter Verbuchung (Idempotenz-Strategie mit Hash).
- NFA-SEC-003: Jeder Request wird serverseitig auf Besitz bzw. Teil-Berechtigung des Zielobjekts gepr�ft (Access Scope Enforcement).
- NFA-SEC-004: Passw�rter werden mit starkem adaptiven Hash (z.B. Argon2id oder bcrypt mit Work-Faktor) gespeichert.
- NFA-SEC-005: Tokens haben begrenzte Lebensdauer (z.B. 15�60 min); optionale Erneuerung �ber Refresh-Token (Blacklist bei Widerruf).
- NFA-SEC-006: Audit Logging sicherheitsrelevanter Aktionen (Login, Konto-Teilen, Entzug, fehlgeschlagene Logins > Schwelle, Benutzeranlage/-l�schung, Entsperrung).
- NFA-SEC-007: Wiederholte fehlgeschlagene Login-Versuche f�hren zu tempor�rer Sperre (60 Minuten ab 3 fehlerhaften Anmeldeversuchen).
- NFA-SEC-008: Administratoraktionen werden mit verantwortlichem Admin-BenutzerId im Audit Log festgehalten.
- NFA-REL-001: Fehler beim Kursabruf d�rfen Hauptfunktionen nicht blockieren (resiliente Retries / Circuit Breaker).
- NFA-USAB-001: Responsive UI (Desktop, iPhone Formfaktor).
- NFA-ARCH-001: Trennung von Dom�nenlogik (Shared) und Pr�sentation (Blazor / MAUI).
- NFA-LOG-001: Zentrales Logging (min. INFO, separate Kategorie f�r Kursabrufe & Import).
- NFA-I18N-001: Zwei vollst�ndig unterst�tzte Sprachen (DE, EN) mit Fallback-Strategie; neue Sprachen ohne Code�nderung �ber Ressourcen erweiterbar.
- NFA-DATA-001: Zeitreihen (Kurse) werden effizient gespeichert (Index auf Symbol+Datum).
- NFA-PRIV-001: Lokale Speicherung personenbezogener Daten; keine Weitergabe an Dritte (au�er Kursdienst).

## 7. Datenobjekte (hochlevelig)
- Benutzer: Id, Username, PasswortHash, PasswortSalt/Parameter, IsAdmin, LockedUntil?, PreferredLanguage (de|en|null), Erstelldatum, LetzterLogin, AktivFlag
- Konto: Id, Typ, BankkontaktId, IBAN, Bezeichnung, Saldo, BesitzerBenutzerId (Prim�rbesitzer)
- BenutzerKontoFreigabe (Join): BenutzerId, KontoId, Rolle (Lesen|Schreiben), FreigabeDatum, EntzugsDatum?
- Kontoauszugseintrag (tempor�r): ImportId, KontoId, Buchungsdatum, Betrag, Betreff, RohdatenHash, KontaktId?, SparplanId?, WertpapierTransaktionsdaten?, QuellFormat (CSV|PDF|...)
- Posten (Bank/Kontakt): Id, KontoId/KontaktId, Betrag, Datum, ReferenzQuelle, BenutzerId (Owner)
- Sparplan: Id, Typ, Zielbetrag?, Zieldatum?, Intervall?, AktivFlag, ArchivDatum?, BenutzerId
- Sparplanposten: Id, SparplanId, Datum, Betrag, Status
- Wertpapier: Id, Symbol/ISIN, Typ, Name, BenutzerId
- Wertpapiertransaktion: Id, WertpapierId, Typ, Datum, Menge, Preis, Geb�hren, Steuern, KontoId
- Kursposten: Id, WertpapierId, Datum, Schlusskurs, Quelle
- Kontakt: Id, Name, KategorieId?, Typ (Bank, Person, Selbst, Sonstige), BenutzerId? (null wenn geteilter Bankkontakt)
- Aliasname: Id, KontaktId, Pattern

## 8. Gesch�ftsregeln (Ausz�ge)
- GR-001: Duplikatserkennung basiert auf (KontoId + Buchungsdatum + Betrag + normalisierter Betreff) oder Hash der Originalzeile.
- GR-002: Kostenneutral wenn Quelle- und Zielkontakt eigene Konten (Transfer) � Betrag beeinflusst nicht Ergebnis�bersicht.
- GR-003: Sparziel erreicht wenn kumulierte Sparplanposten >= Zielbetrag.
- GR-004: Prognose f�r Zielverfehlung nutzt lineare Extrapolation der letzten n Buchungen (n zu definieren, Default 3-6).
- GR-005: Kursimport unterbricht nach Erreichen Rate Limit und setzt Sperrmarke bis Tageswechsel (UTC?).
- GR-006: Benutzer d�rfen nur auf Daten zugreifen, die ihnen geh�ren oder die �ber Freigaben geteilt wurden.
- GR-007: Teilen eines Kontos teilt implizit den zugeordneten Bankkontakt; Kontakt bleibt global unver�ndert.
- GR-008: Entfernen einer Freigabe entzieht sofort Zugriff; Referenzen in Logs / Historie bleiben bestehen.
- GR-009: �nderung von Zugriffsrechten wird auditiert.
- GR-010: Erster erfolgreich registrierter Benutzer wird als IsAdmin = true gespeichert.
- GR-011: L�schen eines Benutzers durch Admin f�hrt zu definiertem L�sch-/�bertragungsworkflow f�r dessen Objekte (Spezifikation erforderlich, s. Offene Punkte).
- GR-012: Sprachermittlung Reihenfolge: Benutzerpr�ferenz -> Browser/Systemsprache -> Standard Deutsch.
- GR-013: Import-Strategieauswahl anhand Dateimerkmale (Extension + Content Detection bei PDF/CSV).

## 9. UI-Anforderungen (Ausz�ge)
- Symbole/Badges f�r Sparziel-Status (erreicht, nahe, verfehlt Prognose) in Kontoauszug�bersicht & Detailkarten.
- Umschaltbare Tabs/Filter f�r aktive vs. archivierte Sparpl�ne.
- Pull-Down (oder Button) zur Einblendung Suchfeld auf Listen.
- Dialog / Oberfl�che zum Teilen eines Bankkontos (Benutzer suchen, Rolle vergeben, Entzug durchf�hren).
- Administrationsbereich: Benutzerliste (Filter, Suche), Aktionen (Anlegen, Bearbeiten, Sperren/Entsperren, L�schen), Detailansicht inkl. Audit-Eintr�ge.
- Sprachumschalter (Toggle / Dropdown) sichtbar nach Login und auf Login-Seite.

## 10. Fehler- & Ausnahmebehandlung
- Importfehler: Protokollierung je Zeile, Fortsetzung �briger Zeilen.
- Kursabruf Timeout: Retry mit Exponential Backoff bis Limit erreicht.
- Authentifizierung: Fehlversuche > Schwellwert -> tempor�re Sperre (Dauer definieren) & Audit Log.
- Benutzerl�schung: Transaktionale Ausf�hrung; bei Fehler Rollback & Fehlerlog.
- PDF-Parser: Bei nicht interpretierbarer Struktur Fehlereintrag mit Hinweis auf manuelle Nachpflege.

## 11. Abgrenzung
- (Entfernt) Vorherige Annahme Einzelbenutzer: System unterst�tzt mehrere Benutzer mit isolierten Datenbereichen und optionalem Teilen von Bankkonten.
- Keine automatische Bank-Schnittstelle (z.B. HBCI) � nur Dateiimport.

## 12. Offene Punkte / Kl�rungsbedarf
- OP-001: Exakte unterst�tzte Kontoauszugsdateiformate (CSV, MT940, CAMT.053?).
- OP-002: Namenskonvention der Projekte: FinanceManager vs. Finanzverwaltung (vereinheitlichen?).
- OP-003: Authentifizierungsmechanismus (Identity, OAuth, Lokal?).
- OP-004: Kategorienstruktur (Hierarchie erlaubt?).
- OP-005: Performanceziele validieren / anpassen nach erstem Datenvolumentest.
- OP-006: Wie viele vergangene Jahre f�r Kurshistorie nachladen? Begrenzung definieren.
- OP-007: Prognoseverfahren f�r Sparziele (Algorithmus finalisieren).
- OP-008: Notwendigkeit & Ausgestaltung von Refresh Tokens kl�ren.
- OP-009: Rollenmodell f�r Konto-Freigaben (nur Lesen / Lesen+Schreiben / Admin?).
- OP-010: Regelwerk f�r Umgang mit geteilten Konten bei L�schung des Prim�rbesitzers (Re-Assign? L�schen? Ownership Transfer).
- OP-011: Aufbewahrungs-/L�schkonzept f�r Audit-Logs (DSGVO-Aspekte?).
- OP-012: Erweiterung weiterer Importformate (MT940, CAMT) Priorit�t & Zeitplan.
- OP-013: Kriterien f�r Echtzeit-Sprachwechsel (Client-Speicherung, Persistenz bei Refresh).

## 13. Priorisierung (grobe Wellen)
Welle 1: Benutzer & Auth-Basis, Konten, Kontakte, Import (CSV/PDF), Buchung, Alias, Basis-Dashboard.
Welle 2: Sparpl�ne + Statuslogik, Erweiterte Auswertungen.
Welle 3: Wertpapiere + Kursdienst + Renditen.
Welle 4: Internationale UI Verfeinerung, zus�tzliche Importformate, Admin-Erweiterungen, Feinschliff KPIs, Prognosen, Optimierungen.

## 14. Akzeptanzkriterien (Beispiele)
- AK-IMP-001: Import einer Datei mit 100 Buchungen erzeugt 100 tempor�re Eintr�ge, Duplikate (simuliert) werden nicht erneut erzeugt.
- AK-SPAR-001: Erreichen eines Zielbetrags markiert Sparplan als Erreicht und zeigt Symbol in n�chstem Seitenreload.
- AK-WERT-001: Kaufbuchung erzeugt Wertpapierposten und aktualisiert Bestand korrekt.
- AK-AUTH-001: Unauthentifizierter Zugriff auf gesch�tzte API liefert 401.
- AK-AUTH-002: Benutzer ohne Freigabe f�r Konto erh�lt 403 beim Zugriff.
- AK-AUTH-003: Teilen eines Kontos macht Konto in Liste des anderen Benutzers sichtbar ohne Zugriff auf nicht geteilte Objekte.
- AK-AUTH-004: Erster angelegte Benutzer besitzt IsAdmin = true.
- AK-AUTH-005: Admin kann Benutzer sperren und entsperren; entsperren entfernt LockedUntil.
- AK-AUTH-006: Versuch eines nicht-Admin auf Admin-Endpunkte liefert 403.
- AK-I18N-001: �nderung der Sprache in den Einstellungen aktualisiert sichtbare Texte ohne Neuanmeldung.
- AK-I18N-002: Benutzer ohne gesetzte Sprache erh�lt UI in Browser-Sprache (Test mit de/en Browser-Locale) sonst Deutsch.
- AK-IMP-CSV-001: CSV-Datei wird korrekt erkannt und geparst (Trennzeichen-Konfiguration testbar).
- AK-IMP-PDF-001: PDF-Kontoauszug mit unterst�tzter Struktur erzeugt korrekte Buchungszeilen.

## 15. Sicherheit & Datenschutz (Erweiterung)
- Lokale Verschl�sselung sensibler Konfigurationsschl�ssel (API Key AlphaVantage).
- Keine �bertragung pers�nlicher Kontaktstammdaten an Dritte.
- Minimalprinzip bei Freigaben (nur n�tige Rechte vergeben).
- Adminaktionen nachvollziehbar (Audit Trail unver�nderlich).

## 16. Tracking & Versionierung
�nderungen an Anforderungen werden versioniert (History Abschnitt erg�nzen bei Updates).

---
Status: Entwurf � zur Review.
