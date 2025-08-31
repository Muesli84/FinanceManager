# Anforderungskatalog

Version: 0.1 (Entwurf)
Datum: 2025-08-31
Autor: (auszufüllen)

## 1. Ziel & Zweck
Die Solution dient der persönlichen Finanzverwaltung des Anwenders. Sie stellt Funktionen zur Verwaltung von Bankkonten, Kontoauszügen, Kontakten, Sparplänen, Wertpapieren sowie Auswertungen & KPIs bereit. Die Blazor Server Anwendung (FinanceManager.Web) fungiert als UI und stellt zusätzlich eine Web API bereit. (Geplante) Zusatz-Clients: .NET MAUI App (iOS). Gemeinsame Logik soll in einer Shared Library gekapselt werden.

## 2. Systemübersicht
- Blazor Server (UI + API)
- (Geplant) .NET MAUI App für iOS
- Shared Library für Domänenmodelle, Services & Schnittstellen
- Externe Dienste: AlphaVantage (Kursdaten)

## 3. Stakeholder
- Endanwender (Privatperson)
- Entwickler / Maintainer

## 4. Begriffe (Glossar)
- Kontoauszug: Datei mit Buchungszeilen eines Bankkontos
- Buch.-Blatt: Temporäre Sammlung eingelesener Buchungszeilen vor endgültiger Verbuchung
- Posten: Verbuchte Buchung (Bank-, Kontakt-, Sparplan- oder Wertpapierposten)
- Kontakt: Gegenpartei einer Buchung (Person, Bank, Institution, eigenes Konto)
- Sparplan: Zielgerichtetes oder offenes Sparen (einmalig, wiederkehrend oder offen)
- Wertpapier: Aktie oder Fonds (mit Transaktionen und Kursverlauf)
- KPIs: Kennzahlenübersicht auf Startseite
- Aliasname: Alternativer Name/Pattern zur automatischen Kontaktzuordnung

## 5. Funktionale Anforderungen
Nummerierung: FA-[DOMÄNE]-[laufende Nummer]

### 5.1 Kontenverwaltung
- FA-KTO-001: System ermöglicht das Anlegen beliebig vieler Bankkonten.
- FA-KTO-002: Jedes Konto hat einen Typ (Girokonto | Sparkonto).
- FA-KTO-003: Automatische Anlage eines Kontakt-Eintrags für jede Bank; Bankkontakt kann mehreren Konten zugeordnet sein.

### 5.2 Kontoauszug Import & Buchungen
- FA-AUSZ-001: Einlesen von Kontoauszugdateien (Formate: offen, zu spezifizieren).
- FA-AUSZ-002: Für jeden Import wird ein Buch.-Blatt erzeugt.
- FA-AUSZ-003: Buch.-Blatt Einträge können bearbeitet, ergänzt, gelöscht werden bis zur Buchung.
- FA-AUSZ-004: Beim Buchen entstehen Bankposten (persistente Buchungen).
- FA-AUSZ-005: Bereits verbuchte Einträge werden beim erneuten Einlesen erkannt und ignoriert (Duplikatserkennung).
- FA-AUSZ-006: Buchungen zwischen eigenen Kontakten (eigene Bankkonten) werden als kostenneutral markiert.
- FA-AUSZ-007: Beim Buchen entstehen gleichzeitig Kontaktposten.
- FA-AUSZ-008: Empfänger eines Kontoauszugeintrags muss einem Kontakt zugeordnet werden (Pflicht).
- FA-AUSZ-009: Wenn Empfänger die eigene Bank ist, kann (optional) eine Wertpapierzuordnung erfolgen (Transaktionstyp, Gebühren, Steuern, Menge).

### 5.3 Kontakte & Kategorien
- FA-KON-001: Verwaltung von Kontakten (CRUD).
- FA-KON-002: Kontakte können einer Kategorie zugeordnet werden.
- FA-KON-003: Anwender wird automatisch als Kontakt angelegt (Initialisierung).
- FA-KON-004: Aliasnamen (mit Wildcards ? und *) können gepflegt werden zur automatischen Zuordnung beim Import.

