#!/usr/bin/env bash
# Spielt ein Backup zurück. Alle Daten seit diesem Backup gehen verloren.
#
#   ./restore.sh backups/codstats-2026-11-02_040000.dump
set -Eeuo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/scripts/server-common.sh"

FILE="${1:-}"
[[ -n "$FILE" && -s "$FILE" ]] || die "Backup-Datei angeben, z. B.: ./restore.sh $(ls -1t "$BACKUP_DIR"/*.dump 2>/dev/null | head -n 1 || echo backups/….dump)"

require_tools flock
require_installed
take_lock

yellow "Achtung: Die Datenbank wird auf den Stand von $FILE zurückgesetzt. Alles danach geht verloren."
read -r -p "Zum Fortfahren 'wiederherstellen' eintippen: " ANSWER
[[ "$ANSWER" == "wiederherstellen" ]] || die "Abgebrochen, nichts geändert."

compose stop api
compose exec -T db pg_restore -U codstats -d codstats --clean --if-exists --no-owner <"$FILE" || { compose start api; die "Wiederherstellung fehlgeschlagen. API läuft wieder mit dem bisherigen Stand."; }
compose start api
wait_healthy 60 || die "Die API startet nach der Wiederherstellung nicht. Logs: docker compose logs api (in deploy/)."
green "✓ Wiederhergestellt aus $FILE"
