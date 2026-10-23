#!/usr/bin/env bash
# Richtet den Server einmalig ein: .env mit Domain und Zufallspasswort, Container bauen und starten.
#
#   ./install.sh                          fragt nach der Domain
#   ./install.sh --domain stats.example.com
#   ./install.sh --local                  Testbetrieb auf http://localhost:8080
#
# Danach nur noch ./update.sh.
set -Eeuo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/scripts/server-common.sh"

DOMAIN=""
LOCAL=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    --domain) DOMAIN="${2:-}"; shift 2 ;;
    --local) LOCAL=1; shift ;;
    -h|--help) sed -n '2,9p' "$0"; exit 0 ;;
    *) die "Unbekannte Option: $1 (siehe ./install.sh --help)" ;;
  esac
done

require_tools git flock
take_lock

[[ -f "$ENV_FILE" ]] && die "Schon installiert ($ENV_FILE existiert). Für neue Versionen: ./update.sh"

if (( LOCAL == 0 )) && [[ -z "$DOMAIN" ]]; then
  read -r -p "Domain der Webseite (z. B. stats.example.com, leer = lokaler Test): " DOMAIN
  [[ -z "$DOMAIN" ]] && LOCAL=1
fi
if (( LOCAL == 0 )); then
  [[ "$DOMAIN" =~ ^[A-Za-z0-9.-]+\.[A-Za-z]{2,}$ ]] || die "Das sieht nicht wie eine Domain aus: $DOMAIN"
fi

# 32 zufällige Bytes, ohne Zeichen, die in einer Verbindungszeichenkette stören.
PASSWORD="$(head -c 32 /dev/urandom | base64 | tr -d '/+=\n' | head -c 40)"

umask 077
if (( LOCAL == 1 )); then
  cat >"$ENV_FILE" <<EOF
SITE_ADDRESS=http://localhost
HTTP_PORT=8080
HTTPS_PORT=8443
POSTGRES_PASSWORD=$PASSWORD
LEADERBOARD_MIN_MATCHES=3
EOF
  URL="http://localhost:8080"
else
  cat >"$ENV_FILE" <<EOF
SITE_ADDRESS=$DOMAIN
HTTP_PORT=80
HTTPS_PORT=443
POSTGRES_PASSWORD=$PASSWORD
LEADERBOARD_MIN_MATCHES=20
EOF
  URL="https://$DOMAIN"
fi
umask 022
green "✓ $ENV_FILE angelegt (Passwort zufällig erzeugt, nur für dich lesbar)."

# A failed first start leaves no half installation behind: .env goes, so install.sh can simply run again.
cleanup() {
  local status=$?
  if (( status != 0 )); then
    red "Installation fehlgeschlagen. Container werden gestoppt, .env entfernt; danach ./install.sh erneut starten."
    compose down >/dev/null 2>&1 || true
    rm -f -- "$ENV_FILE"
  fi
}
trap cleanup EXIT

yellow "→ Container bauen (beim ersten Mal einige Minuten) …"
compose build
compose up -d
yellow "→ Warte auf die API …"
wait_healthy 90 || { compose logs --tail 50 api >&2; die "Die API ist nicht hochgekommen. Logs oben."; }

trap - EXIT
green "✓ Läuft: $URL"
if (( LOCAL == 0 )); then
  echo "  DNS von $DOMAIN muss auf diesen Server zeigen, Ports 80 und 443 offen. Caddy holt das Zertifikat selbst."
fi
echo "  Neue Versionen:   ./update.sh"
echo "  Backup:           ./backup.sh   (update.sh macht vorher automatisch eins)"
echo "  Installer für die Webseite: siehe deploy/BETRIEB.md"
