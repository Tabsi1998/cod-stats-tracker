#!/usr/bin/env bash
# Sichert die Datenbank nach backups/codstats-JJJJ-MM-TT_HHMMSS.dump und behält die letzten 14.
# Für tägliche Backups per Cron:  0 4 * * * /pfad/zum/repo/backup.sh --quiet
#
# Die Dateien liegen auf demselben Server. Kopiere sie zusätzlich woandershin (z. B. Nextcloud, NAS).
set -Eeuo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/scripts/server-common.sh"

QUIET=0
[[ "${1:-}" == "--quiet" ]] && QUIET=1
KEEP=14

require_tools flock
require_installed
# update.sh already holds the lock when it calls this.
[[ "${FLOCK_HELD:-0}" == "1" ]] || take_lock

mkdir -p "$BACKUP_DIR"
chmod 700 "$BACKUP_DIR"
TARGET="$BACKUP_DIR/codstats-$(date +%F_%H%M%S).dump"
# Two backups in the same second (manual backup, then update.sh) must not overwrite each other.
n=2
while [[ -e "$TARGET" ]]; do
  TARGET="$BACKUP_DIR/codstats-$(date +%F_%H%M%S)-$n.dump"
  n=$((n + 1))
done

umask 077
compose exec -T db pg_dump -U codstats -d codstats -Fc >"$TARGET.partial" || { rm -f -- "$TARGET.partial"; die "Backup fehlgeschlagen."; }
[[ -s "$TARGET.partial" ]] || { rm -f -- "$TARGET.partial"; die "Backup ist leer."; }
mv -- "$TARGET.partial" "$TARGET"

# Keep the newest $KEEP.
ls -1t "$BACKUP_DIR"/codstats-*.dump 2>/dev/null | tail -n +$((KEEP + 1)) | xargs -r rm -f --

(( QUIET == 1 )) || green "✓ Backup: $TARGET ($(du -h "$TARGET" | cut -f1))"
