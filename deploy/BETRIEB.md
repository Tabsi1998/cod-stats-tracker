# Betrieb

## Erstinstallation (Linux-Server mit Docker)

1. Repo auf den Server holen, in `deploy/` wechseln.
2. `cp .env.example .env`, Domain (`SITE_ADDRESS`) und ein langes `POSTGRES_PASSWORD` eintragen (`openssl rand -base64 32`).
3. DNS der Domain auf den Server zeigen lassen, Ports 80 und 443 freigeben.
4. `docker compose --env-file .env up -d --build`

Caddy holt das HTTPS-Zertifikat automatisch. Die API spielt Datenbank-Migrationen beim Start selbst ein.

Den Windows-Installer zum Download legst du später nach `/srv/downloads/` im Web-Container (kommt mit dem Installer, Phase 6).

## Updates

```bash
git pull
docker compose --env-file .env up -d --build
```

Vorher ein Backup machen. Migrationen laufen beim Start der neuen API automatisch.

## Backup

```bash
docker compose exec -T db pg_dump -U codstats -d codstats -Fc > backup-$(date +%F).dump
```

Täglich per Cron, Dateien außerhalb des Servers aufbewahren.

## Restore

```bash
docker compose stop api
docker compose exec -T db pg_restore -U codstats -d codstats --clean --if-exists < backup-JJJJ-MM-TT.dump
docker compose start api
```

## Logs und Zustand

```bash
docker compose ps
docker compose logs -f api
curl -s https://<domain>/health
```
