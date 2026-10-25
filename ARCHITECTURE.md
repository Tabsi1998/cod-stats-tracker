# Architektur

Drei Teile, ein Vertrag:

```
Windows-Client (WPF, Tray)  ──HTTPS, signiert──▶  API (ASP.NET Core)  ──▶  PostgreSQL
   SQLite lokal                                      │
   Erkennung lokal                                   ▼
                                       Webseite (React) ◀── öffentliche, lesende Endpunkte
```

## Grundsätze

1. **Der Client schickt nur eigene Daten.** Alles unter `/api/v1/me` gilt nur für das Profil des signierenden Geräts. Eine Profil-ID im Body oder in der URL wird dort nie ausgewertet.
2. **Identität = Geräteschlüssel, nicht der Name.** Der CoD-Anzeigename ist nur ein Anzeigename. Zwei Profile mit gleichem Namen bleiben zwei Profile. Nichts wird anhand des Namens zusammengeführt.
3. **Öffentlich ist nur, was freigegeben ist.** Ein Profil erscheint erst auf der Webseite, wenn `isPublic = true` **und** der Anzeigename bestätigt ist (`nameStatus = confirmed`).
4. **Statistik wird serverseitig aus Rohwerten berechnet.** K/D = Summe Kills / Summe Deaths (nur Matches, in denen beide Werte bekannt sind). Nie der Durchschnitt der Match-K/D.
5. **Community-erfasst.** Werte stammen aus der Bilderkennung des Clients und sind nicht von Activision bestätigt. Server und Webseite sagen das.

## Geräteidentität

- Beim ersten Start erzeugt der Client ein ECDSA-Schlüsselpaar (P-256). Der private Schlüssel bleibt auf dem PC (per DPAPI für den Windows-Benutzer verschlüsselt).
- `POST /api/v1/devices/register` schickt den öffentlichen Schlüssel (SubjectPublicKeyInfo, Base64) und ist bereits mit diesem Schlüssel signiert (Besitznachweis). Der Server legt Profil und Gerät an und gibt `deviceId` und `profileId` zurück.
- Jede weitere Anfrage trägt diese Header:

| Header | Inhalt |
|---|---|
| `X-Device-Id` | Geräte-ID (GUID) |
| `X-Timestamp` | Unix-Zeit in Sekunden |
| `X-Nonce` | Zufallswert, 16–64 Zeichen, nur einmal verwendbar |
| `X-Signature` | Base64 der ECDSA-P-256-SHA256-Signatur (IEEE P1363, 64 Byte) |

Signiert wird diese Zeichenkette (UTF-8, Zeilen mit `\n`):

```
COD-STATS-V1
<HTTP-Methode in Großbuchstaben>
<Pfad mit Query, wie gesendet>
<X-Timestamp>
<X-Nonce>
<SHA-256 des Bodys, hex klein>
```

Der Server prüft: Gerät bekannt und nicht gesperrt, Zeit ±5 Minuten, Nonce unbenutzt (wird gespeichert), Signatur gültig. Implementierung: `shared/contracts/CodStats.Contracts/RequestSigning.cs`, von Client und Server gemeinsam genutzt.

### Umzug auf einen neuen PC

- `POST /api/v1/me/recovery-key` erzeugt einen Wiederherstellungsschlüssel (256 Bit Zufall). Der Server speichert nur den SHA-256-Hash. Ein neuer Aufruf ersetzt den alten Schlüssel.
- Auf dem neuen PC: `POST /api/v1/devices/recover` mit Wiederherstellungsschlüssel und neuem öffentlichen Schlüssel (signiert wie bei der Registrierung). Das neue Gerät wird dem Profil zugeordnet, der Schlüssel ist danach verbraucht.

## Datenmodell (Server)

| Tabelle | Wichtigste Felder |
|---|---|
| `profiles` | `id`, `display_name`, `name_status` (unknown/uncertain/confirmed), `is_public`, `recovery_key_hash` |
| `name_history` | `profile_id`, `name`, `first_seen_at`, `last_seen_at` |
| `devices` | `id`, `profile_id`, `public_key`, `created_at`, `last_seen_at`, `revoked_at` |
| `matches` | `id` (UUID vom Client), `profile_id`, `game`, `mode`, `played_at`, `kills`, `deaths`, `assists`, `result`, `placement`, `map`, `score`, `confidence`, `review_status`, `recognizer_version` |
| `used_nonces` | `device_id`, `nonce`, `expires_at` |

Spiele: `mw4_mp`, `wz_br`, `wz_resurgence`. Gruppen für Auswertungen: `mw4` (= `mw4_mp`), `warzone` (= `wz_br` + `wz_resurgence`), `all` (kombinierte Übersicht, klar gekennzeichnet).

Fehlende Werte sind `null`, nie `0`.

## Synchronisierung

1. Client speichert das Match zuerst lokal (SQLite) mit eigener UUID.
2. Nur Matches mit Prüfstatus `auto` (sichere Erkennung), `confirmed` oder `corrected` werden hochgeladen. `pending` bleibt lokal, bis der Spieler prüft.
3. `POST /api/v1/me/matches/batch` (max. 50 Matches). Antwort je Match: `created`, `unchanged`, `duplicate`, `rejected` (mit Grund).
4. Idempotent: gleiche UUID mit gleichen Werten → `unchanged`. Gleiche UUID mit anderen Werten → `rejected` (Änderungen nur über `PATCH /me/matches/{id}`). UUID gehört einem anderen Profil → `rejected`, ohne etwas zu verraten.
5. Inhaltliche Duplikate (gleiches Spiel, gleiche Werte, Zeitpunkt < 3 Minuten auseinander) → `duplicate`.
6. Bei Netzfehlern wiederholt der Client mit wachsendem Abstand. Verlorene Antworten sind harmlos, weil der nächste Versuch `unchanged` liefert.

