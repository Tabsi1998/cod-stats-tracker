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
| Nur API | `python scripts/local_check.py --only api` |
| API starten | `docker compose -f deploy/docker-compose.dev.yml up -d` dann `dotnet run --project apps/api/CodStats.Api` |
| Migration anlegen | `dotnet tool restore` dann `dotnet ef migrations add <Name> --project apps/api/CodStats.Api` |

## Fallen

- API-Tests brauchen laufendes Docker (Testcontainers startet PostgreSQL).
- Signatur-Format ist in `RequestSigning.cs` festgelegt. Client und Server nutzen dieselbe Datei; Änderungen brechen alle installierten Clients → neue Version `COD-STATS-V2`.
