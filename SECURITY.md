# Sicherheit und Datenschutz

## Sicherheitslücken melden

Bitte nicht als öffentliches Issue. Schreib an fabian.tabelander@gmail.com mit Beschreibung und Schritten zum Nachstellen.

## Was der Client nie tut

- Kein Lesen oder Verändern des Spielspeichers
- Keine DLL-Injection, keine Hooks ins Spiel, keine Kernel-Treiber
- Keine Eingaben an das Spiel, keine Umgehung von Anti-Cheat
- Keine dauerhafte Video- oder Bildaufnahme. Einzelbilder bleiben im Arbeitsspeicher und werden nach der Auswertung verworfen.
- Keine Screenshots an den Server. Übertragen werden nur die erkannten Zahlen und der eigene Anzeigename.
- Keine Namen oder Werte anderer Spieler aus dem Scoreboard

Der Client nimmt das Spielfenster nur auf, wie es OBS oder Discord auch tun. Activision hat dazu keine ausdrückliche Zusage gemacht. Die Nutzung erfolgt auf eigenes Risiko.

## Identität und Rechte

- Jedes Gerät hat ein eigenes ECDSA-P-256-Schlüsselpaar. Der private Schlüssel verlässt den PC nie und ist per DPAPI geschützt.
- Jede Anfrage an `/api/v1/me/...` ist signiert, mit Zeitstempel und einmaliger Nonce (Schutz vor Wiederholung).
- Der Server ordnet Daten nur über das signierende Gerät zu. Eine mitgeschickte Profil-ID oder ein Spielername berechtigt zu nichts.
- Der Geräteschlüssel beweist nicht, dass der erkannte CoD-Name dem Besitzer gehört. Deshalb gibt es keine Zusammenführung über Namen.
- Wiederherstellungsschlüssel werden nur als SHA-256-Hash gespeichert.

## Updates

- Der Client installiert ein Update nur mit gültiger Signatur des Release-Schlüssels (ECDSA P-256). Der private Schlüssel liegt nur als GitHub-Secret vor; wer den Server übernimmt, kann damit keine fremden Programme verteilen.
- Der heruntergeladene Installer muss den signierten SHA-256 haben, sonst wird er gelöscht.
- Installiert wird nur auf Klick des Spielers. Details: [docs/updates.md](docs/updates.md).

## Grenzen

Die Werte kommen aus der Bilderkennung auf dem PC der Spieler. Wer will, kann gefälschte Werte schicken. Der Server prüft Plausibilität und begrenzt Anfragen, die Webseite kennzeichnet alles als „Community-erfasst, nicht von Activision bestätigt“.

## Daten der Spieler

- Veröffentlichung nur nach ausdrücklicher Zustimmung im Client, jederzeit abschaltbar.
- `GET /api/v1/me/export` liefert alle eigenen Daten als JSON.
- `DELETE /api/v1/me/data` löscht Profil, Matches, Namensverlauf und Geräte endgültig.

## Betrieb

- Nur HTTPS (Caddy mit automatischen Zertifikaten).
- Rate Limiting je Gerät und je IP.
- Secrets nur in `deploy/.env`, nie im Repo. Gitleaks läuft in der lokalen Prüfung.
