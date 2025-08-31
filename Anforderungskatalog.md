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

## 5. Funktionale Anforderungen
Nummerierung: FA-[DOM�NE]-[laufende Nummer]

### 5.1 Kontenverwaltung
- FA-KTO-001: System erm�glicht das Anlegen beliebig vieler Bankkonten.
- FA-KTO-002: Jedes Konto hat einen Typ (Girokonto | Sparkonto).
- FA-KTO-003: Automatische Anlage eines Kontakt-Eintrags f�r jede Bank; Bankkontakt kann mehreren Konten zugeordnet sein.

### 5.2 Kontoauszug Import & Buchungen
- FA-AUSZ-001: Einlesen von Kontoauszugdateien (Formate: offen, zu spezifizieren).
- FA-AUSZ-002: F�r jeden Import wird ein Buch.-Blatt erzeugt.
- FA-AUSZ-003: Buch.-Blatt Eintr�ge k�nnen bearbeitet, erg�nzt, gel�scht werden bis zur Buchung.
- FA-AUSZ-004: Beim Buchen entstehen Bankposten (persistente Buchungen).
- FA-AUSZ-005: Bereits verbuchte Eintr�ge werden beim erneuten Einlesen erkannt und ignoriert (Duplikatserkennung).
- FA-AUSZ-006: Buchungen zwischen eigenen Kontakten (eigene Bankkonten) werden als kostenneutral markiert.
- FA-AUSZ-007: Beim Buchen entstehen gleichzeitig Kontaktposten.
- FA-AUSZ-008: Empf�nger eines Kontoauszugeintrags muss einem Kontakt zugeordnet werden (Pflicht).
- FA-AUSZ-009: Wenn Empf�nger die eigene Bank ist, kann (optional) eine Wertpapierzuordnung erfolgen (Transaktionstyp, Geb�hren, Steuern, Menge).

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

## 6. Nicht-funktionale Anforderungen
- NFA-PERF-001: Import von 1.000 Buchungszeilen < 10 Sekunden (Zielwert; Messpunkt nach Implementierung validieren).
- NFA-PERF-002: Dashboard-KPIs laden < 2 Sekunden bei Datenbestand < 50k Posten.
- NFA-SEC-001: Gesch�tzte Endpunkte nur f�r authentifizierten Benutzer.
- NFA-SEC-002: Schutz vor mehrfacher doppelter Verbuchung (Idempotenz-Strategie mit Hash).
- NFA-REL-001: Fehler beim Kursabruf d�rfen Hauptfunktionen nicht blockieren (resiliente Retries / Circuit Breaker).
- NFA-USAB-001: Responsive UI (Desktop, iPhone Formfaktor).
- NFA-ARCH-001: Trennung von Dom�nenlogik (Shared) und Pr�sentation (Blazor / MAUI).
- NFA-LOG-001: Zentrales Logging (min. INFO, separate Kategorie f�r Kursabrufe & Import).
- NFA-I18N-001: Prim�rsprache Deutsch; sp�tere Erweiterbarkeit f�r Mehrsprachigkeit vorgesehen.
- NFA-DATA-001: Zeitreihen (Kurse) werden effizient gespeichert (Index auf Symbol+Datum).
- NFA-PRIV-001: Lokale Speicherung personenbezogener Daten; keine Weitergabe an Dritte (au�er Kursdienst).

## 7. Datenobjekte (hochlevelig)
- Konto: Id, Typ, BankkontaktId, IBAN, Bezeichnung, Saldo
- Kontoauszugseintrag (tempor�r): ImportId, KontoId, Buchungsdatum, Betrag, Betreff, RohdatenHash, KontaktId?, SparplanId?, WertpapierTransaktionsdaten?
- Posten (Bank/Kontakt): Id, KontoId/KontaktId, Betrag, Datum, ReferenzQuelle
- Sparplan: Id, Typ, Zielbetrag?, Zieldatum?, Intervall?, AktivFlag, ArchivDatum?
- Sparplanposten: Id, SparplanId, Datum, Betrag, Status
- Wertpapier: Id, Symbol/ISIN, Typ, Name
- Wertpapiertransaktion: Id, WertpapierId, Typ, Datum, Menge, Preis, Geb�hren, Steuern, KontoId
- Kursposten: Id, WertpapierId, Datum, Schlusskurs, Quelle
- Kontakt: Id, Name, KategorieId?, Typ (Bank, Person, Selbst, Sonstige)
- Aliasname: Id, KontaktId, Pattern

