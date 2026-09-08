Fachliche Zusammenfassung

Der erste Start der Anwendung ohne vorhandenes Benutzerkonto führt zwingend in den Registrierungsfluss. Wenn noch kein Benutzer registriert ist, wird der Anwender auf die Registrierungsseite weitergeleitet. Auf dieser Seite muss eine Checkbox mit der Bezeichnung "Demodaten angelegen" vorhanden sein. Wenn der erste Anwender registriert wird und die Checkbox aktiviert ist, startet ein Hintergrundprozess, der für diesen Benutzer die vollständigen Demodaten in der Anwendung anlegt.

Der Hintergrundprozess muss die vorhandenen Business-Services verwenden, damit die Daten exakt so entstehen, wie sie ein realer Anwender im Produkt selbst angelegt hätte. Als Datumsbasis gilt der erste Tag des aktuellen Monats. Dabei werden Kontakte, Bankkonten, Sparpläne, Budgets, Wertpapiere, Kursdaten und Kontoauszüge mit Buchungen für die letzten 24 Monate inklusive des aktuellen Monats angelegt; Auszüge des aktuellen Monats dürfen dabei nicht gebucht werden. Wenn die Aufgabe länger dauert und der Anwender nach dem Login bereits auf der Startseite ist, muss dort der Fortschritt der Hintergrundaufgabe im regulären Fortschritts-/Hintergrundprozess-Mechanismus angezeigt werden.

Betroffene Klassen und Komponenten

- `IDemoDataService` und die neue Implementierung für die Demodatenerzeugung
- First-User-Check im Start-/Registrierungsfluss
- Registrierungsseite mit Checkbox "Demodaten angelegen"
- Startseite / Dashboard mit Fortschrittsanzeige für laufende Hintergrundaufgaben
- Hintergrund-Job-Orchestrierung einschließlich Status-/Fortschrittsmodell
- Services für Kontakte, Bankkonten, Gruppen, Sparpläne, Budgets, Wertpapiere, Kursimport und Kontoauszüge/Buchungen
- Importmechanismus für Kursdaten, der die bestehende Importfunktion verwendet
- Persistenz- und Prüflogik für Benutzerregistrierung, Erstbenutzer-Identifikation und Job-Status

Implementierungsansatz

1. Registrierungs- und Erstbenutzer-Flow
   - Wenn die Anwendung ohne registrierten Benutzer gestartet wird, muss der Zugriff auf die normale Startseite unterbunden und auf die Registrierungsseite umgeleitet werden.
   - Auf der Registrierungsseite muss die Checkbox "Demodaten angelegen" mit einem klaren Label sichtbar sein.
   - Wenn der erste Benutzer registriert wird und die Checkbox aktiviert ist, muss ein asynchroner Hintergrundjob gestartet werden.
   - Der Job darf nur für die erste Benutzerregistrierung ausgelöst werden; spätere Registrierungen sind nicht betroffen.