### 5.4 Automatische Zuordnung
- FA-AUTO-001: Beim Import wird für jede Buchungszeile versucht, anhand Aliasnamen eine Kontaktzuordnung herzustellen.
- FA-AUTO-002: Bereits verbuchte Einträge (Hash / Idempotenzkriterium) werden ausgelassen.

### 5.5 Sparpläne
- FA-SPAR-001: Verwaltung von Sparplänen (CRUD).
- FA-SPAR-002: Typen: einmalig (mit Zielbetrag + Zieldatum), wiederkehrend (Monatsbetrag + Intervall), offen (kein Zielbetrag/-datum).
- FA-SPAR-003: Wiederkehrende Intervalle: Monatlich, 2-monatlich, Quartalsweise, Halbjährlich, Jährlich.
- FA-SPAR-004: Kontoauszugeinträge mit Empfänger = eigenes Sparkonto werden bzgl. Sparplänen (Betreff-Matching) automatisch vorgeschlagen.
- FA-SPAR-005: Automatisch vorgeschlagene Zuordnung kann manuell geändert werden.
- FA-SPAR-006: Erkennung, wenn Buchung ein Sparziel erreicht (Statusanzeige, Symbole in Übersicht & Eintragskarte).
- FA-SPAR-007: Anzeige, wenn 1 oder 2 weitere Buchungen zum Ziel fehlen.
- FA-SPAR-008: Anzeige, wenn Ziel voraussichtlich verfehlt wird (Prognose auf Basis bisheriger Frequenz / Betrag) – heuristische Logik zu definieren.
- FA-SPAR-009: Negative Buchung, die Sparplan komplett ausbucht, bietet Option zum Archivieren des Sparplans.
- FA-SPAR-010: Erstellung eines Sparplans aus einer Zahlung an Bankkontakt (Arten „Rückzahlung / Kreditähnlich“). Ziel ist Saldo-Ausgleich auf 0.
- FA-SPAR-011: Verbuchte Bewegungen erzeugen Sparplanposten.
- FA-SPAR-012: Übersicht ermöglicht Umschalten zwischen aktiven und archivierten Sparplänen.

### 5.6 Wertpapiere
- FA-WERT-001: Verwaltung von Wertpapieren (Aktien, Fonds) inkl. Stammdaten (Name, ISIN/Symbol, Typ).
- FA-WERT-002: Wertpapiertransaktionen: Kauf, Verkauf, Dividende/Zins inkl. Gebühren & Steuern & Stückzahl.
- FA-WERT-003: Beim Buchen eines Kontoauszugs mit Wertpapierzuordnung entstehen Wertpapierposten.
- FA-WERT-004: Täglicher Abruf der Kurse (Vortag) über AlphaVantage API.
- FA-WERT-005: Historische Kurse werden nachgeholt, falls Lücken bestehen.
- FA-WERT-006: Erkennung & Respektieren des API-Limits (Rate Limit) – keine weiteren Anfragen bis Tageswechsel.
- FA-WERT-007: Speicherung von Kursposten (Datum, Kurs, Quelle, Zeitstempel).
- FA-WERT-008: Berechnung Dividendenrendite, Kursrendite, Gesamtrendite pro Wertpapier.

### 5.7 Auswertungen & Berichte
- FA-REP-001: Aggregation von Buchungen (Posten) monats-, quartals- und jahresweise.
- FA-REP-002: Year-To-Date (YTD) Berechnungen.
- FA-REP-003: Optional Vergleich mit Vorjahreszeitraum.
- FA-REP-004: Gewinn-und-Verlustrechnung pro Monat gruppiert nach Kontaktkategorien / Kategorien.
- FA-REP-005: Renditekennzahlen für Wertpapiere (Dividende, Kurs, Gesamt) über Zeiträume.