(Detailattribute & Beziehungen sind weiter zu verfeinern.)

## 8. Gesch�ftsregeln (Ausz�ge)
- GR-001: Duplikatserkennung basiert auf (KontoId + Buchungsdatum + Betrag + normalisierter Betreff) oder Hash der Originalzeile.
- GR-002: Kostenneutral wenn Quelle- und Zielkontakt eigene Konten (Transfer) � Betrag beeinflusst nicht Ergebnis�bersicht.
- GR-003: Sparziel erreicht wenn kumulierte Sparplanposten >= Zielbetrag.
- GR-004: Prognose f�r Zielverfehlung nutzt lineare Extrapolation der letzten n Buchungen (n zu definieren, Default 3-6).
- GR-005: Kursimport unterbricht nach Erreichen Rate Limit und setzt Sperrmarke bis Tageswechsel (UTC?).

## 9. UI-Anforderungen (Ausz�ge)
- Symbole/Badges f�r Sparziel-Status (erreicht, nahe, verfehlt Prognose) in Kontoauszug�bersicht & Detailkarten.
- Umschaltbare Tabs/Filter f�r aktive vs. archivierte Sparpl�ne.
- Pull-Down (oder Button) zur Einblendung Suchfeld auf Listen.

## 10. Fehler- & Ausnahmebehandlung
- Importfehler: Protokollierung je Zeile, Fortsetzung �briger Zeilen.
- Kursabruf Timeout: Retry mit Exponential Backoff bis Limit erreicht.

## 11. Abgrenzung
- Kein Multi-User / Mandantenbetrieb (Annahme: Einzelbenutzer). (Zu best�tigen)
- Keine automatische Bank-Schnittstelle (z.B. HBCI) � nur Dateiimport.

## 12. Offene Punkte / Kl�rungsbedarf
- OP-001: Exakte unterst�tzte Kontoauszugsdateiformate (CSV, MT940, CAMT.053?).
- OP-002: Namenskonvention der Projekte: FinanceManager vs. Finanzverwaltung (vereinheitlichen?).
- OP-003: Authentifizierungsmechanismus (Identity, OAuth, Lokal?).
- OP-004: Kategorienstruktur (Hierarchie erlaubt?).
- OP-005: Performanceziele validieren / anpassen nach erstem Datenvolumentest.
- OP-006: Wie viele vergangene Jahre f�r Kurshistorie nachladen? Begrenzung definieren.
- OP-007: Prognoseverfahren f�r Sparziele (Algorithmus finalisieren).

## 13. Priorisierung (grobe Wellen)
Welle 1: Konten, Kontakte, Import, Buchung, Alias, Basis-Dashboard.
Welle 2: Sparpl�ne + Statuslogik, Erweiterte Auswertungen.
Welle 3: Wertpapiere + Kursdienst + Renditen.
Welle 4: Feinschliff KPIs, Prognosen, Optimierungen.

## 14. Akzeptanzkriterien (Beispiele)
- AK-IMP-001: Import einer Datei mit 100 Buchungen erzeugt 100 tempor�re Eintr�ge, Duplikate (simuliert) werden nicht erneut erzeugt.
- AK-SPAR-001: Erreichen eines Zielbetrags markiert Sparplan als Erreicht und zeigt Symbol in n�chstem Seitenreload.
- AK-WERT-001: Kaufbuchung erzeugt Wertpapierposten und aktualisiert Bestand korrekt.

## 15. Sicherheit & Datenschutz (Erweiterung)
- Lokale Verschl�sselung sensibler Konfigurationsschl�ssel (API Key AlphaVantage).
- Keine �bertragung pers�nlicher Kontaktstammdaten an Dritte.

## 16. Tracking & Versionierung
�nderungen an Anforderungen werden versioniert (History Abschnitt erg�nzen bei Updates).

---
Status: Entwurf � zur Review.
