# COD Stats Tracker

Installieren und vergessen: Ein kleiner Windows-Client erfasst deine eigenen Matchwerte aus Call of Duty: Modern Warfare 4 (Multiplayer) direkt vom Bildschirm und überträgt sie an eine selbst gehostete Statistik-Webseite.

Live: https://codstats.tabelander.co.at

## Funktionen

- **Automatische Erkennung (MW4 Multiplayer):** Ergebnisbildschirm, Frei für alle mit Platzierung, Einsatzbericht, Tab-Scoreboard als Rückfallebene, verlassene Matches. Gelesen werden Ergebnis, Modus, Karte, Abschüsse, Tode, Assists, Eliminierungen, Punkte, Endstand, XP, Level, Modus-Werte und die Top-Waffe.
- **Lobby-Vergleich:** dein Platz nach Punkten im End-Scoreboard („Platz 3 von 12“). Übertragen werden nur Lobbygröße und Platz, keine fremden Namen oder Werte.
- **Nur sichere Werte gehen automatisch hoch.** Unsichere Matches bleiben im Client zur Prüfung; nicht Gelesenes bleibt „unbekannt“ statt 0.
- **Windows-Client im Tray:** eigenes Dashboard mit Filtern und Diagrammen, Match von Hand nachtragen oder korrigieren, Offline-Speicher mit Synchronisierung, Autostart.
- **Webseite:** Spielersuche, Profile mit Bilanz, Form, Serien, Auswertung nach Modus und Karte, eine eigene Seite je Match, Ranglisten (auch je Modus).
- **Ohne Konto:** Jeder Client hat eine eigene kryptografische Geräteidentität; Umzug auf einen neuen PC per Wiederherstellungsschlüssel.
- **Selbst hostbar:** Docker Compose, `./install.sh` einmal, danach `./update.sh`.

## Was erkannt wird

| Spiel | Oberfläche | Seitenverhältnis | Stand |
|---|---|---|---|
| MW4 Multiplayer | Deutsch | 16:9 (1080p, 1440p, 4K) | erkannt, an echten Aufnahmen geprüft |
| MW4 Multiplayer | Englisch | 16:9 | fehlt, braucht Aufnahmen ([#48](https://github.com/Tabsi1998/cod-stats-tracker/issues/48)) |
| MW4 Multiplayer | Deutsch | 21:9, 16:10 | fehlt, braucht Aufnahmen ([#48](https://github.com/Tabsi1998/cod-stats-tracker/issues/48)) |
| Warzone (BR, Resurgence) | – | – | fehlt, braucht Aufnahmen ([#47](https://github.com/Tabsi1998/cod-stats-tracker/issues/47)); bis dahin von Hand nachtragen |

Wie ein Erkennungsprofil aufgebaut ist: [docs/erkennungsprofile.md](docs/erkennungsprofile.md).

## Installieren (Spieler)

Auf der Webseite „Für Windows herunterladen“, Installer starten (keine Adminrechte nötig), zustimmen und „öffentlich zeigen“ oder „nur für mich“ wählen. Der Client sitzt danach im Tray (Symbol neben der Uhr) und startet mit Windows. Einfach spielen; nach dem Match steht es im Client und, wenn du es freigegeben hast, auf der Webseite.

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
python scripts/local_check.py --release --version 0.2.2
```

Dann `.local-testing/release/CodStats-Setup-win-x64.exe` installieren (ohne Adminrechte) oder direkt `.local-testing/publish/win-x64/CodStats.exe` starten. Beim ersten Start:

- Der Client spricht fest mit https://codstats.tabelander.co.at (Konstante `Settings.Server` in `apps/desktop/CodStats.Desktop/Settings.cs`). Für einen lokalen Stack vor dem Start `CODSTATS_SERVER=http://localhost:8080` setzen; ein eigener Server ist eine Änderung dieser Konstante und ein neuer Build.
- Zustimmen, „öffentlich zeigen“ oder „nur für mich“ wählen

Der Client sitzt danach im Tray (Symbol neben der Uhr).

**3. Durchspielen**

1. Im Client unter *Einstellungen* deinen Spielernamen eintragen und bestätigen, „Profil öffentlich“ anhaken, speichern.
2. *Match nachtragen*: ein paar MW4- und Warzone-Matches eintragen.
3. Der Client synchronisiert automatisch (oder *Synchronisieren* klicken).
4. Auf http://localhost:8080 nach deinem Namen suchen: Profil, Diagramme, Rankings (ab 3 Matches).

Zurücksetzen des Clients: Tray → Beenden, dann `%LOCALAPPDATA%\CodStats` löschen. Protokoll: `%LOCALAPPDATA%\CodStats\client.log`.

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

- [ARCHITECTURE.md](ARCHITECTURE.md): Aufbau, Identität, Synchronisierung, Erkennung, API
- [docs/erkennungsprofile.md](docs/erkennungsprofile.md): Erkennungsprofile und was die echten Bilder gelehrt haben
- [DESIGN.md](DESIGN.md): Design der Webseite
- [SECURITY.md](SECURITY.md): Sicherheit und Datenschutz
- [deploy/BETRIEB.md](deploy/BETRIEB.md): Server, Backup, Updates
- [CHANGELOG.md](CHANGELOG.md)

## Lizenz

Alle Rechte vorbehalten, siehe [LICENSE](LICENSE).
