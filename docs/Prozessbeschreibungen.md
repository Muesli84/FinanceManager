# Prozessablaufbeschreibungen (FinanceManager) (Entwurf)

Dieses Dokument beschreibt die wichtigsten im Programm implementierten Abläufe. Die Schritte sind jeweils in Anwenderaktionen und automatisierte Systemschritte unterteilt. Informationen, die nur das Verständnis der Stammdatenpflege unterstützen, sind als solche gekennzeichnet.

!!! Dieses Dokument muss vervollständigt werden !!!

---

## 1. Benutzeranmeldung

**Anwenderaktionen:**
- Anmeldung mit Benutzername und Passwort.

**Automatisierte Prozessschritte:**
- Prüfung der Zugangsdaten und Erzeugung eines JWT-Tokens.
- Speicherung des Tokens im Cookie.
- Prüfung der Authentifizierung bei jedem API-Aufruf.
- Zeitlich befristete Sperrung des Benutzerkontos bei mehrfachen Fehlversuchen.

---

## 2. Bankkontoverwaltung

**Anwenderaktionen:**
- Anlegen, Bearbeiten und Löschen von Bankkonten.
- Auswahl eines Bankkontos für einen Kontoauszug.

**Information:**  
Bankkonten sind mit einem Bankkontakt verknüpft. Diese Verknüpfung wird bei der automatischen Kontierung verwendet.

---

## 3. Kontaktverwaltung (Stammdatenpflege)

**Anwenderaktionen:**
- Anlegen, Bearbeiten und Löschen von Kontakten.
- Pflegen alternativer Namen (Aliase) für Kontakte.
- Kennzeichnen von Kontakten als Zahlungsvermittler, Bank oder „Self“.

**Information:**  
Die gepflegten Aliase und Kontaktmerkmale werden im Kontoauszugsprozess für die automatische Kontierung und Zuordnung verwendet.

---

## 4. Verbuchung eines Kontoauszugs

**Anwenderaktionen:**
- Auswahl der Funktion „Kontoauszug importieren“.
- Hochladen einer Datei (z.B. CSV, PDF).

**Automatisierte Prozessschritte:**
- Erkennung des Dateiformats und Auswahl des passenden Import-Parsers.
- Analyse der Datei und Extraktion der Buchungen.
- Anlegen eines Statement-Drafts und Speichern der Buchungen als Entwürfe.
- Automatische Kontierung:
    - Zuordnung des Kontoauszugs zu einem Bankkonto anhand der IBAN.
    - Zuordnung jeder Buchung zu einem Kontakt:
        - Erkennung über Empfängernamen, Kontaktname und Aliase (inkl. Platzhalter * und ?).
        - Fallback: Kein Empfänger → Bankkontakt des Kontoauszugs.
        - Fallback: Empfänger ist Bank eines eigenen, aber vom Kontoauszug abweichenden Kontos → Anwender („Self“-Kontakt).
        - Zahlungsvermittler: Kontakt wird erkannt, dann wird anhand des Verwendungszwecks (Betreff) ein weiterer Kontakt gesucht.
    - Duplikatprüfung: Bereits gebuchte Einträge werden erkannt und markiert.
    - Status-Setzung: „Announced“, „AlreadyBooked“, „Open“.
    - Markierung als „kostenneutral“, wenn der Empfänger der Anwender ist.

**Anwenderaktionen:**
- Übersicht der importierten Einträge prüfen.
- Einzelne Buchungen bearbeiten, Kontakt zuweisen oder ändern.
- Ergänzen oder Korrigieren von Buchungsdetails.

**Automatisierte Prozessschritte:**
- Nach jeder Änderung (z.B. manuelle Kontaktzuweisung) erneute automatische Kontierung und Statusaktualisierung.

**Anwenderaktionen:**
- Übernahme des Kontoauszugs (Commit).

**Automatisierte Prozessschritte:**
- Prüfung, ob alle Einträge korrekt zugeordnet und klassifiziert sind.
- Übernahme der Einträge als bestätigter Kontoauszug (Commit).
- (ToDo) Erstellung von Kontaktposten für die Buchung auf den Kontakten.
- (ToDo) Erstellung von Bankposten für die Buchung auf dem Bankkonto.

**Information:**  
Die Navigation zwischen Einträgen (z.B. zum nächsten offenen Eintrag) und die Anzeige der Kontaktdetails sind unterstützende Funktionen für die Nachbearbeitung.

---

*Letzte Aktualisierung: 03.09.2025*