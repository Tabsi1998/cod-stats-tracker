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

## Ubuntu-Server von null

Für einen frischen Ubuntu-Server (22.04 oder 24.04), der hinter deinem eigenen Reverse Proxy läuft:

```bash
# 1. Docker mit Compose-Plugin (offizielles Skript von Docker) und git
curl -fsSL https://get.docker.com | sudo sh
sudo apt-get install -y git
sudo usermod -aG docker "$USER"   # danach einmal ab- und wieder anmelden

# 2. Repo holen (privat: vorher anmelden, z. B. mit "gh auth login" oder einem Deploy-Key)
git clone https://github.com/Tabsi1998/cod-stats-tracker.git
cd cod-stats-tracker

# 3. Einrichten: Auswahl 1 (eigener Reverse Proxy), Port 8080, IP deines Proxys
./install.sh
```

Dann im Reverse Proxy: `https://codstats.tabelander.co.at` → `http://<IP dieses Servers>:8080`, und der DNS-Eintrag der Domain zeigt auf den Proxy. Port 8080 in der Firewall nur für den Proxy öffnen (`sudo ufw allow from <proxy-ip> to any port 8080`).

Neue Versionen danach immer mit `./update.sh`.

## Hinter einem eigenen Reverse Proxy (HTTPS woanders)

```bash
./install.sh --behind-proxy --port 8080 --proxy-ip 10.0.0.5
```

- Der Stack liefert dann nur HTTP auf dem gewählten Port (Standard 8080) und öffnet keinen HTTPS-Port. `install.sh` prüft vorher, ob der Port schon belegt ist; auf einem Server mit anderen Diensten einfach einen freien wählen (`--port 8095`). Später ändern: `HTTP_PORT` in `deploy/.env`, dann `./update.sh --skip-pull`.
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

Die Webseite bietet `deploy/downloads/CodStats-Setup-win-x64.exe` an; der Ordner bleibt bei Updates erhalten. `install.sh` und `update.sh` holen den Installer des neuesten Releases selbst:

- mit `gh` (bei privatem Repo nötig): einmal `gh auth login` auf dem Server,
- bei öffentlichem Repo ohne Anmeldung.

Klappt beides nicht, die Datei von Hand aus dem GitHub-Release in `deploy/downloads/` legen. Solange keine Datei da ist, blendet die Webseite den Download-Knopf aus.

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
