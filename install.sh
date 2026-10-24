#!/usr/bin/env bash
# Richtet den Server einmalig ein: .env mit Zufallspasswort, Container bauen und starten.
#
#   ./install.sh                                         fragt alles ab
#   ./install.sh --domain stats.example.com              eigenes HTTPS mit Let's Encrypt (Ports 80/443)
#   ./install.sh --behind-proxy [--port 8080] [--proxy-ip 10.0.0.5]
#                                                        HTTPS macht dein eigener Reverse Proxy
#   ./install.sh --local                                 Testbetrieb auf http://localhost:8080
#
# Danach nur noch ./update.sh.
set -Eeuo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/scripts/server-common.sh"

MODE=""
DOMAIN=""
PORT="8080"
PROXY_IP="private_ranges"
while [[ $# -gt 0 ]]; do
  case "$1" in
    --domain) MODE="domain"; DOMAIN="${2:-}"; shift 2 ;;
    --behind-proxy) MODE="proxy"; shift ;;
    --port) PORT="${2:-}"; shift 2 ;;
    --proxy-ip) PROXY_IP="${2:-}"; shift 2 ;;
    --local) MODE="local"; shift ;;
    -h|--help) sed -n '2,11p' "$0"; exit 0 ;;
    *) die "Unbekannte Option: $1 (siehe ./install.sh --help)" ;;
  esac
done

require_tools git flock
take_lock

[[ -f "$ENV_FILE" ]] && die "Schon installiert ($ENV_FILE existiert). Für neue Versionen: ./update.sh"

if [[ -z "$MODE" ]]; then
  echo "Wie kommt HTTPS zustande?"
  echo "  1) Eigener Reverse Proxy auf einem anderen Server (der macht das Zertifikat)"
  echo "  2) Dieser Server direkt mit Domain (Caddy holt das Zertifikat, Ports 80/443)"
  echo "  3) Nur lokal testen (http://localhost:8080)"
  read -r -p "Auswahl [1]: " CHOICE
  case "${CHOICE:-1}" in
    1) MODE="proxy"
       read -r -p "Port, an den dein Proxy weiterleitet [$PORT]: " ANSWER; PORT="${ANSWER:-$PORT}"
       read -r -p "IP deines Proxys (leer = alle privaten Netze): " ANSWER; PROXY_IP="${ANSWER:-$PROXY_IP}" ;;
    2) MODE="domain"; read -r -p "Domain (z. B. stats.example.com): " DOMAIN ;;
    3) MODE="local" ;;
    *) die "Ungültige Auswahl: $CHOICE" ;;
  esac
fi
if [[ "$MODE" == "domain" ]]; then
  [[ "$DOMAIN" =~ ^[A-Za-z0-9.-]+\.[A-Za-z]{2,}$ ]] || die "Das sieht nicht wie eine Domain aus: $DOMAIN"
fi
if [[ "$MODE" == "proxy" ]]; then
  [[ "$PORT" =~ ^[0-9]+$ ]] && (( PORT >= 1 && PORT <= 65535 )) || die "Ungültiger Port: $PORT"
  [[ "$PROXY_IP" =~ ^[0-9A-Za-z_.:/\ ]+$ ]] || die "Ungültige Proxy-Adresse: $PROXY_IP"
fi

# Other services may already run on this server: refuse a taken port before anything is set up.
case "$MODE" in
  local) NEEDED=(8080) ;;
  proxy) NEEDED=("$PORT") ;;
  domain) NEEDED=(80 443) ;;
esac
for p in "${NEEDED[@]}"; do
  if port_in_use "$p"; then
    die "Port $p ist auf diesem Server schon belegt. Anderen Port wählen, z. B. ./install.sh --behind-proxy --port 8095"
  fi
done

# 32 zufällige Bytes, ohne Zeichen, die in einer Verbindungszeichenkette stören.
PASSWORD="$(head -c 32 /dev/urandom | base64 | tr -d '/+=\n' | head -c 40)"

umask 077
case "$MODE" in
  local)
    cat >"$ENV_FILE" <<EOF
SITE_ADDRESS=http://localhost
HTTP_PORT=8080
POSTGRES_PASSWORD=$PASSWORD
LEADERBOARD_MIN_MATCHES=3
EOF
    URL="http://localhost:8080" ;;
  proxy)
    cat >"$ENV_FILE" <<EOF
SITE_ADDRESS=:80
HTTP_PORT=$PORT
TRUSTED_PROXIES=$PROXY_IP
POSTGRES_PASSWORD=$PASSWORD
LEADERBOARD_MIN_MATCHES=20
EOF
    URL="http://$(hostname -I 2>/dev/null | awk '{print $1}'):$PORT (für deinen Proxy)" ;;
  domain)
    cat >"$ENV_FILE" <<EOF
SITE_ADDRESS=$DOMAIN
HTTP_PORT=80
HTTPS_PORT=443
POSTGRES_PASSWORD=$PASSWORD
LEADERBOARD_MIN_MATCHES=20
EOF
    URL="https://$DOMAIN" ;;
esac
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
fetch_installer
green "✓ Läuft: $URL"
if [[ "$MODE" == "domain" ]]; then
  echo "  DNS von $DOMAIN muss auf diesen Server zeigen, Ports 80 und 443 offen. Caddy holt das Zertifikat selbst."
elif [[ "$MODE" == "proxy" ]]; then
  echo "  Im Reverse Proxy: https://deine-domain → http://<diese-server-ip>:$PORT"
  echo "  Der Proxy muss Host und X-Forwarded-For weitergeben (Standard bei Nginx Proxy Manager, Caddy, Traefik)."
  echo "  Port $PORT nur für den Proxy freigeben, nicht fürs ganze Internet."
fi
echo "  Neue Versionen:   ./update.sh"
echo "  Backup:           ./backup.sh   (update.sh macht vorher automatisch eins)"
echo "  Installer für die Webseite: siehe deploy/BETRIEB.md"