### 5.8 KPI Dashboard (Startseite)
- FA-KPI-001: Anzeige Dividenden aktueller Monat.
- FA-KPI-002: Anzeige Dividenden aktuelles Jahr.
- FA-KPI-003: Einnahmen / Ausgaben pro Monat (Balkendiagramm).
- FA-KPI-004: Gesamtdepotrendite aktuelles Jahr.
- FA-KPI-005: Jahresanfang: Kennzahlen zeigen bis Vorjahresabschluss alte Jahreswerte, bis neue Daten vorliegen.

### 5.9 Suche & Filter
- FA-UI-001: In allen Übersichtslisten Pull-Down-Geste (oder UI-Interaktion) öffnet Suchfeld.
- FA-UI-002: Live-Filterung der Liste nach Suchtext. Die Suchkritierien sind domänenspezifisch (z.B. Betreff, Kontaktname, Betrag bei Buchungen) und werden an die API übergeben.

### 5.10 API & Integration
- FA-API-001: Web API Endpunkte für alle verwalteten Entitäten (Konten, Kontakte, Sparpläne, Wertpapiere, Buchungsimport, Berichte).
- FA-API-002: Beim Abruf der Daten können Suchkriterien zum Einschränken des Ergebnisses übergeben werden.
- FA-API-003: Authentifizierung & Autorisierung (Mechanismus offen – z.B. Identity / OIDC) – zu definieren.
- FA-API-004: Rate Limiting für externe Kursabfragen (AlphaVantage) + Caching.

## 6. Nicht-funktionale Anforderungen
- NFA-PERF-001: Import von 1.000 Buchungszeilen < 10 Sekunden (Zielwert; Messpunkt nach Implementierung validieren).
- NFA-PERF-002: Dashboard-KPIs laden < 2 Sekunden bei Datenbestand < 50k Posten.
- NFA-SEC-001: Geschützte Endpunkte nur für authentifizierten Benutzer.
- NFA-SEC-002: Schutz vor mehrfacher doppelter Verbuchung (Idempotenz-Strategie mit Hash).
- NFA-REL-001: Fehler beim Kursabruf dürfen Hauptfunktionen nicht blockieren (resiliente Retries / Circuit Breaker).
- NFA-USAB-001: Responsive UI (Desktop, iPhone Formfaktor).
- NFA-ARCH-001: Trennung von Domänenlogik (Shared) und Präsentation (Blazor / MAUI).
- NFA-LOG-001: Zentrales Logging (min. INFO, separate Kategorie für Kursabrufe & Import).
- NFA-I18N-001: Primärsprache Deutsch; spätere Erweiterbarkeit für Mehrsprachigkeit vorgesehen.
- NFA-DATA-001: Zeitreihen (Kurse) werden effizient gespeichert (Index auf Symbol+Datum).
- NFA-PRIV-001: Lokale Speicherung personenbezogener Daten; keine Weitergabe an Dritte (außer Kursdienst).

## 7. Datenobjekte (hochlevelig)
- Konto: Id, Typ, BankkontaktId, IBAN, Bezeichnung, Saldo
- Kontoauszugseintrag (temporär): ImportId, KontoId, Buchungsdatum, Betrag, Betreff, RohdatenHash, KontaktId?, SparplanId?, WertpapierTransaktionsdaten?
- Posten (Bank/Kontakt): Id, KontoId/KontaktId, Betrag, Datum, ReferenzQuelle
- Sparplan: Id, Typ, Zielbetrag?, Zieldatum?, Intervall?, AktivFlag, ArchivDatum?
- Sparplanposten: Id, SparplanId, Datum, Betrag, Status
- Wertpapier: Id, Symbol/ISIN, Typ, Name
- Wertpapiertransaktion: Id, WertpapierId, Typ, Datum, Menge, Preis, Gebühren, Steuern, KontoId
- Kursposten: Id, WertpapierId, Datum, Schlusskurs, Quelle
- Kontakt: Id, Name, KategorieId?, Typ (Bank, Person, Selbst, Sonstige)
- Aliasname: Id, KontaktId, Pattern

