# CI/CD und Branch-Strategie

Übersicht über die Continuous-Integration und Continuous-Deployment Pipeline sowie die Git-Strategie für dieses Projekt.

## Branch-Strategie

Das Projekt folgt einer zweistufigen Branch-Strategie mit `staging` als Integrations- und Qualitätssicherungsbranch sowie `master` als ausschließlichem Release-Branch.

```
Feature-Branch
    ↓
    ├→ Pull Request gegen staging
         ↓
         ├→ test.yml (Tests, Coverage, Build)
         ├→ Branch-Protection-Rules prüfen (1 Approval erforderlich)
         ↓
         ├→ Merge zu staging
              ↓
              ├→ test.yml (Push-Event auf staging)
              ├→ staging-to-master.yml (automatisierte Promotion)
                   ↓
                   ├→ Prüfung: Liegt master hinter staging?
                   ├→ Falls ja: Erstelle Draft-PR staging → master
                        ↓
                        ├→ Maintainer Review + Merge
                             ↓
                             ├→ release.yml (Push-Event auf master)
                                  ↓
                                  ├→ Semantic Release (Version-Bump)
                                  ├→ ZIP-Artefakt erstellen
                                  ├→ GitHub Release veröffentlichen
                             ├→ sync-staging-with-master.yml (Push-Event auf master)
                                  ↓
                                  ├→ Automatischer PR master → staging
                                  ├→ Maintainer Review + Merge ("Create a merge commit")
                                  ├→ Damit bleibt der neue Release-Tag für künftige
                                     RC-Berechnungen auf staging erreichbar
```

## Workflows

### 1. test.yml — Qualitätssicherung (Tests, Coverage, Build)

**Auslöser:**
- `push` zu `staging` oder `master`
- `pull_request` gegen `staging` oder `master`

**Schritte:**
1. Checkout des Branches (Windows-latest)
2. .NET 10 SDK Setup
3. npm Abhängigkeiten installieren
4. Playwright Browser installieren
5. `dotnet restore` und Dependency-Restore
6. Build der Test-Projekte (Debug)
7. **Unit-Tests ausführen** (`FinanceManager.Tests`, XPlat Code Coverage sammeln)
8. **Integrationstests ausführen** (`FinanceManager.Tests.Integration`, XPlat Code Coverage)
9. **E2E-Tests ausführen** (`FinanceManager.Tests.E2E`, continue-on-error)
10. Coverage-Report mit `reportgenerator` generieren
11. **Coverage-Schwellwert erzwingen** (Line Coverage ≥ 70%)
12. Artefakte hochladen (Coverage-Report, Test-Ergebnisse)

**Fehlschlag bei:**
- Build fehlgeschlagen
- Unit- oder Integrationstests fehlgeschlagen
- Coverage unter 70%
- E2E-Tests fehlgeschlagen (aber verzeiht, blockiert nicht)

**Erforderliche Approvals (via Branch-Protection):**
- Mindestens 1 Approval erforderlich

### 2. staging-to-master.yml — Automatisierte Promotion

**Auslöser:**
- Erfolgreicher Abschluss von `test.yml` auf `staging` (workflow_run-Event)

**Schritte:**
1. Checkout von staging (shallow clone, ~13 min nach test.yml-Ende)
2. `git fetch origin master`
3. **Differenzprüfung:** Anzahl Commits berechnen, um die `master` hinter `staging` zurückliegt
4. Falls Commits vorhanden:
   - Stelle sicher, dass Label `automated-promotion` existiert (erstelle es, wenn nicht)
   - Prüfe auf offene PRs von `staging` → `master`
   - Falls keine offen: Erstelle Draft-PR mit Label `automated-promotion`
   - Draft-PR benötigt manuelles Review und Merge durch Maintainer
   - Branch-Protection-Rules auf `master` erzwingen mindestens 1 Approval
5. Falls keine Commits vorhanden: Keine Aktion notwendig (staging == master)

**Fehlschlag bei:**
- Datenextraktion aus test.yml fehlgeschlagen
- Label-Erstellung fehlgeschlagen
- PR-Erstellung fehlgeschlagen

### 3. release.yml — Versionsverwaltung und Publikation

**Auslöser:**
- `push` zu `master` oder `staging`
- `push` eines Tags im Format `vX.Y.Z`

