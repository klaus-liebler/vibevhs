#!/usr/bin/env bash
# Rückweg zu Schritt 10: Container vibevhs-db anhalten (Daten im Volume „vibevhs-db“ bleiben erhalten) und die Verbindungszeile entfernen.
# Danach läuft nur eine VibeVHS-Version mit PostgreSQL nicht mehr; eine ältere SQLite-Version mit ausrollen.sh --zurueck zurückholen.
source "$(dirname "$0")/gemeinsam.sh"
als_root

docker stop vibevhs-db >/dev/null 2>&1 && ok "Container vibevhs-db angehalten" || warn "Container vibevhs-db lief nicht"
if [[ -f /etc/vibevhs/vibevhs.env ]]; then
    sichern /etc/vibevhs/vibevhs.env
    sed -i '/^ConnectionStrings__VibeVHS=Host=/d' /etc/vibevhs/vibevhs.env
    ok "Verbindungszeile aus /etc/vibevhs/vibevhs.env entfernt"
fi
info "Endgültig löschen (nur nach Rücksprache): docker rm vibevhs-db && docker volume rm vibevhs-db"