(Detailattribute & Beziehungen sind weiter zu verfeinern.)

## 8. Geschäftsregeln (Auszüge)
- GR-001: Duplikatserkennung basiert auf (KontoId + Buchungsdatum + Betrag + normalisierter Betreff) oder Hash der Originalzeile.
- GR-002: Kostenneutral wenn Quelle- und Zielkontakt eigene Konten (Transfer) – Betrag beeinflusst nicht Ergebnisübersicht.
- GR-003: Sparziel erreicht wenn kumulierte Sparplanposten >= Zielbetrag.
- GR-004: Prognose für Zielverfehlung nutzt lineare Extrapolation der letzten n Buchungen (n zu definieren, Default 3-6).
- GR-005: Kursimport unterbricht nach Erreichen Rate Limit und setzt Sperrmarke bis Tageswechsel (UTC?).

## 9. UI-Anforderungen (Auszüge)
- Symbole/Badges für Sparziel-Status (erreicht, nahe, verfehlt Prognose) in Kontoauszugübersicht & Detailkarten.
- Umschaltbare Tabs/Filter für aktive vs. archivierte Sparpläne.
- Pull-Down (oder Button) zur Einblendung Suchfeld auf Listen.

## 10. Fehler- & Ausnahmebehandlung
- Importfehler: Protokollierung je Zeile, Fortsetzung übriger Zeilen.
- Kursabruf Timeout: Retry mit Exponential Backoff bis Limit erreicht.

## 11. Abgrenzung
- Kein Multi-User / Mandantenbetrieb (Annahme: Einzelbenutzer). (Zu bestätigen)
- Keine automatische Bank-Schnittstelle (z.B. HBCI) – nur Dateiimport.

## 12. Offene Punkte / Klärungsbedarf
- OP-001: Exakte unterstützte Kontoauszugsdateiformate (CSV, MT940, CAMT.053?).
- OP-002: Namenskonvention der Projekte: FinanceManager vs. Finanzverwaltung (vereinheitlichen?).
- OP-003: Authentifizierungsmechanismus (Identity, OAuth, Lokal?).
- OP-004: Kategorienstruktur (Hierarchie erlaubt?).
- OP-005: Performanceziele validieren / anpassen nach erstem Datenvolumentest.
- OP-006: Wie viele vergangene Jahre für Kurshistorie nachladen? Begrenzung definieren.
- OP-007: Prognoseverfahren für Sparziele (Algorithmus finalisieren).

## 13. Priorisierung (grobe Wellen)
Welle 1: Konten, Kontakte, Import, Buchung, Alias, Basis-Dashboard.
Welle 2: Sparpläne + Statuslogik, Erweiterte Auswertungen.
Welle 3: Wertpapiere + Kursdienst + Renditen.
Welle 4: Feinschliff KPIs, Prognosen, Optimierungen.

## 14. Akzeptanzkriterien (Beispiele)
- AK-IMP-001: Import einer Datei mit 100 Buchungen erzeugt 100 temporäre Einträge, Duplikate (simuliert) werden nicht erneut erzeugt.
- AK-SPAR-001: Erreichen eines Zielbetrags markiert Sparplan als Erreicht und zeigt Symbol in nächstem Seitenreload.
- AK-WERT-001: Kaufbuchung erzeugt Wertpapierposten und aktualisiert Bestand korrekt.

## 15. Sicherheit & Datenschutz (Erweiterung)
- Lokale Verschlüsselung sensibler Konfigurationsschlüssel (API Key AlphaVantage).
- Keine Übertragung persönlicher Kontaktstammdaten an Dritte.

## 16. Tracking & Versionierung
Änderungen an Anforderungen werden versioniert (History Abschnitt ergänzen bei Updates).

---
Status: Entwurf – zur Review.
