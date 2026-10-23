# COD Stats Tracker

Installieren und vergessen: Ein kleiner Windows-Client erfasst deine eigenen Matchwerte aus Call of Duty: Modern Warfare 4 (Multiplayer) und Warzone und überträgt sie an eine selbst gehostete Statistik-Webseite.

> **Stand:** in Entwicklung. Die Spielerkennung ist noch nicht gebaut und braucht echte Spielbilder. Bis dahin gibt es keine echten Matchdaten.

## Bestandteile

| Teil | Technik | Ordner |
|---|---|---|
| Windows-Client | .NET 10, WPF, SQLite, Windows Graphics Capture, Windows-OCR | `apps/desktop` |
| API | ASP.NET Core 10, EF Core, PostgreSQL | `apps/api` |
| Webseite | React, TypeScript, Vite, Tailwind | `apps/web` |
| Gemeinsamer Vertrag | C#-Bibliothek + OpenAPI | `shared/contracts` |
| Betrieb | Docker Compose, Caddy | `deploy` |

## Wichtig

- Statistiken sind **Community-erfasst** und nicht von Activision bestätigt.
- Kein Zugriff auf den Spielspeicher, keine Injektion, keine dauerhafte Aufnahme. Details in [SECURITY.md](SECURITY.md).
- Kein Konto, kein Passwort. Jeder Client hat eine eigene kryptografische Geräteidentität.

## Entwickeln

Voraussetzungen: .NET SDK 10, Node 24, Docker Desktop (für Tests mit PostgreSQL), Python 3.12+.

```bash
python scripts/local_check.py
```

API lokal starten (PostgreSQL per Docker):

```bash
docker compose -f deploy/docker-compose.dev.yml up -d
dotnet run --project apps/api/CodStats.Api
```

OpenAPI: `http://localhost:5080/openapi/v1.json`

## Doku

- [ARCHITECTURE.md](ARCHITECTURE.md): Aufbau, Identität, Synchronisierung, API
- [SECURITY.md](SECURITY.md): Sicherheit und Datenschutz
- [CHANGELOG.md](CHANGELOG.md)

## Lizenz

Alle Rechte vorbehalten, siehe [LICENSE](LICENSE).