**Schritte:**
1. Checkout, .NET 10 + Node 22 Setup
2. npm + .NET Restore
3. Unit- und Integrationstests (als Release-Gate)
4. Build und Publish (self-contained `win-x64` und `linux-x64`)
5. Semantic Release (Version-Bump aus Conventional Commits)
6. ZIP-Artefakte erstellen
7. `update.json` mit Metadaten (Platform, URL, SHA-256) generieren
8. GitHub Release veröffentlichen

**Nicht blockiert durch:**
- E2E-Test-Fehler (continue-on-error)
- Manuelle Tags (haben Vorrang vor Auto-Versioning)

**RC-Versionierung auf `staging`:**

`staging` ist in `release.config.js` als Semantic-Release-Prerelease-Branch mit Identifier `RC` konfiguriert (`{ name: "staging", prerelease: "RC" }`). Jeder Push nach `staging` löst denselben release.yml-Ablauf wie `master` aus, erzeugt aber ein GitHub-Release mit `prerelease: true` statt eines stabilen Releases:

- Die Zielversion (`X.Y.Z`) wird wie auf `master` aus allen Conventional Commits seit dem letzten stabilen Tag berechnet (höchster Schweregrad gewinnt: `feat` → minor, `fix` → patch, `breaking` → major).
- Solange sich diese Zielversion nicht ändert, wird nur der RC-Zähler erhöht: `1.16.1-RC.1` → `1.16.1-RC.2` → …
- Ändert sich die Zielversion (z. B. weil nach reinen Fixes nun ein `feat`-Commit dazukommt), springt die Version auf die neue Ziffer und der RC-Zähler startet bei 1: `1.16.1-RC.3` → `1.17.0-RC.1`.
- Beim Merge nach `master` entfällt das `-RC.N`-Suffix; die zuletzt berechnete Version wird zum finalen, stabilen Release (z. B. `1.17.0-RC.4` → `1.17.0`).
- Da Squash-Merge im Repository deaktiviert ist, bleiben alle einzelnen Commit-Typen beim Promotion-Merge `staging → master` erhalten — die auf `master` berechnete Version stimmt daher exakt mit der zuletzt erreichten RC-Zielversion überein.
- Beide Branches teilen sich dieselbe repository-weite Concurrency-Gruppe (`release-${{ github.repository }}`), damit parallele Versionsberechnungen auf `master` und `staging` nicht dieselbe Tag-Historie gleichzeitig verändern.

### 4. sync-staging-with-master.yml — Rücksynchronisation nach master

**Auslöser:**
- `push` zu `master`

**Hintergrund:** Jede Promotion `staging → master` erzeugt einen Merge-Commit, der ausschließlich auf `master` existiert (Standard-Merge-Verhalten von GitHub-PRs). Ohne Rücksynchronisation ist der dort gesetzte Release-Tag (z. B. `v1.16.0`) aus Sicht von `staging` nicht erreichbar — Semantic Release würde die nächste RC-Version dann fälschlich auf Basis eines älteren, noch erreichbaren Tags berechnen (zu niedrige Zielversion, siehe Fehlersuche unten).

**Schritte:**
1. Checkout von `staging`
2. Prüfen, ob `master` bereits vollständig in `staging` enthalten ist (nichts zu tun, falls ja)
3. Automatischen PR `master → staging` erstellen (analog zu `staging-to-master.yml`, Label `automated-sync`), sofern noch keiner offen ist

**Wichtig beim Mergen:** Dieser PR muss mit **"Create a merge commit"** gemergt werden, nicht mit "Rebase and merge". Die Promotion-Merge-Commits auf `master` sind inhaltlich leer (reine History-Knoten); ein Rebase verwirft sie beim Replay, wodurch `staging` trotz "erfolgreichem" Merge weiterhin hinter `master` zurückbleibt.

**Warum PR-basiert statt direktem Push:** Klassische Branch-Protection-Rules unterstützen keinen granularen Bypass für einzelne Actors (nur den globalen "Include administrators"-Schalter) — ein Bot-Push würde an "Require a pull request before merging" scheitern. Der PR-basierte Ansatz braucht keine Sonderrechte.

## Quality Gates

### Coverage-Schwellwert (test.yml)

- **Mindestanforderung:** 70% Line Coverage
- **Gemessen auf:** `FinanceManager.Tests` und `FinanceManager.Tests.Integration`
- **Methode:** `dotnet test --collect:"XPlat Code Coverage"` + `reportgenerator`
- **Blockiert Merge bei Unterschreitung**

