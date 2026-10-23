# COD Stats Tracker

Installieren und vergessen: Ein kleiner Windows-Client erfasst deine eigenen Matchwerte aus Call of Duty: Modern Warfare 4 (Multiplayer) und Warzone und überträgt sie an eine selbst gehostete Statistik-Webseite.

> **Stand 0.1 (Grundgerüst):** Server, Webseite und Windows-Client laufen zusammen. Die **automatische Spielerkennung fehlt noch**: Sie braucht echte Spielbilder. Bis dahin trägst du Matches im Client von Hand nach („Match nachtragen“). Der Client erkennt schon, ob Call of Duty läuft.

## Bestandteile

| Teil | Technik | Ordner |
|---|---|---|
| Windows-Client | .NET 10, WPF, SQLite, Tray | `apps/desktop` |
| API | ASP.NET Core 10, EF Core, PostgreSQL | `apps/api` |
| Webseite | React, TypeScript, Vite, Tailwind, Recharts | `apps/web` |
| Gemeinsamer Vertrag | C#-Bibliothek (DTOs, Signatur, Statistikregeln) + OpenAPI | `shared/contracts` |
| Betrieb | Docker Compose, Caddy (HTTPS) | `deploy` |

## Lokal ausprobieren

Voraussetzung: Docker Desktop läuft, .NET SDK 10 ist installiert.

**1. Server und Webseite starten**

Auf Linux (oder WSL):

```bash
./install.sh --local
```

Unter Windows mit Docker Desktop (die Skripte brauchen Linux):

```bash
cd deploy
cp .env.example .env
```

In `deploy/.env` für den lokalen Test setzen:

```
SITE_ADDRESS=http://localhost
HTTP_PORT=8080
POSTGRES_PASSWORD=irgendwas-langes
LEADERBOARD_MIN_MATCHES=3
```

```bash
docker compose --env-file .env up -d --build
```

Webseite: http://localhost:8080 · API-Beschreibung: http://localhost:8080/openapi/v1.json

**2. Windows-Client bauen und starten**

```bash
python scripts/local_check.py --release --version 0.1.0
```

Dann `.local-testing/release/CodStats-Setup-win-x64.exe` installieren (ohne Adminrechte) oder direkt `.local-testing/publish/win-x64/CodStats.exe` starten. Beim ersten Start:

- Serveradresse `http://localhost:8080` eintragen (für echte Server immer `https://`)
- Zustimmen, optional „Profil öffentlich“ anhaken

Der Client sitzt danach im Tray (Symbol neben der Uhr).

**3. Durchspielen**

1. Im Client unter *Einstellungen* deinen Spielernamen eintragen und bestätigen, „Profil öffentlich“ anhaken, speichern.
2. *Match nachtragen*: ein paar MW4- und Warzone-Matches eintragen.
3. Der Client synchronisiert automatisch (oder *Synchronisieren* klicken).
4. Auf http://localhost:8080 nach deinem Namen suchen: Profil, Diagramme, Rankings (ab 3 Matches).

Zurücksetzen des Clients: Tray → Beenden, dann `%LOCALAPPDATA%\CodStats` löschen.

## Wichtig

- Statistiken sind **Community-erfasst** und nicht von Activision bestätigt.
- Kein Zugriff auf den Spielspeicher, keine Injektion, keine dauerhafte Aufnahme. Details in [SECURITY.md](SECURITY.md).
- Kein Konto, kein Passwort. Jeder Client hat eine eigene kryptografische Geräteidentität.

## Entwickeln

Voraussetzungen: .NET SDK 10, Node 24, Docker Desktop (API-Tests starten PostgreSQL per Testcontainers), Python 3.12+.

```bash
python scripts/local_check.py
```

API ohne Docker-Stack starten (nur die Datenbank im Container):

```bash
docker compose -f deploy/docker-compose.dev.yml up -d
dotnet run --project apps/api/CodStats.Api
```

Webseite mit Hot Reload (leitet `/api` an Port 5080 weiter):

```bash
cd apps/web && npm install && npm run dev
```

## Eigener Server

Repo klonen, einmal `./install.sh` (fragt, ob HTTPS ein eigener Reverse Proxy macht oder dieser Server selbst), danach bei jeder neuen Version `./update.sh`. Details: [deploy/BETRIEB.md](deploy/BETRIEB.md).

## Doku

- [ARCHITECTURE.md](ARCHITECTURE.md): Aufbau, Identität, Synchronisierung, API
- [SECURITY.md](SECURITY.md): Sicherheit und Datenschutz
- [deploy/BETRIEB.md](deploy/BETRIEB.md): Server, Backup, Updates
- [CHANGELOG.md](CHANGELOG.md)

## Lizenz

Alle Rechte vorbehalten, siehe [LICENSE](LICENSE).
