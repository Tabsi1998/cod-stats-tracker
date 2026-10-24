#!/usr/bin/env bash
# Legt zwei Profile desselben Spielers zusammen, z. B. nach einer Neuinstallation mit gelöschten Daten:
#   ./merge-profiles.sh <BEHALTEN-ID> <AUFLÖSEN-ID>          zeigt, was passieren würde
#   ./merge-profiles.sh <BEHALTEN-ID> <AUFLÖSEN-ID> --yes    führt es aus (vorher automatisch ein Backup)
#
# Die IDs stehen in der Profil-Adresse: https://<deine-domain>/player/<ID>
# Geräte, Matches und frühere Namen des zweiten Profils wandern ins erste, danach wird das zweite gelöscht.
# Der PC des zweiten Profils schreibt ab dem nächsten Sync ins behaltene Profil (sein Schlüssel bleibt gültig).
set -Eeuo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/scripts/server-common.sh"

KEEP="${1:-}"
DROP="${2:-}"
uuid='^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$'
[[ "$KEEP" =~ $uuid && "$DROP" =~ $uuid ]] || die "Aufruf: ./merge-profiles.sh <BEHALTEN-ID> <AUFLÖSEN-ID> [--yes]"
[[ "${KEEP,,}" != "${DROP,,}" ]] || die "Zweimal dieselbe ID."

require_installed

sql() { compose exec -T db psql -U codstats -d codstats -v ON_ERROR_STOP=1 -At "$@"; }

for id in "$KEEP" "$DROP"; do
  [[ "$(sql -c "SELECT count(*) FROM profiles WHERE id = '$id'")" == "1" ]] || die "Profil $id gibt es nicht."
done

echo "Behalten: $(sql -c "SELECT coalesce(display_name,'(ohne Namen)') || ', ' || (SELECT count(*) FROM matches WHERE profile_id = '$KEEP') || ' Matches' FROM profiles WHERE id = '$KEEP'")"
echo "Auflösen: $(sql -c "SELECT coalesce(display_name,'(ohne Namen)') || ', ' || (SELECT count(*) FROM matches WHERE profile_id = '$DROP') || ' Matches, ' || (SELECT count(*) FROM devices WHERE profile_id = '$DROP') || ' Gerät(e)' FROM profiles WHERE id = '$DROP'")"

if [[ "${3:-}" != "--yes" ]]; then
  yellow "Nur Vorschau. Zum Ausführen mit --yes am Ende aufrufen."
  exit 0
fi

take_lock
FLOCK_HELD=1 "$ROOT_DIR/backup.sh"

sql <<SQL
BEGIN;
UPDATE devices SET profile_id = '$KEEP' WHERE profile_id = '$DROP';
UPDATE matches SET profile_id = '$KEEP' WHERE profile_id = '$DROP';
-- Names both profiles carried: keep the widest time span on the kept profile, drop the duplicate row.
UPDATE name_history k SET first_seen_at = least(k.first_seen_at, d.first_seen_at), last_seen_at = greatest(k.last_seen_at, d.last_seen_at)
  FROM name_history d WHERE k.profile_id = '$KEEP' AND d.profile_id = '$DROP' AND k.name = d.name;
DELETE FROM name_history d USING name_history k WHERE d.profile_id = '$DROP' AND k.profile_id = '$KEEP' AND k.name = d.name;
UPDATE name_history SET profile_id = '$KEEP' WHERE profile_id = '$DROP';
DELETE FROM profiles WHERE id = '$DROP';
COMMIT;
SQL

green "✓ Zusammengelegt: $(sql -c "SELECT count(*) FROM matches WHERE profile_id = '$KEEP'") Matches im Profil $KEEP."