## Plausibilität (Server)

| Feld | Erlaubt |
|---|---|
| `kills`, `deaths`, `assists` | 0–200 |
| `placement` | 1–150, nur Warzone |
| `score` | 0–100 000 |
| `confidence` | 0–1 |
| `played_at` | nicht mehr als 5 Minuten in der Zukunft, nicht älter als 180 Tage |
| `mode`, `map` | max. 64 Zeichen |

Warzone mit `placement = 1` muss `result = win` sein, sonst `loss`.

## Erkennung (Client)

```
GameWatcher (Prozessname, alle 5 s) ──CoD läuft──▶ RecognitionLoop (alle 2 s)
   WindowCapture (Windows Graphics Capture, nur Arbeitsspeicher)
   → ScreenReader (Profile: Anker-Text erkennen, dann Felder per Windows-OCR lesen)
   → MatchStateMachine (Idle → Playing → Collecting → Recorded)
   → MatchStore (SQLite): sicher = auto (wird hochgeladen), sonst pending (Spieler prüft)
```

- **Profile sind Daten** (`*.json`), keine Programmlogik: Bildschirmart, Spiel, Seitenverhältnis, Anker-Bereiche mit Schlüsselwörtern, Feld-Bereiche (relativ 0..1), Wörter für Sieg/Niederlage. Geladen aus `profiles/` neben der EXE und `%LOCALAPPDATA%\CodStats\profiles` (überschreibt gleiche `id`). Format: [docs/erkennungsprofile.md](docs/erkennungsprofile.md).
- **Noch keine Spielprofile:** sie entstehen aus echten Screenshots (`tests/capture-fixtures`). Profile mit `verified: false` laufen, liefern aber nie „sichere“ Matches.
- **Ein Bild entscheidet nie:** ein Ergebnis wird 3× gelesen, Werte per Mehrheit bestimmt. „Sicher“ (≥ 0,85) nur, wenn Matchbeginn gesehen, Spiel bekannt, Lesungen einig und Plausibilitätsregeln erfüllt.
- **Tab-Scoreboard mitten im Match zählt nicht.** Es wird nur gemerkt; endet ein Match ohne Ergebnisbildschirm, wird der letzte Stand als `pending` zur Prüfung abgelegt.
- **Duplikate:** nach einem erfassten Match werden Ergebnisbildschirme ignoriert, bis ein neues Match beginnt; zusätzlich prüfen SQLite (gleiches Ergebnis ±3 Min.) und Server.
- **Eigener Name:** gilt erst als bestätigt, wenn derselbe Name sauber in 3 verschiedenen Matches gelesen wurde und kein zweiter Name genauso oft vorkommt. Ein von Hand bestätigter Name bleibt.
- **Windows-OCR** liefert keine Wort-Konfidenz; die Qualität kommt aus sauberem Parsen (Ziffern-Verwechsler wie O/0 senken sie) und der Übereinstimmung mehrerer Bilder.

## Selbst-Update (Client)

Der Client liest `/downloads/CodStats-update.json` und die Signatur daneben vom eigenen Server, prüft sie mit dem öffentlichen Release-Schlüssel (`Updates/UpdateKey.cs`) und bietet eine neuere Version an. Installiert wird nur auf Klick, nie während CoD läuft, und nur ein Installer mit dem signierten SHA-256. Details und Einrichtung des Schlüssels: [docs/updates.md](docs/updates.md).

## API-Überblick (`/api/v1`)

Gerät (signiert): `POST devices/register`, `POST devices/recover`, `GET me`, `PUT me/display-name`, `PATCH me/privacy`, `GET me/stats`, `GET me/matches`, `POST me/matches/batch`, `PATCH me/matches/{id}`, `GET me/sync-status`, `POST me/recovery-key`, `GET me/export`, `DELETE me/data`.

Öffentlich (lesend, ohne Anmeldung): `GET public/players`, `GET public/players/search?q=`, `GET public/players/{id}`, `GET public/players/{id}/stats`, `GET public/players/{id}/matches`, `GET public/leaderboards`, `GET public/platform-stats`.

Statistik-Filter (alle `stats`-Endpunkte): `game=mw4|warzone|all`, `last=10|25|50|100`, `days=7|30`, `from`, `to`.

Die genaue Beschreibung liefert OpenAPI unter `/openapi/v1.json`.

## Ordner

| Ordner | Inhalt |
|---|---|
| `apps/api` | ASP.NET Core API, EF Core, Migrationen |
| `apps/web` | React-Webseite (Phase 3) |
| `apps/desktop/CodStats.Desktop` | WPF-Client: `Identity/` (Geräteschlüssel), `Storage/` (SQLite), `Sync/` (signierte API, Sync-Schleife), `Capture/` (Fensteraufnahme, OCR), `Recognition/` (Profile, Zustandsautomat, Namensprüfung), `Ui/` (Tray, Dashboard, Fenster) |
| `shared/contracts` | DTOs, Spiel-Enums, Request-Signatur, Statistikregeln |
| `tests/api` | API-Tests gegen echtes PostgreSQL (Testcontainers) und Tests der gemeinsamen Regeln |
| `tests/desktop` | Client-Tests: SQLite, Identität, Sync gegen nachgebauten Server |
| `tests/capture-fixtures` | Echte Spielbilder für Erkennungstests (nicht öffentlich) |
| `deploy` | Docker Compose, Caddy, `.env.example` |
