# Release Notes

## Unreleased

- Die Bankübersicht zeigt nun Gesamtbestand, Jahres-/Monatsveränderung und Verteilungen nach Kontotyp sowie Bankkontakt; Suche, leere Ansichten und Ladefehler bleiben konsistent mit der Kontoliste.
- Startseiten-KPIs können in den Profileinstellungen optional im Browser-LocalStorage zwischengespeichert werden; gespeicherte Werte werden sofort angezeigt und im Hintergrund aktualisiert.
- Aktive Navigation, Benutzerinteraktion und Kontoauszugs-Schnellbearbeitung halten die Anmeldung nun im Hintergrund per Keepalive aktiv.
- Beim Verlassen eines QuickEdit-Eingabefelds wird ein gedrosselter Server-Ping ausgelöst, ohne lokale Eingaben zu verlieren oder die Seite neu zu laden.
- Schnellbearbeitung von Kontoauszugsentwürfen: Speichern-Aktivierung, Zeilenvalidierung und Valuta-Übernahme erfolgen nun konsistent; leere/unvollständige Jahreszahlen führen nicht mehr zu fehlerhaften Valuta-Datumswerten.
- Nicht erneuerbare Sessions bleiben kontrolliert: Keepalive-Fehler lösen keinen Redirect-Sturm aus; geschützte Aktionen führen wie bisher einmalig zum Login mit Return-URL.
- `msTools.Updater` wurde auf `0.10.0-rc.1` aktualisiert; die alte `0.8.0-rc.1`-Datei wurde ersetzt.
