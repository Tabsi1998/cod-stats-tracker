#!/usr/bin/env bash
# Holt die neue Version, sichert vorher die Datenbank, baut die Container und startet sie neu.
# Geht etwas schief, läuft danach wieder der vorherige Stand.
#
#   ./update.sh               neue Version von GitHub holen und einspielen
#   ./update.sh --skip-pull   nur neu bauen und starten (z. B. nach Änderung an deploy/.env)
set -Eeuo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/scripts/server-common.sh"

PULL=1
while [[ $# -gt 0 ]]; do
  case "$1" in
    --skip-pull) PULL=0; shift ;;
    -h|--help) sed -n '2,7p' "$0"; exit 0 ;;
    *) die "Unbekannte Option: $1 (siehe ./update.sh --help)" ;;
  esac
done

require_tools git flock
require_installed
take_lock
cd "$ROOT_DIR"

if [[ -n "$(git status --porcelain --untracked-files=normal)" ]]; then
  die "Lokale Änderungen im Repo erkannt. Update abgebrochen, damit nichts überschrieben wird (git status zeigt sie)."
fi

describe() { git log -1 --format='%h (%s)' "$1" 2>/dev/null || printf '%s' "$1"; }
OLD_COMMIT="$(git rev-parse HEAD)"

if (( PULL == 1 )); then
  yellow "→ Neue Version holen …"
  git fetch --quiet
  git merge --ff-only --quiet '@{u}' || die "Kein einfaches Vorspulen möglich (abweichender Stand). Nichts geändert."
  if [[ "$(git rev-parse HEAD)" == "$OLD_COMMIT" ]]; then
    green "✓ Schon aktuell: $(describe HEAD)"
    exit 0
  fi
fi
NEW_COMMIT="$(git rev-parse HEAD)"

# Before anything runs with the new version, so a failed migration can be undone with restore.sh.
BACKUP_NOTE=""
if compose ps --status running --services 2>/dev/null | grep -qx db; then
  yellow "→ Backup vor dem Update …"
  FLOCK_HELD=1 "$ROOT_DIR/backup.sh" --quiet
  BACKUP_NOTE="$(ls -1t "$BACKUP_DIR"/*.dump 2>/dev/null | head -n 1)"
fi

rollback() {
  local status=$?
  trap - EXIT
  (( status == 0 )) && return
  red "↶ Update fehlgeschlagen. Stand $(describe "$OLD_COMMIT") wird wiederhergestellt."
  git reset --hard --quiet "$OLD_COMMIT"
  if compose build && compose up -d && wait_healthy 60; then
    yellow "↶ Es läuft wieder $(describe "$OLD_COMMIT")."
  else
    red "Auch der vorherige Stand startet nicht. Logs: docker compose logs (in deploy/)."
  fi
  if [[ -n "$BACKUP_NOTE" ]]; then
    yellow "  Falls die neue Version die Datenbank schon verändert hat: ./restore.sh $BACKUP_NOTE"
  fi
}
trap rollback EXIT

# Build first, while the old version keeps running: the site is only down for the restart itself.
yellow "→ Container bauen …"
compose build
yellow "→ Neu starten …"
compose up -d --remove-orphans
wait_healthy 90 || { compose logs --tail 50 api >&2; die "Die API antwortet nach dem Update nicht."; }

trap - EXIT
fetch_installer
docker image prune -f >/dev/null 2>&1 || true
green "✓ Aktualisiert: $(describe "$OLD_COMMIT") → $(describe "$NEW_COMMIT")"
[[ -n "$BACKUP_NOTE" ]] && echo "  Backup davor: $BACKUP_NOTE"
exit 0
