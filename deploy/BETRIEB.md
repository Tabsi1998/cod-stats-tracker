# Betrieb

## Kurzfassung

```bash
git clone https://github.com/Tabsi1998/cod-stats-tracker.git
cd cod-stats-tracker
./install.sh                              # einmalig, fragt Proxy/Domain/Port ab
./update.sh                               # bei jeder neuen Version
```

| Skript | Was es tut |
|---|---|
| `./install.sh` | Legt `deploy/.env` mit Domain und Zufallspasswort an, baut und startet alles, wartet bis die API antwortet. Schlägt es fehl, wird aufgeräumt und du kannst es neu starten. `--local` = Testbetrieb auf http://localhost:8080 |
| `./update.sh` | Holt die neue Version (`git pull`), macht vorher ein Backup, baut die Container während die alte Version noch läuft, startet neu. Bei Fehler läuft wieder der alte Stand. `--skip-pull` = nur neu bauen |
| `./backup.sh` | Datenbank nach `backups/`, die letzten 14 bleiben. Für Cron: `0 4 * * * /pfad/backup.sh --quiet` |
| `./restore.sh <datei>` | Spielt ein Backup zurück (fragt vorher nach) |
| `./start.sh`, `./stop.sh` | Starten und Stoppen. Nach einem Server-Neustart starten die Container von selbst |

Voraussetzungen auf dem Server: Linux, Docker mit Compose-Plugin, git, flock (bei Debian/Ubuntu dabei). Der Benutzer muss Docker verwenden dürfen (Gruppe `docker`). DNS der Domain auf den Server, Ports 80 und 443 offen. Caddy holt das HTTPS-Zertifikat selbst, die API spielt Datenbank-Migrationen beim Start ein.

Die Skripte laufen nicht gleichzeitig (Sperre in `run/`), und `update.sh` bricht ab, wenn im Repo jemand Dateien geändert hat.

## Hinter einem eigenen Reverse Proxy (HTTPS woanders)

```bash
./install.sh --behind-proxy --port 8080 --proxy-ip 10.0.0.5
```

- Der Stack liefert dann nur HTTP auf Port 8080 und holt kein eigenes Zertifikat.
- Im Proxy: `https://stats.deine-domain.at` → `http://<ip-dieses-servers>:8080`. Host und `X-Forwarded-For` weitergeben (macht Nginx Proxy Manager, Caddy und Traefik von selbst). Keine Websockets nötig.
- `--proxy-ip` ist die Adresse, von der dein Proxy kommt (mehrere mit Leerzeichen in `deploy/.env` bei `TRUSTED_PROXIES`). Ohne Angabe gelten alle privaten Netze (10.x, 172.16–31.x, 192.168.x) als vertrauenswürdig.
- **Warum das wichtig ist:** Nur von dort wird die echte Besucher-IP aus `X-Forwarded-For` übernommen. Sonst hätten alle Besucher die IP des Proxys und teilten sich ein Ratenlimit (z. B. nur 10 Geräte-Registrierungen pro Stunde für alle zusammen).
- Port 8080 in der Firewall nur für den Proxy öffnen, nicht für das ganze Internet. Sonst könnte jemand direkt mit gefälschtem `X-Forwarded-For` kommen, falls er aus einem vertrauten Netz anfragt.

Nginx-Beispiel (bei Nginx Proxy Manager einfach Proxy-Host mit Ziel-IP und Port 8080 anlegen):

```nginx
location / {
    proxy_pass http://10.0.0.20:8080;
    proxy_set_header Host $host;
    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    proxy_set_header X-Forwarded-Proto $scheme;
}
```

## Windows-Client zum Download anbieten

Die Webseite verlinkt `/downloads/CodStats-Setup-win-x64.exe`. Installer aus dem GitHub-Release holen und in den Web-Container legen:

```bash
docker compose cp CodStats-Setup-win-x64.exe web:/srv/downloads/CodStats-Setup-win-x64.exe
```

Nach einem `--build` des Web-Containers muss die Datei erneut hinein (sie ist nicht Teil des Images).

## Backups woanders aufbewahren

`backups/` liegt auf demselben Server. Kopiere die Dateien zusätzlich regelmäßig woandershin (NAS, Nextcloud, anderer Server), sonst hilft ein Backup bei einem Plattendefekt nichts.

## Ein Update ging schief

`update.sh` stellt den Code automatisch zurück. Hat die neue Version die Datenbank schon verändert, nennt es dir die Backup-Datei von direkt davor: `./restore.sh backups/…dump`.

## Logs und Zustand

```bash
docker compose ps
docker compose logs -f api
curl -s https://<domain>/health
```
