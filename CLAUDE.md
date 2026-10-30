# CLAUDE.md

Statistik-Tracker für CoD MW4 Multiplayer und Warzone: Windows-Client (Bilderkennung) + API + Webseite. Monorepo.

## Regeln

- Workflow nach Skill `fabian-entwicklung`: Issue → Branch `feat/<nr>-<name>` → lokale Prüfung grün → PR als Entwurf gegen `main`. Fabian merged.
- Commit- und PR-Titel Englisch, PR-Text Deutsch, `Closes #n`.
- Vor größeren Änderungen `ARCHITECTURE.md` lesen. Grundsätze dort sind verbindlich (nur eigene Daten, keine Zuordnung über Namen, K/D aus Summen).
- Keine Mock-Daten als echte Matchdaten darstellen. Testdaten nur in Tests.
- Erkennung nur mit echten Spielbildern als „funktioniert“ melden.

## Befehle

| Zweck | Befehl |
|---|---|
| Alles prüfen | `python scripts/local_check.py` |
| Nur .NET | `python scripts/local_check.py --only dotnet` |
| Client-EXE bauen | `python scripts/local_check.py --only desktop` → `.local-testing/publish/win-x64/CodStats.exe` |
| Release-Probelauf | `python scripts/local_check.py --release --version 0.1.0` → Installer, ZIP, SHA256SUMS in `.local-testing/release` |
| Release | Tag `v0.1.0` pushen → GitHub baut Entwurfs-Release; Fabian veröffentlicht |
| Server einrichten / aktualisieren | `./install.sh --domain …` einmal, dann `./update.sh` (Linux; Backup, Rollback eingebaut) |
| Ganzer Stack lokal | `cd deploy && docker compose --env-file .env up -d --build` (siehe README) |
| API starten | `docker compose -f deploy/docker-compose.dev.yml up -d` dann `dotnet run --project apps/api/CodStats.Api` |
| Migration anlegen | `dotnet tool restore` dann `dotnet ef migrations add <Name> --project apps/api/CodStats.Api` |

## Fallen

- API-Tests brauchen laufendes Docker (Testcontainers startet PostgreSQL).
- `tests/recognition` kompiliert Quellen des Clients direkt (`<Compile Include … Link>`). Eine neue Datei unter `Recognition/`, die ohne Windows auskommt, dort eintragen; Windows-Abhängigkeiten (WinRT, WPF, `Diagnostics`, `AppPaths`) gehören nicht in diese Dateien, sonst bricht der Linux-Build.
- Der Client schreibt nach `%LOCALAPPDATA%\CodStats` und setzt `HKCU\...\Run\CodStats` (Autostart). Beim Testen daran denken.
- WPF + WinForms (nur für das Tray-Symbol): `System.Windows.Forms`/`System.Drawing` sind als globale Usings entfernt.
- Selbst-Update: `Updates/UpdateKey.cs` (öffentlich) muss zum GitHub-Secret `UPDATE_SIGNING_KEY` passen, sonst bricht der Release ab. Schlüssel nie ins Repo; Einrichtung in `docs/updates.md`.
- Signatur-Format ist in `RequestSigning.cs` festgelegt. Client und Server nutzen dieselbe Datei; Änderungen brechen alle installierten Clients → neue Version `COD-STATS-V2`.
