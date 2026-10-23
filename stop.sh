#!/usr/bin/env bash
# Stoppt die Container. Datenbank und Zertifikate bleiben erhalten.
set -Eeuo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/scripts/server-common.sh"

require_tools flock
require_installed
take_lock
compose stop
green "✓ Gestoppt. Wieder starten mit ./start.sh"
