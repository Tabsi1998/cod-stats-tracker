# Shared by install.sh, update.sh, start.sh, stop.sh, backup.sh and restore.sh. Not run on its own.
# shellcheck shell=bash

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DEPLOY_DIR="$ROOT_DIR/deploy"
ENV_FILE="$DEPLOY_DIR/.env"
# shellcheck disable=SC2034  # used by the scripts that source this file
BACKUP_DIR="$ROOT_DIR/backups"
RUN_DIR="$ROOT_DIR/run"

green()  { printf "\033[0;32m%s\033[0m\n" "$1"; }
yellow() { printf "\033[0;33m%s\033[0m\n" "$1"; }
red()    { printf "\033[0;31m%s\033[0m\n" "$1" >&2; }
die()    { red "✗ $1"; exit 1; }

compose() {
  docker compose --project-directory "$DEPLOY_DIR" -f "$DEPLOY_DIR/docker-compose.yml" --env-file "$ENV_FILE" "$@"
}

require_tools() {
  local tool
  for tool in "$@"; do
    command -v "$tool" >/dev/null 2>&1 || die "Benötigtes Programm fehlt: $tool"
  done
  docker compose version >/dev/null 2>&1 || die "Docker Compose (Plugin) fehlt: https://docs.docker.com/compose/install/"
  docker info >/dev/null 2>&1 || die "Docker läuft nicht oder dieser Benutzer darf Docker nicht verwenden (Gruppe 'docker')."
}

require_installed() {
  [[ -f "$ENV_FILE" ]] || die "Noch nicht installiert: zuerst ./install.sh ausführen."
}

# Only one install, update, start, stop, backup or restore at a time.
take_lock() {
  mkdir -p "$RUN_DIR"
  exec 9>"$RUN_DIR/server.lock"
  flock -n 9 || die "Ein anderer Vorgang (install, update, start, stop, backup, restore) läuft bereits."
}

# Waits until the API answers /health through the internal network. The API image has no curl, so the web
# container (Caddy on Alpine, with busybox wget) asks for it.
wait_healthy() {
  local tries=${1:-60}
  local i
  for ((i = 1; i <= tries; i++)); do
    if compose exec -T web wget -qO- http://api:8080/health >/dev/null 2>&1; then
      return 0
    fi
    sleep 2
  done
  return 1
}

site_address() {
  grep -E '^SITE_ADDRESS=' "$ENV_FILE" | head -n 1 | cut -d= -f2-
}
