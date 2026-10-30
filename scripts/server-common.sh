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
  local files=(-f "$DEPLOY_DIR/docker-compose.yml")
  if grep -qE '^HTTPS_PORT=' "$ENV_FILE" 2>/dev/null; then
    files+=(-f "$DEPLOY_DIR/docker-compose.https.yml")
  fi
  docker compose --project-directory "$DEPLOY_DIR" "${files[@]}" --env-file "$ENV_FILE" "$@"
}

# True when something on this server already listens on the TCP port.
port_in_use() {
  command -v ss >/dev/null 2>&1 && ss -Hltn "sport = :$1" 2>/dev/null | grep -q .
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

# Puts the latest release's Windows installer into deploy/downloads for the website's download button, and with it
# the signed update manifest the clients look for (docs/updates.md). Never fatal: the site runs without them, the
# button just stays hidden and clients see no update.
fetch_installer() {
  local target="$DEPLOY_DIR/downloads" file="CodStats-Setup-win-x64.exe" manifest="CodStats-update.json" repo staging
  repo="$(git -C "$ROOT_DIR" remote get-url origin 2>/dev/null | sed -E 's#(git@github.com:|https://github.com/)##; s#\.git$##')"
  mkdir -p "$target"
  [[ -n "$repo" ]] || { yellow "  Installer: GitHub-Repo unbekannt, Download-Knopf bleibt ausgeblendet."; return 0; }
  # Next to the target, so the moves below are atomic renames on the same file system.
  staging="$(mktemp -d "$target/.staging.XXXXXX")"
  if ! release_file "$repo" "$file" "$staging"; then
    rm -rf -- "$staging"
    yellow "  Installer nicht geholt (privates Repo ohne 'gh auth login'?). Von Hand: Datei $file aus dem"
    yellow "  GitHub-Release nach $target legen. Bis dahin ist der Download-Knopf ausgeblendet."
    return 0
  fi
  # The manifest only goes out with the installer it describes; an old one next to a new installer is removed.
  rm -f -- "$target/$manifest" "$target/$manifest.sig"
  if release_file "$repo" "$manifest" "$staging" && release_file "$repo" "$manifest.sig" "$staging" \
     && grep -qi "\"$(sha256sum "$staging/$file" | cut -d' ' -f1)\"" "$staging/$manifest"; then
    mv -- "$staging/$file" "$target/$file"
    mv -- "$staging/$manifest.sig" "$target/$manifest.sig"
    mv -- "$staging/$manifest" "$target/$manifest"
    green "✓ Installer und Update für die Clients bereitgestellt."
  else
    mv -- "$staging/$file" "$target/$file"
    green "✓ Installer der neuesten Version für die Webseite geholt."
    yellow "  Kein signiertes Update im Release: Clients bieten diese Version nicht selbst an."
  fi
  rm -rf -- "$staging"
  # Caddy reads these without the capability to bypass file permissions, so a strict umask must not hide them.
  chmod -R a+rX -- "$target"
}

# Downloads one file of the latest release into a folder: with gh (also private repos), else anonymously.
release_file() {
  local repo="$1" name="$2" dir="$3"
  if command -v gh >/dev/null 2>&1 && gh auth status >/dev/null 2>&1 \
     && gh release download --repo "$repo" --pattern "$name" --dir "$dir" --clobber >/dev/null 2>&1 && [[ -s "$dir/$name" ]]; then
    return 0
  fi
  curl -fsSL -o "$dir/$name" "https://github.com/$repo/releases/latest/download/$name" 2>/dev/null && [[ -s "$dir/$name" ]]
}

site_address() {
  grep -E '^SITE_ADDRESS=' "$ENV_FILE" | head -n 1 | cut -d= -f2-
}
