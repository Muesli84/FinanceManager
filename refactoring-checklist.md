# 🧹 Refactoring-Checkliste

Diese Checkliste dient als Leitfaden für alle Refactoring-Maßnahmen nach Änderungen durch Agenten oder automatisierte Prozesse. Ziel ist es, die Codequalität, Lesbarkeit und Wartbarkeit sicherzustellen.

---

## 🔧 1. Code-Struktur

- [ ] Funktionen logisch gruppiert?
- [ ] Einhaltung des Single-Responsibility-Prinzips?
- [ ] Können Funktionen in separate Services oder Module ausgelagert werden?
- [ ] Sind Klassen und Komponenten klar voneinander abgegrenzt?

---

## 📚 2. Lesbarkeit & Verständlichkeit

- [ ] Sprechende Namen für Variablen, Funktionen und Klassen?
- [ ] Kommentare aktuell und sinnvoll?
- [ ] Vermeidung von überflüssigen oder veralteten TODOs?
- [ ] Komplexität pro Methode angemessen (z. B. max. 20 Zeilen)?

---

## 🧼 3. Sauberkeit & Wiederverwendbarkeit

- [ ] Duplizierter Code entfernt?
- [ ] Wiederverwendbare Funktionen ausgelagert?
- [ ] Magic Numbers durch Konstanten ersetzt?
- [ ] Unnötige Abhängigkeiten entfernt?

---

## 🧪 4. Tests

- [ ] Neue Tests für geänderte Funktionen vorhanden?
- [ ] Bestehende Tests erfolgreich ausgeführt?
- [ ] Testabdeckung geprüft?
- [ ] Edge Cases berücksichtigt?

---

## 📄 5. Dokumentation

- [ ] README oder API-Dokumentation aktualisiert?
- [ ] Neue Funktionen dokumentiert?
- [ ] Changelog ergänzt?
- [ ] Hinweise zur Nutzung oder Konfiguration aktualisiert?

---

## 🧠 6. Optional: Architektur & Performance

- [ ] Gibt es Hinweise auf technische Schulden?
- [ ] Performance-Engpässe identifiziert?
- [ ] Skalierbarkeit berücksichtigt?
- [ ] Logging und Monitoring angepasst?

---

> **Hinweis:** Diese Checkliste ist Teil des Standardprozesses und sollte bei jedem Review berücksichtigt werden.