2. Datenerzeugung für den Anwender
   - Alle Datumswerte beziehen sich auf den ersten Tag des aktuellen Monats.
   - Bankkontakte und Bankkonten:
     - Bankkontakt mit Girokonto und Sparkonto
     - Bankkontakt mit zusätzlichem Sparkonto
   - Kontakte und Gruppen:
     - Arbeitgeber GmbH, Gruppe "Arbeit"
     - Zentral Versicherung, Gruppe "Versicherungen"
     - SDAC, Gruppe "Versicherungen"
     - Sabbel Lüchtenhausen, Gruppe "Dienstleister", Art = Person
     - Adli, Gruppe "Supermärkte & Einzelhandel"
     - Didl, Gruppe "Supermärkte & Einzelhandel"
     - Adeka, Gruppe "Supermärkte & Einzelhandel"
     - Bäckerei Kramphove, Gruppe "Bäckereien & Cafés"
     - Bäckerei Feiping, Gruppe "Bäckereien & Cafés"
     - Bäckerei Schlonz, Gruppe "Bäckereien & Cafés"
   - Sparpläne:
     - "SDAC Gebühr": Zielbetrag 99 €, Zieldatum nächster 01. Januar, jährlich wiederkehrend, Kategorie "Wiederkehrende Ausgaben", zufällige Vertragsnummer
     - "Hausratversicherung": Zielbetrag 62,60 €, Zieldatum nächster 01. Dezember, jährlich wiederkehrend, Kategorie "Wiederkehrende Ausgaben", zufällige Vertragsnummer
     - "Auto": Zielbetrag 14000 €, Kategorie "Anlage", Zieldatum 06.07. in 10 Jahren, Typ = Einmalig
     - "Urlaub": ohne Zielbetrag, Typ = Unbefristet
   - Budgets:
     - "Gehalt" für Arbeitgeber GmbH, monatliche Regel 3642,50 €, Kategorie "Arbeit"
     - "Rückstellung Hausratversicherung" für Self-Kontakt, zwei Regeln: monatlich -5,22 € und jährlich im Dezember +62,64 €, Kategorie "Versicherungen"
     - "Hausratversicherung" für Zentral Versicherung, jährliche Regel -62,60 € im Dezember, Kategorie "Versicherungen"
     - "Rückstellung SDAC" für Self-Kontakt, zwei Regeln: monatlich -8,25 € und jährlich im Januar +99 €, Kategorie "Versicherungen"
     - "SDAC" für Kontakt SDAC, jährliche Regel im Januar -99 €, Kategorie "Versicherungen"
     - "Wohnungsmiete" für Sabbel Lüchtenhausen, monatlich -845 €, Kategorie "Wohnen"
     - "Supermärkte & Einzelhandel" mit Budgetwertungsart "Gesamtbudget" und ohne Regel, Kategorie "Einkaufen & Verpflegung"
     - "Bäckereien & Cafés" mit Budgetwertungsart "Gesamtbudget" und ohne Regel, Kategorie "Einkaufen & Verpflegung"
     - Budgetregel für Kategorie "Einkaufen & Verpflegung", monatlich -300 €
   - Wertpapiere und Kurse:
     - "USHSIV-MSCI WLD", Kennung "LU00ABACAD96", AlphaVantage = "", Währung = "EUR", Kategorie = "ETF", Beschreibung = "UShares MSCI World ETF", Region = "Global", Sektor = "MSCI World"
     - "Inländische Post AG", Kennung = "DE0001112026", Währung = "EUR", Kategorie = "Aktien", Region = "DE", Sektor = "Logistik"
     - Für beide Wertpapiere müssen für alle Werktage der letzten zwei Jahre Kurse angelegt werden, beginnend bei 11,36 bzw. 44,25, mit Änderungen zwischen -0,5 % und +2 %; der Import erfolgt über die vorhandene Importfunktion.

