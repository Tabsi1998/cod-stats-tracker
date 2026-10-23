#!/usr/bin/env bash
# Startet die Container (nach einem Neustart des Servers laufen sie wegen "restart: unless-stopped" von selbst).
set -Eeuo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/scripts/server-common.sh"

require_tools flock
require_installed
take_lock
compose up -d
wait_healthy 60 || die "Die API antwortet nicht. Logs: docker compose logs api (in deploy/)."
green "✓ Läuft: $(site_address)"
