# Anforderung: GitHooks übernehmen

Quelle: `issue.md` (Aufgaben-ID 75c640ee-07ad-454a-bfc7-a043985ed0b8)
Referenz-Repository: https://github.com/martin-stromberg/Pattern-Collection.git

## Beschreibung

1. Die im Repository `Pattern-Collection` unter `Git-Hooks/githooks/` enthaltenen Git-Hooks und zugehörigen Prüfskripte sollen in dieses Repository übernommen werden.
2. Eventuell bereits vorhandene alte Versionen der Hooks (Verzeichnis `githooks/`) sollen durch die aktuellen Versionen ersetzt werden.
3. Beim Commit oder Push können sich durch die neuen Prüfungen Fehlermeldungen ergeben. Diese Fehler sollen in der Anwendung korrigiert werden, sodass die Bedingungen der Prüfungen erfüllt sind.
4. Die Prüfungen dürfen **auf keinen Fall entschärft** werden – die Korrekturen erfolgen ausschließlich am Anwendungscode.
5. Bei sehr großer Fehlerliste sollen die Fehler thematisch aufgeteilt und systematisch abgearbeitet werden.

## Akzeptanzkriterien

- Aktuelle Hook-Dateien aus `Pattern-Collection` (`pre-commit`, `pre-push`, `translation-check.py`, `csproj-xmldoc-check.py`, `razor-l10n-check.py`, `razor-usage-check.py`, `no-notimplemented-check.py`, `enum-coverage-check.py`, `install-hooks.cmd`, `install-hooks.sh`) liegen im Repository vor.
- Alte Hook-Versionen (`githooks/`) sind ersetzt.
- Die Hook-Prüfungen laufen fehlerfrei bzw. alle gemeldeten Fehler sind im Anwendungscode behoben.
- Die Prüfskripte selbst wurden nicht abgeschwächt.