3. Buchungslogik über 24 Monate
   - Die Anlage muss rückwirkend für die letzten 24 Monate einschließlich des aktuellen Monats erfolgen, aber die Auszüge des aktuellen Monats dürfen nicht gebucht werden.
   - Für jede Monats-/Jahresperiode müssen die Buchungen über den regulären Weg angelegt werden, damit sie identisch zu manuell erfassten Daten wirken.
   - Spezifische Buchungen:
     - Monatlich zum letzten Werktag des Monats 3642,50 € Gehalt von "Arbeitgeber GmbH" auf das Girokonto; nur im aktuellen Monat, wenn der letzte Werktag bereits erreicht ist.
     - Monatlich 5,22 € für den Self-Kontakt als Sparbetrag mit Verwendungszweck "Rückstellung Hausratversicherung"; negativ auf dem Girokonto, positiv auf dem Sparkonto derselben Bank; Zuordnung zum Sparplan "Hausratversicherung".
     - Jährlich am ersten Werktag im Dezember 62,64 € für den Self-Kontakt als Auflösung der Rückstellung mit Verwendungszweck "Auflösung Rückstellung Hausratversicherung"; positiv auf dem Girokonto, negativ auf dem Sparkonto derselben Bank; Zuordnung zum Sparplan "Hausratversicherung".
     - Jährlich am ersten Werktag nach dem 15. Dezember 62,60 € an Kontakt "Zentral Versicherung" als Lastschrift des Versicherungsbeitrags mit Verwendungszweck "Beitrag Hausratversicherung {Jahr}, Vertragsnummer {Nummer}"; Platzhalter entsprechend ersetzen.
     - Monatlich 50,00 € für den Self-Kontakt als Sparbetrag mit Verwendungszweck "Sparplan Urlaub"; negativ auf dem Girokonto, positiv auf dem Sparkonto der zweiten Bank; Zuordnung zum Sparplan "Urlaub".
     - Monatlich 8,25 € für den Self-Kontakt als Sparbetrag mit Verwendungszweck "Rückstellung SDAC Jahresgebühr"; negativ auf dem Girokonto, positiv auf dem Sparkonto derselben Bank; Zuordnung zum Sparplan "SDAC".
     - Jährlich am ersten Werktag im Januar 99,00 € für den Self-Kontakt als Auflösung der Rückstellung mit Verwendungszweck "Auflösung Rückstellung SDAC Jahresgebühr"; positiv auf dem Girokonto, negativ auf dem Sparkonto derselben Bank; Zuordnung zum Sparplan "SDAC".
     - Monatlich am ersten Werktag des Monats Überweisung der Wohnungsmiete 845,00 € an "Sabbel Lüchtenhausen".
     - Für die drei Supermärkte und drei Bäckereien: in jedem Monat wöchentlich ein bis zwei zufällige Kartenzahlungen mit einem Betrag zwischen 10 € und 30 €; negativ auf dem Girokonto; jeweils pro Laden/Restaurant 1–2 Zahlungen pro Woche.
     - Ein Wertpapierkauf für "USHSIV-MSCI WLD" über 2000 € mit dem ersten Kontoauszug des Girokontos; Empfänger = "", Kontakt ist die Bank.
     - In jedem dritten Kontoauszug des Girokontos eine Dividendenzahlung für "USHSIV-MSCI WLD" über einen zufälligen Betrag zwischen 15 € und 30 € abzüglich 25 % Steuer.
     - Ein Wertpapierkauf für "Inländische Post AG" über 62 Stück mal aktueller Kurswert mit dem sechsten Kontoauszug des Girokontos; Empfänger = "", Kontakt ist die Bank.
     - In jedem Kontoauszug des Girokontos im Mai, beginnend ab dem Kauf des Wertpapiers, eine Dividendenzahlung für "Inländische Post AG" über 4 % des aktuellen Werts, abzüglich 25 % Steuer.

4. Fortschrittsanzeige im UI
   - Wenn die Hintergrundaufgabe länger als der Login-/Startseiten-Flow dauert, muss die Startseite den laufenden Hintergrundprozess identifizieren und den Fortschritt in der bestehenden allgemeinen Fortschrittsanzeige anzeigen.
   - Das Verhalten muss mit anderen, bereits implementierten Hintergrundprozessen konsistent sein.
   - Die Anzeige muss unabhängig von der konkreten Datenquelle für den Anwender sichtbar und nachvollziehbar sein.

Konfiguration

- Die Checkbox auf der Registrierungsseite ist nur für den ersten Benutzer relevant und wird standardmäßig nicht aktiviert.
- Es ist keine zusätzliche globale Feature-Konfiguration erforderlich; das Verhalten hängt direkt am Erstregistrierungsflow.
- Mit aktivierter Checkbox startet der Demodaten-Job automatisch für den ersten registrierten Benutzer.

Offene Fragen

- Ist die Checkbox auf der Registrierungsseite nur für den ersten Benutzer relevant oder soll sie auch bei späteren Registrierungen sichtbar und nutzbar sein?
- Gibt es bereits ein generisches Modell für Fortschrittszustände und Hintergrundjobs, das für diese Aufgabe unverändert verwendet werden kann?
- Muss die Reihenfolge der erzeugten Datensätze und Buchungen deterministisch sein, damit sie in Tests reproduzierbar ist?
- Wie genau sollen die Zufallswerte für Kartenzahlungen, Vertragsnummern und Dividendenauszahlungen in Tests abgesichert werden, damit sie deterministisch und trotzdem realistisch sind?
- Ist das Verhalten für die aktuelle Monatsgrenze in Bezug auf den letzten Werktag bereits durch bestehende Kalender-/Datumshilfen definiert oder muss es in der Demo-Data-Logik selbst implementiert werden?