### Dependency-Updates (dependabot.yml)

- **Automatische Updates für:** NuGet, npm, GitHub Actions
- **Erstellt automatische PRs** gegen `staging` und `master`
- **Quality Gates:** Abhängigkeits-PRs müssen alle bestehenden Checks (test.yml) bestehen

### Branch-Protection-Rules

**Für `staging`:**
- Status-Checks erforderlich (test.yml muss erfolgreich sein)
- Mindestens 1 Approval erforderlich
- Stale Approvals nach neuen Commits invalidieren
- Direct-Push blockieren (nur PRs erlaubt)

**Für `master`:**
- Status-Checks erforderlich (test.yml muss erfolgreich sein)
- Mindestens 1 Approval erforderlich
- Branch up-to-date sein vor Merge
- Direct-Push blockieren
- **Strict:** Nur PRs aus `staging` erlaubt (keine anderen Quell-Branches)

## Typsicher Prozesse

### Feature-Entwicklung

1. **Branch erstellen:** `git checkout -b feature/my-feature staging`
2. **Lokale Entwicklung und Tests**
3. **Commit mit Conventional Commits:** `feat(component): description`
4. **Push:** `git push origin feature/my-feature`
5. **PR erstellen:** Gegen `staging`, nicht `master`
6. **GitHub Checks:**
   - test.yml läuft automatisch
   - Mindestens 1 Approval erforderlich
   - Alle Checks müssen grün sein
7. **Merge:** "Create a merge commit" oder "Rebase and merge" (Squash-Merge ist im Repository deaktiviert, damit Conventional-Commit-Typen für Semantic Release erhalten bleiben)
8. **RC-Release:** Der Merge nach `staging` löst release.yml aus und erzeugt/erhöht die RC-Version (z. B. `1.16.1-RC.1`)
9. **Automatische Promotion:** Nach dem Merge zu `staging` prüft `staging-to-master.yml`, ob ein PR zu `master` nötig ist

### Hotfix-Prozess

1. **Branch aus `master`:** `git checkout -b hotfix/issue-description master`
2. **Hotfix implementieren**
3. **PR gegen `staging` erstellen** (nicht direkt gegen `master`)
4. **Gleicher Test- und Review-Prozess wie Features**
5. **Merge zu `staging`**
6. **Automatische Promotion zu `master`** erfolgt wie üblich
7. **Release** über standard release.yml-Prozess

### Release-Prozess

1. **Automatisch:** Merge zu `staging` oder `master` löst release.yml aus
2. **Versionierung:** Semantic Release aus Conventional Commits; auf `staging` als RC-Prerelease (`X.Y.Z-RC.N`), auf `master` als finales, stabiles Release
3. **Artefakte:** ZIP-Pakete für Windows und Linux werden erstellt (für RC-Releases identisch zum stabilen Release-Prozess)
4. **GitHub Release:** Automatisch veröffentlicht mit Update-Manifest; RC-Releases werden dabei als `prerelease: true` markiert

## Fehlersuche

### PR gegen staging hängt fest / Checks laufen nicht

**Ursachen:**
- `.github/workflows/test.yml` ist nicht erreichbar
- Workflow hat Syntax-Fehler (YAML-Indentation)

**Lösung:**
1. Prüfe Workflow-Syntax: `yamllint .github/workflows/test.yml` (optional)
2. Manuell triggern via GitHub UI: "Re-run jobs"
3. Logs prüfen: Actions-Tab → letzte Workflow-Ausführung

### Coverage unter 70% — Merge blockiert

**Ursachen:**
- Neue Code-Zeilen sind nicht getestet
- Alte Tests wurden gelöscht oder deaktiviert

**Lösung:**
1. `dotnet test --collect:"XPlat Code Coverage"` lokal ausführen
2. Coverage-Report prüfen: `coverage-report/Summary.txt`
3. Untestete Zeilen identifizieren und Tests hinzufügen
4. Push und PR erneut versuchen

### staging-to-master PR wird nicht erstellt

**Ursachen:**
- `staging` und `master` sind identisch (kein Unterschied)
- Workflow-Fehler (selten)
- Existiert bereits offener PR von `staging` → `master`

**Lösung:**
1. Lokal prüfen: `git log master..staging --oneline` (Commits anzahl)
2. Falls Commits vorhanden, aber kein PR: GitHub Actions-Logs prüfen
3. Falls bereits PR vorhanden: Diesen erst schließen oder mergen

### master und staging sind desynchronisiert

**Symptom:**
- `staging` hat Commits, die `master` nicht hat
- Es ist unklar, warum `staging-to-master.yml` keinen PR erstellt hat

**Ursachen:**
- Alte Integration, bevor `staging-to-master.yml` implementiert war
- Manuell erstellter PR, der nicht gemergt wurde

**Lösung:**
1. Manuell einen PR zu `master` erstellen und mergen
2. Überprüfen, dass `test.yml` erfolgreich läuft
3. Nach Merge sollte `master` auf dem Stand von `staging` sein

### RC-Version auf staging ist niedriger als der letzte master-Release

**Symptom:**
- Der letzte stabile Release ist z. B. `v1.16.0`, ein Push nach `staging` erzeugt aber `v1.16.0-RC.1` statt des erwarteten `v1.16.1-RC.1` (oder eine andere Zielversion, die niedriger ist als erwartet)

**Ursache:**
- Der Release-Tag auf `master` sitzt auf einem Merge-Commit, der nur auf `master` existiert (Standard-Verhalten der Promotion-PR-Merges) und (noch) nicht per `sync-staging-with-master.yml` nach `staging` zurückgemerged wurde. Semantic Release findet den Tag dann auf `staging` nicht als erreichbar und berechnet die nächste Version auf Basis eines älteren, noch erreichbaren Tags.
- Prüfen: `git merge-base --is-ancestor v1.16.0 origin/staging` (Exit-Code `1` = Tag ist kein Vorfahre von `staging`, Ursache bestätigt)

**Lösung:**
1. Prüfen, ob ein offener Sync-PR (`master → staging`, Label `automated-sync`) existiert: `gh pr list --base staging --head master`
2. Diesen PR mit **"Create a merge commit"** mergen (nicht "Rebase and merge" — verwirft die inhaltsleeren Promotion-Merge-Commits, siehe unten)
3. Falls kein PR existiert, obwohl `staging` erkennbar hinter `master` liegt: `gh run list --workflow=sync-staging-with-master.yml` prüfen, ob der Workflow fehlgeschlagen ist

### staging bleibt nach dem Merge des Sync-PRs weiterhin "N commits behind master"

**Ursache:**
- Der Sync-PR wurde mit "Rebase and merge" gemergt. Die Promotion-Merge-Commits auf `master` (z. B. `Merge pull request #255 from martin-stromberg/staging`) enthalten selbst keine Dateiänderungen — beim Rebase werden inhaltsleere Commits beim Replay verworfen, wodurch effektiv nichts nach `staging` übertragen wird, obwohl GitHub den PR als gemergt anzeigt.

**Lösung:**
1. Erneut einen PR `master → staging` erstellen (oder den bestehenden Zustand direkt lokal prüfen: `git rev-list origin/staging..origin/master --count`)
2. Dieses Mal zwingend **"Create a merge commit"** verwenden

### Release schlägt fehl

**Ursachen:**
- Version-Konflikt (Semantic Release kann nächste Version nicht bestimmen)
- Test fehlgeschlagen (Release-Gate)
- ZIP-Paketierung fehlgeschlagen
- GitHub Release-Publikation fehlgeschlagen

**Lösung:**
1. Logs in GitHub Actions prüfen (Release-Workflow)
2. Bei Test-Fehler: Tests lokal reproduzieren
3. Bei ZIP/Release-Fehler: `release.yml` Workflow überprüfen
4. Manueller Tag als Fallback: `git tag vX.Y.Z` → `git push --tags`

## Weiterführende Ressourcen

- [CONTRIBUTING.md](CONTRIBUTING.md#branch-workflow-staging--master) — Branch-Workflow für Entwickler
- [README.md](README.md) — Projekteinsteig und Weiterführende Ressourcen
- [.github/workflows/test.yml](.github/workflows/test.yml) — Test-Workflow (Quelle)
- [.github/workflows/staging-to-master.yml](.github/workflows/staging-to-master.yml) — Promotion-Workflow (Quelle)
- [.github/workflows/release.yml](.github/workflows/release.yml) — Release-Workflow (Quelle)
- [.github/dependabot.yml](.github/dependabot.yml) — Abhängigkeits-Update-Konfiguration
